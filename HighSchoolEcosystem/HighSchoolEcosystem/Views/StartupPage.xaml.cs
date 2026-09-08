using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace HighSchoolEcosystem.Views;

/// <summary>
/// Cold-start landing page. Restoring a Firebase session requires an async
/// token refresh, which can't happen in App.CreateWindow, so the app starts
/// here and swaps in the real root page once we know whether the user is
/// still signed in — and, if they are, what role they have.
/// </summary>
public partial class StartupPage : ContentPage
{
    private readonly IFirebaseAuthService _auth;
    private readonly IHomePageResolver _homeResolver;
    private readonly IServiceProvider _services;
    private bool _hasRun;

    public StartupPage(IFirebaseAuthService auth, IHomePageResolver homeResolver, IServiceProvider services)
    {
        InitializeComponent();
        _auth = auth;
        _homeResolver = homeResolver;
        _services = services;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_hasRun) return;
        _hasRun = true;

        Page next;
        try
        {
            next = await _auth.TryRestoreSessionAsync()
                ? await _homeResolver.ResolveHomeAsync()
                : _services.GetRequiredService<LoginPage>();
        }
        catch (Exception ex)
        {
            // Never block startup on a failed restore — fall through to sign-in.
            CrashLogger.Log("Startup", ex);
            next = _services.GetRequiredService<LoginPage>();
        }

        // Replace the root rather than pushing, so the back button can't
        // navigate back into this loading screen. Dispatched because swapping
        // the window's root page from inside OnAppearing is fragile on Android.
        Dispatcher.Dispatch(() =>
        {
            if (Application.Current?.Windows.Count > 0)
            {
                Application.Current.Windows[0].Page = new NavigationPage(next);
            }
        });
    }
}
