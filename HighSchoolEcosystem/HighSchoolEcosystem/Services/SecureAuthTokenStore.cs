using HighSchoolEcosystem.Services.Interfaces;
using Microsoft.Maui.Storage;

namespace HighSchoolEcosystem.Services;

public sealed class SecureAuthTokenStore : IAuthTokenStore
{
    private const string RefreshTokenKey = "firebase_refresh_token";

    public async Task<string?> GetRefreshTokenAsync()
    {
        try
        {
            return await SecureStorage.Default.GetAsync(RefreshTokenKey);
        }
        catch
        {
            // SecureStorage can throw if the platform keystore is unavailable
            // (notably on some Windows dev setups). A missing token just means
            // "not signed in", which is a recoverable state, so don't crash.
            return null;
        }
    }

    public async Task SaveRefreshTokenAsync(string refreshToken)
    {
        try
        {
            await SecureStorage.Default.SetAsync(RefreshTokenKey, refreshToken);
        }
        catch
        {
            // Non-fatal: the user stays signed in for this session, they'll
            // just have to sign in again next launch.
        }
    }

    public Task ClearAsync()
    {
        try
        {
            SecureStorage.Default.Remove(RefreshTokenKey);
        }
        catch
        {
            // Ignore — see above.
        }
        return Task.CompletedTask;
    }
}
