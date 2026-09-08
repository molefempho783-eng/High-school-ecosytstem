using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.Services.Interfaces;
using HighSchoolEcosystem.ViewModels;

namespace HighSchoolEcosystem.Views;

public partial class RegisterPage : ContentPage
{
    private readonly IHomePageResolver _homeResolver;

    public RegisterPage(RegisterViewModel viewModel, IHomePageResolver homeResolver)
    {
        InitializeComponent();
        BindingContext = viewModel;
        _homeResolver = homeResolver;
        viewModel.Registered += OnRegistered;
    }

    private async void OnRegistered(object? sender, EventArgs e)
    {
        try
        {
            // Registration signs the user straight in. Resolve rather than
            // assume DashboardPage: a rostered admin should land on the admin
            // home immediately.
            var home = await _homeResolver.ResolveHomeAsync();

            if (Application.Current?.Windows.Count > 0)
            {
                Application.Current.Windows[0].Page = new NavigationPage(home);
            }
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Register.GoHome", ex);
        }
    }
}
