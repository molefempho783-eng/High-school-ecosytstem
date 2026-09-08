using System.Net.Http.Json;
using System.Text.Json.Nodes;
using HighSchoolEcosystem.Models;
using HighSchoolEcosystem.Services.Firebase;
using HighSchoolEcosystem.Services.Interfaces;

namespace HighSchoolEcosystem.Services;

/// <summary>
/// Firebase Authentication over the Identity Toolkit REST API.
///
/// Deliberately REST rather than a native SDK: Plugin.Firebase.Auth only
/// supports Android and iOS, which meant sign-in threw
/// PlatformNotSupportedException on the Windows and MacCatalyst targets. REST
/// gives one code path that works on all four, and matches how FirestoreClient
/// already talks to Firestore.
///
/// Registered as a singleton — it owns the in-memory session for the app.
/// </summary>
public sealed class FirebaseAuthService : IFirebaseAuthService
{
    // Refresh slightly before actual expiry so a token can't lapse mid-request.
    private static readonly TimeSpan ExpiryLeeway = TimeSpan.FromMinutes(1);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAuthTokenStore _tokenStore;
    private readonly FirebaseEndpoints _endpoints;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private string? _idToken;
    private string? _refreshToken;
    private DateTimeOffset _idTokenExpiresAt;

    public AuthSession? CurrentSession { get; private set; }
    public bool IsSignedIn => CurrentSession is not null;

    public FirebaseAuthService(
        IHttpClientFactory httpClientFactory,
        IAuthTokenStore tokenStore,
        FirebaseEndpoints endpoints)
    {
        _httpClientFactory = httpClientFactory;
        _tokenStore = tokenStore;
        _endpoints = endpoints;
    }

    public async Task<AuthSession> RegisterAsync(string email, string password, string displayName)
    {
        var payload = new { email, password, returnSecureToken = true };
        var response = await PostAsync("accounts:signUp", payload);
        var session = await ApplyTokenResponseAsync(response);

        // signUp has no displayName parameter, so it's a second call. Set before
        // anything reads the profile, because UserProfileService falls back to
        // the email address when there's no name to use.
        var trimmed = displayName?.Trim() ?? string.Empty;
        if (trimmed.Length > 0)
        {
            try
            {
                await UpdateDisplayNameAsync(trimmed);
                session = CurrentSession ?? session;
            }
            catch (Exception ex)
            {
                // Non-fatal: the account exists and they're signed in. They'd
                // just show up under their email until they set a name.
                System.Diagnostics.Debug.WriteLine($"[auth] couldn't set display name: {ex}");
            }
        }

        // Fire off the verification email, but don't fail the registration if
        // it bounces — the account exists and the user is signed in either way.
        try
        {
            await SendEmailVerificationAsync();
        }
        catch
        {
            // Ignored; the Account page has a "resend verification" action.
        }

        return session;
    }

    public async Task<AuthSession> SignInAsync(string email, string password)
    {
        var payload = new { email, password, returnSecureToken = true };
        var response = await PostAsync("accounts:signInWithPassword", payload);
        return await ApplyTokenResponseAsync(response);
    }

    public async Task UpdateDisplayNameAsync(string displayName)
    {
        var idToken = await GetIdTokenAsync()
            ?? throw new InvalidOperationException("Sign in before changing your name.");

        var response = await PostAsync("accounts:update", new
        {
            idToken,
            displayName = displayName.Trim(),
            returnSecureToken = false
        });

        if (CurrentSession is not null)
        {
            CurrentSession = CurrentSession with
            {
                DisplayName = response["displayName"]?.GetValue<string>() ?? displayName.Trim()
            };
        }
    }

    public async Task SendPasswordResetAsync(string email)
    {
        await PostAsync("accounts:sendOobCode", new { requestType = "PASSWORD_RESET", email });
    }

    public async Task SendEmailVerificationAsync()
    {
        var idToken = await GetIdTokenAsync()
            ?? throw new InvalidOperationException("Sign in before requesting verification.");
        await PostAsync("accounts:sendOobCode", new { requestType = "VERIFY_EMAIL", idToken });
    }

