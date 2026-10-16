using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HighSchoolEcosystem.Models;
using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.Services.Firebase;
using HighSchoolEcosystem.Services.Interfaces;

namespace HighSchoolEcosystem.ViewModels;

/// <summary>One weekly period on the student's timetable.</summary>
public sealed record StudentPeriodRow(
    DayOfWeek Day, string StartTime, string EndTime, string ClassName, string Subject, string Room)
{
    public string When => $"{Day} · {StartTime}–{EndTime}";

    public string Detail
    {
        get
        {
            // Subject and room are both optional on a class, so build only from
            // the parts that are actually set.
            var parts = new List<string> { ClassName };
            if (!string.IsNullOrWhiteSpace(Subject)) parts.Add(Subject);
            if (!string.IsNullOrWhiteSpace(Room)) parts.Add(Room);
            return string.Join(" · ", parts);
        }
    }

    /// <summary>The bold line in the timetable widget: subject, or class name if unset.</summary>
    public string Heading => string.IsNullOrWhiteSpace(Subject) ? ClassName : Subject;

    /// <summary>"08:00–08:45 · Room B12" — the grey line beneath.</summary>
    public string TimeAndPlace
    {
        get
        {
            var slot = $"{StartTime}–{EndTime}";
            return string.IsNullOrWhiteSpace(Room) ? slot : $"{slot} · {Room}";
        }
    }

    public string DayLabel => Day.ToString();

    /// <summary>
    /// Colour of the spine to the left of the period. Keyed off the class name
    /// so the same subject keeps the same colour between renders and matches its
    /// card in "My classes". Set by the view model, which owns the palette.
    /// </summary>
    public Color SpineColor { get; init; } = Colors.Transparent;
}

/// <summary>One piece of work due, across every class the student is in.</summary>
public sealed record StudentWorkRow(
    string ClassId, string AssignmentId, string Title, string ClassName,
    AssignmentKind Kind, DateTimeOffset DueDate, int Points)
{
    public string Detail => $"{Kind} · {ClassName} · {Points} pts";

    /// <summary>
    /// ARC puts everything on one line under the title: "Due 26 Aug ·
    /// Mathematics · Homework". Denser than stacking three labels and it's what
    /// makes the work list scan quickly.
    /// </summary>
    public string MetaLine => $"{ShortDue} · {ClassName} · {Kind}";

    private string ShortDue
    {
        get
        {
            var local = DueDate.ToLocalTime();

            // "Was due" is wrong for an exam — you sat it, you didn't miss a
            // deadline. Assessments never reach this state anyway (they're
            // dropped once the date passes), but the wording has to be right
            // for the hours before that.
            if (IsAssessment) return $"{local:d MMM, HH:mm}";

            return IsOverdue ? $"Was due {local:d MMM}" : $"Due {local:d MMM}";
        }
    }

    /// <summary>
    /// Shown as a pill when the work isn't open yet. Null hides the pill —
    /// the view binds IsVisible to HasStartNote.
    /// </summary>
    public string? StartNote { get; init; }

    public bool HasStartNote => !string.IsNullOrWhiteSpace(StartNote);

    public bool IsOverdue => DueDate < DateTimeOffset.Now;

    /// <summary>
    /// True for a test or exam. These appear in Work to do so a student sees
    /// them coming, but nothing is handed in — tapping one shows its details
    /// rather than opening a submission page with no submit box on it.
    /// </summary>
    public bool IsAssessment => Kind.IsAssessment();

    /// <summary>The row's glyph: a paper for work, a clock for an assessment.</summary>
    public string Glyph => IsAssessment ? Icons.Assessment : Icons.Work;

    /// <summary>
    /// Accent strip: red when late, amber when due within a day, brand
    /// otherwise. Urgency is the one thing worth encoding in colour.
    ///
    /// An assessment is never "late" — it happened or it didn't — so it stays
    /// on the countdown colours right up to the day.
    /// </summary>
    public Color AccentColor
    {
        get
        {
            if (IsOverdue && !IsAssessment) return Color.FromArgb("#B3261E");

            var hours = (DueDate - DateTimeOffset.Now).TotalHours;
            return hours <= 24 ? Color.FromArgb("#A85400") : Color.FromArgb("#178C8C");
        }
    }

    /// <summary>
    /// What tapping an assessment shows. There's nothing to open, so the row
    /// explains itself rather than leading somewhere useless.
    /// </summary>
    public string DetailText
    {
        get
        {
            // ClassName is the sheet's chip, so it isn't repeated here.
            var lines = new List<string>
            {
                $"{Kind} · {Points} marks",
                $"When: {DueDate.ToLocalTime():dddd d MMMM yyyy, HH:mm}"
            };

            lines.Add(string.Empty);
            lines.Add(IsAssessment
                ? "This is written in class — there is nothing to hand in here. "
                  + "It disappears from Work to do once the date has passed."
                : "Open it to hand your work in.");

            return string.Join(Environment.NewLine, lines);
        }
    }

    /// <summary>"Due 26 Aug" — the short form, for a tile where width is tight.</summary>
    public string DueShort => IsOverdue
        ? $"Was due {DueDate.ToLocalTime():d MMM}"
        : $"Due {DueDate.ToLocalTime():d MMM}";

    /// <summary>Class and kind, the second line of a work tile.</summary>
    public string ClassAndKind => $"{ClassName} · {Kind}";

    public string Due
    {
        get
        {
            var local = DueDate.ToLocalTime();
            var days = (local.Date - DateTimeOffset.Now.ToLocalTime().Date).Days;

            // Relative wording for the next couple of days, because that's when
            // it actually matters; absolute after that.
            return days switch
            {
                < 0 => $"Overdue — was {local:ddd d MMM}",
                0 => $"Due today at {local:HH:mm}",
                1 => $"Due tomorrow at {local:HH:mm}",
                _ => $"Due {local:ddd d MMM} at {local:HH:mm}"
            };
        }
    }
}

