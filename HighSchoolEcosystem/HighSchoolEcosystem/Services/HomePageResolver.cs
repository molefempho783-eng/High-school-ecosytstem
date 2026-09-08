using HighSchoolEcosystem.Models;
using HighSchoolEcosystem.Services.Interfaces;
using HighSchoolEcosystem.Views;
using Microsoft.Extensions.DependencyInjection;

namespace HighSchoolEcosystem.Services;

public sealed class HomePageResolver : IHomePageResolver
{
    private readonly IServiceProvider _services;
    private readonly IFirebaseAuthService _auth;
    private readonly IUserProfileService _profiles;

    public HomePageResolver(IServiceProvider services, IFirebaseAuthService auth, IUserProfileService profiles)
    {
        _services = services;
        _auth = auth;
        _profiles = profiles;
    }

    public async Task<Page> ResolveHomeAsync(CancellationToken cancellationToken = default)
    {
        // Every role gets its own landing page. Parents and coaches used to
        // fall through to the student dashboard, which told them they weren't
        // enrolled in any classes — true, and useless.
        //
        // Guests browse the public surface on the normal dashboard.
        if (!_auth.IsSignedIn) return _services.GetRequiredService<DashboardPage>();

        try
        {
            var profile = await _profiles.GetOrCreateCurrentProfileAsync(cancellationToken);

            // The PRIMARY role decides the landing page — that's what makes it
            // "primary", and it's the admin's explicit choice for where this
            // person should start. Every other role they hold is reachable from
            // there: each dashboard links to the others the user qualifies for.
            //
            // (Routing on the most-privileged role held instead would ignore the
            // admin's choice and dump a Student-who-also-teaches into the
            // teacher console every time they open the app.)
            if (profile is not null)
            {
                if (profile.Role == Role.Admin) return _services.GetRequiredService<AdminDashboardPage>();
                if (profile.Role == Role.Teacher) return _services.GetRequiredService<TeacherDashboardPage>();
                if (profile.Role == Role.Parent) return _services.GetRequiredService<ParentDashboardPage>();
                if (profile.Role == Role.Coach) return _services.GetRequiredService<CoachDashboardPage>();
            }
        }
        catch (Exception ex)
        {
            // Includes the "rostered staff, email not yet verified" case. Fall
            // through to DashboardPage, which re-runs the same load and renders
            // the appropriate explanation rather than a blank screen.
            CrashLogger.Log("HomePageResolver", ex);
        }

        return _services.GetRequiredService<DashboardPage>();
    }
}
