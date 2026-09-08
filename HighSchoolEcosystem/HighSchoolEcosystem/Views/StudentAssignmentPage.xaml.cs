using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.ViewModels;

namespace HighSchoolEcosystem.Views;

public partial class StudentAssignmentPage : ContentPage
{
    private readonly StudentAssignmentViewModel _viewModel;
    private string? _classId;
    private string? _assignmentId;
    private string _title = string.Empty;
    private string _className = string.Empty;

    public StudentAssignmentPage(StudentAssignmentViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    /// <summary>Called before pushing, to say which assignment is being opened.</summary>
    public void Load(string classId, string assignmentId, string title, string className)
    {
        _classId = classId;
        _assignmentId = assignmentId;
        _title = title;
        _className = className;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_classId is not null && _assignmentId is not null)
        {
            await _viewModel.LoadAsync(_classId, _assignmentId, _title, _className);
        }
    }

    /// <summary>
    /// The file picker lives in the view: FilePicker is a platform UI API, and
    /// putting it behind the view model would make the view model untestable
    /// without a UI thread.
    /// </summary>
    private async void OnChooseFileClicked(object? sender, EventArgs e)
    {
        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Choose the file to hand in"
            });

            // Null means they backed out. Not an error.
            if (result is null) return;

            var stream = await result.OpenReadAsync();

            var contentType = string.IsNullOrWhiteSpace(result.ContentType)
                ? GuessContentType(result.FileName)
                : result.ContentType;

            _viewModel.AttachFile(stream, result.FileName, contentType);
        }
        catch (PermissionException)
        {
            await DisplayAlert("Permission needed",
                "Allow file access in your device settings to attach a file.", "OK");
        }
        catch (Exception ex)
        {
            CrashLogger.Log("StudentAssignment.PickFile", ex);
            await DisplayAlert("Couldn't open that file",
                "Please try a different file.", "OK");
        }
    }

    /// <summary>
    /// Extension-to-MIME fallback for platforms whose picker doesn't report a
    /// content type. Storage rules require one, and a null denies with no
    /// explanation.
    /// </summary>
    private static string GuessContentType(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".heic" => "image/heic",
            ".txt" => "text/plain",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".ppt" => "application/vnd.ms-powerpoint",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".zip" => "application/zip",
            _ => "application/octet-stream"
        };
    }
}
