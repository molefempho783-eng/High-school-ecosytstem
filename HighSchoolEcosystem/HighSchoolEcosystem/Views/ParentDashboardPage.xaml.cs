using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace HighSchoolEcosystem.Views;

public partial class ParentDashboardPage : ContentPage
{
    private readonly ParentDashboardViewModel _viewModel;
    private readonly IServiceProvider _services;

    public ParentDashboardPage(ParentDashboardViewModel viewModel, IServiceProvider services)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _services = services;

        // The view model owns WHICH rail items exist; this owns what they do.
        _viewModel.OnRailAction = async action => await HandleRailAsync(action);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    private async Task HandleRailAsync(string action)
    {
        switch (action)
        {
            // Scroll only. Account is the single exception.
            case "children": await ScrollToAsync(ChildrenSection); break;
            case "messages": await ScrollToAsync(MessagesSection); break;
            case "announcements": await ScrollToAsync(AnnouncementsSection); break;
            case "sport": await ScrollToAsync(SportsSection); break;
            case "newspaper": await ScrollToAsync(NewspaperSection); break;
            case "roles": await ScrollToAsync(RolesSection); break;
            case "account": await PushAsync<AccountPage>(); break;
        }
    }

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
            CrashLogger.Log("ParentDashboard.ScrollTo", ex);
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

    private async void OnSportsTapped(object? sender, EventArgs e)
        => await PushAsync<SportsPage>();

    private async void OnAccountClicked(object? sender, EventArgs e)
        => await PushAsync<AccountPage>();

    /// <summary>
    /// The whole notice plus who posted it. Read-only here: neither a coach nor
    /// a parent can edit school announcements, and offering Manage would be a
    /// button that always fails.
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

    // Cross-role links. These are the only navigation left outside the Account
    // button, and they live in a widget rather than on the rail — see the
    // "Your other roles" card.
    private async void OnTeacherToolsClicked(object? sender, EventArgs e)
        => await PushAsync<TeacherDashboardPage>();

    private async void OnAdminToolsClicked(object? sender, EventArgs e)
        => await PushAsync<AdminDashboardPage>();

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
