using HighSchoolEcosystem.ViewModels;
namespace HighSchoolEcosystem.Views;
public partial class ForgotPasswordPage : ContentPage
{
    public ForgotPasswordPage(ForgotPasswordViewModel viewModel) { InitializeComponent(); BindingContext = viewModel; }
}
