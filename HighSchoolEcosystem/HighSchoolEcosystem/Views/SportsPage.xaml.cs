using HighSchoolEcosystem.ViewModels;

namespace HighSchoolEcosystem.Views;

public partial class SportsPage : ContentPage
{
    private readonly SportsViewModel _viewModel;

    public SportsPage(SportsViewModel viewModel)
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
