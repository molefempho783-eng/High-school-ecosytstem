using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace HighSchoolEcosystem.Views;

public partial class AdminDashboardPage : ContentPage
{
    private readonly AdminDashboardViewModel _viewModel;
    private readonly IServiceProvider _services;

    public AdminDashboardPage(AdminDashboardViewModel viewModel, IServiceProvider services)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;

        _viewModel.OnRailAction = async action => await HandleRailAsync(action);
        _services = services;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    private async void OnManageUsersClicked(object? sender, EventArgs e) => await PushAsync<AdminUsersPage>();
    private async void OnInvitesClicked(object? sender, EventArgs e) => await PushAsync<AdminInvitesPage>();
    private async void OnSettingsClicked(object? sender, EventArgs e) => await PushAsync<AdminSettingsPage>();
    private async void OnAccountClicked(object? sender, EventArgs e) => await PushAsync<AccountPage>();

    // The same person may also be a student, parent or coach; that content
    // lives on the shared dashboard.
    private async void OnPersonalViewClicked(object? sender, EventArgs e) => await PushAsync<DashboardPage>();

    // Pushed rather than swapping the root, so the back button returns here.
    private async void OnTeacherToolsClicked(object? sender, EventArgs e) => await PushAsync<TeacherDashboardPage>();

    // These handlers are `async void`, so an escaping exception has nowhere to go.
    private async Task HandleRailAsync(string action)
    {
        switch (action)
        {
            // Scroll only. Account is the single exception.
            case "tools": await ScrollToAsync(ToolsSection); break;
            case "roles": await ScrollToAsync(RolesSection); break;
            case "content": await ScrollToAsync(ContentSection); break;
            case "announcements": await ScrollToAsync(AnnouncementsSection); break;
            case "sport": await ScrollToAsync(SportsSection); break;
            case "newspaper": await ScrollToAsync(NewspaperSection); break;
            case "account": await PushAsync<AccountPage>(); break;
        }
    }

    /// <summary>
    /// Scrolls a widget into view. This page had no such helper until the rail
    /// stopped navigating — every one of its rail items used to open a
    /// different page instead.
    /// </summary>
    private async Task ScrollToAsync(VisualElement? target)
    {
        if (target is null || !target.IsVisible) return;

        try
        {
            await ContentScroll.ScrollToAsync(target, ScrollToPosition.Start, animated: true);
        }
        catch (Exception ex)
        {
            // Scrolling to an element that hasn't been laid out yet throws.
            CrashLogger.Log("AdminDashboard.ScrollTo", ex);
        }
    }

    private async Task PushAsync<TPage>() where TPage : Page
    {
        try
        {
            await Navigation.PushAsync(_services.GetRequiredService<TPage>());
        }
        catch (Exception ex)
        {
            CrashLogger.Log($"Navigate:{typeof(TPage).Name}", ex);
        }
    }

    /// <summary>
    /// The whole notice plus who posted it. Read-only on this dashboard —
    /// school-wide notices are composed with the admin tools.
    /// </summary>
    private async void OnAnnouncementTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not AnnouncementRow row) return;

        try
        {
            await DetailPopup.ShowAsync(
                this, row.Title, row.Source.DetailText,
                row.Source.ScopeLabel, row.AccentColor, canManage: false);
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Dashboard.AnnouncementDetail", ex);
        }
    }

    /// <summary>Full details for one fixture, result or article.</summary>
    private async void OnSportsItemTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not SportsRow row) return;

        try
        {
            await DetailPopup.ShowAsync(
                this, row.Title, row.Source.DetailText,
                row.Source.SportAndTeam, Color.FromArgb("#8CC63E"), canManage: false);
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Dashboard.SportsDetail", ex);
        }
    }

    /// <summary>
    /// Opens the sports page to post a fixture, result or article.
    ///
    /// Navigation, not a popup: a post needs sport, team, opponent, venue, date
    /// and time. The button is hidden unless the rules would accept the write,
    /// which is politeness rather than security — firestore.rules is the
    /// boundary either way.
    /// </summary>
    private async void OnPostSportsClicked(object? sender, EventArgs e)
        => await PushAsync<SportsPage>();

    /// <summary>
    /// Opens the school dashboard, where school-wide announcements are written.
    ///
    /// There is deliberately ONE composer rather than one per dashboard: two
    /// would drift, and an announcement form has enough state (pinned, public,
    /// expiry, edit-in-place) that the drift would be silent.
    /// </summary>
    private async void OnPostAnnouncementClicked(object? sender, EventArgs e)
        => await PushAsync<DashboardPage>();
}
