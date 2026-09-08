using HighSchoolEcosystem.ViewModels;

namespace HighSchoolEcosystem.Views;

public partial class AdminUserDetailPage : ContentPage
{
    private readonly AdminUserDetailViewModel _viewModel;
    private string? _uid;

    public AdminUserDetailPage(AdminUserDetailViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;

        _viewModel.ConfirmSelfDemotion = () => DisplayAlert(
            "Remove your own admin access?",
            "You'll lose access to these admin tools immediately. If you're the only administrator, "
            + "restoring it means editing the user document in the Firebase console.",
            "Yes, change my role", "Cancel");

        _viewModel.Saved += async (_, _) =>
        {
            // Back to the list, which reloads on appearing and shows the new role.
            try { await Navigation.PopAsync(); }
            catch (Exception ex) { Services.CrashLogger.Log("AdminUserDetail.Pop", ex); }
        };
    }

    /// <summary>Called by AdminUsersPage before pushing, to say who is being edited.</summary>
    public void Load(string uid) => _uid = uid;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_uid is not null) await _viewModel.LoadAsync(_uid);
    }
}
