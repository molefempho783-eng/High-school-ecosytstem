using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HighSchoolEcosystem.Models;
using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.Services.Interfaces;

namespace HighSchoolEcosystem.ViewModels;

/// <summary>
/// One card in the visual table of contents: a learning unit with its progress.
/// </summary>
public sealed record UnitCardRow(
    string UnitId, string Title, int Completed, int Total)
{
    /// <summary>"1/4 Topics Completed" — ARC's wording.</summary>
    public string ProgressLabel => $"{Completed}/{Total} Topics Completed";

    /// <summary>
    /// 0–1 for the bar. A unit with no material reads as 0 rather than
    /// dividing by zero, and shows an empty bar — which is honest: there's
    /// nothing to complete yet.
    /// </summary>
    public double Fraction => Total <= 0 ? 0 : (double)Completed / Total;

    /// <summary>
    /// Fill width in device units, not a proportion.
    ///
    /// MAUI can't bind a Grid's star-sizing to a value, so a proportional bar
    /// isn't expressible in XAML. The cards are a fixed width, so the usable
    /// track is a known constant and the pixel width can just be computed here.
    /// Keep TrackWidth in step with the card width in StudentClassPage.xaml:
    /// the card is 132 with 9px of padding each side, so the track is 114.
    /// </summary>
    public const double TrackWidth = 114;

    public double FillWidth => Math.Round(Fraction * TrackWidth);

    public bool IsComplete => Total > 0 && Completed >= Total;

    /// <summary>Flat banner colour standing in for ARC's photography.</summary>
    public Color BannerColor { get; init; } = Colors.Transparent;
}

/// <summary>One piece of course material in a list.</summary>
public sealed record MaterialRow(CourseMaterial Source, bool IsCompleted)
{
    public string Id => Source.Id;
    public string Title => Source.Title;
    public string MetaLine => Source.MetaLine;
    public bool IsChapter => Source.IsChapter;
    public bool IsOpenable => Source.IsOpenable;
    public string Body => Source.Body;
    public string Url => Source.Url;
    public string UnitId => Source.UnitId;
    public string Theme => Source.Theme;

    public string Glyph => Source.Kind switch
    {
        MaterialKind.Chapter => Icons.Chapter,
        MaterialKind.Link => Icons.Link,
        _ => Icons.File
    };

    /// <summary>Tick shown against completed items in the contents tree.</summary>
    public string CompletionGlyph => IsCompleted ? "✓" : string.Empty;
}

/// <summary>One marked piece of work, for the grades list.</summary>
public sealed record GradeRow(string Title, AssignmentKind Kind, int Score, int OutOf, DateTimeOffset DueDate)
{
    /// <summary>
    /// Which class this mark belongs to. Empty inside a single class, where the
    /// heading already says it; set on the dashboard, where marks from every
    /// subject are merged into one grid and "17 / 20" is meaningless without it.
    /// </summary>
    public string ClassName { get; init; } = string.Empty;

    /// <summary>
    /// Which class this mark belongs to. Carried so the dashboard can filter by
    /// class and open the class when a mark is tapped — a mark is only ever
    /// interesting next to the rest of that subject's work.
    /// </summary>
    public string ClassId { get; init; } = string.Empty;

    public bool HasClassName => !string.IsNullOrWhiteSpace(ClassName);

    public bool HasScore => Score >= 0 && OutOf > 0;

    public string ScoreLabel => HasScore ? $"{Score} / {OutOf}" : "Not marked";

    public double? Percentage => HasScore ? (double)Score / OutOf * 100 : null;

    public string MetaLine
    {
        get
        {
            var parts = new List<string> { Kind.ToString() };
            if (Percentage is { } pct) parts.Add($"{pct:0}%");
            parts.Add(DueDate.ToLocalTime().ToString("d MMM"));
            return string.Join(" · ", parts);
        }
    }

    /// <summary>
    /// Accent strip on the grade tile, banded by percentage. Colour is a hint
    /// that speeds up scanning a wall of marks; the number is still the fact,
    /// and the bands deliberately avoid pass/fail language because that varies
    /// by school and subject.
    /// </summary>
    public Color BandColor => Percentage switch
    {
        null => Color.FromArgb("#C4C4C4"),
        >= 75 => Color.FromArgb("#2E6B33"),
        >= 50 => Color.FromArgb("#178C8C"),
        >= 40 => Color.FromArgb("#A85400"),
        _ => Color.FromArgb("#B3261E")
    };

