using HighSchoolEcosystem.ViewModels;

namespace HighSchoolEcosystem.Views;

public partial class AdminInvitesPage : ContentPage
{
    private readonly AdminInvitesViewModel _viewModel;

    public AdminInvitesPage(AdminInvitesViewModel viewModel)
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
