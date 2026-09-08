using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.Services.Firebase;
using HighSchoolEcosystem.Services.Interfaces;

namespace HighSchoolEcosystem.ViewModels;

public partial class AccountViewModel : ObservableObject
{
    private readonly IFirebaseAuthService _auth;
    private readonly IUserProfileService _profiles;

    // The name as last loaded from the server. Used to tell an untouched box
    // from one the user is midway through editing.
    private string _loadedName = string.Empty;
    [ObservableProperty] private string displayName = string.Empty;
    [ObservableProperty] private string email = string.Empty;
    [ObservableProperty] private bool isEmailVerified;
    [ObservableProperty] private bool needsVerification;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private bool isBusy;
    public event EventHandler? SignedOut;

    public AccountViewModel(IFirebaseAuthService auth, IUserProfileService profiles)
    {
        _auth = auth;
        _profiles = profiles;
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

            // Accounts created before there was a name field have no name on the
            // Auth record, but may have one on their profile — fall back to it
            // rather than showing an empty box.
            if (name.Length == 0)
            {
                try
                {
                    var profile = await _profiles.GetOrCreateCurrentProfileAsync();
                    if (profile is not null && profile.DisplayName != profile.Email)
                        name = profile.DisplayName;
                }
                catch
                {
                    // Includes the rostered-but-unverified case, where no profile
                    // exists yet. Not worth surfacing on this screen.
                }
            }

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