    /// <summary>"72%" for the tile's large figure, or a dash when unmarked.</summary>
    public string PercentLabel => Percentage is { } p ? $"{p:0}%" : "—";
}

/// <summary>An upcoming test or exam.</summary>
public sealed record AssessmentRow(string Title, AssignmentKind Kind, DateTimeOffset When, int Points)
{
    public string MetaLine
    {
        get
        {
            var local = When.ToLocalTime();
            var days = (local.Date - DateTimeOffset.Now.ToLocalTime().Date).Days;

            var timing = days switch
            {
                < 0 => $"was {local:ddd d MMM}",
                0 => $"today at {local:HH:mm}",
                1 => $"tomorrow at {local:HH:mm}",
                <= 7 => $"in {days} days — {local:ddd d MMM}",
                _ => local.ToString("ddd d MMM")
            };

            return $"{Kind} · {timing} · {Points} marks";
        }
    }

    public bool IsSoon => (When.ToLocalTime().Date - DateTimeOffset.Now.ToLocalTime().Date).Days is >= 0 and <= 7;

    /// <summary>Amber within the week, brand otherwise.</summary>
    public Color AccentColor => IsSoon ? Color.FromArgb("#A85400") : Color.FromArgb("#0D5257");

    /// <summary>"Test" or "Exam", the tile's kicker.</summary>
    public string KindLabel => Kind.ToString();

    /// <summary>"Wed 3 Sep" — the tile's large figure is the date, not a score.</summary>
    public string DateLabel => When.ToLocalTime().ToString("ddd d MMM");

    public string TimeLabel => When.ToLocalTime().ToString("HH:mm");
}

/// <summary>
/// A class as a student sees it: a visual table of contents of learning units,
/// their own marks, and what's coming up.
///
/// Read-only apart from progress, which a student writes only for themselves —
/// the rules enforce that, and the teacher can read it but not write it.
/// </summary>
public partial class StudentClassViewModel : ObservableObject
{
    private readonly IClassService _classes;

    private string _classId = string.Empty;

    /// <summary>Cached so the unit page can be opened without another round trip.</summary>
    private List<LearningUnit> _units = [];
    private List<CourseMaterial> _materials = [];
    private ClassProgress _progress = new();

    private readonly IClassChatService _chat;

    public StudentClassViewModel(IClassService classes, IClassChatService chat)
    {
        _classes = classes;
        _chat = chat;
    }

    /// <summary>Unread messages, shown on the "Class chat" row.</summary>
    public ChatUnread ChatUnread { get; } = new();

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isRefreshing;
    [ObservableProperty] private string statusMessage = string.Empty;

    [ObservableProperty] private string className = string.Empty;
    [ObservableProperty] private string classMeta = string.Empty;

    /// <summary>Shown in the "Welcome" widget at the top, as ARC does.</summary>
    [ObservableProperty] private string welcomeMessage = "Welcome to the course!";

    // ---- visual table of contents ----
    [ObservableProperty] private ObservableCollection<UnitCardRow> unitCards = [];
    [ObservableProperty] private bool hasUnits;
    [ObservableProperty] private string unitsEmptyMessage = string.Empty;

    // ---- grades ----
    [ObservableProperty] private ObservableCollection<GradeRow> grades = [];
    [ObservableProperty] private bool hasGrades;
    [ObservableProperty] private string gradesCount = "0";
    [ObservableProperty] private string gradesEmptyMessage = string.Empty;
    [ObservableProperty] private string averageLabel = string.Empty;
    [ObservableProperty] private bool hasAverage;

    // ---- tests and exams ----
    [ObservableProperty] private ObservableCollection<AssessmentRow> assessments = [];
    [ObservableProperty] private bool hasAssessments;
    [ObservableProperty] private string assessmentsCount = "0";
    [ObservableProperty] private string assessmentsEmptyMessage = string.Empty;

    // ---- class notices ----

    /// <summary>
    /// Notices the teacher posted to this class. Also merged into the
    /// dashboard feed, but repeated here so a student opening a class sees
    /// everything about it in one place.
    /// </summary>
    [ObservableProperty] private ObservableCollection<AnnouncementRow> announcements = [];

