using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HighSchoolEcosystem.Models;
using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.Services.Interfaces;

namespace HighSchoolEcosystem.ViewModels;

/// <summary>A student with a tick box for class enrolment.</summary>
public partial class StudentEnrolment : ObservableObject
{
    public string Uid { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    [ObservableProperty] private bool isEnrolled;
}

/// <summary>
/// One assignment in the teacher's list.
///
/// An ObservableObject rather than a record because of the badge: the
/// needs-marking counts are fetched AFTER the list is on screen (one read per
/// assignment), so each row has to be able to grow a badge in place without the
/// whole collection being rebuilt underneath the teacher's finger.
/// </summary>
public sealed partial class AssignmentRow : ObservableObject
{
    public AssignmentRow(string id, string title, AssignmentKind kind, DateTimeOffset dueDate, int points, SubmissionMode submissionMode)
    {
        Id = id;
        Title = title;
        Kind = kind;
        DueDate = dueDate;
        Points = points;
        SubmissionMode = submissionMode;
    }

    public string Id { get; }
    public string Title { get; }
    public AssignmentKind Kind { get; }
    public DateTimeOffset DueDate { get; }
    public int Points { get; }
    public SubmissionMode SubmissionMode { get; }

    public string Subtitle => $"{Kind} · {HandInLabel} · due {DueDate.ToLocalTime():ddd d MMM, HH:mm} · {Points} pts";

    /// <summary>
    /// How it's handed in, in the teacher's own words. Assessments say nothing:
    /// a test isn't handed in at all, so naming a submission mode there would be
    /// a lie the row tells at a glance.
    /// </summary>
    public string HandInLabel => !Kind.IsSubmittable()
        ? "written in class"
        : SubmissionMode switch
        {
            SubmissionMode.File => "file",
            SubmissionMode.Either => "typed or file",
            SubmissionMode.Questions => "multiple choice",
            _ => "typed"
        };

    /// <summary>
    /// How many submissions are waiting on a person. Drives the orange badge.
    /// Zero hides it — an empty circle would read as "marked nothing yet"
    /// rather than "nothing to mark".
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWorkToMark))]
    [NotifyPropertyChangedFor(nameof(NeedsMarkingLabel))]
    [NotifyPropertyChangedFor(nameof(NeedsMarkingDescription))]
    private int needsMarkingCount;

    public bool HasWorkToMark => NeedsMarkingCount > 0;

    /// <summary>Capped, because the badge is a fixed-width circle.</summary>
    public string NeedsMarkingLabel => NeedsMarkingCount > 99 ? "99+" : NeedsMarkingCount.ToString();

    /// <summary>
    /// What a screen reader says, since "7" on its own means nothing. Spelled
    /// out rather than abbreviated for the same reason.
    /// </summary>
    public string NeedsMarkingDescription => NeedsMarkingCount == 1
        ? "1 submission still needs marking"
        : $"{NeedsMarkingCount} submissions still need marking";
}

public partial class TeacherClassDetailViewModel : ObservableObject
{
    private readonly IClassService _classes;
    private string _classId = string.Empty;

    // Every student in the school, held separately from the filtered view.
    // Students is a *window* onto these same instances, so ticks survive a
    // search — and, critically, saving reads from here rather than from what
    // happens to be on screen.
    private List<StudentEnrolment> _allStudents = [];

    // Which class the student list currently reflects, so re-entering the page
    // doesn't refetch and silently discard unsaved ticks.
    private string? _studentsLoadedFor;

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private string className = string.Empty;

    [ObservableProperty] private string editName = string.Empty;
    [ObservableProperty] private string editSubject = string.Empty;
    [ObservableProperty] private string editRoom = string.Empty;

    [ObservableProperty] private ObservableCollection<StudentEnrolment> students = [];

    /// <summary>Filters the enrolment list by name or email.</summary>
    [ObservableProperty] private string studentSearch = string.Empty;

    /// <summary>"3 of 120 enrolled" — so the count is visible while filtered.</summary>
    [ObservableProperty] private string enrolmentSummary = string.Empty;
    [ObservableProperty] private ObservableCollection<AssignmentRow> assignments = [];

    // New assignment form
    [ObservableProperty] private string newTitle = string.Empty;
    [ObservableProperty] private string newDescription = string.Empty;
    [ObservableProperty] private ObservableCollection<AssignmentKind> assignmentKinds =
        new(Enum.GetValues<AssignmentKind>());
    [ObservableProperty] private AssignmentKind newKind = AssignmentKind.Assignment;
    [ObservableProperty] private DateTime newDueDate = DateTime.Today.AddDays(7);
    [ObservableProperty] private TimeSpan newDueTime = new(15, 30, 0);
    [ObservableProperty] private string newPoints = "100";

