using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace HighSchoolEcosystem.Views;

public partial class TeacherTextbooksPage : ContentPage
{
    private readonly TeacherTextbooksViewModel _viewModel;
    private readonly IServiceProvider _services;
    private bool _visible;

    // Bumped on every appearance so a timer left over from a previous visit
    // stops itself instead of running alongside the new one.
    private int _pollGeneration;

    public TeacherTextbooksPage(TeacherTextbooksViewModel viewModel, IServiceProvider services)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _services = services;
    }

    public void Load(string classId, string className) => _viewModel.Initialise(classId, className);

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _visible = true;
        await _viewModel.LoadAsync();
        StartPolling();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _visible = false;
    }

    /// <summary>
    /// Re-reads every 8 seconds while a textbook is still being processed, so
    /// progress appears without pulling to refresh. Stops when the page is
    /// left — returning false from the callback ends the timer.
    /// </summary>
    private void StartPolling()
    {
        var generation = ++_pollGeneration;
        Dispatcher.StartTimer(TimeSpan.FromSeconds(8), () =>
        {
            if (!_visible || generation != _pollGeneration) return false;
            if (_viewModel.AnyWorking) _ = _viewModel.RefreshQuietlyAsync();
            return true;
        });
    }

    private async void OnChooseTextbookClicked(object? sender, EventArgs e)
    {
        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Choose a textbook PDF",
                FileTypes = FilePickerFileType.Pdf
            });
            if (result is null) return;

            // Read into memory so the size is known up front and the stream
            // survives the upload. 50 MB is the ceiling, which is fine to hold.
            using var source = await result.OpenReadAsync();
            var buffer = new MemoryStream();
            await source.CopyToAsync(buffer);

            if (buffer.Length > TeacherTextbooksViewModel.MaxPdfBytes)
            {
                await DisplayAlertAsync("That PDF is too large",
                    $"The AI can read PDFs up to 50 MB. This one is {buffer.Length / (1024 * 1024)} MB. "
                    + "Try splitting it into parts, for example one per term.", "OK");
                return;
            }

            var sure = await DisplayAlertAsync("Send this textbook to the AI?",
                $"\"{result.FileName}\" will be uploaded and read by Google's Gemini AI to draft your course. "
                + "Nothing is shown to students until you publish it.",
                "Upload", "Cancel");
            if (!sure) return;

            buffer.Position = 0;
            await _viewModel.StartAsync(buffer, result.FileName, buffer.Length);
        }
        catch (PermissionException)
        {
            await DisplayAlertAsync("Permission needed",
                "Allow file access in your device settings to upload a textbook.", "OK");
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Textbooks.Pick", ex);
            await DisplayAlertAsync("Couldn't open that file", "Please try a different PDF.", "OK");
        }
    }

    private async void OnJobTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not TextbookJobRow row) return;

        try
        {
            var page = _services.GetRequiredService<TeacherTextbookReviewPage>();
            page.Load(_viewModel.ClassId, row.Id);
            await Navigation.PushAsync(page);
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Navigate:TextbookReview", ex);
        }
    }
}
