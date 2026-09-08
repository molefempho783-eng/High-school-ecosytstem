namespace HighSchoolEcosystem.Services.Interfaces;

/// <summary>
/// Persists the Firebase refresh token between app launches so a signed-in
/// user isn't asked to re-authenticate on every cold start. Only the refresh
/// token is stored — ID tokens are short-lived and kept in memory.
/// </summary>
public interface IAuthTokenStore
{
    Task<string?> GetRefreshTokenAsync();
    Task SaveRefreshTokenAsync(string refreshToken);
    Task ClearAsync();
}
