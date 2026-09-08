using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace HighSchoolEcosystem.Views;

public partial class AdminUsersPage : ContentPage
{
    private readonly AdminUsersViewModel _viewModel;
    private readonly IServiceProvider _services;

    public AdminUsersPage(AdminUsersViewModel viewModel, IServiceProvider services)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _services = services;
    }

    // Reloads every time, so a role edited on the detail page is reflected on return.
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    private async void OnUserSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not AdminUserRow row) return;

        // Clear immediately so the same row can be tapped again on return.
        if (sender is CollectionView collectionView) collectionView.SelectedItem = null;

        try
        {
            var page = _services.GetRequiredService<AdminUserDetailPage>();
            page.Load(row.Uid);
            await Navigation.PushAsync(page);
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Navigate:AdminUserDetail", ex);
        }
    }
}