    [ObservableProperty] private bool hasAnnouncements;
    [ObservableProperty] private string announcementsCount = "0";
    [ObservableProperty] private string announcementsEmptyMessage = string.Empty;

    // ---- work to do ----
    [ObservableProperty] private ObservableCollection<StudentWorkRow> classWork = [];
    [ObservableProperty] private bool hasClassWork;
    [ObservableProperty] private string workEmptyMessage = string.Empty;

    public string ClassId => _classId;

    public void Initialise(string classId, string className)
    {
        _classId = classId;
        ClassName = className;
    }

    public async Task LoadAsync()
    {
        if (IsBusy || string.IsNullOrEmpty(_classId)) return;

        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            // Unawaited: one COUNT read; refreshed each time the page appears,
            // so coming back from the chat clears it.
            _ = ChatUnread.LoadAllAsync(_chat, [(_classId, ChatUnread)]);

            _units = (await _classes.GetUnitsAsync(_classId)).ToList();
            _materials = (await _classes.GetMaterialsAsync(_classId)).ToList();
            _progress = await _classes.GetMyProgressAsync(_classId);

            BuildUnitCards();

            var assignments = await _classes.GetAssignmentsAsync(_classId);

            var upcomingAssessments = assignments
                .Where(a => a.Kind.IsAssessment() && a.DueDate >= DateTimeOffset.Now.AddDays(-1))
                .OrderBy(a => a.DueDate)
                .Select(a => new AssessmentRow(a.Title, a.Kind, a.DueDate, a.Points))
                .ToList();

            Assessments = new ObservableCollection<AssessmentRow>(upcomingAssessments);
            HasAssessments = upcomingAssessments.Count > 0;
            AssessmentsCount = upcomingAssessments.Count.ToString();
            AssessmentsEmptyMessage = HasAssessments ? string.Empty : "No tests or exams scheduled.";

            // Submittable work for this class only, mirroring the dashboard's
            // "Work To Do" widget but scoped to one course.
            var work = assignments
                .Where(a => a.Kind.IsSubmittable() && a.DueDate >= DateTimeOffset.Now.AddDays(-7))
                .OrderBy(a => a.DueDate)
                .Select(a => new StudentWorkRow(
                    _classId, a.Id, a.Title, ClassName, a.Kind, a.DueDate, a.Points))
                .ToList();

            ClassWork = new ObservableCollection<StudentWorkRow>(work);
            HasClassWork = work.Count > 0;
            WorkEmptyMessage = HasClassWork
                ? string.Empty
                : "You have no activities with due or end dates available. "
                + "Come back later to see if you have work to do.";

            await LoadAnnouncementsAsync();
            await LoadGradesAsync(assignments);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            StatusMessage = "You don't have access to this class. You may no longer be enrolled.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "That took too long. Check your connection and try again.";
        }
        catch (Exception ex)
        {
            CrashLogger.Log("StudentClass.Load", ex);
            StatusMessage = "We couldn't load this class. Pull down to try again.";
        }
        finally
        {
            IsBusy = false;
            IsRefreshing = false;
        }
    }

    /// <summary>
    /// Builds a card per unit, plus a "Module information" card for loose
    /// material. The loose card comes first, as ARC puts it — it's the module
    /// admin a student reads before anything else.
    /// </summary>
    private void BuildUnitCards()
    {
        var palette = DashboardPalette;
        var cards = new List<UnitCardRow>();

        var loose = _materials.Where(m => string.IsNullOrWhiteSpace(m.UnitId)).ToList();
        if (loose.Count > 0)
        {
            cards.Add(new UnitCardRow(
                string.Empty, "Module information",
                loose.Count(m => _progress.IsComplete(m.Id)), loose.Count)
            {
                BannerColor = palette[0]
            });
        }

        for (var i = 0; i < _units.Count; i++)
        {
            var unit = _units[i];
            var inUnit = _materials.Where(m => m.UnitId == unit.Id).ToList();

            cards.Add(new UnitCardRow(
                unit.Id, unit.Title,
                inUnit.Count(m => _progress.IsComplete(m.Id)), inUnit.Count)
            {
                // Offset by one so the first real unit doesn't share a colour
                // with the "Module information" card above it.
                BannerColor = palette[(i + 1) % palette.Count]
            });
        }

        UnitCards = new ObservableCollection<UnitCardRow>(cards);
        HasUnits = cards.Count > 0;
        UnitsEmptyMessage = HasUnits
            ? string.Empty
            : "Your teacher hasn't added any course content yet.";
    }

    private async Task LoadAnnouncementsAsync()
    {
        try
        {
            var items = await _classes.GetClassAnnouncementsAsync(_classId);

            // Read-only for a student — CanManage false hides the edit controls,
            // and the rules reject the write regardless.
            Announcements = new ObservableCollection<AnnouncementRow>(
                items.Select(a => new AnnouncementRow(a, false)));

            HasAnnouncements = Announcements.Count > 0;
            AnnouncementsCount = Announcements.Count.ToString();
            AnnouncementsEmptyMessage = HasAnnouncements
                ? string.Empty
                : "No notices from your teacher yet.";
        }
        catch (Exception ex)
        {
            // A failed notice read shouldn't blank the course content, which
            // loaded fine.
            CrashLogger.Log("StudentClass.Announcements", ex);
            AnnouncementsEmptyMessage = "We couldn't load this class's notices right now.";
        }
    }

    private async Task LoadGradesAsync(IReadOnlyList<Assignment> assignments)
    {
        var submissions = await _classes.GetMySubmissionsForClassAsync(_classId);
        var byAssignmentId = submissions.ToDictionary(
            s => s.AssignmentId, s => s.Submission, StringComparer.Ordinal);

        var rows = new List<GradeRow>();
        foreach (var assignment in assignments.Where(a => a.Kind.IsSubmittable()))
        {
            if (!byAssignmentId.TryGetValue(assignment.Id, out var submission)) continue;
            if (!submission.IsMarked) continue;

            // An auto-marked score is out of the multiple-choice total, which is
            // lower than the assignment total whenever there's also a written
            // answer. Using the assignment total would understate it.
            var outOf = submission.IsAutoMarked && submission.AutoScoreMax > 0
                ? submission.AutoScoreMax
                : assignment.Points;

            rows.Add(new GradeRow(assignment.Title, assignment.Kind, submission.Grade, outOf, assignment.DueDate));
        }

        rows = rows.OrderByDescending(r => r.DueDate).ToList();

        Grades = new ObservableCollection<GradeRow>(rows);
        HasGrades = rows.Count > 0;
        GradesCount = rows.Count.ToString();
        GradesEmptyMessage = HasGrades
            ? string.Empty
            : "Nothing marked yet. Grades appear here once your teacher marks your work.";

        // Unweighted mean of percentages, so a 10-mark quiz counts the same as a
        // 100-mark assignment. Not a term mark and not presented as one — a
        // weighted gradebook is milestone M5.
        var scored = rows.Where(r => r.Percentage is not null).ToList();
        if (scored.Count > 0)
        {
            var average = scored.Average(r => r.Percentage!.Value);
            AverageLabel = $"{average:0}% average across {scored.Count} marked "
                         + (scored.Count == 1 ? "piece" : "pieces");
            HasAverage = true;
        }
        else
        {
            AverageLabel = string.Empty;
            HasAverage = false;
        }
    }

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync();

    /// <summary>
    /// Banner colours, read from Colors.xaml so recolouring a client school
    /// doesn't leave course cards on the old palette.
    /// </summary>
    private static IReadOnlyList<Color> DashboardPalette
    {
        get
        {
            var keys = new[] { "Banner1", "Banner2", "Banner3", "Banner4", "Banner5", "Banner6" };
            var resources = Application.Current?.Resources;
            if (resources is null) return FallbackPalette;

            var found = new List<Color>(keys.Length);
            foreach (var key in keys)
            {
                if (resources.TryGetValue(key, out var value) && value is Color colour)
                    found.Add(colour);
            }
            return found.Count > 0 ? found : FallbackPalette;
        }
    }

    private static readonly Color[] FallbackPalette =
    [
        Color.FromArgb("#0D5257"), Color.FromArgb("#178C8C"), Color.FromArgb("#4A6572"),
        Color.FromArgb("#7A5C3E"), Color.FromArgb("#2C5F8A"), Color.FromArgb("#6B7A3A")
    ];
}
