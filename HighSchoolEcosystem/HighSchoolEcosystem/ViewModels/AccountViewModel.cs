using HighSchoolEcosystem.Models;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.Services.Firebase;
using HighSchoolEcosystem.Services.Interfaces;

namespace HighSchoolEcosystem.ViewModels;

/// <summary>
/// One thing this account is allowed to do, in plain language.
///
/// Derived from the roles held rather than stored anywhere: the roster and the
/// Firestore rules are the truth, and a second hand-maintained list of
/// capabilities would drift away from them silently.
/// </summary>
public sealed record PermissionRow(string Glyph, string Title, string Detail);

public partial class AccountViewModel : ObservableObject
{
    private readonly IFirebaseAuthService _auth;
    private readonly IUserProfileService _profiles;
    private readonly IPushNotificationService _push;

    // The name as last loaded from the server. Used to tell an untouched box
    // from one the user is midway through editing.
    private string _loadedName = string.Empty;
    [ObservableProperty] private string displayName = string.Empty;
    [ObservableProperty] private string email = string.Empty;
    [ObservableProperty] private bool isEmailVerified;
    [ObservableProperty] private bool needsVerification;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private bool isBusy;

    // ---- roles and permissions ----

    /// <summary>
    /// The role that decides which dashboard they land on. Shown on its own
    /// because "why does my app look different from my colleague's" is the
    /// question this screen most often has to answer.
    /// </summary>
    [ObservableProperty] private string primaryRole = string.Empty;

    /// <summary>"Journalist, Coach" — everything held beyond the primary role.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAdditionalRoles))]
    private string additionalRoles = string.Empty;

    public bool HasAdditionalRoles => !string.IsNullOrWhiteSpace(AdditionalRoles);

    /// <summary>What those roles actually let this person do.</summary>
    [ObservableProperty] private ObservableCollection<PermissionRow> permissions = [];

    /// <summary>
    /// Shown when the roles couldn't be read. Deliberately not silent: a blank
    /// permissions card would read as "you can do nothing", which is a much
    /// more alarming claim than "we couldn't check".
    /// </summary>
    [ObservableProperty] private string permissionsMessage = string.Empty;
    public event EventHandler? SignedOut;

    public AccountViewModel(IFirebaseAuthService auth, IUserProfileService profiles, IPushNotificationService push)
    {
        _auth = auth;
        _profiles = profiles;
        _push = push;
    }

    public async Task LoadAsync()
    {
        // Must not throw: this is awaited from AccountPage.OnAppearing, which is
        // `async void` — an escaping exception there has nowhere to go and takes
        // the process down. Fall back to the cached session instead.
        try
        {
            var session = await _auth.RefreshCurrentUserAsync();
            Email = session?.Email ?? string.Empty;
            IsEmailVerified = session?.IsEmailVerified ?? false;

            var name = session?.DisplayName ?? string.Empty;

            // One profile read serves two purposes: the name fallback below and
            // the roles card. It used to be fetched only when the name was
            // missing, which is why the roles card was never populated.
            UserProfile? profile = null;
            try
            {
                profile = await _profiles.GetOrCreateCurrentProfileAsync();
            }
            catch (Exception ex)
            {
                // Includes the rostered-but-unverified case, where no profile
                // document exists yet.
                CrashLogger.Log("Account.LoadProfile", ex);
            }

            ApplyRoles(profile);

            // Accounts created before there was a name field have no name on the
            // Auth record, but may have one on their profile — fall back to it
            // rather than showing an empty box.
            if (name.Length == 0 && profile is not null && profile.DisplayName != profile.Email)
                name = profile.DisplayName;

            SetLoadedName(name);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[account] refresh failed: {ex}");
            var cached = _auth.CurrentSession;
            Email = cached?.Email ?? string.Empty;
            IsEmailVerified = cached?.IsEmailVerified ?? false;
            SetLoadedName(cached?.DisplayName ?? string.Empty);
            StatusMessage = "We couldn't refresh your account details. Showing the last known state.";
        }
        finally
        {
            NeedsVerification = !IsEmailVerified;
        }
    }

    /// <summary>
    /// Applies a server-loaded name without discarding an unsaved edit — this
    /// runs on every OnAppearing, so overwriting unconditionally would wipe
    /// whatever the user was typing.
    /// </summary>
    private void SetLoadedName(string name)
    {
        if (DisplayName == _loadedName) DisplayName = name;
        _loadedName = name;
    }

    [RelayCommand]
    private async Task SaveNameAsync()
    {
        if (IsBusy) return;
        var name = DisplayName?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            StatusMessage = "Enter your name.";
            return;
        }

        try
        {
            IsBusy = true;

            // Two places hold the name: the Firebase Auth account (so it follows
            // you to any device) and the Firestore profile (what other people see
            // in class lists).
            await _auth.UpdateDisplayNameAsync(name);
            _loadedName = name;
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Account.SaveName.Auth", ex);
            StatusMessage = "We couldn't update your name. Please try again.";
            IsBusy = false;
            return;
        }

