using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HighSchoolEcosystem.Models;
using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.Services.Firebase;
using HighSchoolEcosystem.Services.Interfaces;

namespace HighSchoolEcosystem.ViewModels;

public sealed record TeacherClassRow(string Id, string Name, string Subject, int StudentCount)
{
    public string Subtitle => StudentCount == 1
        ? $"{Subject} · 1 student"
        : $"{Subject} · {StudentCount} students";

    /// <summary>Subject leads on the tile, falling back to the class name.</summary>
    public string Heading => string.IsNullOrWhiteSpace(Subject) ? Name : Subject;

    public string CountLabel => StudentCount == 1 ? "1 student" : $"{StudentCount} students";

    /// <summary>
    /// Banner colour, assigned by the view model so a teacher's class tiles use
    /// the same palette a student sees on their own class grid.
    /// </summary>
    public Color BannerColor { get; init; } = Colors.Transparent;

    /// <summary>Unread class-chat messages; fills in after the tile is shown.</summary>
    public ChatUnread Unread { get; } = new();
}

/// <summary>
/// The Teacher home screen — their own classes plus the tools to run them.
/// Distinct from the student/parent dashboard, like the admin one.
/// </summary>
public partial class TeacherDashboardViewModel : DashboardShellViewModel
{
    private readonly IUserProfileService _profiles;
    private readonly IClassService _classes;
    private readonly IContentService _content;
    private readonly IClassChatService _chat;
    private readonly IConversationService _conversations;

    /// <summary>
    /// The classes this teacher owns, cached from the last load so the
    /// announcements feed can ask for their notices without a second fetch.
    /// </summary>
    private List<SchoolClass> _myClasses = [];


    /// <summary>True when this teacher also holds the Admin role.</summary>
    [ObservableProperty] private bool isAlsoAdmin;

    /// <summary>
    /// True when this user also holds a role whose content lives on the normal
    /// dashboard — Student, Parent, Coach or Visitor. A teacher can be a student
    /// too, and that half of their account has to stay reachable.
    /// </summary>
    [ObservableProperty] private bool hasPersonalView;

    /// <summary>Labels the button with whichever of those roles they hold.</summary>
    [ObservableProperty] private string personalViewLabel = "Switch to my school view";