/// <summary>One tile in the "My classes" grid.</summary>
public sealed record ClassCardRow(
    string ClassId, string Name, string Subject, string TeacherName, string Room)
{
    public string Heading => string.IsNullOrWhiteSpace(Subject) ? Name : Subject;

    public string SubHeading => string.IsNullOrWhiteSpace(Subject) ? string.Empty : Name;

    /// <summary>"Mr Dlamini · Room B12", skipping whichever part is missing.</summary>
    public string Meta
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(TeacherName)) parts.Add(TeacherName);
            if (!string.IsNullOrWhiteSpace(Room)) parts.Add(Room);
            return parts.Count == 0 ? "No teacher assigned" : string.Join(" · ", parts);
        }
    }

    /// <summary>Flat banner colour standing in for ARC's photography.</summary>
    public Color BannerColor { get; init; } = Colors.Transparent;

    /// <summary>Unread class-chat messages; fills in after the card is shown.</summary>
    public ChatUnread Unread { get; } = new();
}

/// <summary>
/// Role-based home screen. This is a shell for now — each section listed here
/// becomes a real feature in a later milestone (see PLAN.md §7):
/// announcements + sports in M2, timetable in M3, courses in M4, gradebook M5.
/// </summary>
public partial class DashboardViewModel : DashboardShellViewModel
{
    private readonly IUserProfileService _userProfileService;
    private readonly IFirebaseAuthService _auth;
    private readonly ITeamService _teams;
    private readonly IClassChatService _chat;
    private readonly IClassService _classes;
    private readonly IContentService _content;

    [ObservableProperty] private ObservableCollection<string> sections = [];

    /// <summary>True when nobody is signed in — drives the "Sign in" affordance.</summary>
    [ObservableProperty] private bool isGuest;

    /// <summary>
    /// Set when this user also holds a staff role. Someone can be a Student
    /// *and* a Teacher, so the content dashboard has to offer a way across —
    /// otherwise half their account is unreachable.
    /// </summary>
    [ObservableProperty] private bool hasTeacherTools;
    [ObservableProperty] private bool hasAdminTools;

    // ---- real student content ----

    /// <summary>The student's weekly periods, ordered by day then start time.</summary>
    [ObservableProperty] private ObservableCollection<StudentPeriodRow> timetable = [];

    /// <summary>Assignments and homework across every class they're enrolled in.</summary>
    [ObservableProperty] private ObservableCollection<StudentWorkRow> upcomingWork = [];

    /// <summary>
    /// Late work, split out of UpcomingWork so the "Overdue" section can carry
    /// its own count badge the way ARC does. The two collections partition the
    /// same set — nothing appears in both.
    /// </summary>
    [ObservableProperty] private ObservableCollection<StudentWorkRow> overdueWork = [];

    [ObservableProperty] private bool hasOverdueWork;
    [ObservableProperty] private bool hasUpcomingWork;

    /// <summary>Badge counts, as strings because that's what a Label binds to.</summary>
    [ObservableProperty] private string overdueCount = "0";
    [ObservableProperty] private string upcomingCount = "0";

    /// <summary>Heading over the non-overdue section, e.g. "26 August – 8 September".</summary>
    [ObservableProperty] private string upcomingRangeLabel = string.Empty;

    /// <summary>Tiles for the "My classes" widget.</summary>
    [ObservableProperty] private ObservableCollection<ClassCardRow> classCards = [];

    /// <summary>"View all classes (9)".</summary>
    [ObservableProperty] private string classCountLabel = string.Empty;

    /// <summary>Today's periods only, for the right-hand rail.</summary>
    [ObservableProperty] private ObservableCollection<StudentPeriodRow> todayPeriods = [];

    [ObservableProperty] private bool hasTodayPeriods;
    [ObservableProperty] private string todayLabel = string.Empty;

    // ---- timetable day picker ----

    /// <summary>
    /// Options for the day selector. "Whole week" is index 0 so it's the
    /// obvious escape hatch, and the weekdays follow in calendar order.
    /// Weekends are included: some schools run Saturday sport and extra lessons.
    /// </summary>
    public ObservableCollection<string> TimetableDays { get; } =
    [
        WholeWeek, "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"
    ];

    public const string WholeWeek = "Whole week";

    /// <summary>
    /// Which day the timetable widget is showing. Setting this refilters both
    /// the period list and the work list, so the two always describe the same
    /// scope — showing Tuesday's lessons beside the whole week's homework would
    /// be actively misleading.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWholeWeek))]
    private string selectedTimetableDay = WholeWeek;

    public bool IsWholeWeek => SelectedTimetableDay == WholeWeek;

    /// <summary>Periods for the selected scope.</summary>
    [ObservableProperty] private ObservableCollection<StudentPeriodRow> visiblePeriods = [];

    [ObservableProperty] private bool hasVisiblePeriods;
    [ObservableProperty] private string periodsEmptyMessage = string.Empty;

    /// <summary>Heading over the timetable list, e.g. "Tuesday" or "Whole week".</summary>
    [ObservableProperty] private string timetableScopeLabel = WholeWeek;

    // ---- "all caught up" ----

    /// <summary>
    /// True when the selected scope has classes but no outstanding work. Drives
    /// the "All caught up" panel in place of an empty homework list.
    ///
    /// Distinct from "no work exists": a student with nothing assigned all week
    /// hasn't caught up with anything, so that case gets different wording.
    /// </summary>
    [ObservableProperty] private bool isAllCaughtUp;

    [ObservableProperty] private string caughtUpMessage = string.Empty;

    partial void OnSelectedTimetableDayChanged(string value) => ApplyDayFilter();

    // ---- announcements ----

    /// <summary>
    /// The notice board, shown as a dashboard section rather than its own page.
    /// Capped at a handful of rows: this is a summary, and a term's notices
    /// would push everything below it off the screen.
    /// </summary>
    [ObservableProperty] private ObservableCollection<AnnouncementRow> announcements = [];

    [ObservableProperty] private bool hasAnnouncements;
    [ObservableProperty] private string announcementsCount = "0";
    [ObservableProperty] private string announcementsEmptyMessage = string.Empty;

    /// <summary>True only for admins — drives the inline compose form.</summary>
    [ObservableProperty] private bool canManageAnnouncements;

    [ObservableProperty] private bool isAnnouncementFormVisible;
    [ObservableProperty] private string newAnnouncementTitle = string.Empty;
    [ObservableProperty] private string newAnnouncementBody = string.Empty;
    [ObservableProperty] private bool newAnnouncementPinned;
    [ObservableProperty] private bool newAnnouncementPublic;

