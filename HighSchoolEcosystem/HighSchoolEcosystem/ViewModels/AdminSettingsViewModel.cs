using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HighSchoolEcosystem.Models;
using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.Services.Interfaces;

namespace HighSchoolEcosystem.ViewModels;

/// <summary>
/// Edits the school branding stored at /meta/config.
///
/// Note this is a Firestore *override* of the values baked into
/// appsettings.json at build time. The app currently reads its branding from
/// appsettings (ISchoolInfoProvider), so changes here won't be reflected in the
/// UI until that provider is taught to prefer the Firestore copy — see the note
/// on screen and PLAN.md.
/// </summary>
public partial class AdminSettingsViewModel : ObservableObject
{
    private readonly IAdminService _admin;

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private string displayName = string.Empty;
    [ObservableProperty] private string primaryColor = string.Empty;
    [ObservableProperty] private string secondaryColor = string.Empty;
    [ObservableProperty] private string accentColor = string.Empty;
    [ObservableProperty] private string logoUrl = string.Empty;

    public AdminSettingsViewModel(IAdminService admin, ISchoolInfoProvider fallback)
    {
        _admin = admin;

        // Seed from the build-time values so the form is never blank.
        var current = fallback.Current;
        DisplayName = current.DisplayName;
        PrimaryColor = current.PrimaryColor;
        SecondaryColor = current.SecondaryColor;
        AccentColor = current.AccentColor;
        LogoUrl = current.LogoUrl;
    }

    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            var stored = await _admin.GetSchoolConfigAsync();
            if (stored is null) return; // nothing saved yet; keep the seeded values

            DisplayName = stored.DisplayName;
            PrimaryColor = stored.PrimaryColor;
            SecondaryColor = stored.SecondaryColor;
            AccentColor = stored.AccentColor;
            LogoUrl = stored.LogoUrl;
        }
        catch (Exception ex)
        {
            CrashLogger.Log("AdminSettings.Load", ex);
            StatusMessage = "We couldn't load the saved settings. Showing the built-in values.";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy) return;
        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            StatusMessage = "The school name can't be empty.";
            return;
        }

        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            await _admin.SaveSchoolConfigAsync(new SchoolInfo
            {
                DisplayName = DisplayName.Trim(),
                PrimaryColor = PrimaryColor.Trim(),
                SecondaryColor = SecondaryColor.Trim(),
                AccentColor = AccentColor.Trim(),
                LogoUrl = LogoUrl.Trim()
            });
            StatusMessage = "Saved.";
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            StatusMessage = "Admin access is required to change school settings.";
        }
        catch (Exception ex)
        {
            CrashLogger.Log("AdminSettings.Save", ex);
            StatusMessage = "We couldn't save those settings. Please try again.";
        }
        finally { IsBusy = false; }
    }
}
