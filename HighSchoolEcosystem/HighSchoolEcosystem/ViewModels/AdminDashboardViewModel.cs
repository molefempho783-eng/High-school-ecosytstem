using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HighSchoolEcosystem.Models;
using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.Services.Firebase;
using HighSchoolEcosystem.Services.Interfaces;

namespace HighSchoolEcosystem.ViewModels;

/// <summary>
/// The Admin home screen. Deliberately distinct from <see cref="DashboardViewModel"/>:
/// admins land on management tools rather than the student/teacher content view.
/// </summary>
public partial class AdminDashboardViewModel : DashboardShellViewModel
{
    private readonly IUserProfileService _userProfileService;
    private readonly IContentService _content;


    /// <summary>True when this admin also holds the Teacher role.</summary>
    [ObservableProperty] private bool isAlsoTeacher;

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



    /// <summary>Set by the page. See DashboardViewModel for why this split exists.</summary>
    public Action<string>? OnRailAction { get; set; }

    /// <summary>
    /// Rebuilds the rail for the roles this user holds. Called once the profile
    /// has loaded, so cross-links only appear for roles they actually have.
    /// </summary>
    private void BuildRail()
    {
        // Same order as the widgets on AdminDashboardPage, top to bottom.
        // Scroll only — Account is the sole exception. Manage users, invites
        // and settings are rows INSIDE the administration widget, so the rail
        // scrolls to the widget and the rows themselves open the pages.
        var items = new List<RailItem>
        {
            new(Icons.Admin, "School administration", new RelayCommand(() => OnRailAction?.Invoke("tools")))
        };

        if (IsAlsoTeacher || HasPersonalView)
            items.Add(new(Icons.OtherRoles, "Your other roles", new RelayCommand(() => OnRailAction?.Invoke("roles"))));

        items.Add(new(Icons.CourseContent, "School content", new RelayCommand(() => OnRailAction?.Invoke("content"))));
        items.Add(new(Icons.Announcement, "Announcements", new RelayCommand(() => OnRailAction?.Invoke("announcements"))));
        items.Add(new(Icons.Sport, "Sport", new RelayCommand(() => OnRailAction?.Invoke("sport"))));
        items.Add(new(Icons.Newspaper, "Newspaper", new RelayCommand(() => OnRailAction?.Invoke("newspaper"))));

        items.Add(new(Icons.Account, "Account",
            new RelayCommand(() => OnRailAction?.Invoke("account"))) { StartsGroup = true });

        RailItems = new ObservableCollection<RailItem>(items);
    }

    public AdminDashboardViewModel(IUserProfileService userProfileService, IContentService content, ISchoolInfoProvider schoolInfo)
    {
        _userProfileService = userProfileService;
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
            var profile = await _userProfileService.GetOrCreateCurrentProfileAsync();
            DisplayName = profile?.DisplayName ?? "Administrator";
            IsAlsoTeacher = profile?.HasRole(Role.Teacher) ?? false;
            ApplyPersonalView(profile);

            // Unawaited: an admin comes here for the tools, and the feed
            // shouldn't hold those behind two network reads.
            _ = LoadSchoolFeedAsync();
        }
        catch (ProfileVerificationRequiredException)
        {
            StatusMessage = "Verify your email address to finish setting up your account.";
        }
        catch (Exception ex)
        {
            CrashLogger.Log("AdminDashboard", ex);
            StatusMessage = "We couldn't load your details. Pull down or reopen to retry.";
        }
        finally
        {
            IsBusy = false;
        }
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
            CrashLogger.Log("AdminDashboard.Announcements", ex);
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
            CrashLogger.Log("AdminDashboard.Sport", ex);
            SportsEmptyMessage = "We couldn't load sport.";
        }

        await LoadNewspaperAsync(_content);
    }
}