    public async Task<AuthSession?> RefreshCurrentUserAsync()
    {
        var idToken = await GetIdTokenAsync();
        if (idToken is null) return null;

        // accounts:lookup returns the authoritative current state of the
        // account, which is how we notice the user has verified their email.
        var response = await PostAsync("accounts:lookup", new { idToken });
        if (response["users"] is not JsonArray users || users.Count == 0 || users[0] is not JsonObject user)
            return CurrentSession;

        var wasVerified = CurrentSession?.IsEmailVerified ?? false;
        var isVerified = ReadBool(user["emailVerified"]);

        CurrentSession = new AuthSession(
            user["localId"]?.GetValue<string>() ?? CurrentSession?.UserId ?? string.Empty,
            user["email"]?.GetValue<string>() ?? CurrentSession?.Email,
            isVerified,
            user["displayName"]?.GetValue<string>() ?? CurrentSession?.DisplayName);

        // The user just verified their email. `email_verified` is a claim baked
        // into the ID token, so the cached token still says false for up to an
        // hour — and the Firestore rules read the token, not this lookup. Expire
        // it now so the next call mints one carrying the verified claim, which
        // is what lets a rostered teacher/parent/admin finally get their role.
        if (isVerified && !wasVerified)
        {
            _idTokenExpiresAt = default;
        }

        return CurrentSession;
    }

    public async Task SignOutAsync()
    {
        // Take the refresh lock so an in-flight RefreshIdTokenAsync can't
        // complete after we've cleared state and quietly re-persist the tokens.
        await _refreshLock.WaitAsync();
        try
        {
            ClearSessionState();
        }
        finally
        {
            _refreshLock.Release();
        }
        await _tokenStore.ClearAsync();
    }

    private void ClearSessionState()
    {
        CurrentSession = null;
        _idToken = null;
        _refreshToken = null;
        _idTokenExpiresAt = default;
    }

    public async Task<string?> GetIdTokenAsync(CancellationToken cancellationToken = default)
    {
        if (IsTokenFresh()) return _idToken;
        if (_refreshToken is null) return null;

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            // Re-check: another caller may have refreshed while we waited.
            if (IsTokenFresh()) return _idToken;

            var refreshToken = _refreshToken;
            if (refreshToken is null) return null; // signed out while we waited

            await RefreshIdTokenAsync(refreshToken, cancellationToken);
            return _idToken;
        }
        catch (FirebaseAuthException)
        {
            // The refresh token is expired or revoked, so this session is over.
            // Clear it rather than leaving IsSignedIn true with tokens that will
            // fail every subsequent Firestore call.
            ClearSessionState();
            await _tokenStore.ClearAsync();
            return null;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private bool IsTokenFresh()
        => _idToken is not null && DateTimeOffset.UtcNow < _idTokenExpiresAt - ExpiryLeeway;

    /// <summary>
    /// Restores a session from the persisted refresh token on cold start.
    /// Returns true if the user is signed in afterwards.
    /// </summary>
    public async Task<bool> TryRestoreSessionAsync(CancellationToken cancellationToken = default)
    {
        if (IsSignedIn) return true;

        var storedRefreshToken = await _tokenStore.GetRefreshTokenAsync();
        if (string.IsNullOrEmpty(storedRefreshToken)) return false;

        // Held while mutating token state so a concurrent GetIdTokenAsync can't
        // interleave. SignOutAsync is deliberately called *after* releasing —
        // SemaphoreSlim isn't reentrant, so calling it here would deadlock.
        bool rejected;
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            await RefreshIdTokenAsync(storedRefreshToken, cancellationToken);
            rejected = false;
        }
        catch (FirebaseAuthException)
        {
            // Genuinely rejected (expired/revoked) — drop the stored token so we
            // don't retry it forever, and send the user to the sign-in screen.
            rejected = true;
        }
        catch
        {
            // Network failure, emulator not running, etc. The stored credentials
            // may well still be good, so keep them and just don't restore now.
            ClearSessionState();
            return false;
        }
        finally
        {
            _refreshLock.Release();
        }

        if (rejected)
        {
            await SignOutAsync();
            return false;
        }

        try
        {
            // Fills in email/emailVerified. A failure here doesn't invalidate the
            // session — we already hold a valid ID token.
            await RefreshCurrentUserAsync();
        }
        catch
        {
            // Ignored; the Account page refreshes this again on demand.
        }

        return IsSignedIn;
    }

