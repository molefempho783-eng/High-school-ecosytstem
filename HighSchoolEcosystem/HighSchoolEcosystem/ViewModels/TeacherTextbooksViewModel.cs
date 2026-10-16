using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HighSchoolEcosystem.Models;
using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.Services.Interfaces;

namespace HighSchoolEcosystem.ViewModels;

/// <summary>One uploaded textbook as a row.</summary>
public sealed record TextbookJobRow(TextbookJob Source)
{
    public string Id => Source.Id;
    public string Title => Source.DisplayTitle;
    public string StatusLine => Source.StatusLine;
    public bool IsWorking => Source.IsWorking;
    public bool IsFailed => Source.Status == TextbookStatus.Failed;
    public string Meta => $"{Source.FileName} · uploaded {Source.CreatedAt.ToLocalTime():d MMM, HH:mm}";
}

/// <summary>
/// "Create from a textbook": upload a PDF, then watch the AI draft it. Each
/// upload becomes a row; tapping one opens its review page.
/// </summary>
public partial class TeacherTextbooksViewModel : ObservableObject
{
    /// <summary>Gemini's own ceiling, also enforced by storage.rules.</summary>
    public const long MaxPdfBytes = 50L * 1024 * 1024;

    private readonly ITextbookService _textbooks;
    private string _classId = string.Empty;

    public TeacherTextbooksViewModel(ITextbookService textbooks) => _textbooks = textbooks;

    [ObservableProperty] private string className = string.Empty;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isRefreshing;
    [ObservableProperty] private string busyMessage = string.Empty;
    [ObservableProperty] private string statusMessage = string.Empty;

    [ObservableProperty] private ObservableCollection<TextbookJobRow> jobs = [];
    [ObservableProperty] private bool hasJobs;

    /// <summary>True while any textbook is still being read, so the page keeps polling.</summary>
    [ObservableProperty] private bool anyWorking;

    public bool IsConfigured => _textbooks.IsConfigured;

    public string ClassId => _classId;

    public void Initialise(string classId, string className)
    {
        _classId = classId;
        ClassName = className;
    }

    public async Task LoadAsync()
    {
        if (IsBusy || string.IsNullOrEmpty(_classId)) return;
        IsBusy = true;
        BusyMessage = string.Empty;
        try
        {
            await ReloadJobsAsync();
            StatusMessage = IsConfigured
                ? StatusMessage
                : "File uploads aren't set up for this school, so textbooks can't be uploaded yet.";
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Textbooks.Load", ex);
            StatusMessage = "We couldn't load your textbooks. Pull down to try again.";
        }
        finally
        {
            IsBusy = false;
            IsRefreshing = false;
        }
    }

    /// <summary>
    /// Polling refresh while the AI works: no spinner and no error message, so
    /// a dropped connection for one tick doesn't flash a warning.
    /// </summary>
    public async Task RefreshQuietlyAsync()
    {
        if (IsBusy || string.IsNullOrEmpty(_classId)) return;
        try { await ReloadJobsAsync(); }
        catch (Exception ex) { CrashLogger.Log("Textbooks.Poll", ex); }
    }

    private async Task ReloadJobsAsync()
    {
        var jobs = await _textbooks.GetJobsAsync(_classId);
        Jobs = new ObservableCollection<TextbookJobRow>(jobs.Select(j => new TextbookJobRow(j)));
        HasJobs = Jobs.Count > 0;
        AnyWorking = jobs.Any(j => j.IsWorking);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        // Not `=> LoadAsync()`: that returns early while busy and would leave
        // the pull-to-refresh spinner hanging.
        try { await LoadAsync(); }
        finally { IsRefreshing = false; }
    }

    /// <summary>
    /// Uploads the picked PDF and queues it. The page picks the file and checks
    /// its size first; this owns the upload and its errors.
    /// </summary>
    public async Task StartAsync(Stream pdf, string fileName, long sizeBytes)
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = string.Empty;
        BusyMessage = "Uploading the textbook. A big PDF can take a few minutes on a school connection…";
        try
        {
            await _textbooks.StartImportAsync(_classId, fileName, pdf, sizeBytes);
            StatusMessage = "Uploaded. The AI is reading it now — this usually takes a few minutes. "
                          + "You can leave this page and come back.";
        }
        catch (InvalidOperationException ex)
        {
            // Storage's own explanation (rules not deployed, bucket missing).
            StatusMessage = ex.Message;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "The upload took too long and was stopped. Try again on a faster connection, "
                          + "or split the PDF into smaller parts.";
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            StatusMessage = "You can only add textbooks to classes you teach.";
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Textbooks.Start", ex);
            StatusMessage = "We couldn't upload that textbook. Please try again.";
        }
        finally
        {
            BusyMessage = string.Empty;
            IsBusy = false;
        }

        await LoadAsync();
    }
}
