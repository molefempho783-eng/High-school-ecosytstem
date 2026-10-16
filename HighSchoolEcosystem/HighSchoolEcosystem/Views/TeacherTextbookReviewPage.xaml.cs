using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.ViewModels;

namespace HighSchoolEcosystem.Views;

public partial class TeacherTextbookReviewPage : ContentPage
{
    private readonly TeacherTextbookReviewViewModel _viewModel;
    private bool _visible;
    private int _pollGeneration;

    public TeacherTextbookReviewPage(TeacherTextbookReviewViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    public void Load(string classId, string jobId) => _viewModel.Initialise(classId, jobId);

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _visible = true;
        await _viewModel.LoadAsync();

        // Keep re-reading while chapters are still being drafted.
        var generation = ++_pollGeneration;
        Dispatcher.StartTimer(TimeSpan.FromSeconds(8), () =>
        {
            if (!_visible || generation != _pollGeneration) return false;
            if (_viewModel.AnyWorking) _ = _viewModel.RefreshQuietlyAsync();
            return true;
        });
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _visible = false;
    }

    private async void OnChapterTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not TextbookChapterRow row) return;
        await _viewModel.SelectAsync(row);
        await ScrollToEditorAsync();
    }

    private async void OnRemoveQuestionClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not TextbookQuestionRow row) return;
        await _viewModel.RemoveQuestionAsync(row);
    }

    private async void OnPublishClicked(object? sender, EventArgs e)
    {
        var sure = await DisplayAlertAsync("Publish this chapter?",
            "Students in the class will see the unit and its study notes straight away, "
            + "and the practice quiz will be set with a due date two weeks from today.",
            "Publish", "Not yet");
        if (sure) await _viewModel.PublishAsync();
    }

    private async void OnRetryClicked(object? sender, EventArgs e)
    {
        var sure = await DisplayAlertAsync("Write this chapter again?",
            "The AI will redo the notes and quiz. Any edits you've made to this draft will be replaced.",
            "Write again", "Cancel");
        if (sure) await _viewModel.RetryAsync();
    }

    private async void OnDiscardClicked(object? sender, EventArgs e)
    {
        var sure = await DisplayAlertAsync("Discard this draft?",
            "The chapter's notes and quiz will be removed. This can't be undone.",
            "Discard", "Keep it");
        if (sure) await _viewModel.DiscardAsync();
    }

    private async void OnDeleteJobClicked(object? sender, EventArgs e)
    {
        var sure = await DisplayAlertAsync("Delete this import?",
            "All drafts from this textbook will be removed. Chapters you've already published stay in the class. "
            + "If nothing was published, the uploaded PDF is deleted too.",
            "Delete", "Cancel");
        if (!sure) return;

        try
        {
            if (await _viewModel.DeleteJobAsync()) await Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            CrashLogger.Log("TextbookReview.DeleteJob.Pop", ex);
        }
    }

    private async Task ScrollToEditorAsync()
    {
        try
        {
            await ContentScroll.ScrollToAsync(EditorSection, ScrollToPosition.Start, animated: true);
        }
        catch (Exception ex)
        {
            // Scrolling to something not yet laid out throws; not worth an error.
            CrashLogger.Log("TextbookReview.ScrollToEditor", ex);
        }
    }
}
