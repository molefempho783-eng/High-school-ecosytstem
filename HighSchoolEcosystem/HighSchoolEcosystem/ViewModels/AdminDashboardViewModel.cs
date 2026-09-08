using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HighSchoolEcosystem.Models;
using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.Services.Firebase;
using HighSchoolEcosystem.Services.Interfaces;

namespace HighSchoolEcosystem.ViewModels;

/// <summary>
/// The Admin home screen. Deliberately distinct from <see cref="DashboardViewModel"/>:
/// admins land on management tools rather than the student/teacher content view.
/// </summary>
public partial class AdminDashboardViewModel : DashboardShellViewModel
{
    private readonly IUserProfileService _userProfileService;


    /// <summary>True when this admin also holds the Teacher role.</summary>
    [ObservableProperty] private bool isAlsoTeacher;

    /// <summary>
    /// True when this user also holds a role whose content lives on the normal
    /// dashboard — Student, Parent, Coach or Visitor. A teacher can be a student
    /// too, and that half of their account has to stay reachable.
    /// </summary>
    [ObservableProperty] private bool hasPersonalView;

    /// <summary>Labels the button with whichever of those roles they hold.</summary>
    [ObservableProperty] private string personalViewLabel = "Switch to my school view";

    private void ApplyPersonalView(UserProfile? profile)
    {
        if (profile is null)
        {
            HasPersonalView = false;
            BuildRail();
            return;
        }

        var personal = new[] { Role.Student, Role.Parent, Role.Coach, Role.Visitor }
            .Where(profile.HasRole)
            .ToList();

        HasPersonalView = personal.Count > 0;
        PersonalViewLabel = personal.Count == 1
            ? $"Switch to {personal[0].ToString().ToLowerInvariant()} view"
            : "Switch to my school view";
    }



    /// <summary>Set by the page. See DashboardViewModel for why this split exists.</summary>
    public Action<string>? OnRailAction { get; set; }

    /// <summary>
    /// Rebuilds the rail for the roles this user holds. Called once the profile
    /// has loaded, so cross-links only appear for roles they actually have.
    /// </summary>
    private void BuildRail()
    {
        var items = new List<RailItem>
        {
            new("\U0001F465", "Manage users", new RelayCommand(() => OnRailAction?.Invoke("users"))),
            new("\u2709", "Roster and invites", new RelayCommand(() => OnRailAction?.Invoke("invites"))),
            new("\u2699", "School settings", new RelayCommand(() => OnRailAction?.Invoke("settings"))),
            new("\U0001F4E2", "Announcements", new RelayCommand(() => OnRailAction?.Invoke("announcements"))){ StartsGroup = true },
            new("\U0001F3C6", "Sport", new RelayCommand(() => OnRailAction?.Invoke("sport")))
        };

        if (IsAlsoTeacher)
        {
            items.Add(new("\U0001F4D8", "Teacher tools",
                new RelayCommand(() => OnRailAction?.Invoke("teacher"))) { StartsGroup = true });
        }

        if (HasPersonalView)
        {
            items.Add(new("\U0001F464", PersonalViewLabel,
                new RelayCommand(() => OnRailAction?.Invoke("personal")))
            { StartsGroup = !IsAlsoTeacher });
        }

        items.Add(new("\u25CF", "Account",
            new RelayCommand(() => OnRailAction?.Invoke("account"))) { StartsGroup = true });

        RailItems = new ObservableCollection<RailItem>(items);
    }

    public AdminDashboardViewModel(IUserProfileService userProfileService, ISchoolInfoProvider schoolInfo)
    {
        _userProfileService = userProfileService;
        SchoolName = schoolInfo.Current.DisplayName;

        // Built here as well as after load, so a failed profile read leaves a
        // usable rail rather than an empty strip.
        BuildRail();
    }

    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            var profile = await _userProfileService.GetOrCreateCurrentProfileAsync();
            DisplayName = profile?.DisplayName ?? "Administrator";
            IsAlsoTeacher = profile?.HasRole(Role.Teacher) ?? false;
            ApplyPersonalView(profile);
        }
        catch (ProfileVerificationRequiredException)
        {
            StatusMessage = "Verify your email address to finish setting up your account.";
        }
        catch (Exception ex)
        {
            CrashLogger.Log("AdminDashboard", ex);
            StatusMessage = "We couldn't load your details. Pull down or reopen to retry.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
