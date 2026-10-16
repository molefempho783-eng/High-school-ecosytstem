using System.Text;
using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.ViewModels;

namespace HighSchoolEcosystem.Views;

public partial class TeacherStudentMarksPage : ContentPage
{
    private readonly TeacherStudentMarksViewModel _viewModel;

    public TeacherStudentMarksPage(TeacherStudentMarksViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    /// <summary>Called before pushing, to say whose marks these are.</summary>
    public void Load(string classId, string className, string studentId, string studentName)
        => _viewModel.Load(classId, className, studentId, studentName);

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    /// <summary>
    /// Writes the CSV to the cache directory and hands it to the share sheet.
    ///
    /// Cache, not app data: this is a copy for sending on, and the OS is free
    /// to reclaim it. Sharing rather than saving to a path because on Android
    /// and iOS the app has nowhere the user can browse to — the share sheet is
    /// how a file gets to email, Drive or a laptop.
    ///
    /// UTF-8 WITH a BOM. Excel on Windows reads a BOM-less UTF-8 CSV as the
    /// system codepage, which mangles every accented name in the school.
    /// </summary>
    private async void OnExportClicked(object? sender, EventArgs e)
    {
        try
        {
            var csv = _viewModel.BuildCsv();
            var path = Path.Combine(FileSystem.CacheDirectory, _viewModel.CsvFileName);

            await File.WriteAllTextAsync(path, csv, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = $"{_viewModel.StudentName} — marks",
                File = new ShareFile(path)
            });
        }
        catch (Exception ex)
        {
            CrashLogger.Log("TeacherStudentMarks.Export", ex);
            await DisplayAlertAsync(
                "Export failed",
                "We couldn't create the file. Check that there's space on the device and try again.",
                "OK");
        }
    }
}
