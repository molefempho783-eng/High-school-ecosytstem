using HighSchoolEcosystem.ViewModels;

namespace HighSchoolEcosystem.Views;

public partial class TeacherTimetablePage : ContentPage
{
    private readonly TeacherTimetableViewModel _viewModel;

    public TeacherTimetablePage(TeacherTimetableViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }
}
