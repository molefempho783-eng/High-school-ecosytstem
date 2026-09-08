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
}

/// <summary>
/// The Teacher home screen — their own classes plus the tools to run them.
/// Distinct from the student/parent dashboard, like the admin one.
/// </summary>
public partial class TeacherDashboardViewModel : DashboardShellViewModel
{
    private readonly IUserProfileService _profiles;
    private readonly IClassService _classes;


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

    [ObservableProperty] private ObservableCollection<TeacherClassRow> myClasses = [];


    /// <summary>Set by the page. See DashboardViewModel for why this split exists.</summary>
    public Action<string>? OnRailAction { get; set; }

    /// <summary>
    /// Rebuilds the rail for the roles this user holds. Called once the profile
    /// has loaded, so cross-links only appear for roles they actually have.
    /// </summary>
    private void BuildRail()
    {
        var items = new List<RailItem>
        {
            // "classes" and "timetable" are sections on this page, so they
            // scroll. The manage/edit entries below open the full editors.
            new("\u2714", "To mark", new RelayCommand(() => OnRailAction?.Invoke("marking"))),
            new("\U0001F4D8", "My classes", new RelayCommand(() => OnRailAction?.Invoke("classes"))),
            new("\U0001F5D3", "Timetable", new RelayCommand(() => OnRailAction?.Invoke("timetable"))),
            new("✎", "Edit timetable", new RelayCommand(() => OnRailAction?.Invoke("edit-timetable"))),
            new("⚙", "Manage classes", new RelayCommand(() => OnRailAction?.Invoke("manage-classes"))),
            new("\U0001F4E2", "Announcements", new RelayCommand(() => OnRailAction?.Invoke("announcements"))){ StartsGroup = true },
            new("\U0001F3C6", "Sport", new RelayCommand(() => OnRailAction?.Invoke("sport")))
        };

        // Cross-links only for roles actually held, so nobody sees a door
        // they can't open.
        if (IsAlsoAdmin)
        {
            items.Add(new("\u2699", "Admin tools",
                new RelayCommand(() => OnRailAction?.Invoke("admin"))) { StartsGroup = true });
        }

        if (HasPersonalView)
        {
            items.Add(new("\U0001F464", PersonalViewLabel,
                new RelayCommand(() => OnRailAction?.Invoke("personal")))
            { StartsGroup = !IsAlsoAdmin });
        }

        items.Add(new("\u25CF", "Account",
            new RelayCommand(() => OnRailAction?.Invoke("account"))) { StartsGroup = true });

        RailItems = new ObservableCollection<RailItem>(items);
    }

    public TeacherDashboardViewModel(IUserProfileService profiles, IClassService classes, ISchoolInfoProvider schoolInfo)
    {
        _profiles = profiles;
        _classes = classes;
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

            await LoadTimetableAsync(mine, colourByClassId);
            // NOT awaited: this is a read per class plus one per recent
            // assignment, and holding IsBusy for it would leave the whole
            // dashboard behind a spinner while classes and the timetable are
            // already loaded and ready to show. The "Checking..." placeholder
            // in the widget exists precisely to cover this gap.
            _ = LoadMarkingQueueAsync(mine);

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

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync();
}
