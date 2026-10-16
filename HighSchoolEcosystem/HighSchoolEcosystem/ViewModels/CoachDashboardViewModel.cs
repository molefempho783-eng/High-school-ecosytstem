using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HighSchoolEcosystem.Models;
using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.Services.Interfaces;

namespace HighSchoolEcosystem.ViewModels;

/// <summary>
/// The coach's home screen: sport first, because that's the job.
///
/// Coaches previously landed on the student dashboard and were told they
/// weren't enrolled in any classes. This leads with the fixtures they run and
/// the results they're expected to file.
/// </summary>
public partial class CoachDashboardViewModel : DashboardShellViewModel
{
    private readonly IUserProfileService _profiles;
    private readonly IContentService _content;
    private readonly ITeamService _teams;

    public CoachDashboardViewModel(
        IUserProfileService profiles,
        IContentService content,
        ITeamService teams,
        ISchoolInfoProvider schoolInfo)
    {
        _profiles = profiles;
        _content = content;
        _teams = teams;
        SchoolName = schoolInfo.Current.DisplayName;

        // Built here as well as after load, so a failed profile read leaves a
        // usable rail rather than an empty strip.
        BuildRail();
    }

    /// <summary>Set by the page; see DashboardViewModel for why this split exists.</summary>
    public Action<string>? OnRailAction { get; set; }

    // ---- fixtures ----
    [ObservableProperty] private ObservableCollection<SportsRow> upcoming = [];
    [ObservableProperty] private bool hasUpcoming;
    [ObservableProperty] private string upcomingCount = "0";
    [ObservableProperty] private string upcomingEmptyMessage = string.Empty;

    // ---- results and news ----
    [ObservableProperty] private ObservableCollection<SportsRow> recent = [];
    [ObservableProperty] private bool hasRecent;
    [ObservableProperty] private string recentCount = "0";
    [ObservableProperty] private string recentEmptyMessage = string.Empty;

    /// <summary>
    /// Fixtures whose date has passed but which carry no score yet — the
    /// coach's actual to-do list. Surfacing this is the difference between a
    /// results page that stays current and one that quietly rots.
    /// </summary>
    [ObservableProperty] private ObservableCollection<SportsRow> awaitingResult = [];

    [ObservableProperty] private bool hasAwaitingResult;
    [ObservableProperty] private string awaitingResultCount = "0";

    // ---- announcements ----
    [ObservableProperty] private ObservableCollection<AnnouncementRow> announcements = [];
    [ObservableProperty] private bool hasAnnouncements;
    [ObservableProperty] private string announcementsCount = "0";
    [ObservableProperty] private string announcementsEmptyMessage = string.Empty;

    [ObservableProperty] private bool canPostSports;

    /// <summary>True for admins: school-wide notices are admin-only.</summary>
    [ObservableProperty] private bool canManageAnnouncements;
    [ObservableProperty] private bool hasTeacherTools;
    [ObservableProperty] private bool hasAdminTools;

