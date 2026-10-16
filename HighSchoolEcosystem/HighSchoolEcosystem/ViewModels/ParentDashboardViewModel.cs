using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HighSchoolEcosystem.Models;
using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.Services.Interfaces;

namespace HighSchoolEcosystem.ViewModels;

/// <summary>One of the parent's children, with their work summarised.</summary>
public sealed record ChildSummaryRow(string StudentId, string Name)
{
    public ObservableCollection<StudentWorkRow> Overdue { get; init; } = [];

    /// <summary>Tests and exams, in their own section above the dated list.</summary>
    public ObservableCollection<StudentWorkRow> Assessments { get; init; } = [];

    public bool HasAssessments => Assessments.Count > 0;
    public string AssessmentsCount => Assessments.Count.ToString();

    public ObservableCollection<StudentWorkRow> Upcoming { get; init; } = [];
    public ObservableCollection<GradeRow> RecentGrades { get; init; } = [];

    public bool HasOverdue => Overdue.Count > 0;
    public bool HasUpcoming => Upcoming.Count > 0;
    public bool HasGrades => RecentGrades.Count > 0;

    public string OverdueCount => Overdue.Count.ToString();
    public string UpcomingCount => Upcoming.Count.ToString();

    /// <summary>"3 classes · 72% average", or whichever parts are known.</summary>
    public string MetaLine { get; init; } = string.Empty;

    /// <summary>
    /// True when the child has nothing outstanding. Distinct from having no
    /// classes at all, which gets different wording.
    /// </summary>
    public bool IsAllCaughtUp => Overdue.Count == 0 && Upcoming.Count == 0 && ClassCount > 0;

    public int ClassCount { get; init; }

    public bool HasNoClasses => ClassCount == 0;

    public Color BannerColor { get; init; } = Colors.Transparent;

    public string Initials
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Name)) return "?";
            var words = Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return "?";
            if (words.Length == 1) return words[0][..1].ToUpperInvariant();
            return (words[0][..1] + words[^1][..1]).ToUpperInvariant();
        }
    }
}

/// <summary>
/// The parent's home screen.
///
/// Parents previously landed on the student dashboard and were told they
/// weren't enrolled in any classes, which is true and useless. This shows the
/// children they're linked to instead.
///
/// Everything here is read-only. The rules let a linked parent read their
/// children's submissions and their classes' assignments — see
/// isParentOfClassMember in firestore.rules — and nothing else.
/// </summary>
public partial class ParentDashboardViewModel : DashboardShellViewModel
{
    private readonly IUserProfileService _profiles;
    private readonly IClassService _classes;
    private readonly IContentService _content;
    private readonly ITeamService _teams;

    private readonly IConversationService _conversations;

    public ParentDashboardViewModel(
        IUserProfileService profiles,
        IClassService classes,
        IContentService content,
        ITeamService teams,
        ISchoolInfoProvider schoolInfo,
        IConversationService conversations)
    {
        _conversations = conversations;
        _profiles = profiles;
        _classes = classes;
        _content = content;
        _teams = teams;
        SchoolName = schoolInfo.Current.DisplayName;

        // Built here as well as after load, so a failed profile read leaves a
        // usable rail rather than an empty strip.
        BuildRail();
    }

    /// <summary>Set by the page; see DashboardViewModel for why this split exists.</summary>
    public Action<string>? OnRailAction { get; set; }

    [ObservableProperty] private ObservableCollection<ChildSummaryRow> children = [];
    [ObservableProperty] private bool hasChildren;
    [ObservableProperty] private string childrenEmptyMessage = string.Empty;

    // ---- school feeds, same as every other role sees ----
    [ObservableProperty] private ObservableCollection<AnnouncementRow> announcements = [];
    [ObservableProperty] private bool hasAnnouncements;
    [ObservableProperty] private string announcementsCount = "0";
    [ObservableProperty] private string announcementsEmptyMessage = string.Empty;

    [ObservableProperty] private ObservableCollection<SportsRow> sportsItems = [];
    [ObservableProperty] private bool hasSports;
    [ObservableProperty] private string sportsCount = "0";
    [ObservableProperty] private string sportsEmptyMessage = string.Empty;

    /// <summary>
    /// Every class any of this parent's children is in, deduplicated — two
    /// siblings in the same class shouldn't double its notices.
    /// </summary>
    private readonly List<SchoolClass> _childClasses = [];

    /// <summary>
    /// Teams the children are in, gathered during the child pass so the
    /// announcements feed can pull their notices without refetching.
    /// </summary>
    private readonly List<SportsTeam> _childTeams = [];

    /// <summary>Set when this parent also holds a staff role.</summary>

    /// <summary>
    /// Mirrors IContentService, which mirrors the rules. UI convenience only —
    /// hiding a button the rules would refuse is politeness, not security.
    /// </summary>
    [ObservableProperty] private bool canPostSports;

    /// <summary>True for admins: school-wide notices are admin-only.</summary>
    [ObservableProperty] private bool canManageAnnouncements;

