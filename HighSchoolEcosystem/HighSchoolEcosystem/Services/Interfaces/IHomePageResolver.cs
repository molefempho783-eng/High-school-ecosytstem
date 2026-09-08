namespace HighSchoolEcosystem.Services.Interfaces;

/// <summary>
/// Decides which page a user lands on. Admins get a different home screen from
/// everyone else, and that can only be known after the profile has been loaded —
/// so every entry point (cold start, sign-in, registration) has to go through
/// here rather than hard-coding DashboardPage.
/// </summary>
public interface IHomePageResolver
{
    Task<Page> ResolveHomeAsync(CancellationToken cancellationToken = default);
}