    private async Task RefreshIdTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        _endpoints.EnsureConfigured();
        using var client = _httpClientFactory.CreateClient();
        var url = _endpoints.WithKey($"{_endpoints.SecureTokenBaseUrl}/token");

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken
        });

        using var response = await client.PostAsync(url, content, cancellationToken);
        var json = await ReadJsonAsync(response, cancellationToken);

        // The secure-token endpoint uses snake_case, unlike Identity Toolkit.
        // Treat a missing id_token as a hard failure: leaving it null while a
        // refresh token is present would make every GetIdTokenAsync call retry
        // the round-trip forever.
        _idToken = json["id_token"]?.GetValue<string>()
            ?? throw new FirebaseAuthException("MISSING_ID_TOKEN", "Your session has expired. Please sign in again.");
        _refreshToken = json["refresh_token"]?.GetValue<string>() ?? refreshToken;
        _idTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(ReadInt(json["expires_in"], 3600));

        var userId = json["user_id"]?.GetValue<string>();
        if (userId is not null && CurrentSession is null)
        {
            // Populated properly by the RefreshCurrentUserAsync call that follows.
            CurrentSession = new AuthSession(userId, null, false);
        }

        await _tokenStore.SaveRefreshTokenAsync(_refreshToken);
    }

    private async Task<AuthSession> ApplyTokenResponseAsync(JsonObject response)
    {
        _idToken = response["idToken"]?.GetValue<string>();
        _refreshToken = response["refreshToken"]?.GetValue<string>();
        _idTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(ReadInt(response["expiresIn"], 3600));

        CurrentSession = new AuthSession(
            response["localId"]?.GetValue<string>() ?? string.Empty,
            response["email"]?.GetValue<string>(),
            ReadBool(response["emailVerified"]),
            response["displayName"]?.GetValue<string>());

        if (_refreshToken is not null)
            await _tokenStore.SaveRefreshTokenAsync(_refreshToken);

        return CurrentSession;
    }

    private async Task<JsonObject> PostAsync(string endpoint, object payload)
    {
        _endpoints.EnsureConfigured();
        using var client = _httpClientFactory.CreateClient();
        var url = _endpoints.WithKey($"{_endpoints.IdentityToolkitBaseUrl}/{endpoint}");
        using var response = await client.PostAsJsonAsync(url, payload);
        return await ReadJsonAsync(response, CancellationToken.None);
    }

    private static async Task<JsonObject> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var json = JsonNode.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body) as JsonObject ?? new JsonObject();

        if (response.IsSuccessStatusCode) return json;

        // Server-side and throttling failures are transient: the caller's
        // credentials are probably fine, so surface them as HttpRequestException.
        // FirebaseAuthException is reserved for genuine credential/account
        // problems, because callers treat it as "this session is over" and
        // discard the stored refresh token.
        var status = (int)response.StatusCode;
        if (status >= 500 || status == 408 || status == 429)
        {
            throw new HttpRequestException(
                $"Firebase returned {status} ({response.StatusCode}). Please try again.");
        }

        // Firebase error shape: { "error": { "code": 400, "message": "EMAIL_EXISTS", ... } }
        // The message sometimes carries detail after the code, e.g.
        // "WEAK_PASSWORD : Password should be at least 6 characters".
        var rawMessage = json["error"]?["message"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(rawMessage))
        {
            // A non-Firebase-shaped 4xx (a proxy, or the emulator not running on
            // the expected port) isn't an auth failure either.
            throw new HttpRequestException(
                $"Unexpected response from Firebase: {status} ({response.StatusCode}).");
        }

        var errorCode = rawMessage.Split(':', StringSplitOptions.TrimEntries)[0];
        throw FirebaseAuthException.FromErrorCode(errorCode);
    }

    private static bool ReadBool(JsonNode? node)
    {
        if (node is null) return false;
        try { return node.GetValue<bool>(); }
        catch { return bool.TryParse(node.ToString(), out var parsed) && parsed; }
    }

    private static int ReadInt(JsonNode? node, int fallback)
    {
        if (node is null) return fallback;
        // expiresIn comes back as a string ("3600") from Identity Toolkit but as
        // a number from the secure-token endpoint, so handle both.
        try { return node.GetValue<int>(); }
        catch { return int.TryParse(node.ToString(), out var parsed) ? parsed : fallback; }
    }
}
