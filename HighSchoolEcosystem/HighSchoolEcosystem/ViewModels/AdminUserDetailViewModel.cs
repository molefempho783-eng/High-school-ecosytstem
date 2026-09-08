using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HighSchoolEcosystem.Models;
using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.Services.Interfaces;

namespace HighSchoolEcosystem.ViewModels;

/// <summary>A student a parent can be linked to, with its selected state.</summary>
public partial class StudentLink : ObservableObject
{
    public string Uid { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    [ObservableProperty] private bool isLinked;
}

/// <summary>An additional role, tickable alongside the primary one.</summary>
public partial class RoleOption : ObservableObject
{
    public Role Role { get; init; }
    public string Name => Role.ToString();
    [ObservableProperty] private bool isGranted;
}

public partial class AdminUserDetailViewModel : ObservableObject
{
    private readonly IAdminService _admin;
    private readonly IFirebaseAuthService _auth;

    private string _uid = string.Empty;
    private Role _originalRole;

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private string displayName = string.Empty;
    [ObservableProperty] private string email = string.Empty;

    [ObservableProperty] private ObservableCollection<Role> availableRoles =
        new(Enum.GetValues<Role>());

    [ObservableProperty] private Role selectedRole;

    /// <summary>
    /// Every role except the selected primary, tickable as an extra. This is
    /// what lets an Admin also be a Teacher, or a Student also be a Journalist.
    /// </summary>
    [ObservableProperty] private ObservableCollection<RoleOption> extraRoles = [];

    /// <summary>Student picker, shown only when the selected role is Parent.</summary>
    [ObservableProperty] private ObservableCollection<StudentLink> students = [];
    [ObservableProperty] private bool isParentSelected;

    /// <summary>True when the admin is editing their own account.</summary>
    [ObservableProperty] private bool isSelf;

    /// <summary>Set when the change would remove the admin's own admin access.</summary>
    [ObservableProperty] private bool isSelfDemotion;

    public AdminUserDetailViewModel(IAdminService admin, IFirebaseAuthService auth)
    {
        _admin = admin;
        _auth = auth;
    }

    partial void OnSelectedRoleChanged(Role value) => UpdateRoleFlags(value);

    private void UpdateRoleFlags(Role value)
    {
        IsParentSelected = value == Role.Parent;

        // Losing admin means losing it as BOTH primary and extra — an admin who
        // demotes their primary role but keeps Admin ticked hasn't demoted.
        IsSelfDemotion = IsSelf && value != Role.Admin
            && !ExtraRoles.Any(r => r.Role == Role.Admin && r.IsGranted);

        RebuildExtraRoles(value);
    }

    /// <summary>
    /// Rebuilds the extra-role list to exclude the current primary, carrying
    /// existing ticks across so changing the primary doesn't silently drop them.
    /// </summary>
    private void RebuildExtraRoles(Role primary)
    {
        var granted = ExtraRoles.Where(r => r.IsGranted).Select(r => r.Role).ToHashSet();

        ExtraRoles = new ObservableCollection<RoleOption>(
            Enum.GetValues<Role>()
                .Where(r => r != primary)
                .Select(r => new RoleOption { Role = r, IsGranted = granted.Contains(r) }));
    }

    public async Task LoadAsync(string uid)
    {
        if (IsBusy) return;
        _uid = uid;
        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            var all = await _admin.GetUsersAsync();

            var target = all.FirstOrDefault(u => u.Uid == uid);
            if (target is null)
            {
                StatusMessage = "That user no longer exists.";
                return;
            }

            DisplayName = string.IsNullOrWhiteSpace(target.DisplayName) ? target.Email : target.DisplayName;
            Email = target.Email;
            _originalRole = target.Role;
            IsSelf = _auth.CurrentSession?.UserId == uid;
            SelectedRole = target.Role;

            // Seed ticks from the stored Roles before UpdateRoleFlags rebuilds
            // the list, so they survive the rebuild.
            ExtraRoles = new ObservableCollection<RoleOption>(
                Enum.GetValues<Role>()
                    .Where(r => r != target.Role)
                    .Select(r => new RoleOption { Role = r, IsGranted = target.HasRole(r) }));

            // Called explicitly: [ObservableProperty] suppresses the change
            // notification when the value already equals the field default
            // (Student), which would leave these flags stale.
            UpdateRoleFlags(SelectedRole);

            Students = new ObservableCollection<StudentLink>(
                all.Where(u => u.HasRole(Role.Student) && u.Uid != uid)
                   .Select(s => new StudentLink
                   {
                       Uid = s.Uid,
                       Name = string.IsNullOrWhiteSpace(s.DisplayName) ? s.Email : s.DisplayName,
                       IsLinked = target.LinkedStudentIds.Contains(s.Uid)
                   }));
        }
        catch (Exception ex)
        {
            CrashLogger.Log("AdminUserDetail.Load", ex);
            StatusMessage = "We couldn't load that user. Please go back and try again.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Raised when the save succeeds, so the page can pop back to the list.
    /// </summary>
    public event EventHandler? Saved;

    /// <summary>
    /// Asks the page to confirm removing the admin's own access. A property
    /// rather than an event because the page assigns it directly and we need a
    /// return value, which multicast events can't give us cleanly.
    /// </summary>
    public Func<Task<bool>>? ConfirmSelfDemotion { get; set; }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy) return;

        var extras = ExtraRoles.Where(r => r.IsGranted).Select(r => r.Role).ToList();

        // Recomputed here rather than trusting the cached flag: ticking an extra
        // role doesn't re-run UpdateRoleFlags, so IsSelfDemotion can be stale.
        var losingAdmin = IsSelf && SelectedRole != Role.Admin && !extras.Contains(Role.Admin);
        IsSelfDemotion = losingAdmin;

        if (losingAdmin && ConfirmSelfDemotion is not null)
        {
            // Losing your own admin rights can't be undone from inside the app —
            // if you're the only admin, recovery means editing Firestore directly.
            if (!await ConfirmSelfDemotion.Invoke()) return;
        }

        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            await _admin.SetUserRolesAsync(_uid, SelectedRole, extras);
            _originalRole = SelectedRole;

            // Only meaningful for parents. Clearing the list when someone stops
            // being a parent avoids leaving stale access behind.
            var isParent = SelectedRole == Role.Parent || extras.Contains(Role.Parent);
            var linked = isParent
                ? Students.Where(s => s.IsLinked).Select(s => s.Uid).ToList()
                : [];
            await _admin.SetLinkedStudentsAsync(_uid, linked);

            StatusMessage = "Saved.";
            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            StatusMessage = "You don't have permission to make that change.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "That took too long. Check your connection and try again.";
        }
        catch (Exception ex)
        {
            CrashLogger.Log("AdminUserDetail.Save", ex);
            StatusMessage = "We couldn't save that change. Please try again.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
