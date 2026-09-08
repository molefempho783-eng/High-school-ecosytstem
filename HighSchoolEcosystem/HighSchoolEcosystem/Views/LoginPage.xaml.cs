using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.Services.Interfaces;
using HighSchoolEcosystem.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace HighSchoolEcosystem.Views;

public partial class LoginPage : ContentPage
{
    private readonly IServiceProvider _services;
    private readonly IHomePageResolver _homeResolver;

    // Pages are resolved lazily rather than constructor-injected, so opening
    // the sign-in screen doesn't eagerly build the entire dashboard graph.
    public LoginPage(LoginViewModel viewModel, IHomePageResolver homeResolver, IServiceProvider services)
    {
        InitializeComponent();
        BindingContext = viewModel;
        _services = services;
        _homeResolver = homeResolver;
        viewModel.SignedIn += OnSignedIn;
    }

    // Admins land on a different home screen, which is only known once the
    // profile has loaded — hence resolving rather than hard-coding a page.
    private async void OnSignedIn(object? sender, EventArgs e) => await GoHomeAsync();

    // Guests browse the same public content a signed-in Visitor sees; the
    // Firestore rules are what actually enforce that, not this navigation.
    private async void OnVisitorClicked(object? sender, EventArgs e) => await GoHomeAsync();

    private async Task GoHomeAsync()
    {
        try
        {
            var home = await _homeResolver.ResolveHomeAsync();

            // Replace the root instead of pushing, so the back button can't
            // return to the sign-in form.
            if (Application.Current?.Windows.Count > 0)
            {
                Application.Current.Windows[0].Page = new NavigationPage(home);
            }
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Login.GoHome", ex);
        }
    }

    private async void OnRegisterClicked(object? sender, EventArgs e)
        => await PushAsync<RegisterPage>();

    private async void OnForgotPasswordClicked(object? sender, EventArgs e)
        => await PushAsync<ForgotPasswordPage>();

    // These handlers are `async void`, so an exception escaping here has nowhere
    // to propagate and would take the process down.
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
