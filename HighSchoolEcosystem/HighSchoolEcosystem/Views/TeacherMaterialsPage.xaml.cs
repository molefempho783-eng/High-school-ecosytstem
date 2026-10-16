using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace HighSchoolEcosystem.Views;

public partial class TeacherMaterialsPage : ContentPage
{
    private readonly TeacherMaterialsViewModel _viewModel;

    private readonly IServiceProvider _services;

    public TeacherMaterialsPage(TeacherMaterialsViewModel viewModel, IServiceProvider services)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _services = services;
    }

    private async void OnTextbookImportClicked(object? sender, EventArgs e)
    {
        try
        {
            var page = _services.GetRequiredService<TeacherTextbooksPage>();
            page.Load(_viewModel.ClassId, _viewModel.ClassName);
            await Navigation.PushAsync(page);
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Navigate:TeacherTextbooks", ex);
        }
    }

    public void Load(string classId, string className)
        => _viewModel.Initialise(classId, className);

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    /// <summary>
    /// The file picker lives in the view, not the view model: FilePicker is a
    /// platform UI API, and putting it behind the view model would make the
    /// view model untestable without a UI thread.
    /// </summary>
    private async void OnChooseFileClicked(object? sender, EventArgs e)
    {
        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Choose course material"
            });

            // Null means the user backed out. Not an error.
            if (result is null) return;

            var stream = await result.OpenReadAsync();

            // ContentType is null on some platforms, so fall back to the
            // extension. Storage rules check the content type, and an empty one
            // is rejected.
            var contentType = string.IsNullOrWhiteSpace(result.ContentType)
                ? GuessContentType(result.FileName)
                : result.ContentType;

            _viewModel.AttachFile(stream, result.FileName, contentType);
        }
        catch (PermissionException)
        {
            await DisplayAlertAsync("Permission needed",
                "Allow file access in your device settings to upload material.", "OK");
        }
        catch (Exception ex)
        {
            CrashLogger.Log("TeacherMaterials.PickFile", ex);
            await DisplayAlertAsync("Couldn't open that file",
                "Please try a different file.", "OK");
        }
    }

    /// <summary>
    /// Minimal extension-to-MIME map covering what storage.rules accepts.
    /// Anything unrecognised gets a type the rules will reject, which is the
    /// safe direction — better a clear refusal than an unopenable upload.
    /// </summary>
    private static string GuessContentType(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".txt" => "text/plain",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".ppt" => "application/vnd.ms-powerpoint",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            _ => "application/octet-stream"
        };
    }
}
