using System.Net;
using HighSchoolEcosystem.Models;
using HighSchoolEcosystem.Services.Firebase;
using HighSchoolEcosystem.Services.Interfaces;

namespace HighSchoolEcosystem.Services;

public sealed class UserProfileService : IUserProfileService
{
    private readonly IFirestoreClient _firestoreClient;
    private readonly IFirebaseAuthService _authService;

    public UserProfileService(IFirestoreClient firestoreClient, IFirebaseAuthService authService)
    {
        _firestoreClient = firestoreClient;
        _authService = authService;
    }

    public async Task<UserProfile?> GetOrCreateCurrentProfileAsync(CancellationToken cancellationToken = default)
    {
        var session = _authService.CurrentSession;
        if (session is null) return null;

        var path = $"users/{session.UserId}";
        var existing = await _firestoreClient.GetDocumentAsync<UserProfile>(path, cancellationToken);
        if (existing is not null) return existing;

        // First sign-in: build the profile from the school's roster.
        var invite = await GetInviteAsync(session.Email, cancellationToken);

        var bootstrapped = new UserProfile
        {
            Uid = session.UserId,
            Email = session.Email ?? string.Empty,
            // The name captured at registration, which lives on the Firebase
            // Auth account. Falls back to the email only for accounts created
            // before there was a name field.
            DisplayName = !string.IsNullOrWhiteSpace(session.DisplayName)
                ? session.DisplayName
                : session.Email ?? "New user",

            // No roster entry means open enrolment: they come in as a Student
            // with no classes and no linked children. The Firestore rules
            // enforce exactly this, so a tampered client can't do better.
            Role = invite?.Role ?? Role.Student,
            // Must match the roster exactly — the rules reject any mismatch.
            Roles = invite?.Roles ?? [],
            ClassIds = invite?.ClassIds ?? [],
            LinkedStudentIds = invite?.LinkedStudentIds ?? []
        };

        try
        {
            await _firestoreClient.SetDocumentAsync(path, bootstrapped, cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden
                                              && IsPrivileged(invite))
        {
            // Expected: the rules only grant a privileged role once the user has
            // proved they own the rostered mailbox. Surface it as a "check your
            // email" prompt rather than a generic failure.
            //
            // Deliberately NOT also checking session.IsEmailVerified: that flag
            // comes from accounts:lookup and flips to true the moment the user
            // clicks the link, while the cached ID token — which is what the
            // rules actually read — still says false. Gating on it here would
            // hide this message exactly when the user most needs it.
            throw new ProfileVerificationRequiredException(session.Email);
        }

        return bootstrapped;
    }

    public async Task<UserProfile?> GetProfileAsync(
        string uid, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(uid)) return null;

        try
        {
            return await _firestoreClient.GetDocumentAsync<UserProfile>(
                $"users/{uid}", cancellationToken);
        }
        catch (Exception ex)
        {
            // A denial here is expected for anyone not entitled to read this
            // profile. Callers treat null as "name unavailable" and fall back.
            CrashLogger.Log($"UserProfile.GetProfile:{uid}", ex);
            return null;
        }
    }

    public Task UpdateDisplayNameAsync(string displayName, CancellationToken cancellationToken = default)
    {
        var session = _authService.CurrentSession
            ?? throw new InvalidOperationException("Sign in before changing your name.");

        return _firestoreClient.PatchFieldsAsync($"users/{session.UserId}",
            new Dictionary<string, object?> { ["DisplayName"] = displayName.Trim() }, cancellationToken);
    }

    // Must consider the extra roles too: an invite of primary Student with
    // Roles: ["Journalist"] is still privileged, and the rules gate it on email
    // verification — so the "verify your email" prompt has to fire for it.
    private static bool IsPrivileged(Invite? invite)
    {
        if (invite is null) return false;
        if (IsPrivilegedRole(invite.Role)) return true;

        return invite.Roles.Any(name =>
            Enum.TryParse<Role>(name, ignoreCase: false, out var parsed) && IsPrivilegedRole(parsed));
    }

    private static bool IsPrivilegedRole(Role role)
        => role is Role.Admin or Role.Teacher or Role.Parent or Role.Coach or Role.Journalist;

    private async Task<Invite?> GetInviteAsync(string? email, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;

        // Invite documents are keyed by lower-cased email; the rules compare
        // against request.auth.token.email.lower(), so the casing must match.
        var inviteId = email.Trim().ToLowerInvariant();

        try
        {
            return await _firestoreClient.GetDocumentAsync<Invite>($"invites/{Uri.EscapeDataString(inviteId)}", cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            // Couldn't reach or read the roster. Not on the roster and "roster
            // unavailable" are indistinguishable here, so fall back to the
            // Student default — the rules still have the final say.
            System.Diagnostics.Debug.WriteLine($"[roster] lookup failed for '{inviteId}': {ex.StatusCode} {ex.Message}");
            return null;
        }
        catch (OperationCanceledException)
        {
            // A timeout or an explicit cancel — NOT a data problem. Must be
            // rethrown before the catch below, or a flaky connection would tell
            // the user their roster entry is corrupt and to go find an admin.
            throw;
        }
        catch (Exception ex)
        {
            // The document exists but doesn't deserialize — almost always a
            // typo'd Role in the console (it must be one of the Role enum names
            // exactly). Swallowing this would silently demote
            // the person to Student and then leave them permanently 403'd by
            // the rules, with nothing on screen explaining why.
            System.Diagnostics.Debug.WriteLine($"[roster] malformed invite '{inviteId}': {ex}");
            throw new InvalidOperationException(
                $"Your roster entry ({inviteId}) is invalid. Ask an administrator to check the " +
                "Role field — it must be exactly Student, Teacher, Parent, Admin, Visitor, " +
                "Coach or Journalist.", ex);
        }
    }
}
