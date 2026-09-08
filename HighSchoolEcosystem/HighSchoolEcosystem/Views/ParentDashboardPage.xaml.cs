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
            case "children": await ScrollToAsync(ChildrenSection); break;
            case "sport": await ScrollToAsync(SportsSection); break;
            case "announcements": await ScrollToAsync(AnnouncementsSection); break;
            case "teacher": await PushAsync<TeacherDashboardPage>(); break;
            case "admin": await PushAsync<AdminDashboardPage>(); break;
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
}