    private void ApplyPersonalView(UserProfile? profile)
    {
        if (profile is null)
        {
            HasPersonalView = false;
            BuildRail();
            return;
        }

        var personal = new[] { Role.Student, Role.Parent, Role.Coach, Role.Visitor }
            .Where(profile.HasRole)
            .ToList();

        HasPersonalView = personal.Count > 0;
        PersonalViewLabel = personal.Count == 1
            ? $"Switch to {personal[0].ToString().ToLowerInvariant()} view"
            : "Switch to my school view";
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasClasses))]
    private ObservableCollection<TeacherClassRow> myClasses = [];

    /// <summary>
    /// Gates the "post a class notice" button. A teacher with no classes has
    /// nowhere to post one, so offering it would be a dead end.
    /// </summary>
    public bool HasClasses => MyClasses.Count > 0;


    /// <summary>Set by the page. See DashboardViewModel for why this split exists.</summary>
    public Action<string>? OnRailAction { get; set; }

    /// <summary>
    /// Rebuilds the rail for the roles this user holds. Called once the profile
    /// has loaded, so cross-links only appear for roles they actually have.
    /// </summary>
    private void BuildRail()
    {
        // Same order as the widgets on TeacherDashboardPage, top to bottom.
        // Scroll only — Account is the sole exception.
        var items = new List<RailItem>
        {
            new(Icons.Teaching, "Teaching", new RelayCommand(() => OnRailAction?.Invoke("tools")))
        };

        if (IsAlsoAdmin || HasPersonalView)
            items.Add(new(Icons.OtherRoles, "Your other roles", new RelayCommand(() => OnRailAction?.Invoke("roles"))));

        items.Add(new(Icons.Timetable, "Timetable", new RelayCommand(() => OnRailAction?.Invoke("timetable"))));
        items.Add(new(Icons.Marking, "To mark", new RelayCommand(() => OnRailAction?.Invoke("marking"))));
        items.Add(new(Icons.Classes, "Your classes", new RelayCommand(() => OnRailAction?.Invoke("classes"))));
        items.Add(new(Icons.Chat, "Messages", new RelayCommand(() => OnRailAction?.Invoke("messages"))));
        items.Add(new(Icons.Announcement, "Announcements", new RelayCommand(() => OnRailAction?.Invoke("announcements"))));
        items.Add(new(Icons.Sport, "Sport", new RelayCommand(() => OnRailAction?.Invoke("sport"))));
        items.Add(new(Icons.Newspaper, "Newspaper", new RelayCommand(() => OnRailAction?.Invoke("newspaper"))));

        items.Add(new(Icons.Account, "Account",
            new RelayCommand(() => OnRailAction?.Invoke("account"))) { StartsGroup = true });

        RailItems = new ObservableCollection<RailItem>(items);
    }

    public TeacherDashboardViewModel(IUserProfileService profiles, IClassService classes, IContentService content,
        ISchoolInfoProvider schoolInfo, IClassChatService chat, IConversationService conversations)
    {
        _chat = chat;
        _conversations = conversations;
        _profiles = profiles;
        _classes = classes;
        _content = content;
        SchoolName = schoolInfo.Current.DisplayName;

        // Built here as well as after load, so a failed profile read leaves a
        // usable rail rather than an empty strip.
        BuildRail();
    }

    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            var profile = await _profiles.GetOrCreateCurrentProfileAsync();
            DisplayName = profile?.DisplayName ?? "Teacher";
            IsAlsoAdmin = profile?.HasRole(Role.Admin) ?? false;
            ApplyPersonalView(profile);

            var mine = await _classes.GetMyClassesAsync();
            // Colours assigned by ordering class IDs, matching how the student
            // dashboard does it — so adding a class doesn't reshuffle the rest.
            var palette = BannerPalette;
            var colourByClassId = mine
                .OrderBy(c => c.Id, StringComparer.Ordinal)
                .Select((c, i) => (c.Id, Colour: palette[i % palette.Count]))
                .ToDictionary(x => x.Id, x => x.Colour, StringComparer.Ordinal);

            MyClasses = new ObservableCollection<TeacherClassRow>(
                mine.Select(c => new TeacherClassRow(c.Id, c.Name, c.Subject, c.StudentIds.Count)
                {
                    BannerColor = colourByClassId.TryGetValue(c.Id, out var col) ? col : palette[0]
                }));

            // Unawaited: one COUNT read per class.
            _ = ChatUnread.LoadAllAsync(_chat, MyClasses.Select(c => (c.Id, c.Unread)));

            await LoadTimetableAsync(mine, colourByClassId);
            // NOT awaited: this is a read per class plus one per recent
            // assignment, and holding IsBusy for it would leave the whole
            // dashboard behind a spinner while classes and the timetable are
            // already loaded and ready to show. The "Checking..." placeholder
            // in the widget exists precisely to cover this gap.
            // Cached BEFORE the feed starts: LoadSchoolFeedAsync reads it to
            // fetch this teacher's own class notices.
            _myClasses = mine.ToList();

            _ = LoadMarkingQueueAsync(mine);
            _ = LoadSchoolFeedAsync();
            // Unawaited too: a query for the teacher's threads, and nothing
            // above waits on it.
            _ = LoadMessagesAsync(_conversations);

            if (mine.Count == 0)
                StatusMessage = "You don't have any classes yet. Tap \"My classes\" to create one.";
        }
        catch (ProfileVerificationRequiredException)
        {
            StatusMessage = "Verify your email address to finish setting up your account.";
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            StatusMessage = "You don't have permission to load classes. Teacher access is required.";
        }
        catch (Exception ex)
        {
            CrashLogger.Log("TeacherDashboard", ex);
            StatusMessage = "We couldn't load your classes. Please try again.";
        }
        finally
        {
            IsBusy = false;
            IsRefreshing = false;
        }
    }

    // ==================== marking queue ====================

    /// <summary>
    /// Work handed in and still waiting on this teacher, across every class
    /// they own. Each row carries the orange badge.
    /// </summary>
    [ObservableProperty] private ObservableCollection<MarkingQueueItem> markingQueue = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWorkToMark))]
    [NotifyPropertyChangedFor(nameof(MarkingTotalLabel))]
    private int markingTotal;

    public bool HasWorkToMark => MarkingTotal > 0;

    /// <summary>Capped, because the badge is a fixed-width circle.</summary>
    public string MarkingTotalLabel => MarkingTotal > 99 ? "99+" : MarkingTotal.ToString();

    /// <summary>
    /// Shown while the counts are still coming in, and replaced once they have.
    /// Without it the section reads as "all marked" for the second or two it
    /// takes to find out otherwise, which is exactly the wrong thing to tell a
    /// teacher.
    /// </summary>
    [ObservableProperty] private string markingEmptyMessage = "Checking what needs marking...";

    /// <summary>
    /// Fills the queue. Kept off the class-list path so a slow count never
    /// stops the dashboard painting, and swallows its own failures: a missing
    /// badge is a far smaller harm than an error banner over a dashboard that
    /// otherwise loaded fine.
    /// </summary>
    private async Task LoadMarkingQueueAsync(IReadOnlyList<SchoolClass> classes)
    {
        if (classes.Count == 0)
        {
            MarkingQueue = [];
            MarkingTotal = 0;
            MarkingEmptyMessage = string.Empty;
            return;
        }

        try
        {
            var queue = await _classes.GetMarkingQueueAsync(classes);
            MarkingQueue = new ObservableCollection<MarkingQueueItem>(queue);
            MarkingTotal = queue.Sum(q => q.Count);
            MarkingEmptyMessage = queue.Count == 0
                ? "Nothing waiting. Everything handed in has been marked."
                : string.Empty;
        }
        catch (Exception ex)
        {
            CrashLogger.Log("TeacherDashboard.MarkingQueue", ex);
            MarkingQueue = [];
            MarkingTotal = 0;
            MarkingEmptyMessage = "We couldn't check what needs marking. Open a class to see its submissions.";
        }
    }

    // ==================== timetable ====================

    /// <summary>
    /// Periods for the classes this teacher owns. Same day picker as the
    /// student dashboard, because a teacher checking "what am I teaching
    /// Tuesday" has exactly the question a student has.
    /// </summary>
    private List<StudentPeriodRow> _allPeriods = [];

    public ObservableCollection<string> TimetableDays { get; } =
    [
        WholeWeek, "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"
    ];

    public const string WholeWeek = "Whole week";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWholeWeek))]
    private string selectedTimetableDay = WholeWeek;

    public bool IsWholeWeek => SelectedTimetableDay == WholeWeek;

    partial void OnSelectedTimetableDayChanged(string value) => ApplyDayFilter();

    [ObservableProperty] private ObservableCollection<StudentPeriodRow> visiblePeriods = [];
    [ObservableProperty] private bool hasVisiblePeriods;
    [ObservableProperty] private string periodsEmptyMessage = string.Empty;
    [ObservableProperty] private string periodsCount = "0";

    private async Task LoadTimetableAsync(
        IReadOnlyList<SchoolClass> mine, IReadOnlyDictionary<string, Color> colourByClassId)
    {
        try
        {
            // The timetable is school-wide; keep only the periods for classes
            // this teacher owns.
            var mineIds = mine.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
            var byId = mine.ToDictionary(c => c.Id, StringComparer.Ordinal);
            var all = await _classes.GetTimetableAsync();

            _allPeriods = all
                .Where(e => mineIds.Contains(e.ClassId))
                .OrderBy(e => e.DayOfWeek)
                .ThenBy(e => e.StartTime, StringComparer.Ordinal)
                .Select(e => new StudentPeriodRow(
                    e.DayOfWeek, e.StartTime, e.EndTime,
                    // Prefer the live class over the denormalised copy, so a
                    // rename shows through.
                    byId.TryGetValue(e.ClassId, out var c) ? c.Name : e.ClassName,
                    byId.TryGetValue(e.ClassId, out var sc) ? sc.Subject : string.Empty,
                    e.Room)
                {
                    SpineColor = colourByClassId.TryGetValue(e.ClassId, out var col)
                        ? col
                        : Color.FromArgb("#178C8C")
                })
                .ToList();

            ApplyDayFilter();
        }
        catch (Exception ex)
        {
            // A failed timetable read shouldn't blank the class list, which
            // loaded fine.
            CrashLogger.Log("TeacherDashboard.Timetable", ex);
            _allPeriods = [];
            ApplyDayFilter();
            PeriodsEmptyMessage = "We couldn't load your timetable right now.";
        }
    }

    private void ApplyDayFilter()
    {
        var scope = SelectedTimetableDay;

        var periods = IsWholeWeek
            ? _allPeriods
            : _allPeriods.Where(p => string.Equals(p.DayLabel, scope, StringComparison.Ordinal)).ToList();

        VisiblePeriods = new ObservableCollection<StudentPeriodRow>(periods);
        HasVisiblePeriods = periods.Count > 0;
        PeriodsCount = periods.Count.ToString();

        PeriodsEmptyMessage = periods.Count > 0
            ? string.Empty
            : IsWholeWeek
                ? "No periods scheduled for your classes yet. Tap \"Timetable\" to add some."
                : $"Nothing scheduled for {scope}.";
    }

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

    // ==================== school feed ====================
    //
    // Announcements and sport are on EVERY dashboard, not just the student's.
    // A teacher, a coach and an admin all need to know what the school has been
    // told — and it's also what lets the rail scroll to them instead of pushing
    // a different role's page, which is what it used to do.
    //
    // Read-only here. Composing a school-wide notice stays admin-only on the
    // student dashboard, and posting sport stays on the sports page, because
    // both need more fields than a widget can hold.


    /// <summary>
    /// Mirrors IContentService, which mirrors the rules. UI convenience only —
    /// hiding a button the rules would refuse is politeness, not security.
    /// </summary>
    [ObservableProperty] private bool canPostSports;

    /// <summary>True for admins: school-wide notices are admin-only.</summary>
    [ObservableProperty] private bool canManageAnnouncements;

    [ObservableProperty] private ObservableCollection<AnnouncementRow> announcements = [];
    [ObservableProperty] private bool hasAnnouncements;
    [ObservableProperty] private string announcementsCount = "0";
    [ObservableProperty] private string announcementsEmptyMessage = string.Empty;

    [ObservableProperty] private ObservableCollection<SportsRow> sportsItems = [];
    [ObservableProperty] private bool hasSports;
    [ObservableProperty] private string sportsCount = "0";
    [ObservableProperty] private string sportsEmptyMessage = string.Empty;

    /// <summary>
    /// Loads both feeds. Unawaited by the caller, so a slow read never holds up
    /// the parts of the dashboard this role actually came here for, and a
    /// failure leaves empty widgets rather than an error over the whole page.
    /// </summary>
    private async Task LoadSchoolFeedAsync()
    {
        try
        {
            // Must run before the Can* flags mean anything; they fail closed
            // until it has.
            await _content.PrimeAsync();
            CanPostSports = _content.CanPostSports;
            CanManageAnnouncements = _content.CanManageAnnouncements;
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Dashboard.Prime", ex);
        }

        try
        {
            // School-wide notices. includeExpired for an admin, who manages
            // them from the school dashboard and needs to see why one vanished.
            var schoolNotices = await _content.GetAnnouncementsAsync(
                includeExpired: CanManageAnnouncements);

            // Plus the teacher's OWN class notices. A teacher who posts to their
            // class had no way to see it afterwards without opening the class —
            // which meant no way to notice a typo, or that it had expired.
            //
            // includeExpired is deliberately true here regardless of role: on
            // their own dashboard a teacher is the AUTHOR of these, and the
            // whole point of showing an author a lapsed notice is so they can
            // tell it lapsed and extend it.
            var classNotices = _myClasses.Count > 0
                ? await _classes.GetAnnouncementsForClassesAsync(_myClasses)
                : [];

            var merged = schoolNotices
                .Select(a => new AnnouncementRow(a, CanManageAnnouncements))
                // CanManage is true for class notices because this teacher owns
                // the class — the rules agree, and it's what puts Edit and
                // Delete behind the detail popup's Manage step.
                .Concat(classNotices.Select(a => new AnnouncementRow(a, true)))
                // Pinned first, then newest, across both sources. Sorting each
                // separately would bury a pinned class notice under school news.
                .OrderByDescending(r => r.IsPinned)
                .ThenByDescending(r => r.Source.PostedAt)
                .Take(6)
                .ToList();

            Announcements = new ObservableCollection<AnnouncementRow>(merged);
            HasAnnouncements = merged.Count > 0;
            AnnouncementsCount = merged.Count.ToString();
            AnnouncementsEmptyMessage = HasAnnouncements
                ? string.Empty
                : "There are no announcements to display.";
        }
        catch (Exception ex)
        {
            CrashLogger.Log("TeacherDashboard.Announcements", ex);
            AnnouncementsEmptyMessage = "We couldn't load announcements.";
        }

        try
        {
            var sport = (await _content.GetSportsAsync()).Take(6).ToList();
            SportsItems = new ObservableCollection<SportsRow>(
                sport.Select(i => new SportsRow(i, _content.CanEditSportsItem(i))));
            HasSports = sport.Count > 0;
            SportsCount = sport.Count.ToString();
            SportsEmptyMessage = HasSports ? string.Empty : "Nothing posted yet.";
        }
        catch (Exception ex)
        {
            CrashLogger.Log("TeacherDashboard.Sport", ex);
            SportsEmptyMessage = "We couldn't load sport.";
        }

        await LoadNewspaperAsync(_content);
    }
}