    [ObservableProperty] private bool hasTeacherTools;
    [ObservableProperty] private bool hasAdminTools;

    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            var profile = await _profiles.GetOrCreateCurrentProfileAsync();
            if (profile is null)
            {
                StatusMessage = "We couldn't load your profile. Please sign in again.";
                return;
            }

            DisplayName = profile.DisplayName;
            Initials = InitialsFor(profile.DisplayName);
            RoleLabel = string.Join(" · ", profile.AllRoles());
            HasTeacherTools = profile.HasRole(Role.Teacher);
            HasAdminTools = profile.HasRole(Role.Admin);

            BuildRail();
            await LoadChildrenAsync(profile);
            await LoadMessagesAsync(_conversations);
            await LoadSchoolContentAsync();
        }
        catch (Exception ex)
        {
            CrashLogger.Log("ParentDashboard.Load", ex);
            StatusMessage = "We couldn't load your dashboard. Pull down to try again.";
        }
        finally
        {
            IsBusy = false;
            IsRefreshing = false;
        }
    }

    private async Task LoadChildrenAsync(UserProfile profile)
    {
        if (profile.LinkedStudentIds.Count == 0)
        {
            Children = [];
            HasChildren = false;
            ChildrenEmptyMessage =
                "No children are linked to your account yet. Ask the school office to link them — "
                + "they'll need your email address and your child's.";
            return;
        }

        var palette = BannerPalette;
        var rows = new List<ChildSummaryRow>();
        var index = 0;

        // Rebuilt from scratch: a child unlinked since the last load must not
        // leave their classes' notices in the feed.
        _childClasses.Clear();
        _childTeams.Clear();

        foreach (var studentId in profile.LinkedStudentIds)
        {
            try
            {
                rows.Add(await BuildChildAsync(studentId, palette[index % palette.Count]));
                index++;
            }
            catch (Exception ex)
            {
                // One unreadable child shouldn't blank the others — a link to a
                // student who has since left the school would do exactly that.
                CrashLogger.Log($"ParentDashboard.Child:{studentId}", ex);
            }
        }

        Children = new ObservableCollection<ChildSummaryRow>(rows);
        HasChildren = rows.Count > 0;
        ChildrenEmptyMessage = HasChildren
            ? string.Empty
            : "We couldn't load your children's work right now.";
    }

    private async Task<ChildSummaryRow> BuildChildAsync(string studentId, Color colour)
    {
        // The child's own name, so the card doesn't just say a UID. Falls back
        // to the ID if the directory read fails.
        var name = studentId;
        try
        {
            var child = await _profiles.GetProfileAsync(studentId);
            if (child is not null && !string.IsNullOrWhiteSpace(child.DisplayName))
                name = child.DisplayName;
        }
        catch (Exception ex)
        {
            CrashLogger.Log($"ParentDashboard.ChildName:{studentId}", ex);
        }

        var classes = await _classes.GetClassesForStudentAsync(studentId);

        // Kept so the announcements feed can include notices from the classes
        // this parent's children are actually in. isParentOfClassMember in the
        // rules is what makes that readable.
        // Same idea for teams: isParentOfTeamMember in the rules is what makes
        // a squad's notices readable to this parent.
        try
        {
            foreach (var t in await _teams.GetTeamsForStudentAsync(studentId))
            {
                if (_childTeams.All(existing => existing.Id != t.Id)) _childTeams.Add(t);
            }
        }
        catch (Exception ex)
        {
            // A missing team list must not cost the parent their child's
            // classwork, which is the more important half of this screen.
            CrashLogger.Log($"ParentDashboard.ChildTeams:{studentId}", ex);
        }

        foreach (var c in classes)
        {
            if (_childClasses.All(existing => existing.Id != c.Id)) _childClasses.Add(c);
        }

        var overdue = new List<StudentWorkRow>();
        var upcoming = new List<StudentWorkRow>();
        var sittings = new List<StudentWorkRow>();
        var grades = new List<GradeRow>();

        var now = DateTimeOffset.Now;
        var cutoff = now.AddDays(-7);

        foreach (var schoolClass in classes)
        {
            var assignments = await _classes.GetAssignmentsAsync(schoolClass.Id);

            // Assessments are included, and bucketed separately — a parent needs
            // to know an exam is coming as much as the child does. They keep the
            // same asymmetric lifetime as on the student dashboard: homework
            // lingers a week past its deadline, an exam vanishes once it's sat.
            foreach (var assignment in assignments)
            {
                var isSitting = assignment.Kind.IsAssessment();
                if (!isSitting && !assignment.Kind.IsSubmittable()) continue;

                var floor = isSitting ? now : cutoff;
                if (assignment.DueDate < floor) continue;

                var row = new StudentWorkRow(
                    schoolClass.Id, assignment.Id, assignment.Title, schoolClass.Name,
                    assignment.Kind, assignment.DueDate, assignment.Points);

                if (isSitting) sittings.Add(row);
                else if (assignment.DueDate < now) overdue.Add(row);
                else upcoming.Add(row);
            }

            var submissions = await _classes.GetSubmissionsForStudentAsync(schoolClass.Id, studentId);
            var byId = submissions.ToDictionary(s => s.AssignmentId, s => s.Submission, StringComparer.Ordinal);

            foreach (var assignment in assignments.Where(a => a.Kind.IsSubmittable()))
            {
                if (!byId.TryGetValue(assignment.Id, out var submission) || !submission.IsMarked) continue;

                var outOf = submission.IsAutoMarked && submission.AutoScoreMax > 0
                    ? submission.AutoScoreMax
                    : assignment.Points;

                grades.Add(new GradeRow(assignment.Title, assignment.Kind, submission.Grade, outOf, assignment.DueDate));
            }
        }

        var scored = grades.Where(g => g.Percentage is not null).ToList();
        var meta = new List<string>
        {
            classes.Count == 1 ? "1 class" : $"{classes.Count} classes"
        };
        if (scored.Count > 0) meta.Add($"{scored.Average(g => g.Percentage!.Value):0}% average");

        return new ChildSummaryRow(studentId, name)
        {
            ClassCount = classes.Count,
            BannerColor = colour,
            MetaLine = string.Join(" · ", meta),
            Overdue = new ObservableCollection<StudentWorkRow>(overdue.OrderBy(w => w.DueDate)),
            Assessments = new ObservableCollection<StudentWorkRow>(sittings.OrderBy(w => w.DueDate)),
            Upcoming = new ObservableCollection<StudentWorkRow>(upcoming.OrderBy(w => w.DueDate).Take(5)),
            // Most recent first, capped: this is a summary, not a transcript.
            RecentGrades = new ObservableCollection<GradeRow>(
                grades.OrderByDescending(g => g.DueDate).Take(5))
        };
    }

    private async Task LoadSchoolContentAsync()
    {
        try
        {
            await _content.PrimeAsync();
            CanPostSports = _content.CanPostSports;
            CanManageAnnouncements = _content.CanManageAnnouncements;

            var schoolNotices = await _content.GetAnnouncementsAsync();

            // Class notices from the children's classes, so a parent doesn't
            // have to open each class to find out a test moved.
            var classNotices = _childClasses.Count > 0
                ? await _classes.GetAnnouncementsForClassesAsync(_childClasses)
                : [];

            // Team notices as well. A parent is usually the lift to practice,
            // so "training moved to Thursday" matters to them at least as much
            // as it does to the child.
            var teamNotices = _childTeams.Count > 0
                ? await _teams.GetNoticesForTeamsAsync(_childTeams)
                : [];

            var merged = schoolNotices.Concat(classNotices).Concat(teamNotices)
                .Select(a => new AnnouncementRow(a, false))
                .OrderByDescending(r => r.IsPinned)
                .ThenByDescending(r => r.Source.PostedAt)
                .ToList();

            var notices = merged.Take(6).ToList();
            Announcements = new ObservableCollection<AnnouncementRow>(notices);
            HasAnnouncements = notices.Count > 0;
            AnnouncementsCount = merged.Count.ToString();
            AnnouncementsEmptyMessage = HasAnnouncements
                ? string.Empty
                : "There are no announcements to display.";

            var sport = (await _content.GetSportsAsync()).Take(5).ToList();
            SportsItems = new ObservableCollection<SportsRow>(
                sport.Select(i => new SportsRow(i, false)));
            HasSports = sport.Count > 0;
            SportsCount = sport.Count.ToString();
            SportsEmptyMessage = HasSports
                ? string.Empty
                : "No fixtures, results or sports news yet.";
        }
        catch (Exception ex)
        {
            // A broken notice board must not make the children's work look
            // broken — it loaded fine.
            CrashLogger.Log("ParentDashboard.SchoolContent", ex);
            AnnouncementsEmptyMessage = "We couldn't load announcements right now.";
            SportsEmptyMessage = "We couldn't load the sports feed right now.";
        }

        await LoadNewspaperAsync(_content);
    }

    private void BuildRail()
    {
        // Same order as the widgets on ParentDashboardPage, top to bottom.
        // Scroll only — Account is the sole exception.
        var items = new List<RailItem>
        {
            new(Icons.Children, "My children", new RelayCommand(() => OnRailAction?.Invoke("children"))),
            new(Icons.Chat, "Messages", new RelayCommand(() => OnRailAction?.Invoke("messages"))),
            new(Icons.Announcement, "Announcements", new RelayCommand(() => OnRailAction?.Invoke("announcements"))),
            new(Icons.Sport, "Sport", new RelayCommand(() => OnRailAction?.Invoke("sport"))),
            new(Icons.Newspaper, "Newspaper", new RelayCommand(() => OnRailAction?.Invoke("newspaper")))
        };

        if (HasTeacherTools || HasAdminTools)
            items.Add(new(Icons.OtherRoles, "Your other roles", new RelayCommand(() => OnRailAction?.Invoke("roles"))));

        items.Add(new(Icons.Account, "Account",
            new RelayCommand(() => OnRailAction?.Invoke("account"))) { StartsGroup = true });

        RailItems = new ObservableCollection<RailItem>(items);
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
}
