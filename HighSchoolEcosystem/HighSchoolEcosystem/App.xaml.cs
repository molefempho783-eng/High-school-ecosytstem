using HighSchoolEcosystem.Views;
using Microsoft.Extensions.DependencyInjection;

namespace HighSchoolEcosystem;

public partial class App : Application
{
    private readonly IServiceProvider _services;

    public App(IServiceProvider services)
    {
        _services = services;
        InitializeComponent();
    }

    // Always start on StartupPage: deciding between the dashboard and the
    // sign-in screen needs an async token refresh, which can't happen here.
    protected override Window CreateWindow(IActivationState? activationState)
        => new(new NavigationPage(_services.GetRequiredService<StartupPage>()));
}
