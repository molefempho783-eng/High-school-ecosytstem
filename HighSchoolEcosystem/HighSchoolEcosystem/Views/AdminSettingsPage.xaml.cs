using HighSchoolEcosystem.ViewModels;

namespace HighSchoolEcosystem.Views;

public partial class AdminSettingsPage : ContentPage
{
    private readonly AdminSettingsViewModel _viewModel;

    public AdminSettingsPage(AdminSettingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }
}
