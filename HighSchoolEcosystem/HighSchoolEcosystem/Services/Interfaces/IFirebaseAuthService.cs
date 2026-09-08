using HighSchoolEcosystem.Models;

namespace HighSchoolEcosystem.Services.Interfaces;

public interface IFirebaseAuthService
{
    AuthSession? CurrentSession { get; }
    bool IsSignedIn { get; }
    Task<AuthSession> SignInAsync(string email, string password);
    /// <summary>Creates the account and sets its display name.</summary>
    Task<AuthSession> RegisterAsync(string email, string password, string displayName);

    /// <summary>Renames the signed-in Firebase Auth account.</summary>
    Task UpdateDisplayNameAsync(string displayName);
    Task SendPasswordResetAsync(string email);
    Task SendEmailVerificationAsync();
    Task<AuthSession?> RefreshCurrentUserAsync();
    Task SignOutAsync();

    /// <summary>
    /// Returns the current user's Firebase ID token, refreshing it first if it
    /// has expired. Returns null if nobody is signed in.
    /// </summary>
    Task<string?> GetIdTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores a previously persisted session on cold start. Returns true if
    /// the user is signed in afterwards. Call this before deciding whether to
    /// show the sign-in screen or the dashboard.
    /// </summary>
    Task<bool> TryRestoreSessionAsync(CancellationToken cancellationToken = default);
}