    /// <summary>
    /// Writes a score onto a match and turns it into a result.
    ///
    /// The score string comes from the page, which prompts for it — free text
    /// rather than two numbers because a cricket score, an athletics placing
    /// and "24 - 17" are all legitimate answers, and forcing two integers would
    /// make the field unusable for half the school's sports.
    ///
    /// Returns nothing: the status message and the reloaded lists are the
    /// feedback. CanManage is re-checked here even though the button is hidden
    /// without it, because a hidden button is a UI convenience and this is the
    /// last place we control before the write.
    /// </summary>
    public async Task RecordResultAsync(SportsRow? row, string? score)
    {
        if (row is null || IsBusy) return;

        if (!row.CanManage)
        {
            StatusMessage = "You can only record results for posts you can edit.";
            return;
        }

        var trimmed = score?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            StatusMessage = "Enter the score to record the result.";
            return;
        }

        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            await _content.SetSportsScoreAsync(row.Id, trimmed);
            StatusMessage = $"Result recorded for {row.Title}.";
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            StatusMessage = "You don't have permission to update that match.";
            return;
        }
        catch (Exception ex)
        {
            CrashLogger.Log("CoachDashboard.RecordResult", ex);
            StatusMessage = "We couldn't save that score. Please try again.";
            return;
        }
        finally { IsBusy = false; }

        // Outside the finally: reloading moves the match from "needs a result"
        // into the recent list, which is the visible confirmation it worked.
        await LoadAsync();
    }

    /// <summary>
    /// Removes a sports post from the coach's own dashboard.
    ///
    /// The confirmation lives in the page, not here — this is the write, and by
    /// the time it's called the decision has been made.
    /// </summary>
    public async Task DeleteSportsAsync(SportsRow? row)
    {
        if (row is null || IsBusy) return;

        if (!row.CanManage)
        {
            StatusMessage = "You can only delete posts you wrote.";
            return;
        }

        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            await _content.DeleteSportsItemAsync(row.Id);
            StatusMessage = $"Removed {row.Title}.";
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            StatusMessage = "You don't have permission to delete that post.";
            return;
        }
        catch (Exception ex)
        {
            CrashLogger.Log("CoachDashboard.DeleteSports", ex);
            StatusMessage = "We couldn't delete that. Please try again.";
            return;
        }
        finally { IsBusy = false; }

        await LoadAsync();
    }

    // ==================== my teams ====================

    /// <summary>
    /// Squads this coach runs. A team is shaped like a class — an owner, a
    /// roster and a notice board — so this widget behaves like the teacher's
    /// "Your classes": a grid you browse, with the real work on the page behind
    /// each tile.
    /// </summary>
    [ObservableProperty] private ObservableCollection<SportsTeam> myTeams = [];

    [ObservableProperty] private bool hasTeams;
    [ObservableProperty] private string teamsCount = "0";
    [ObservableProperty] private string teamsEmptyMessage = string.Empty;

    /// <summary>True for Coach and Admin. Hides the create form for everyone else.</summary>
    [ObservableProperty] private bool canManageTeams;

    // ---- new team form ----

    [ObservableProperty] private bool isTeamFormVisible;
    [ObservableProperty] private string newTeamName = string.Empty;
    [ObservableProperty] private string newTeamSport = string.Empty;
    [ObservableProperty] private string newTeamSeason = string.Empty;

    [RelayCommand]
    private void StartCreateTeam()
    {
        NewTeamName = string.Empty;
        NewTeamSport = string.Empty;
        NewTeamSeason = string.Empty;
        IsTeamFormVisible = true;
    }

    [RelayCommand]
    private void CancelCreateTeam() => IsTeamFormVisible = false;

    [RelayCommand]
    private async Task CreateTeamAsync()
    {
        if (IsBusy) return;
        if (string.IsNullOrWhiteSpace(NewTeamName))
        {
            StatusMessage = "Give the team a name.";
            return;
        }

        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            var name = NewTeamName.Trim();
            await _teams.CreateTeamAsync(name, NewTeamSport, NewTeamSeason);

            IsTeamFormVisible = false;
            await LoadTeamsAsync();
            StatusMessage = $"Created {name}. Open it to pick the squad.";
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            StatusMessage = "You don't have permission to create teams.";
        }
        catch (Exception ex)
        {
            CrashLogger.Log("CoachDashboard.CreateTeam", ex);
            StatusMessage = "We couldn't create that team. Please try again.";
        }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// Loads this coach's teams. Swallows its own failures into an empty state:
    /// a broken team list shouldn't make the fixtures look broken too.
    /// </summary>
    // The rail is built before the teams land, so rebuild once they have —
    // otherwise "My teams" is missing on the first paint of every visit.
    partial void OnHasTeamsChanged(bool value) => BuildRail();

    private async Task LoadTeamsAsync()
    {
        try
        {
            await _teams.PrimeAsync();
            CanManageTeams = _teams.CanManageTeams;

            var mine = await _teams.GetMyTeamsAsync();

            MyTeams = new ObservableCollection<SportsTeam>(mine);
            HasTeams = mine.Count > 0;
            TeamsCount = mine.Count.ToString();
            TeamsEmptyMessage = HasTeams
                ? string.Empty
                : "No teams yet. Create one to pick a squad and post practice times.";
        }
        catch (Exception ex)
        {
            CrashLogger.Log("CoachDashboard.Teams", ex);
            TeamsEmptyMessage = "We couldn't load your teams.";
        }
    }

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

            await _content.PrimeAsync();
            CanPostSports = _content.CanPostSports;
            CanManageAnnouncements = _content.CanManageAnnouncements;

            // Unawaited: the coach came here for fixtures and results, and the
            // team list shouldn't hold those behind another round trip.
            _ = LoadTeamsAsync();

            var all = await _content.GetSportsAsync();
            var rows = all.Select(i => new SportsRow(i, _content.CanEditSportsItem(i))).ToList();

            var upcomingRows = rows.Where(r => r.IsUpcoming).ToList();

            // A fixture in the past with no score is one nobody has written up.
            var awaiting = rows
                .Where(r => r.Source.Kind == SportsItemKind.Fixture
                            && r.Source.EventDate < DateTimeOffset.Now
                            && string.IsNullOrWhiteSpace(r.Source.Score))
                .OrderByDescending(r => r.Source.EventDate)
                .ToList();

            var recentRows = rows
                .Where(r => !r.IsUpcoming && !awaiting.Contains(r))
                .Take(6)
                .ToList();

            Upcoming = new ObservableCollection<SportsRow>(upcomingRows);
            HasUpcoming = upcomingRows.Count > 0;
            UpcomingCount = upcomingRows.Count.ToString();
            UpcomingEmptyMessage = HasUpcoming ? string.Empty : "No fixtures scheduled.";

            AwaitingResult = new ObservableCollection<SportsRow>(awaiting);
            HasAwaitingResult = awaiting.Count > 0;
            AwaitingResultCount = awaiting.Count.ToString();

            Recent = new ObservableCollection<SportsRow>(recentRows);
            HasRecent = recentRows.Count > 0;
            RecentCount = recentRows.Count.ToString();
            RecentEmptyMessage = HasRecent ? string.Empty : "Nothing posted yet.";

            var notices = (await _content.GetAnnouncementsAsync()).Take(4).ToList();
            Announcements = new ObservableCollection<AnnouncementRow>(
                notices.Select(a => new AnnouncementRow(a, false)));
            HasAnnouncements = notices.Count > 0;
            AnnouncementsCount = notices.Count.ToString();
            AnnouncementsEmptyMessage = HasAnnouncements
                ? string.Empty
                : "There are no announcements to display.";

            // Never throws — handles its own failure.
            await LoadNewspaperAsync(_content);
        }
        catch (Exception ex)
        {
            CrashLogger.Log("CoachDashboard.Load", ex);
            StatusMessage = "We couldn't load your dashboard. Pull down to try again.";
        }
        finally
        {
            IsBusy = false;
            IsRefreshing = false;
        }
    }

    private void BuildRail()
    {
        // Same order as the widgets on CoachDashboardPage, top to bottom.
        // Scroll only — Account is the sole exception.
        var items = new List<RailItem>();

        if (HasAwaitingResult)
            items.Add(new(Icons.Warning, "Needs a result", new RelayCommand(() => OnRailAction?.Invoke("needs-result"))));

        items.Add(new(Icons.Groups, "My teams", new RelayCommand(() => OnRailAction?.Invoke("teams"))));

        items.Add(new(Icons.Sport, "Sport", new RelayCommand(() => OnRailAction?.Invoke("sport"))));
        items.Add(new(Icons.Announcement, "Announcements", new RelayCommand(() => OnRailAction?.Invoke("announcements"))));
        items.Add(new(Icons.Newspaper, "Newspaper", new RelayCommand(() => OnRailAction?.Invoke("newspaper"))));

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
