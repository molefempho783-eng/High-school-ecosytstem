using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.Services.Firebase;
using HighSchoolEcosystem.Services.Interfaces;

namespace HighSchoolEcosystem.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly IFirebaseAuthService _auth;
    [ObservableProperty] private string email = string.Empty;
    [ObservableProperty] private string password = string.Empty;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private bool isBusy;
    public event EventHandler? SignedIn;

    public LoginViewModel(IFirebaseAuthService auth) => _auth = auth;

    [RelayCommand]
    private async Task SignInAsync()
    {
        if (IsBusy) return;
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
        {
            StatusMessage = "Enter your email address and password.";
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = string.Empty;
            await _auth.SignInAsync(Email.Trim(), Password);
            SignedIn?.Invoke(this, EventArgs.Empty);
        }
        catch (FirebaseAuthException ex)
        {
            StatusMessage = ex.Message;
        }
        catch (InvalidOperationException ex)
        {
            // Misconfiguration, e.g. a missing Firebase:WebApiKey.
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
            CrashLogger.Log("SignIn", ex);
            StatusMessage = "Something went wrong signing you in. Please try again.";
        }
        finally { IsBusy = false; }
    }
}
