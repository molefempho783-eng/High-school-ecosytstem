using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HighSchoolEcosystem.Models;
using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.Services.Interfaces;

namespace HighSchoolEcosystem.ViewModels;

/// <summary>One drafted chapter as a row in the review list.</summary>
public sealed partial class TextbookChapterRow : ObservableObject
{
    public TextbookChapterRow(TextbookChapter source) => Source = source;

    public TextbookChapter Source { get; }
    public string Id => Source.Id;
    public string Title => $"{Source.Index}. {Source.Title}";

    public string StatusLabel => Source.Status switch
    {
        TextbookStatus.Pending or TextbookStatus.Generating => "Drafting…",
        TextbookStatus.Ready => $"Ready to review · {Source.QuestionCount} quiz questions",
        TextbookStatus.Published => "Published to the class",
        TextbookStatus.Failed => string.IsNullOrWhiteSpace(Source.Error) ? "Failed" : $"Failed: {Source.Error}",
        _ => Source.Status
    };

    public string Meta => $"{Source.PagesLabel} · {StatusLabel}";
    public bool IsFailed => Source.IsFailed;

    /// <summary>Shows the green check: reviewed and copied into the class.</summary>
    public bool IsPublished => Source.IsPublished;

    /// <summary>Highlights the chapter open in the editor.</summary>
    [ObservableProperty] private bool isSelected;
}

/// <summary>A draft question, with its options spelled out and the answer ticked.</summary>
public sealed record TextbookQuestionRow(TextbookQuestion Source, int Number)
{
    public string Id => Source.Id;
    public string Heading => $"{Number}. {Source.Text}";

    public string OptionsText => string.Join(Environment.NewLine,
        Source.Options.Select((o, i) => i == Source.CorrectIndex ? $"✓ {o}" : $"   {o}"));
}

/// <summary>
/// Review one textbook's drafts: read and edit each chapter's notes, prune its
/// quiz, then publish it into the class. Nothing reaches students until then.
/// </summary>
public partial class TeacherTextbookReviewViewModel : ObservableObject
{
    private readonly ITextbookService _textbooks;
    private string _classId = string.Empty;
    private string _jobId = string.Empty;
    private TextbookJob? _job;

    public TeacherTextbookReviewViewModel(ITextbookService textbooks) => _textbooks = textbooks;

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isRefreshing;
    [ObservableProperty] private string statusMessage = string.Empty;

    [ObservableProperty] private string bookTitle = string.Empty;
    [ObservableProperty] private string jobStatusLine = string.Empty;

    [ObservableProperty] private ObservableCollection<TextbookChapterRow> chapters = [];
    [ObservableProperty] private bool hasChapters;
    [ObservableProperty] private string readyCount = "0";

    /// <summary>"2 of 12 published" — progress through the book at a glance.</summary>
    [ObservableProperty] private string publishedSummary = string.Empty;
    [ObservableProperty] private bool anyWorking;

    // ---- the chapter open in the editor ----

