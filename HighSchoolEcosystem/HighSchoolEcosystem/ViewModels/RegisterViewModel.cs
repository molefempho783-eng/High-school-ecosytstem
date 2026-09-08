using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.Services.Firebase;
using HighSchoolEcosystem.Services.Interfaces;

namespace HighSchoolEcosystem.ViewModels;

public partial class RegisterViewModel : ObservableObject
{
    private readonly IFirebaseAuthService _auth;
    [ObservableProperty] private string fullName = string.Empty;
    [ObservableProperty] private string email = string.Empty;
    [ObservableProperty] private string password = string.Empty;
    [ObservableProperty] private string confirmPassword = string.Empty;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private bool isBusy;
    public event EventHandler? Registered;

    public RegisterViewModel(IFirebaseAuthService auth) => _auth = auth;

    [RelayCommand]
    private async Task RegisterAsync()
    {
        if (IsBusy) return;
        if (string.IsNullOrWhiteSpace(FullName)) { StatusMessage = "Enter your full name."; return; }
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password)) { StatusMessage = "Enter an email address and password."; return; }
        if (Password.Length < 8) { StatusMessage = "Use at least 8 characters for your password."; return; }
        if (Password != ConfirmPassword) { StatusMessage = "The passwords do not match."; return; }

        try
        {
            IsBusy = true;
            StatusMessage = string.Empty;
            await _auth.RegisterAsync(Email.Trim(), Password, FullName.Trim());
            Registered?.Invoke(this, EventArgs.Empty);
        }
        catch (FirebaseAuthException ex)
        {
            // Already user-facing (e.g. "That email address already has an account").
            StatusMessage = ex.Message;
        }
        catch (InvalidOperationException ex)
        {
            // Misconfiguration, e.g. a missing Firebase:WebApiKey — surface it
            // rather than hiding a setup problem behind a generic message.
            StatusMessage = ex.Message;
        }
        catch (HttpRequestException)
        {
            StatusMessage = "We couldn't reach the server. Check your connection and try again.";
        }
        catch (OperationCanceledException)
        {
            // HttpClient surfaces its timeout as a TaskCanceledException, not an
            // HttpRequestException. Catching the base type also covers the plain
            // OperationCanceledException that SemaphoreSlim.WaitAsync throws.
            StatusMessage = "That took too long. Check your connection and try again.";
        }
        catch (Exception ex)
        {
            // Nothing may escape a RelayCommand: it would be swallowed as an
            // unobserved task fault, leaving the user staring at a dead button.
            CrashLogger.Log("Register", ex);
            StatusMessage = "Something went wrong creating your account. Please try again.";
        }
        finally { IsBusy = false; }
    }
}
