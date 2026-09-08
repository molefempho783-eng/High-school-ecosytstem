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

    public CoachDashboardViewModel(
        IUserProfileService profiles,
        IContentService content,
        ISchoolInfoProvider schoolInfo)
    {
        _profiles = profiles;
        _content = content;
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

            await _content.PrimeAsync();
            CanPostSports = _content.CanPostSports;

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
        var items = new List<RailItem>
        {
            new("\U0001F3C6", "Sport", new RelayCommand(() => OnRailAction?.Invoke("sport"))),
            new("\U0001F4E2", "Announcements", new RelayCommand(() => OnRailAction?.Invoke("announcements")))
            { StartsGroup = true }
        };

        if (HasTeacherTools)
        {
            items.Add(new("\U0001F4D8", "Teacher tools",
                new RelayCommand(() => OnRailAction?.Invoke("teacher"))) { StartsGroup = true });
        }

        if (HasAdminTools)
        {
            items.Add(new("⚙", "Admin tools",
                new RelayCommand(() => OnRailAction?.Invoke("admin")))
            { StartsGroup = !HasTeacherTools });
        }

        items.Add(new("●", "Account",
            new RelayCommand(() => OnRailAction?.Invoke("account"))) { StartsGroup = true });

        RailItems = new ObservableCollection<RailItem>(items);
    }

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync();
}
