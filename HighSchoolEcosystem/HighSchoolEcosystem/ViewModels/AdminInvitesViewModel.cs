using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HighSchoolEcosystem.Models;
using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.Services.Interfaces;

namespace HighSchoolEcosystem.ViewModels;

public sealed record InviteRow(string Email, Role Role, bool IsJournalist)
{
    public string Subtitle => IsJournalist
        ? $"Will join as {Role} · Journalist"
        : $"Will join as {Role}";
}

/// <summary>
/// Manages the pre-registration roster at /invites/{email}. Entries added here
/// apply the moment that person signs up; they have no effect on someone who has
/// already registered (edit them under Manage users instead).
/// </summary>
public partial class AdminInvitesViewModel : ObservableObject
{
    private readonly IAdminService _admin;

    [ObservableProperty] private bool isBusy;

    // Kept separate from IsBusy — see the note in AdminUsersViewModel.
    [ObservableProperty] private bool isRefreshing;

    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private ObservableCollection<InviteRow> invites = [];

    [ObservableProperty] private string newEmail = string.Empty;
    [ObservableProperty] private ObservableCollection<Role> availableRoles = new(Enum.GetValues<Role>());
    [ObservableProperty] private Role newRole = Role.Student;

    /// <summary>
    /// Grants the additive Journalist role at sign-up. Any other combination is
    /// assigned after they register, under Manage users.
    /// </summary>
    [ObservableProperty] private bool newIsJournalist;

    public AdminInvitesViewModel(IAdminService admin) => _admin = admin;

    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            var entries = await _admin.GetInvitesAsync();
            Invites = new ObservableCollection<InviteRow>(entries.Select(ToRow));

            if (entries.Count == 0)
                StatusMessage = "No roster entries yet. Anyone who signs up will join as a Student.";
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            StatusMessage = "Admin access is required to manage the roster.";
        }
        catch (Exception ex)
        {
            CrashLogger.Log("AdminInvites.Load", ex);
            StatusMessage = "We couldn't load the roster. Please try again.";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try { await LoadAsync(); }
        finally { IsRefreshing = false; }
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        var email = NewEmail?.Trim() ?? string.Empty;
        if (email.Length == 0 || !email.Contains('@') || email.StartsWith('@') || email.EndsWith('@'))
        {
            StatusMessage = "Enter a valid email address.";
            return;
        }

        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            // Captured before the form resets, otherwise the confirmation
            // always reports the default role rather than the one just saved.
            var savedRole = NewRole;
            var savedJournalist = NewIsJournalist;

            await _admin.SaveInviteAsync(email, new Invite
            {
                Role = savedRole,
                Roles = savedJournalist ? [nameof(Role.Journalist)] : []
            });

            NewEmail = string.Empty;
            NewRole = Role.Student;
            NewIsJournalist = false;
            await LoadInnerAsync();
            StatusMessage = savedJournalist
                ? $"Added. {email.ToLowerInvariant()} will join as {savedRole} and Journalist."
                : $"Added. {email.ToLowerInvariant()} will join as {savedRole}.";
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            StatusMessage = "Admin access is required to manage the roster.";
        }
        catch (Exception ex)
        {
            CrashLogger.Log("AdminInvites.Add", ex);
            StatusMessage = "We couldn't add that entry. Please try again.";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task DeleteAsync(InviteRow? row)
    {
        if (row is null || IsBusy) return;
        IsBusy = true;
        try
        {
            await _admin.DeleteInviteAsync(row.Email);
            await LoadInnerAsync();
            StatusMessage = $"Removed {row.Email}.";
        }
        catch (Exception ex)
        {
            CrashLogger.Log("AdminInvites.Delete", ex);
            StatusMessage = "We couldn't remove that entry. Please try again.";
        }
        finally { IsBusy = false; }
    }

    // Reload without the IsBusy guard, for use while already inside a busy block.
    private async Task LoadInnerAsync()
    {
        var entries = await _admin.GetInvitesAsync();
        Invites = new ObservableCollection<InviteRow>(entries.Select(ToRow));
    }

    private static InviteRow ToRow(RosterEntry entry) => new(
        entry.Email,
        entry.Invite.Role,
        entry.Invite.Roles.Contains(nameof(Role.Journalist), StringComparer.Ordinal));
}