    /// <summary>
    /// How students hand this in. Named plainly rather than "SubmissionMode",
    /// because the picker's label is what a teacher reads.
    /// </summary>
    public ObservableCollection<string> SubmissionModes { get; } =
    [
        "Type an answer",
        "Attach a file",
        "Either one",
        MultipleChoiceMode
    ];

    /// <summary>
    /// The multiple-choice option, held as a constant because two things match
    /// on it: resolving the enum, and deciding whether Add should carry on to
    /// the question editor. A typo in one of two string literals would leave a
    /// teacher on a paper with no questions and no way to notice.
    /// </summary>
    public const string MultipleChoiceMode = "Multiple choice questions";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMultipleChoice))]
    private string newSubmissionMode = "Type an answer";

    /// <summary>
    /// Drives the note under the picker. Worth saying out loud, because
    /// "multiple choice" is the one option where tapping Add doesn't finish the
    /// job — it opens the question editor, and a teacher who backs out of that
    /// has an empty paper.
    /// </summary>
    public bool IsMultipleChoice => NewSubmissionMode == MultipleChoiceMode;

    private SubmissionMode ResolveSubmissionMode() => NewSubmissionMode switch
    {
        "Attach a file" => SubmissionMode.File,
        "Either one" => SubmissionMode.Either,
        MultipleChoiceMode => SubmissionMode.Questions,
        _ => SubmissionMode.Written
    };

    /// <summary>
    /// Raised after a multiple-choice assignment is created, carrying its new ID
    /// and title so the page can push the question editor.
    ///
    /// An event rather than a Navigation call here: the view model has no
    /// business knowing about pages, and every other navigation on this screen
    /// already goes through the code-behind.
    /// </summary>
    public event Action<string, string>? QuestionsRequested;

    public TeacherClassDetailViewModel(IClassService classes) => _classes = classes;

    partial void OnStudentSearchChanged(string value) => ApplyStudentFilter();

    private void OnEnrolmentChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StudentEnrolment.IsEnrolled)) UpdateEnrolmentSummary();
    }

    private void ApplyStudentFilter()
    {
        var term = StudentSearch?.Trim();

        var matches = string.IsNullOrEmpty(term)
            ? _allStudents
            : _allStudents.Where(s =>
                s.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                s.Email.Contains(term, StringComparison.CurrentCultureIgnoreCase))
              .ToList();

        // Same objects, not copies — that's what keeps ticks intact across
        // searches.
        Students = new ObservableCollection<StudentEnrolment>(matches);
        UpdateEnrolmentSummary();
    }

    private void UpdateEnrolmentSummary()
    {
        var enrolled = _allStudents.Count(s => s.IsEnrolled);
        var hidden = _allStudents.Count - Students.Count;

        EnrolmentSummary = hidden > 0
            ? $"{enrolled} enrolled of {_allStudents.Count} students · {hidden} hidden by search"
            : $"{enrolled} enrolled of {_allStudents.Count} students";
    }

    // ==================== class announcements ====================

    /// <summary>
    /// Notices this teacher has posted to the class. Distinct from school-wide
    /// announcements, which stay admin-only — a teacher can only address their
    /// own class.
    /// </summary>
    [ObservableProperty] private ObservableCollection<AnnouncementRow> announcements = [];

    [ObservableProperty] private bool hasAnnouncements;
    [ObservableProperty] private string announcementsCount = "0";
    [ObservableProperty] private string announcementsEmptyMessage = string.Empty;

    [ObservableProperty] private bool isAnnouncementFormVisible;
    [ObservableProperty] private string newAnnouncementTitle = string.Empty;
    [ObservableProperty] private string newAnnouncementBody = string.Empty;
    [ObservableProperty] private bool newAnnouncementPinned;

    /// <summary>
    /// Whether this notice should stop showing on a date. Off by default: most
    /// school notices have no natural end, and MAUI's DatePicker can't be null,
    /// so a flag carries "no expiry" rather than a sentinel date.
    /// </summary>
    [ObservableProperty] private bool newAnnouncementExpires;

    [ObservableProperty] private DateTime newAnnouncementExpiryDate = DateTime.Today.AddDays(14);

    private string _editingAnnouncementId = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AnnouncementFormHeading))]
    [NotifyPropertyChangedFor(nameof(AnnouncementSaveText))]
    private bool isEditingAnnouncement;

    public string AnnouncementFormHeading => IsEditingAnnouncement ? "Edit notice" : "New notice";
    public string AnnouncementSaveText => IsEditingAnnouncement ? "Save changes" : "Post to class";

    private async Task LoadAnnouncementsAsync()
    {
        try
        {
            // Management view: the teacher sees their own lapsed notices so
            // they can extend or delete them. Students never do.
            var items = await _classes.GetClassAnnouncementsAsync(_classId, includeExpired: true);

            // CanManage is true throughout: this screen only opens for the
            // class's own teacher, and the rules reject anyone else regardless.
            Announcements = new ObservableCollection<AnnouncementRow>(
                items.Select(a => new AnnouncementRow(a, true)));

            HasAnnouncements = Announcements.Count > 0;
            AnnouncementsCount = Announcements.Count.ToString();
            AnnouncementsEmptyMessage = HasAnnouncements
                ? string.Empty
                : "Nothing posted to this class yet.";
        }
        catch (Exception ex)
        {
            // A failed notice load shouldn't blank the roster and assignments,
            // which loaded fine.
            CrashLogger.Log("TeacherClassDetail.Announcements", ex);
            AnnouncementsEmptyMessage = "We couldn't load this class's notices right now.";
        }
    }


    /// <summary>
    /// The chosen expiry as an instant, or default for "never".
    ///
    /// End of the chosen day, not its start — picking "12 September" means the
    /// notice is still up ON the 12th.
    /// </summary>
    private DateTimeOffset ResolveExpiry()
        => NewAnnouncementExpires
            ? new DateTimeOffset(
                NewAnnouncementExpiryDate.Date.AddDays(1).AddSeconds(-1),
                DateTimeOffset.Now.Offset).ToUniversalTime()
            : default;

    [RelayCommand]
    private void StartCreateAnnouncement()
    {
        ResetAnnouncementForm();
        IsAnnouncementFormVisible = true;
    }

    [RelayCommand]
    private void StartEditAnnouncement(AnnouncementRow? row)
    {
        if (row is null) return;

        _editingAnnouncementId = row.Id;
        IsEditingAnnouncement = true;
        NewAnnouncementTitle = row.Source.Title;
        NewAnnouncementBody = row.Source.Body;
        NewAnnouncementPinned = row.Source.IsPinned;
        NewAnnouncementExpires = row.Source.HasExpiry;
        if (row.Source.HasExpiry)
            NewAnnouncementExpiryDate = row.Source.ExpiresAt.ToLocalTime().Date;
        IsAnnouncementFormVisible = true;
    }

    [RelayCommand]
    private void CancelAnnouncementEdit() => ResetAnnouncementForm();

    [RelayCommand]
    private async Task SaveAnnouncementAsync()
    {
        if (string.IsNullOrWhiteSpace(NewAnnouncementTitle))
        {
            StatusMessage = "Give the notice a title — students see this in their feed.";
            return;
        }

        // Save and reload are separate: if the save succeeds but the reload
        // fails, resetting here would clear the editing ID and a retry would
        // create a duplicate.
        try
        {
            await _classes.SaveClassAnnouncementAsync(_classId, ClassName, new Announcement
            {
                Id = IsEditingAnnouncement ? _editingAnnouncementId : string.Empty,
                Title = NewAnnouncementTitle,
                Body = NewAnnouncementBody,
                IsPinned = NewAnnouncementPinned,
                ExpiresAt = ResolveExpiry()
            });

            ResetAnnouncementForm();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            StatusMessage = "Only this class's teacher can post notices to it.";
            return;
        }
        catch (Exception ex)
        {
            CrashLogger.Log("TeacherClassDetail.SaveAnnouncement", ex);
            StatusMessage = "We couldn't post that. Please try again.";
            return;
        }

        await LoadAnnouncementsAsync();
    }

    [RelayCommand]
    private async Task DeleteAnnouncementAsync(AnnouncementRow? row)
    {
        if (row is null) return;

        try
        {
            await _classes.DeleteClassAnnouncementAsync(_classId, row.Id);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            StatusMessage = "Only this class's teacher can remove its notices.";
            return;
        }
        catch (Exception ex)
        {
            CrashLogger.Log("TeacherClassDetail.DeleteAnnouncement", ex);
            StatusMessage = "We couldn't delete that. Please try again.";
            return;
        }

        await LoadAnnouncementsAsync();
    }

    private void ResetAnnouncementForm()
    {
        _editingAnnouncementId = string.Empty;
        IsEditingAnnouncement = false;
        IsAnnouncementFormVisible = false;
        NewAnnouncementTitle = string.Empty;
        NewAnnouncementBody = string.Empty;
        NewAnnouncementPinned = false;
        NewAnnouncementExpires = false;
        NewAnnouncementExpiryDate = DateTime.Today.AddDays(14);
    }

    public async Task LoadAsync(string classId)
    {
        if (IsBusy) return;
        _classId = classId;
        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            var schoolClass = await _classes.GetClassAsync(classId);
            if (schoolClass is null)
            {
                StatusMessage = "That class no longer exists.";
                return;
            }

            ClassName = schoolClass.Name;

            await LoadAnnouncementsAsync();
            EditName = schoolClass.Name;
            EditSubject = schoolClass.Subject;
            EditRoom = schoolClass.Room;

            // OnAppearing re-runs this every time the teacher comes back from the
            // questions page. Refetching would rebuild the list and wipe any
            // ticks they hadn't saved yet, so only load when it's a different
            // class (or the first visit).
            if (_studentsLoadedFor != classId)
            {
                var allStudents = await _classes.GetStudentsAsync();
                _allStudents = allStudents.Select(s => new StudentEnrolment
                {
                    Uid = s.Uid,
                    Name = string.IsNullOrWhiteSpace(s.DisplayName) ? s.Email : s.DisplayName,
                    Email = s.Email,
                    IsEnrolled = schoolClass.StudentIds.Contains(s.Uid)
                }).ToList();

                // Ticking a box mutates the instance, so the summary would go stale.
                foreach (var student in _allStudents)
                    student.PropertyChanged += OnEnrolmentChanged;

                _studentsLoadedFor = classId;
                StudentSearch = string.Empty;

                if (allStudents.Count == 0)
                    StatusMessage = "No students have registered yet, so there's nobody to enrol.";
            }

            ApplyStudentFilter();
            await ReloadAssignmentsAsync();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            StatusMessage = "You don't have permission to open this class.";
        }
        catch (Exception ex)
        {
            CrashLogger.Log("TeacherClassDetail.Load", ex);
            StatusMessage = "We couldn't load this class. Please go back and try again.";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task SaveDetailsAsync()
    {
        if (IsBusy) return;
        if (string.IsNullOrWhiteSpace(EditName))
        {
            StatusMessage = "The class needs a name.";
            return;
        }

        IsBusy = true;
        try
        {
            await _classes.UpdateClassDetailsAsync(_classId, EditName, EditSubject, EditRoom);
            ClassName = EditName.Trim();
            StatusMessage = "Class details saved.";
        }
        catch (Exception ex) { StatusMessage = Describe(ex, "save the class details"); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task SaveEnrolmentAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            // _allStudents, NOT Students: saving while a search is active would
            // otherwise drop every enrolled student the filter is hiding.
            var enrolled = _allStudents.Where(s => s.IsEnrolled).Select(s => s.Uid).ToList();
            await _classes.SetEnrolledStudentsAsync(_classId, enrolled);
            StatusMessage = enrolled.Count == 1
                ? "1 student enrolled."
                : $"{enrolled.Count} students enrolled.";
        }
        catch (Exception ex) { StatusMessage = Describe(ex, "save the enrolment"); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task AddAssignmentAsync()
    {
        if (IsBusy) return;
        if (string.IsNullOrWhiteSpace(NewTitle))
        {
            StatusMessage = "Give the assignment a title.";
            return;
        }
        if (!int.TryParse(NewPoints, out var points) || points < 0)
        {
            StatusMessage = "Points must be a whole number.";
            return;
        }

        IsBusy = true;
        try
        {
            // The pickers give a local wall-clock date and time; Firestore stores
            // UTC. Tagging the DateTime as Local and letting DateTimeOffset work
            // out the offset handles DST correctly — and can't throw the way
            // passing an explicit offset can if the two disagree.
            var localDue = DateTime.SpecifyKind(NewDueDate.Date.Add(NewDueTime), DateTimeKind.Local);
            var due = new DateTimeOffset(localDue);

            var kind = NewKind;
            var mode = ResolveSubmissionMode();
            var title = NewTitle.Trim();

            var assignmentId = await _classes.SaveAssignmentAsync(_classId, new Assignment
            {
                Title = title,
                Description = NewDescription,
                Kind = kind,
                DueDate = due,
                Points = points,
                SubmissionMode = mode
            });

            NewTitle = string.Empty;
            NewDescription = string.Empty;
            await ReloadAssignmentsAsync();

            if (mode == SubmissionMode.Questions)
            {
                // A multiple-choice paper isn't finished when it's saved — it
                // has no questions yet. Carrying straight on to the editor is
                // the whole point of the option, so a teacher never has to
                // discover that the next step is hidden behind tapping the row.
                StatusMessage = $"{kind} added. Now add its questions.";
                QuestionsRequested?.Invoke(assignmentId, title);
                return;
            }

            StatusMessage = $"{kind} added.";
        }
        catch (Exception ex) { StatusMessage = Describe(ex, "add that assignment"); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task DeleteAssignmentAsync(AssignmentRow? row)
    {
        if (row is null || IsBusy) return;
        IsBusy = true;
        try
        {
            await _classes.DeleteAssignmentAsync(_classId, row.Id);
            await ReloadAssignmentsAsync();
            StatusMessage = $"Removed {row.Title}.";
        }
        catch (Exception ex) { StatusMessage = Describe(ex, "remove that assignment"); }
        finally { IsBusy = false; }
    }

    private async Task ReloadAssignmentsAsync()
    {
        var items = await _classes.GetAssignmentsAsync(_classId);
        Assignments = new ObservableCollection<AssignmentRow>(
            items.Select(a => new AssignmentRow(a.Id, a.Title, a.Kind, a.DueDate, a.Points, a.SubmissionMode)));

        // NOT awaited. This is one read per assignment; awaiting it would hold
        // IsBusy true while a term of homework is counted, and would sit between
        // "Add" and the question editor opening. Nothing downstream depends on
        // the counts, and the method swallows its own failures, so letting it
        // finish on its own is safe as well as faster.
        _ = LoadNeedsMarkingAsync(Assignments);
    }

    /// <summary>
    /// Fills in the orange badges once the list is already on screen.
    ///
    /// Deliberately not awaited into the page load path in a way that could
    /// block it: this costs one read per assignment, and a class with a term of
    /// work behind it would otherwise stare at a spinner. Only work that can
    /// actually be handed in is counted — a test has no submissions.
    ///
    /// Rows are matched back by ID rather than by position, because the
    /// collection can be replaced by another reload while this is in flight.
    /// </summary>
    private async Task LoadNeedsMarkingAsync(IReadOnlyCollection<AssignmentRow> rows)
    {
        var submittable = rows.Where(r => r.Kind.IsSubmittable()).ToList();
        if (submittable.Count == 0) return;

        try
        {
            var counts = await _classes.GetNeedsMarkingCountsAsync(
                _classId, submittable.Select(r => r.Id).ToList());

            foreach (var row in submittable)
            {
                row.NeedsMarkingCount = counts.TryGetValue(row.Id, out var waiting) ? waiting : 0;
            }

            var total = counts.Values.Sum();
            NeedsMarkingTotal = total;
        }
        catch (Exception ex)
        {
            // A badge that fails to load is not worth an error message. The
            // teacher can still open any assignment and see the queue itself.
            CrashLogger.Log("TeacherClassDetail.NeedsMarking", ex);
        }
    }

    /// <summary>
    /// Everything waiting across the whole class, for the badge on the section
    /// header — so a teacher scrolling past knows there's work below without
    /// reading every row.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWorkToMark))]
    [NotifyPropertyChangedFor(nameof(NeedsMarkingTotalLabel))]
    private int needsMarkingTotal;

    public bool HasWorkToMark => NeedsMarkingTotal > 0;

    public string NeedsMarkingTotalLabel =>
        NeedsMarkingTotal > 99 ? "99+" : NeedsMarkingTotal.ToString();

    private static string Describe(Exception ex, string action)
    {
        if (ex is HttpRequestException http && http.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            // A 403 here is almost always undeployed rules or a class whose
            // TeacherId isn't this teacher — say so rather than leaving them
            // to guess which.
            return $"Permission denied trying to {action}. Check that firestore.rules "
                 + "has been deployed, and that this class's TeacherId is your account.";
        }
        if (ex is OperationCanceledException)
            return "That took too long. Check your connection and try again.";

        CrashLogger.Log("TeacherClassDetail", ex);
        return $"We couldn't {action}. Please try again.";
    }
}
