using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.ViewModels;

namespace HighSchoolEcosystem.Views;

public partial class NewspaperPage : ContentPage
{
    private readonly NewspaperViewModel _viewModel;

    public NewspaperPage(NewspaperViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    /// <summary>Opens with a blank article form. Call before pushing.</summary>
    public void StartComposing() => _viewModel.RequestCompose();

    /// <summary>Opens with the given article in the form. Call before pushing.</summary>
    public void StartEditing(string articleId) => _viewModel.RequestEdit(articleId);

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
        if (_viewModel.IsFormVisible) await ScrollToFormAsync();
    }

    /// <summary>
    /// The whole article in the shared detail sheet. Edit and Delete appear
    /// separately: a teacher can take an article down but not rewrite it.
    /// </summary>
    private async void OnArticleTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not ArticleRow row) return;

        try
        {
            var action = await DetailPopup.ShowAsync(
                this, row.Title, row.Source.DetailText,
                "Newspaper", NewspaperAccent, row.CanEdit, row.CanDelete);

            switch (action)
            {
                case DetailAction.Edit:
                    _viewModel.StartEdit(row);
                    await ScrollToFormAsync();
                    break;

                case DetailAction.Delete:
                    if (await ConfirmDeleteAsync(this, row.Title))
                        await _viewModel.DeleteAsync(row);
                    break;
            }
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Newspaper.ArticleDetail", ex);
        }
    }

    /// <summary>
    /// Shared with the dashboard widget so every place an article can be
    /// removed asks the same question.
    /// </summary>
    internal static Task<bool> ConfirmDeleteAsync(Page page, string title)
        => page.DisplayAlertAsync(
            "Remove this article?",
            $"\"{title}\" will be taken out of the Newspaper for everyone. This can't be undone.",
            "Remove", "Keep it");

    /// <summary>The Newspaper's accent, from the palette rather than a raw hex.</summary>
    internal static Color NewspaperAccent
    {
        get
        {
            var resources = Application.Current?.Resources;
            if (resources is not null
                && resources.TryGetValue("BrandBright", out var value)
                && value is Color colour)
            {
                return colour;
            }
            return Colors.SteelBlue;
        }
    }

    private async Task ScrollToFormAsync()
    {
        try
        {
            await ContentScroll.ScrollToAsync(ComposeSection, ScrollToPosition.Start, animated: true);
        }
        catch (Exception ex)
        {
            // Scrolling to something not yet laid out throws; not worth an error.
            CrashLogger.Log("Newspaper.ScrollToForm", ex);
        }
    }

    /// <summary>
    /// Picks the article's picture. Lives in the view because FilePicker is a
    /// platform UI API; the bytes are handed over rather than the stream, which
    /// doesn't reliably survive being held until Publish.
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

            // Checked here as well as in storage.rules, so a journalist finds
            // out before a slow upload rather than after it.
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
            CrashLogger.Log("Newspaper.PickImage", ex);
            await DisplayAlertAsync("Couldn't open that file", "Please try a different picture.", "OK");
        }
    }
}
