using HighSchoolEcosystem.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace HighSchoolEcosystem.Views;

public partial class AccountPage : ContentPage
{
    private readonly AccountViewModel _viewModel;
    private readonly IServiceProvider _services;

    // LoginPage is resolved lazily rather than injected: LoginPage depends on
    // DashboardPage, which depends on this page, so constructor-injecting it
    // would be a dependency cycle.
    public AccountPage(AccountViewModel viewModel, IServiceProvider services)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _services = services;
        _viewModel.SignedOut += OnSignedOut;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    private void OnSignedOut(object? sender, EventArgs e)
    {
        // Replace the whole navigation stack — the dashboard is the root now,
        // so popping would just land the signed-out user back on it.
        if (Application.Current?.Windows.Count > 0)
        {
            Application.Current.Windows[0].Page =
                new NavigationPage(_services.GetRequiredService<LoginPage>());
        }
    }
}
