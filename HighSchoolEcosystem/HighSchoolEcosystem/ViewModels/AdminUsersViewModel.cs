using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HighSchoolEcosystem.Models;
using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.Services.Interfaces;

namespace HighSchoolEcosystem.ViewModels;

/// <summary>A user row, flattened for display.</summary>
public sealed record AdminUserRow(string Uid, string Name, string Email, string RoleSummary, string LinkedSummary)
{
    public string Subtitle => $"{RoleSummary} · {Email}";

    /// <summary>Drives visibility of the linked-children line.</summary>
    public bool HasLinked => LinkedSummary.Length > 0;
}

public partial class AdminUsersViewModel : ObservableObject
{
    private readonly IAdminService _admin;
    private List<UserProfile> _all = [];

    [ObservableProperty] private bool isBusy;

    // Separate from IsBusy on purpose. Binding RefreshView.IsRefreshing to IsBusy
    // is a feedback loop: LoadAsync sets IsBusy, which pushes IsRefreshing true,
    // which fires RefreshCommand, which early-returns on the IsBusy guard and
    // can leave the spinner stuck on forever.
    [ObservableProperty] private bool isRefreshing;

    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private ObservableCollection<AdminUserRow> users = [];

    public AdminUsersViewModel(IAdminService admin) => _admin = admin;

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            _all = [.. await _admin.GetUsersAsync()];
            ApplyFilter();

            if (_all.Count == 0)
                StatusMessage = "Nobody has registered yet.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "That took too long. Check your connection and try again.";
        }
        catch (HttpRequestException ex)
        {
            // The most likely cause by far is a non-admin reaching this screen,
            // or rules that haven't been deployed yet.
            StatusMessage = ex.StatusCode == System.Net.HttpStatusCode.Forbidden
                ? "You don't have permission to list users. Admin access is required."
                : "We couldn't load the user list. Check your connection and try again.";
        }
        catch (Exception ex)
        {
            CrashLogger.Log("AdminUsers", ex);
            StatusMessage = "We couldn't load the user list. Please try again.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try { await LoadAsync(); }
        finally { IsRefreshing = false; }
    }

    private void ApplyFilter()
    {
        var term = SearchText?.Trim();
        var matches = string.IsNullOrEmpty(term)
            ? _all
            : _all.Where(u =>
                DisplayNameOf(u).Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                (u.Email?.Contains(term, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
                // Every role held, so searching "Journalist" finds a Student who
                // also writes for the paper — not just people whose PRIMARY role
                // happens to match.
                RoleSummaryOf(u).Contains(term, StringComparison.CurrentCultureIgnoreCase));

        // Built from the unfiltered list so a parent's children resolve even
        // when the search has filtered those students out of view.
        var namesByUid = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var user in _all) namesByUid[user.Uid] = DisplayNameOf(user);

        Users = new ObservableCollection<AdminUserRow>(matches.Select(u =>
            new AdminUserRow(
                u.Uid,
                DisplayNameOf(u),
                u.Email,
                RoleSummaryOf(u),
                LinkedSummaryOf(u, namesByUid))));
    }

    private static string DisplayNameOf(UserProfile user)
        => string.IsNullOrWhiteSpace(user.DisplayName) ? user.Email : user.DisplayName;

    /// <summary>"Teacher + Admin" — primary role first, then any extras.</summary>
    private static string RoleSummaryOf(UserProfile user)
        => string.Join(" + ", user.AllRoles());

    /// <summary>
    /// Names the children a parent is linked to rather than counting them — the
    /// count alone doesn't tell an admin whether the link is the right one.
    /// </summary>
    private static string LinkedSummaryOf(UserProfile user, IReadOnlyDictionary<string, string> namesByUid)
    {
        if (user.LinkedStudentIds.Count == 0) return string.Empty;

        const int maxNamed = 3;

        var names = user.LinkedStudentIds
            // A linked student whose account was deleted still leaves their UID
            // behind. Say so plainly rather than showing a raw ID.
            .Select(id => namesByUid.TryGetValue(id, out var name) ? name : "unknown student")
            .ToList();

        var shown = string.Join(", ", names.Take(maxNamed));
        return names.Count <= maxNamed
            ? $"Parent of {shown}"
            : $"Parent of {shown} +{names.Count - maxNamed} more";
    }
}
