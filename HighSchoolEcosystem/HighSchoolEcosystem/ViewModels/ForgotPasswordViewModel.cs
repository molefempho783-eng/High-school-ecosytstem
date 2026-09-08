using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HighSchoolEcosystem.Services.Interfaces;

namespace HighSchoolEcosystem.ViewModels;

public partial class ForgotPasswordViewModel : ObservableObject
{
    private readonly IFirebaseAuthService _auth;
    [ObservableProperty] private string email = string.Empty;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private bool isBusy;

    public ForgotPasswordViewModel(IFirebaseAuthService auth) => _auth = auth;

    [RelayCommand]
    private async Task SendResetAsync()
    {
        if (string.IsNullOrWhiteSpace(Email)) { StatusMessage = "Enter your email address."; return; }
        try { IsBusy = true; await _auth.SendPasswordResetAsync(Email.Trim()); }
        catch { }
        finally { IsBusy = false; StatusMessage = "If an account exists for this email, a reset link has been sent."; }
    }
}