        // The auth name is already changed, so a failure here is partial, not
        // total — saying "couldn't update your name" would be wrong. It also
        // legitimately fails for a rostered user whose email isn't verified yet:
        // they have no profile document, and the rules refuse to create one
        // without a role.
        try
        {
            await _profiles.UpdateDisplayNameAsync(name);
            StatusMessage = "Name updated.";
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Account.SaveName.Profile", ex);
            StatusMessage = "Saved to your account, but your school profile "
                          + "couldn't be updated yet. It'll apply once your "
                          + "account setup is complete.";
        }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// Fills in the roles card.
    ///
    /// Reads the profile rather than the auth session: roles live in Firestore,
    /// assigned from the roster, and the auth token knows nothing about them.
    ///
    /// This is a MIRROR of what the rules allow, not the thing that allows it.
    /// `firestore.rules` is the boundary; if the two ever disagree the rules
    /// win and the user gets a permission error. Keep the wording here
    /// describing what someone CAN do, never promising it.
    /// </summary>
    private void ApplyRoles(UserProfile? profile)
    {
        if (profile is null)
        {
            PrimaryRole = string.Empty;
            AdditionalRoles = string.Empty;
            Permissions = [];
            PermissionsMessage =
                "We couldn't read your roles. If you've just signed up, verify "
                + "your email address and they'll appear here.";
            return;
        }

        PermissionsMessage = string.Empty;
        PrimaryRole = Describe(profile.Role);

        // Everything held beyond the primary role, de-duplicated: the roster can
        // legitimately list a role that is also the primary one, and showing
        // "Teacher" twice reads like a bug.
        var extra = profile.AllRoles()
            .Where(r => r != profile.Role)
            .Distinct()
            .Select(Describe)
            .ToList();

        AdditionalRoles = string.Join(", ", extra);

        var rows = new List<PermissionRow>();

        if (profile.HasRole(Role.Student))
            rows.Add(new(Icons.Work, "Hand in work",
                "Submit homework and assignments for the classes you're enrolled in, and see your marks."));

        if (profile.HasRole(Role.Teacher))
        {
            rows.Add(new(Icons.Teaching, "Run your classes",
                "Create classes, enrol students, set work and add course material."));
            rows.Add(new(Icons.Marking, "Mark work",
                "Read what your students hand in, grade it and leave feedback."));
            rows.Add(new(Icons.Announcement, "Post class notices",
                "Write notices for the classes you teach. Only those classes see them."));
        }

        if (profile.HasRole(Role.Parent))
            rows.Add(new(Icons.Children, "Follow your children",
                "See the work, marks and notices for the children linked to your account."));

        if (profile.HasRole(Role.Coach))
            rows.Add(new(Icons.Sport, "Manage sport",
                "Post fixtures, record results and write sports news for the whole school."));

        if (profile.HasRole(Role.Journalist))
            rows.Add(new(Icons.Edit, "Write articles",
                "Publish articles with pictures to the sports and news feed. You can edit and delete your own."));

        if (profile.HasRole(Role.Admin))
        {
            rows.Add(new(Icons.Admin, "Administer the school",
                "Manage people's roles, the roster and the school's settings."));
            rows.Add(new(Icons.Announcement, "Post school announcements",
                "Write notices the whole school sees, and take them down."));
        }

        if (profile.HasRole(Role.Visitor))
            rows.Add(new(Icons.Visibility, "Read public content",
                "See public announcements, fixtures and sports news. Class content stays private."));

        // Everyone can do this, and saying so out loud is the point of the card:
        // a student who sees only one line shouldn't think the screen failed.
        rows.Add(new(Icons.Account, "Your own account",
            "Change the name other people see, and sign out."));

        Permissions = new ObservableCollection<PermissionRow>(rows);
    }

    /// <summary>
    /// Role names in the school's own words. "Journalist" is the enum; "School
    /// journalist" is what a pupil would call it.
    /// </summary>
    private static string Describe(Role role) => role switch
    {
        Role.Student => "Student",
        Role.Teacher => "Teacher",
        Role.Parent => "Parent",
        Role.Admin => "Administrator",
        Role.Coach => "Sports coach",
        Role.Journalist => "School journalist",
        Role.Visitor => "Visitor",
        _ => role.ToString()
    };

    [RelayCommand]
    private async Task SendVerificationAsync()
    {
        if (IsBusy) return; // its finally would otherwise clear IsBusy mid-save
        try { IsBusy = true; await _auth.SendEmailVerificationAsync(); StatusMessage = "Verification email sent. Check your inbox."; }
        catch (FirebaseAuthException ex) { StatusMessage = ex.Message; }
        catch { StatusMessage = "We could not send a verification email. Please try again."; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task SignOutAsync()
    {
        try
        {
            // FIRST, while still signed in: removing this device needs the
            // user's credentials. Otherwise the next person to sign in on a
            // shared tablet would get this person's notifications.
            await _push.UnregisterAsync();
            await _auth.SignOutAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[account] sign-out failed: {ex}");
        }
        finally
        {
            // Always navigate away. Leaving someone stranded on an account page
            // they think they've signed out of is worse than a failed cleanup —
            // and the local session state is cleared before anything can throw.
            SignedOut?.Invoke(this, EventArgs.Empty);
        }
    }
}