    /// <summary>
    /// Whether this notice should stop showing on a date.
    ///
    /// ON by default, with a fortnight's date already filled in. It used to
    /// default off, which meant a notice only ever disappeared if its author
    /// remembered to tick a box — so in practice nothing expired and the feed
    /// filled up with last term's news. Making it opt-OUT means the common case
    /// (a notice about something that happens on a date) is handled by doing
    /// nothing, and the rarer standing notice costs one tap.
    ///
    /// MAUI's DatePicker can't hold null, so a flag carries "no expiry" rather
    /// than a sentinel date.
    /// </summary>
    [ObservableProperty] private bool newAnnouncementExpires = true;

    [ObservableProperty] private DateTime newAnnouncementExpiryDate = DateTime.Today.AddDays(14);

    private string _editingAnnouncementId = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AnnouncementFormHeading))]
    [NotifyPropertyChangedFor(nameof(AnnouncementSaveText))]
    private bool isEditingAnnouncement;

    public string AnnouncementFormHeading => IsEditingAnnouncement ? "Edit announcement" : "New announcement";
    public string AnnouncementSaveText => IsEditingAnnouncement ? "Save changes" : "Post";

    // ---- sport ----

    [ObservableProperty] private ObservableCollection<SportsRow> sportsItems = [];
    [ObservableProperty] private bool hasSports;
    [ObservableProperty] private string sportsCount = "0";
    [ObservableProperty] private string sportsEmptyMessage = string.Empty;

    /// <summary>True for Coach, Admin and Journalist.</summary>
    [ObservableProperty] private bool canPostSports;



    [ObservableProperty] private bool hasClasses;

    /// <summary>A student with no enrolment yet — needs an explanation, not a blank screen.</summary>
    [ObservableProperty] private bool showNotEnrolledNote;
    [ObservableProperty] private string classesSummary = string.Empty;
    [ObservableProperty] private string timetableEmptyMessage = string.Empty;
    [ObservableProperty] private string workEmptyMessage = string.Empty;

    /// <summary>
    /// Shown OUTSIDE the enrolled-content block. The empty-state labels sit
    /// inside it, so a load failure — which also clears HasClasses — would
    /// otherwise render an explanation nobody can see.
    /// </summary>
    [ObservableProperty] private string schoolWorkError = string.Empty;

    /// <summary>
    /// True when this user is rostered for a privileged role but hasn't verified
    /// their email yet, so their profile can't be created until they do.
    /// </summary>
    [ObservableProperty] private bool needsEmailVerification;

    public DashboardViewModel(
        IUserProfileService userProfileService,
        IFirebaseAuthService auth,
        IClassService classes,
        IContentService content,
        ITeamService teams,
        ISchoolInfoProvider schoolInfoProvider,
        IClassChatService chat)
    {
        _chat = chat;
        _userProfileService = userProfileService;
        _auth = auth;
        _classes = classes;
        _content = content;
        _teams = teams;
        SchoolName = schoolInfoProvider.Current.DisplayName;

        // Built here as well as after load, so a failed profile read leaves a
        // usable rail rather than an empty strip. Load rebuilds it once roles
        // are known, which is what adds the cross-links.
        BuildRail();
    }

    /// <summary>
    /// Set by the page. The view model decides WHICH rail items exist for this
    /// user's roles; the page decides what each one DOES, because scrolling and
    /// navigation are view concerns.
    /// </summary>
    public Action<string>? OnRailAction { get; set; }

    /// <summary>
    /// Rebuilds the rail for the roles this user actually holds. Called after
    /// the profile loads, so a teacher-who-is-also-a-student gets both.
    /// </summary>
    /// <summary>
    /// Whether this user can read the Newspaper — decided from the profile so
    /// the rail can be built before the content service has been primed.
    /// </summary>
    private bool _readsNewspaper;

    private void BuildRail()
    {
        // ORDER MATTERS: these must appear in the same order as the widgets on
        // DashboardPage, top to bottom. The rail is a table of contents for the
        // page, so a rail that disagrees with the page is worse than no rail.
        //
        // Every item SCROLLS. Account is the only one that opens another
        // screen — cross-role links live in the "Your other roles" widget,
        // which the rail scrolls to like anything else.
        var items = new List<RailItem>();

        // Matches the RolesSection widget's own visibility condition exactly.
        // If they ever disagree the rail offers a jump to an invisible card,
        // which just silently does nothing.
        if (HasTeacherTools || HasAdminTools)
            items.Add(new(Icons.OtherRoles, "Your other roles", new RelayCommand(() => OnRailAction?.Invoke("roles"))));

        items.Add(new(Icons.Work, "Work to do", new RelayCommand(() => OnRailAction?.Invoke("work"))));
        items.Add(new(Icons.Classes, "My classes", new RelayCommand(() => OnRailAction?.Invoke("classes"))));
        items.Add(new(Icons.Grades, "My grades", new RelayCommand(() => OnRailAction?.Invoke("grades"))));
        items.Add(new(Icons.Timetable, "Timetable", new RelayCommand(() => OnRailAction?.Invoke("timetable"))));
        items.Add(new(Icons.Announcement, "Announcements", new RelayCommand(() => OnRailAction?.Invoke("announcements"))));
        items.Add(new(Icons.Sport, "Sport", new RelayCommand(() => OnRailAction?.Invoke("sport"))));

        // Same test as the Newspaper widget's own visibility: visitors and
        // guests can't read it, so they get neither the section nor the jump.
        if (_readsNewspaper)
            items.Add(new(Icons.Newspaper, "Newspaper", new RelayCommand(() => OnRailAction?.Invoke("newspaper"))));

        items.Add(new(Icons.Account, "Account",
            new RelayCommand(() => OnRailAction?.Invoke("account"))) { StartsGroup = true });

        RailItems = new ObservableCollection<RailItem>(items);
    }

    // ==================== announcements and sport ====================

    /// <summary>
    /// Loads both school-wide feeds. Failures here are swallowed into an empty
    /// state rather than surfaced as an error: a broken notice board shouldn't
    /// make the dashboard look broken when the student's own work loaded fine.
    /// </summary>
    private async Task LoadSchoolContentAsync()
    {
        try
        {
            await _content.PrimeAsync();
            CanManageAnnouncements = _content.CanManageAnnouncements;
            CanPostSports = _content.CanPostSports;

            // Admins manage announcements from this same widget, so they keep
            // seeing lapsed ones — otherwise a notice vanishes with no way to
            // tell why or extend it. Everyone else gets the live set.
            var schoolNotices = await _content.GetAnnouncementsAsync(
                includeExpired: CanManageAnnouncements);

            // Class notices from the student's own classes, merged into the same
            // feed. A student shouldn't have to check two places to find out
            // their Maths test moved.
            var classNotices = _enrolledClasses.Count > 0
                ? await _classes.GetAnnouncementsForClassesAsync(_enrolledClasses)
                : [];

            // Team notices too. A practice time posted to a squad is useless if
            // the squad has to go looking for it, and sport is the one thing in
            // this app that changes at short notice — "practice moved to
            // Thursday" is worth more than anything else on this screen.
            var myTeams = await _teams.GetTeamsForStudentAsync(_auth.CurrentSession?.UserId ?? string.Empty);
            var teamNotices = myTeams.Count > 0
                ? await _teams.GetNoticesForTeamsAsync(myTeams)
                : [];

            var merged = schoolNotices
                .Select(a => new AnnouncementRow(a, CanManageAnnouncements)
                {
                    AccentColor = Color.FromArgb("#178C8C")
                })
                .Concat(classNotices.Select(a => new AnnouncementRow(a, false)
                {
                    // Class notices carry their class colour, so the kinds are
                    // distinguishable without reading the chip.
                    AccentColor = ColourForClass(a.ClassId)
                }))
                .Concat(teamNotices.Select(a => new AnnouncementRow(a, false)
                {
                    // Sport green, matching the sport widget below — a team
                    // notice belongs to that half of the student's life.
                    AccentColor = Color.FromArgb("#8CC63E")
                }))
                // Pinned first, then newest — across both sources, so a pinned
                // class notice can outrank an older school one.
                .OrderByDescending(r => r.IsPinned)
                .ThenByDescending(r => r.Source.PostedAt)
                .ToList();

            // Capped: this is a dashboard summary, and a term of notices would
            // push every other widget off the screen.
            var shown = merged.Take(6).ToList();
            Announcements = new ObservableCollection<AnnouncementRow>(shown);

            HasAnnouncements = shown.Count > 0;
            AnnouncementsCount = merged.Count.ToString();
            AnnouncementsEmptyMessage = HasAnnouncements
                ? string.Empty
                : "There are no announcements to display.";

            var sport = await _content.GetSportsAsync();
            var sportShown = sport.Take(5).ToList();
            SportsItems = new ObservableCollection<SportsRow>(
                sportShown.Select(i => new SportsRow(i, _content.CanEditSportsItem(i))));

            HasSports = sportShown.Count > 0;
            SportsCount = sport.Count.ToString();
            SportsEmptyMessage = HasSports
                ? string.Empty
                : "No fixtures, results or sports news yet.";
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Dashboard.SchoolContent", ex);
            Announcements = [];
            SportsItems = [];
            HasAnnouncements = false;
            HasSports = false;
            AnnouncementsEmptyMessage = "We couldn't load announcements right now.";
            SportsEmptyMessage = "We couldn't load the sports feed right now.";
        }

        // Its own failure handling, so a broken Newspaper can't blank the
        // notice board above it (or the other way round).
        await LoadNewspaperAsync(_content);
    }


    /// <summary>
    /// The chosen expiry as an instant, or default for "never".
    ///
    /// End of the chosen day, not midnight at its start — a teacher picking
    /// "12 September" means the notice is still up ON the 12th, not that it
    /// vanishes as the 12th begins.
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
        if (row is null || !CanManageAnnouncements) return;

        _editingAnnouncementId = row.Id;
        IsEditingAnnouncement = true;
        NewAnnouncementTitle = row.Source.Title;
        NewAnnouncementBody = row.Source.Body;
        NewAnnouncementPinned = row.Source.IsPinned;
        NewAnnouncementPublic = row.Source.IsPublic;
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
            StatusMessage = "Give the announcement a title.";
            return;
        }

        // Save and reload are separate: if the save succeeds but the reload
        // fails, resetting the form here would clear the editing ID and a retry
        // would create a duplicate.
        try
        {
            if (IsEditingAnnouncement)
            {
                await _content.UpdateAnnouncementAsync(
                    _editingAnnouncementId, NewAnnouncementTitle, NewAnnouncementBody,
                    NewAnnouncementPinned, NewAnnouncementPublic, ResolveExpiry());
            }
            else
            {
                await _content.CreateAnnouncementAsync(
                    NewAnnouncementTitle, NewAnnouncementBody,
                    NewAnnouncementPinned, NewAnnouncementPublic, ResolveExpiry());
            }

            ResetAnnouncementForm();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            StatusMessage = "Only administrators can post announcements.";
            return;
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Dashboard.SaveAnnouncement", ex);
            StatusMessage = "We couldn't save that. Please try again.";
            return;
        }

        await LoadSchoolContentAsync();
    }

    [RelayCommand]
    private async Task DeleteAnnouncementAsync(AnnouncementRow? row)
    {
        if (row is null || !CanManageAnnouncements) return;

        try
        {
            await _content.DeleteAnnouncementAsync(row.Id);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            StatusMessage = "Only administrators can delete announcements.";
            return;
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Dashboard.DeleteAnnouncement", ex);
            StatusMessage = "We couldn't delete that. Please try again.";
            return;
        }

        await LoadSchoolContentAsync();
    }

    private void ResetAnnouncementForm()
    {
        _editingAnnouncementId = string.Empty;
        IsEditingAnnouncement = false;
        IsAnnouncementFormVisible = false;
        NewAnnouncementTitle = string.Empty;
        NewAnnouncementBody = string.Empty;
        NewAnnouncementPinned = false;
        NewAnnouncementPublic = false;
        NewAnnouncementExpires = false;
        NewAnnouncementExpires = true;
        NewAnnouncementExpiryDate = DateTime.Today.AddDays(14);
    }

    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = string.Empty;
        NeedsEmailVerification = false;
        try
        {
            // Browsing without an account: show the same public surface a
            // Visitor gets. The Firestore rules enforce this independently —
            // an unauthenticated caller can only read public content.
            if (!_auth.IsSignedIn)
            {
                IsGuest = true;
                HasTeacherTools = false;
                HasAdminTools = false;
                _readsNewspaper = false;
                BuildRail();
                ClearSchoolWork();
                DisplayName = "Guest";
                Initials = InitialsFor("Guest");
                RoleLabel = "Visitor";
                Sections = new ObservableCollection<string>(SectionsFor(Role.Visitor));

                // Visitors still get the public surface: sport is public, and
                // announcements marked IsPublic are readable. The rules filter
                // the rest out, so this returns only what they're allowed.
                await LoadSchoolContentAsync();
                return;
            }

            IsGuest = false;
            var profile = await _userProfileService.GetOrCreateCurrentProfileAsync();
            if (profile is null)
            {
                StatusMessage = "We couldn't load your profile. Please sign in again.";
                return;
            }

            DisplayName = profile.DisplayName;
            Initials = InitialsFor(profile.DisplayName);
            HasTeacherTools = profile.HasRole(Role.Teacher);
            HasAdminTools = profile.HasRole(Role.Admin);
            _readsNewspaper = ReadsNewspaper(profile);
            BuildRail();

            // Show every role held, not just the primary one.
            var held = profile.AllRoles();
            RoleLabel = string.Join(" · ", held);

            // Union of the sections for each role, first-seen order preserved,
            // so a Student who also writes for the newspaper sees both sets.
            Sections = new ObservableCollection<string>(
                held.SelectMany(SectionsFor).Distinct());

            await LoadSchoolWorkAsync(profile);

            // School-wide feeds, loaded after the student's own content so a
            // slow notice board doesn't delay their timetable appearing.
            await LoadSchoolContentAsync();
        }
        catch (ProfileVerificationRequiredException)
        {
            // Not an error — their account just isn't finished yet.
            NeedsEmailVerification = true;
            DisplayName = "Almost there";
            // Not InitialsFor(DisplayName) — "AT" on the avatar would read as a
            // person's initials. The account has no confirmed name yet.
            Initials = "?";
            RoleLabel = string.Empty;
            Sections = [];
            ClearSchoolWork();
        }
        catch (InvalidOperationException ex)
        {
            // Actionable setup/configuration problems (bad roster entry, missing
            // Firebase key) carry a message worth showing verbatim.
            StatusMessage = ex.Message;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "That took too long. Check your connection and try again.";
        }
        catch (HttpRequestException)
        {
            StatusMessage = "We couldn't reach the server. Check your connection and try again.";
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Dashboard", ex);
            StatusMessage = "We couldn't load your dashboard. Please try again.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Pulls the student's real timetable and work. Deliberately does NOT throw:
    /// a failure here shouldn't blank the whole dashboard, since the roles and
    /// navigation above it are still perfectly usable.
    /// </summary>
    private async Task LoadSchoolWorkAsync(UserProfile profile)
    {
        // Only students have enrolment. A teacher's own classes live on their
        // dashboard; a parent's children's work needs a rules change first
        // (isClassStudent checks request.auth.uid, so a parent is denied).
        if (!profile.HasRole(Role.Student))
        {
            ClearSchoolWork();
            return;
        }

        try
        {
            var myClasses = await _classes.GetMyEnrolledClassesAsync();
            HasClasses = myClasses.Count > 0;

            if (!HasClasses)
            {
                ClearSchoolWork();
                ShowNotEnrolledNote = true;
                ClassesSummary = "You're not enrolled in any classes yet. Your timetable and "
                               + "homework appear here once a teacher adds you to a class.";
                return;
            }

            ClassesSummary = myClasses.Count == 1
                ? $"1 class · {myClasses[0].Name}"
                : $"{myClasses.Count} classes · {string.Join(", ", myClasses.Select(c => c.Name))}";

            // Timetable is school-wide; keep only the periods for my classes.
            var myClassIds = myClasses.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
            var allPeriods = await _classes.GetTimetableAsync();

            // ClassName on a timetable entry is denormalised at write time, so a
            // class renamed afterwards would still show its old name. Prefer the
            // live class, falling back to the stored copy.
            var classesById = myClasses.ToDictionary(c => c.Id, StringComparer.Ordinal);

            // Assign each class a banner colour up front, so the card grid, the
            // timetable spines and the "today" rail all agree. Ordering is by
            // class ID rather than enumeration order, so a class added later
            // doesn't reshuffle every other subject's colour.
            var palette = BannerPalette;
            var colourByClassId = myClasses
                .OrderBy(c => c.Id, StringComparer.Ordinal)
                .Select((c, i) => (c.Id, Colour: palette[i % palette.Count]))
                .ToDictionary(x => x.Id, x => x.Colour, StringComparer.Ordinal);

            // Shared with the announcements feed, which runs after this.
            _enrolledClasses = myClasses.ToList();
            _classColours = colourByClassId;

            // Unawaited: two reads per class, and nothing above depends on it.
            // The widget carries its own empty state until this lands.
            _ = LoadGradesAsync(_enrolledClasses);

            Color ColourFor(string classId) =>
                colourByClassId.TryGetValue(classId, out var col) ? col : palette[0];

            ClassCards = new ObservableCollection<ClassCardRow>(
                myClasses
                    .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
                    .Select(c => new ClassCardRow(
                        c.Id, c.Name, c.Subject, c.TeacherName, c.Room)
                    {
                        BannerColor = ColourFor(c.Id)
                    }));

            // Unawaited, like grades: one COUNT read per class, and the cards
            // are useful before their circles arrive.
            _ = ChatUnread.LoadAllAsync(_chat, ClassCards.Select(c => (c.ClassId, c.Unread)));

            ClassCountLabel = myClasses.Count == 1
                ? "View all classes (1)"
                : $"View all classes ({myClasses.Count})";

            Timetable = new ObservableCollection<StudentPeriodRow>(
                allPeriods
                    .Where(e => myClassIds.Contains(e.ClassId))
                    .OrderBy(e => e.DayOfWeek)
                    .ThenBy(e => e.StartTime, StringComparer.Ordinal)
                    .Select(e => new StudentPeriodRow(
                        e.DayOfWeek, e.StartTime, e.EndTime,
                        classesById.TryGetValue(e.ClassId, out var c) ? c.Name : e.ClassName,
                        classesById.TryGetValue(e.ClassId, out var sc) ? sc.Subject : string.Empty,
                        e.Room)
                    {
                        SpineColor = ColourFor(e.ClassId)
                    }));

            TimetableEmptyMessage = Timetable.Count == 0
                ? "No periods scheduled for your classes yet."
                : string.Empty;

            // The rail shows only today. On a Saturday that's legitimately
            // empty, which is why the label says the day rather than "Today".
            var today = DateTimeOffset.Now.DayOfWeek;
            TodayPeriods = new ObservableCollection<StudentPeriodRow>(
                Timetable.Where(t => t.Day == today));
            HasTodayPeriods = TodayPeriods.Count > 0;
            TodayLabel = $"{today}";

            // Anything more than a week past due is history, not homework —
            // otherwise last term's work permanently tops the list.
            var cutoff = DateTimeOffset.Now.AddDays(-7);

            var work = await _classes.GetAssignmentsForAsync(myClasses);

            // Tests and exams ARE included now. They can't be handed in, but a
            // student needs to see them coming more than they need to see any
            // homework — it's the one thing on this screen they can't catch up
            // on afterwards.
            //
            // They follow a different lifetime, though: an assessment vanishes
            // once its date has PASSED (you sat it, it's done), whereas homework
            // lingers a week in Overdue because you can still hand it in late.
            var now = DateTimeOffset.Now;

            var open = work
                .Where(w => w.Assignment.Kind.IsSubmittable()
                    ? w.Assignment.DueDate >= cutoff
                    : w.Assignment.DueDate >= now)
                .OrderBy(w => w.Assignment.DueDate)
                .ToList();

            // Drop anything already handed in. "Work to do" that still lists
            // finished work isn't a to-do list, and a student can't tell at a
            // glance what's actually left.
            //
            // One read per open assignment. Bounded by what's shown — a term's
            // back-catalogue is already excluded by the cutoff above — but it is
            // the most expensive thing on this screen, so it's worth watching.
            // Only submittable work can have been submitted, so don't spend a
            // read per exam asking about a submission that cannot exist.
            var submitted = await SubmittedAssignmentIdsAsync(
                open.Where(w => w.Assignment.Kind.IsSubmittable()).ToList());

            var rows = open
                .Where(w => !submitted.Contains(w.Assignment.Id))
                .Select(w => new StudentWorkRow(
                    w.ClassId, w.Assignment.Id, w.Assignment.Title, w.ClassName,
                    w.Assignment.Kind, w.Assignment.DueDate, w.Assignment.Points))
                .ToList();

            // Partition rather than filter twice: a row is overdue or it isn't,
            // and evaluating IsOverdue once keeps both lists consistent even if
            // the clock ticks past a due time mid-load.
            // Three buckets, not two. An assessment is neither overdue (you sat
            // it) nor ordinary upcoming work (you can't hand it in), and burying
            // an exam among homework is how it gets missed.
            // Named `sittings` rather than `assessments`: the latter would shadow
            // the [ObservableProperty] backing field of the same name, which
            // compiles but reads as a bug every time you come back to it.
            var sittings = rows.Where(r => r.IsAssessment).ToList();
            var handIn = rows.Where(r => !r.IsAssessment).ToList();

            var late = handIn.Where(r => r.DueDate < now).ToList();
            var soon = handIn.Where(r => r.DueDate >= now).ToList();

            OverdueWork = new ObservableCollection<StudentWorkRow>(late);

            HasOverdueWork = late.Count > 0;
            OverdueCount = late.Count.ToString();

            // Soonest first, and NOT day-filtered — same reasoning as overdue.
            // An exam three weeks out still matters on a Tuesday, and hiding it
            // behind a day tab is exactly how it gets forgotten.
            Assessments = new ObservableCollection<StudentWorkRow>(
                sittings.OrderBy(r => r.DueDate));

            HasAssessments = sittings.Count > 0;
            AssessmentsCount = sittings.Count.ToString();

            // Cached unfiltered; ApplyDayFilter below sets UpcomingWork and its
            // count from this, honouring whichever day is selected.
            _allUpcoming = soon;

            WorkEmptyMessage = rows.Count == 0
                ? "Nothing due right now."
                : string.Empty;

            // Runs last: it reads Timetable, HasOverdueWork and _allUpcoming,
            // so all three have to be populated first.
            ApplyDayFilter();
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Dashboard.SchoolWork", ex);
            ClearSchoolWork();
            // No pull-to-refresh on this page; reopening it is what reloads.
            SchoolWorkError = "We couldn't load your timetable and homework. "
                            + "Check your connection, then reopen this screen.";
        }
    }

    /// <summary>
    /// Unfiltered upcoming work, kept so changing the day selector refilters in
    /// memory rather than refetching.
    ///
    /// Overdue is deliberately NOT part of this — late work stays visible
    /// whatever day is selected, because hiding it behind a day tab is how it
    /// gets forgotten.
    /// </summary>
    /// <summary>
    /// Tests and exams, in their own section above the dated list.
    ///
    /// Deliberately NOT filtered by the day picker, like overdue work: an exam
    /// three weeks out still matters on a Tuesday.
    /// </summary>
    [ObservableProperty] private ObservableCollection<StudentWorkRow> assessments = [];

    [ObservableProperty] private bool hasAssessments;
    [ObservableProperty] private string assessmentsCount = "0";

    private List<StudentWorkRow> _allUpcoming = [];

    /// <summary>
    /// The student's classes, kept so the announcements feed can read each
    /// class's notices without re-fetching the enrolment.
    /// </summary>
    private List<SchoolClass> _enrolledClasses = [];

    /// <summary>Class ID to banner colour, so notices match their class card.</summary>
    private Dictionary<string, Color> _classColours = new(StringComparer.Ordinal);

    private Color ColourForClass(string classId)
        => _classColours.TryGetValue(classId, out var colour) ? colour : Color.FromArgb("#178C8C");

    /// <summary>
    /// Applies the day selector to both the period list and the work list, so
    /// the two always describe the same scope. Showing Tuesday's lessons beside
    /// the whole week's homework would be actively misleading.
    ///
    /// Called on load and whenever SelectedTimetableDay changes.
    /// </summary>
    private void ApplyDayFilter()
    {
        var scope = SelectedTimetableDay;
        TimetableScopeLabel = scope;

        // ---- periods ----
        var periods = IsWholeWeek
            ? Timetable.ToList()
            : Timetable.Where(t => string.Equals(t.DayLabel, scope, StringComparison.Ordinal)).ToList();

        VisiblePeriods = new ObservableCollection<StudentPeriodRow>(periods);
        HasVisiblePeriods = periods.Count > 0;

        PeriodsEmptyMessage = periods.Count > 0
            ? string.Empty
            : IsWholeWeek
                ? "No periods scheduled for your classes yet."
                : $"No lessons scheduled for {scope}.";

        // ---- work ----
        var work = IsWholeWeek
            ? _allUpcoming
            : _allUpcoming
                .Where(w => string.Equals(w.DueDate.ToLocalTime().DayOfWeek.ToString(), scope,
                                          StringComparison.Ordinal))
                .ToList();

        UpcomingWork = new ObservableCollection<StudentWorkRow>(work);
        HasUpcomingWork = work.Count > 0;
        UpcomingCount = work.Count.ToString();

        UpcomingRangeLabel = !IsWholeWeek
            ? $"Due {scope}"
            : work.Count == 0
                ? "Coming up"
                : $"{DateTimeOffset.Now:d MMMM} – {work.Max(r => r.DueDate).ToLocalTime():d MMMM}";

        // ---- "all caught up" ----
        // Only claim this when there was something to be caught up ON. A student
        // with nothing assigned all term hasn't achieved anything, and telling
        // them they have is hollow.
        var nothingOutstanding = work.Count == 0 && !HasOverdueWork;
        var hasSomethingToTrack = _allUpcoming.Count > 0 || HasClasses;

        IsAllCaughtUp = nothingOutstanding && hasSomethingToTrack;

        CaughtUpMessage = _allUpcoming.Count == 0
            ? "Nothing has been set yet."
            : IsWholeWeek
                ? "All caught up — nothing outstanding this week."
                : $"All caught up — nothing due on {scope}.";
    }

    // ==================== my grades ====================

    /// <summary>
    /// Every mark this student has, across every class they're enrolled in.
    /// Newest first — a student opening this wants the mark that just landed,
    /// not the one from February.
    /// </summary>
    [ObservableProperty] private ObservableCollection<GradeRow> myGrades = [];

    [ObservableProperty] private bool hasGrades;
    [ObservableProperty] private string gradesCount = "0";
    [ObservableProperty] private string gradesEmptyMessage = string.Empty;

    /// <summary>
    /// "68% average across 12 marks". Only counts work that actually carries a
    /// percentage: an unmarked or zero-total item would drag the average
    /// towards a number that means nothing.
    /// </summary>
    [ObservableProperty] private string gradeAverageLabel = string.Empty;

    [ObservableProperty] private bool hasGradeAverage;

    /// <summary>
    /// Builds the grades widget. Costs two reads per class, so it runs
    /// unawaited after the rest of the dashboard is on screen — same treatment
    /// as the teacher's marking queue, and for the same reason.
    ///
    /// A class that fails to read is skipped rather than thrown: a partial
    /// grades list is far better than a dashboard with an error banner over it.
    /// </summary>
    private async Task LoadGradesAsync(IReadOnlyList<SchoolClass> classes)
    {
        if (classes.Count == 0)
        {
            MyGrades = [];
            _allGrades = [];
            HasGrades = false;
            GradesCount = "0";
            HasGradeAverage = false;
            GradeAverageLabel = string.Empty;
            GradesEmptyMessage = string.Empty;
            return;
        }

        var rows = new List<GradeRow>();

        foreach (var schoolClass in classes)
        {
            try
            {
                var assignments = await _classes.GetAssignmentsAsync(schoolClass.Id);
                var submissions = await _classes.GetMySubmissionsForClassAsync(schoolClass.Id);

                var byAssignmentId = submissions.ToDictionary(
                    x => x.AssignmentId, x => x.Submission, StringComparer.Ordinal);

                foreach (var assignment in assignments.Where(a => a.Kind.IsSubmittable()))
                {
                    if (!byAssignmentId.TryGetValue(assignment.Id, out var submission)) continue;
                    if (!submission.IsMarked) continue;

                    // An auto-marked score is out of the multiple-choice total,
                    // which is lower than the assignment total whenever there's
                    // also a written answer. Using the assignment total there
                    // would understate the mark.
                    var outOf = submission.IsAutoMarked && submission.AutoScoreMax > 0
                        ? submission.AutoScoreMax
                        : assignment.Points;

                    rows.Add(new GradeRow(
                        assignment.Title, assignment.Kind, submission.Grade, outOf, assignment.DueDate)
                    {
                        ClassId = schoolClass.Id,
                        ClassName = string.IsNullOrWhiteSpace(schoolClass.Name)
                            ? schoolClass.Subject
                            : schoolClass.Name
                    });
                }
            }
            catch (Exception ex)
            {
                CrashLogger.Log($"Dashboard.Grades:{schoolClass.Id}", ex);
            }
        }

        // Held unfiltered so changing the class picker refilters in memory
        // rather than refetching two documents per class.
        _allGrades = rows.OrderByDescending(r => r.DueDate).ToList();

        GradeClasses = new ObservableCollection<string>(
            new[] { AllClasses }.Concat(
                _allGrades.Select(r => r.ClassName)
                          .Where(n => !string.IsNullOrWhiteSpace(n))
                          .Distinct(StringComparer.CurrentCultureIgnoreCase)
                          .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)));

        // Reset only if the previous selection no longer exists — a reload
        // shouldn't throw away the subject the student was looking at.
        if (!GradeClasses.Contains(SelectedGradeClass))
            SelectedGradeClass = AllClasses;

        ApplyGradeFilter();
    }

    /// <summary>The "everything" option in the class picker.</summary>
    public const string AllClasses = "All classes";

    private List<GradeRow> _allGrades = [];

    /// <summary>Subjects that actually have a mark, plus "All classes".</summary>
    [ObservableProperty] private ObservableCollection<string> gradeClasses = [AllClasses];

    [ObservableProperty] private string selectedGradeClass = AllClasses;

    partial void OnSelectedGradeClassChanged(string value) => ApplyGradeFilter();

    /// <summary>
    /// Narrows the list to one subject.
    ///
    /// The average follows the filter deliberately: a student looking at
    /// Mathematics wants their Mathematics average, and showing the whole-school
    /// figure under a filtered list would be quietly wrong.
    /// </summary>
    private void ApplyGradeFilter()
    {
        var shown = SelectedGradeClass == AllClasses
            ? _allGrades
            : _allGrades.Where(g =>
                string.Equals(g.ClassName, SelectedGradeClass, StringComparison.CurrentCultureIgnoreCase))
              .ToList();

        MyGrades = new ObservableCollection<GradeRow>(shown);
        HasGrades = shown.Count > 0;
        GradesCount = shown.Count.ToString();

        var scored = shown.Where(r => r.Percentage is not null).ToList();
        HasGradeAverage = scored.Count > 0;
        GradeAverageLabel = HasGradeAverage
            ? $"{scored.Average(r => r.Percentage!.Value):0}% average across " +
              (scored.Count == 1 ? "1 mark" : $"{scored.Count} marks")
            : string.Empty;

        GradesEmptyMessage = HasGrades
            ? string.Empty
            : _allGrades.Count > 0
                ? $"No marks for {SelectedGradeClass} yet."
                : "Nothing marked yet. Marks appear here as your teachers return work.";
    }

    /// <summary>
    /// IDs of assignments this student has already submitted, so they can be
    /// dropped from "Work to do".
    ///
    /// A failed read is treated as "not submitted": showing a student work they
    /// have already handed in is a small annoyance, hiding work they still owe
    /// is a real problem.
    /// </summary>
    private async Task<HashSet<string>> SubmittedAssignmentIdsAsync(
        IReadOnlyList<ClassAssignment> open)
    {
        var done = new HashSet<string>(StringComparer.Ordinal);

        // Grouped by class so each class's submissions are fetched once.
        foreach (var byClass in open.GroupBy(w => w.ClassId))
        {
            try
            {
                var submissions = await _classes.GetMySubmissionsForClassAsync(byClass.Key);
                foreach (var submission in submissions)
                {
                    // A document with no SubmittedAt is a stub, not a hand-in.
                    if (submission.Submission.SubmittedAt != default)
                        done.Add(submission.AssignmentId);
                }
            }
            catch (Exception ex)
            {
                CrashLogger.Log($"Dashboard.SubmittedIds:{byClass.Key}", ex);
            }
        }

        return done;
    }

    private void ClearSchoolWork()
    {
        Timetable = [];
        UpcomingWork = [];
        OverdueWork = [];
        ClassCards = [];
        TodayPeriods = [];
        HasOverdueWork = false;
        HasUpcomingWork = false;
        HasTodayPeriods = false;
        OverdueCount = "0";
        UpcomingCount = "0";
        Assessments = [];
        HasAssessments = false;
        AssessmentsCount = "0";
        UpcomingRangeLabel = string.Empty;
        VisiblePeriods = [];
        HasVisiblePeriods = false;
        PeriodsEmptyMessage = string.Empty;
        IsAllCaughtUp = false;
        CaughtUpMessage = string.Empty;
        _allUpcoming = [];
        MyGrades = [];
        _allGrades = [];
        GradeClasses = [AllClasses];
        SelectedGradeClass = AllClasses;
        HasGrades = false;
        GradesCount = "0";
        HasGradeAverage = false;
        GradeAverageLabel = string.Empty;
        GradesEmptyMessage = string.Empty;
        _enrolledClasses = [];
        _classColours = new Dictionary<string, Color>(StringComparer.Ordinal);
        ClassCountLabel = string.Empty;
        TodayLabel = string.Empty;
        HasClasses = false;
        ShowNotEnrolledNote = false;
        SchoolWorkError = string.Empty;
        ClassesSummary = string.Empty;
        TimetableEmptyMessage = string.Empty;
        WorkEmptyMessage = string.Empty;
    }

    [RelayCommand]
    private async Task ResendVerificationAsync()
    {
        try
        {
            await _auth.SendEmailVerificationAsync();
            StatusMessage = "Verification email sent. Check your inbox, then sign in again.";
        }
        catch
        {
            StatusMessage = "We couldn't send the email. Please try again in a moment.";
        }
    }

    private static IReadOnlyList<string> SectionsFor(Role role) => role switch
    {
        Role.Student => ["Announcements", "Timetable", "Sports", "My Courses & Assignments", "My Grades"],

        // Teachers and admins normally land on their own dashboards; these are
        // only reached if routing falls back here (e.g. a failed profile load).
        Role.Teacher => ["Announcements", "Timetable", "Sports", "My Classes", "Gradebook"],
        Role.Parent => ["Announcements", "Timetable", "Sports", "My Children's Grades"],

        Role.Coach => ["Announcements", "Timetable", "Team Fixtures & Results", "Team Rosters", "Sports News"],

        // Additive: held alongside Student or Teacher, so it contributes the
        // newsroom on top of whatever that role already grants.
        Role.Journalist => ["School Newspaper", "My Articles"],
        Role.Admin => ["Announcements", "Timetable", "Sports", "Manage Classes", "Manage Users", "School Settings"],

        // Visitors and guests get only what the rules let them read.
        Role.Visitor => ["News & Announcements", "Sports Fixtures & Results"],
        _ => []
    };

    /// <summary>
    /// Pull to refresh.
    ///
    /// The try/finally is load-bearing: LoadAsync opens with `if (IsBusy)
    /// return`, so pulling while a load is already running would return without
    /// ever clearing IsRefreshing, and the spinner would hang there forever.
    /// </summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        try { await LoadAsync(); }
        finally { IsRefreshing = false; }
    }
}
