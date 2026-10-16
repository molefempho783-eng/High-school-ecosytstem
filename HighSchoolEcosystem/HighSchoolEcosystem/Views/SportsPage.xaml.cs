using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.ViewModels;

namespace HighSchoolEcosystem.Views;

public partial class SportsPage : ContentPage
{
    private readonly SportsViewModel _viewModel;

    public SportsPage(SportsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    /// <summary>
    /// Picks a picture for the post.
    ///
    /// The picker lives in the view because FilePicker is a platform UI API —
    /// the same split the material and submission uploads use. The bytes are
    /// read here and handed over, rather than the stream: the stream the picker
    /// returns doesn't reliably survive being held until Save.
    /// </summary>
    private async void OnChooseImageClicked(object? sender, EventArgs e)
    {
        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Choose a picture",
                FileTypes = FilePickerFileType.Images
            });

            if (result is null) return;

            using var stream = await result.OpenReadAsync();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            var bytes = buffer.ToArray();

            // Checked here as well as in storage.rules. The rule is the boundary
            // that matters, but finding out after a slow upload on a school
            // connection is a poor way to learn the file was too big.
            const long limit = 8 * 1024 * 1024;
            if (bytes.LongLength > limit)
            {
                await DisplayAlertAsync(
                    "That picture is too large",
                    $"Pictures must be under 8 MB. This one is {bytes.LongLength / (1024 * 1024)} MB. "
                        + "Try a photo taken at a lower resolution.",
                    "OK");
                return;
            }

            var contentType = string.IsNullOrWhiteSpace(result.ContentType)
                ? "image/jpeg"
                : result.ContentType;

            _viewModel.SetPickedImage(bytes, result.FileName, contentType);
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Sports.PickImage", ex);
            await DisplayAlertAsync("Couldn't open that file", "Please try a different picture.", "OK");
        }
    }
}
