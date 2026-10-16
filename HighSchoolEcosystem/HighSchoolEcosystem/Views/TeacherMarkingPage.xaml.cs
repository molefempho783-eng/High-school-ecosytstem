using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.ViewModels;

namespace HighSchoolEcosystem.Views;

public partial class TeacherMarkingPage : ContentPage
{
    private readonly TeacherMarkingViewModel _viewModel;

    public TeacherMarkingPage(TeacherMarkingViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    public void Load(string classId, string assignmentId, string title, string className)
        => _viewModel.Initialise(classId, assignmentId, title, className);

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    /// <summary>
    /// Opens a student's attached file in the system browser. Not a WebView:
    /// the Storage download URL carries its own token and opens fine, and an
    /// embedded view would offer no way to save or print — which is what a
    /// teacher marking a stack of scans actually wants.
    /// </summary>
    private async void OnFileTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not MarkingRow row) return;
        if (!row.HasFile) return;

        try
        {
            await Launcher.Default.OpenAsync(new Uri(row.FileUrl));
        }
        catch (Exception ex)
        {
            CrashLogger.Log("TeacherMarking.OpenFile", ex);
            await DisplayAlertAsync("Can't open this",
                "We couldn't open that file. Check your connection and try again.", "OK");
        }
    }
}