    private TextbookChapter? _selected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPublish))]
    [NotifyPropertyChangedFor(nameof(CanRetry))]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    private bool hasSelection;

    [ObservableProperty] private string selectedHeading = string.Empty;
    [ObservableProperty] private string selectedMeta = string.Empty;
    [ObservableProperty] private string editTitle = string.Empty;
    [ObservableProperty] private string editNotes = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPublish))]
    [NotifyPropertyChangedFor(nameof(CanRetry))]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyPropertyChangedFor(nameof(IsSelectedPublished))]
    private string selectedStatus = string.Empty;

    [ObservableProperty] private ObservableCollection<TextbookQuestionRow> questions = [];
    [ObservableProperty] private bool hasQuestions;
    [ObservableProperty] private string questionsCount = "0";

    public bool CanEdit => HasSelection && SelectedStatus == TextbookStatus.Ready;
    public bool CanPublish => CanEdit;
    public bool CanRetry => HasSelection && SelectedStatus is TextbookStatus.Ready or TextbookStatus.Failed;
    public bool IsSelectedPublished => SelectedStatus == TextbookStatus.Published;

    public TextbookJob? Job => _job;

    public void Initialise(string classId, string jobId)
    {
        _classId = classId;
        _jobId = jobId;
    }

    public async Task LoadAsync()
    {
        if (IsBusy || string.IsNullOrEmpty(_jobId)) return;
        IsBusy = true;
        try
        {
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            CrashLogger.Log("TextbookReview.Load", ex);
            StatusMessage = "We couldn't load the drafts. Pull down to try again.";
        }
        finally
        {
            IsBusy = false;
            IsRefreshing = false;
        }
    }

    /// <summary>Polling while chapters are being drafted — no spinner, no errors.</summary>
    public async Task RefreshQuietlyAsync()
    {
        if (IsBusy || string.IsNullOrEmpty(_jobId)) return;
        try { await ReloadAsync(keepEdits: true); }
        catch (Exception ex) { CrashLogger.Log("TextbookReview.Poll", ex); }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try { await LoadAsync(); }
        finally { IsRefreshing = false; }
    }

    private async Task ReloadAsync(bool keepEdits = false)
    {
        _job = await _textbooks.GetJobAsync(_classId, _jobId);
        if (_job is null)
        {
            StatusMessage = "This textbook import no longer exists.";
            Chapters = [];
            HasChapters = false;
            return;
        }

        BookTitle = _job.DisplayTitle;
        JobStatusLine = _job.StatusLine;

        var list = await _textbooks.GetChaptersAsync(_classId, _jobId);
        var rows = list.Select(c => new TextbookChapterRow(c) { IsSelected = c.Id == _selected?.Id }).ToList();
        Chapters = new ObservableCollection<TextbookChapterRow>(rows);
        HasChapters = rows.Count > 0;
        ReadyCount = list.Count(c => c.IsReady).ToString();
        var published = list.Count(c => c.IsPublished);
        PublishedSummary = published == 0
            ? "None published yet"
            : published == list.Count
                ? $"All {list.Count} published"
                : $"{published} of {list.Count} published";
        AnyWorking = _job.IsWorking || list.Any(c => c.IsWorking);

        // Keep the open chapter in step — e.g. a retried one finishing — but
        // never overwrite what the teacher is typing.
        if (_selected is not null)
        {
            var current = list.FirstOrDefault(c => c.Id == _selected.Id);
            if (current is null) { CloseEditor(); }
            else if (!keepEdits || current.Status != _selected.Status) { await OpenAsync(current); }
        }
    }

    // ---- selecting a chapter ----

    public async Task SelectAsync(TextbookChapterRow? row)
    {
        if (row is null) return;
        foreach (var r in Chapters) r.IsSelected = r.Id == row.Id;
        try { await OpenAsync(row.Source); }
        catch (Exception ex)
        {
            CrashLogger.Log("TextbookReview.Open", ex);
            StatusMessage = "We couldn't open that chapter.";
        }
    }

    private async Task OpenAsync(TextbookChapter chapter)
    {
        _selected = chapter;
        HasSelection = true;
        SelectedStatus = chapter.Status;
        SelectedHeading = $"Chapter {chapter.Index}";
        SelectedMeta = new TextbookChapterRow(chapter).Meta;
        EditTitle = chapter.Title;
        EditNotes = chapter.Notes;

        IReadOnlyList<TextbookQuestion> list = chapter.IsReady || chapter.IsPublished
            ? await _textbooks.GetQuestionsAsync(_classId, _jobId, chapter.Id)
            : Array.Empty<TextbookQuestion>();
        Questions = new ObservableCollection<TextbookQuestionRow>(list.Select((q, i) => new TextbookQuestionRow(q, i + 1)));
        HasQuestions = Questions.Count > 0;
        QuestionsCount = Questions.Count.ToString();
    }

    [RelayCommand]
    private void CloseEditor()
    {
        _selected = null;
        HasSelection = false;
        SelectedStatus = string.Empty;
        foreach (var r in Chapters) r.IsSelected = false;
    }

    // ---- actions on the open chapter ----

    [RelayCommand]
    private async Task SaveDraftAsync()
    {
        if (_selected is not { } chapter || !CanEdit || IsBusy) return;
        await RunAsync("TextbookReview.Save", async () =>
        {
            await _textbooks.SaveChapterEditsAsync(_classId, _jobId, chapter.Id, EditTitle, EditNotes);
            StatusMessage = "Draft saved. Students can't see it until you publish.";
        });
    }

    public async Task RemoveQuestionAsync(TextbookQuestionRow? row)
    {
        if (_selected is not { } chapter || row is null || !CanEdit || IsBusy) return;
        await RunAsync("TextbookReview.RemoveQuestion", async () =>
        {
            await _textbooks.DeleteQuestionAsync(_classId, _jobId, chapter.Id, row.Id);
            var remaining = Questions.Where(q => q.Id != row.Id).Select(q => q.Source).ToList();
            Questions = new ObservableCollection<TextbookQuestionRow>(remaining.Select((q, i) => new TextbookQuestionRow(q, i + 1)));
            HasQuestions = Questions.Count > 0;
            QuestionsCount = Questions.Count.ToString();
        });
    }

    public async Task PublishAsync()
    {
        if (_selected is not { } chapter || _job is not { } job || !CanPublish || IsBusy) return;
        var title = EditTitle;
        await RunAsync("TextbookReview.Publish", async () =>
        {
            await _textbooks.PublishChapterAsync(_classId, job, chapter, EditTitle, EditNotes);
            StatusMessage = $"\"{title}\" is now in the class's course content"
                          + (HasQuestions ? ", with its practice quiz due in two weeks." : ".");
        }, reload: true);
    }

    public async Task RetryAsync()
    {
        if (_selected is not { } chapter || !CanRetry || IsBusy) return;
        await RunAsync("TextbookReview.Retry", async () =>
        {
            await _textbooks.RetryChapterAsync(_classId, _jobId, chapter.Id);
            StatusMessage = "The AI is writing this chapter again. It takes a minute or two.";
        }, reload: true);
    }

    public async Task DiscardAsync()
    {
        if (_selected is not { } chapter || IsBusy) return;
        await RunAsync("TextbookReview.Discard", async () =>
        {
            await _textbooks.DiscardChapterAsync(_classId, _jobId, chapter.Id);
            CloseEditor();
            StatusMessage = "Draft removed.";
        }, reload: true);
    }

    /// <summary>Deletes the whole import. Returns true when the page should close.</summary>
    public async Task<bool> DeleteJobAsync()
    {
        if (_job is not { } job || IsBusy) return false;
        var ok = false;
        await RunAsync("TextbookReview.DeleteJob", async () =>
        {
            await _textbooks.DeleteJobAsync(_classId, job);
            ok = true;
        });
        return ok;
    }

    /// <summary>Busy flag, error handling and an optional reload, shared by every action.</summary>
    private async Task RunAsync(string logKey, Func<Task> action, bool reload = false)
    {
        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            await action();
        }
        catch (InvalidOperationException ex)
        {
            StatusMessage = ex.Message;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            StatusMessage = "That was refused. You can only change drafts for classes you teach, "
                          + "and only once the AI has finished with them.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "That took too long. Check your connection and try again.";
        }
        catch (Exception ex)
        {
            CrashLogger.Log(logKey, ex);
            StatusMessage = "Something went wrong. Please try again.";
        }
        finally
        {
            IsBusy = false;
        }

        if (reload)
        {
            try { await ReloadAsync(); }
            catch (Exception ex) { CrashLogger.Log(logKey + ".Reload", ex); }
        }
    }
}
