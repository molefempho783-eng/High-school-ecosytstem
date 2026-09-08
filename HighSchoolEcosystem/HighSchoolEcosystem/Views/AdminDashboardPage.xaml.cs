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
            case "users": await PushAsync<AdminUsersPage>(); break;
            case "invites": await PushAsync<AdminInvitesPage>(); break;
            case "settings": await PushAsync<AdminSettingsPage>(); break;
            // Announcements are composed from the dashboard's own section, so
            // both of these land on the shared dashboard rather than a page
            // that no longer exists.
            case "announcements": await PushAsync<DashboardPage>(); break;
            case "sport": await PushAsync<SportsPage>(); break;
            case "teacher": await PushAsync<TeacherDashboardPage>(); break;
            case "personal": await PushAsync<DashboardPage>(); break;
            case "account": await PushAsync<AccountPage>(); break;
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
}
