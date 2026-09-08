using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HighSchoolEcosystem.Models;
using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.Services.Interfaces;

namespace HighSchoolEcosystem.ViewModels;

/// <summary>
/// One student's submission, as the teacher marks it.
///
/// An ObservableObject rather than a record: the grade and feedback boxes are
/// edited in place, and the row has to reflect its own saved state without the
/// whole list being rebuilt.
/// </summary>
public sealed partial class MarkingRow : ObservableObject
{
    public MarkingRow(Submission source, int outOf)
    {
        Source = source;
        OutOf = outOf;

        // Seed the editors from what's already recorded, so a teacher revising
        // a mark sees what they gave last time rather than an empty box.
        GradeText = source.IsMarked ? source.Grade.ToString() : string.Empty;
        Feedback = source.Feedback;
    }

    public Submission Source { get; }
    public int OutOf { get; }

    public string StudentId => Source.StudentId;
    public string StudentName => string.IsNullOrWhiteSpace(Source.StudentName)
        ? "Unknown student"
        : Source.StudentName;

    public string Initials
    {
        get
        {
            var name = StudentName;
            var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return "?";
            if (words.Length == 1) return words[0][..1].ToUpperInvariant();
            return (words[0][..1] + words[^1][..1]).ToUpperInvariant();
        }
    }

    public string SubmittedLabel => Source.SubmittedAt == default
        ? "Not submitted"
        : $"Handed in {Source.SubmittedAt.ToLocalTime():ddd d MMM, HH:mm}";

    public bool HasWritten => Source.HasWritten;
    public string WrittenAnswer => Source.Text;

    public bool HasFile => Source.HasFile;
    public string FileLabel => Source.FileLabel;
    public string FileUrl => Source.FileUrl;

    /// <summary>
    /// What the auto-marker awarded, when it ran. Shown to the teacher so they
    /// can see the multiple-choice score before deciding the overall mark —
    /// that's the whole point of surfacing it here.
    /// </summary>
    public bool HasAutoScore => Source.IsAutoMarked && Source.AutoScoreMax > 0;

    public string AutoScoreLabel => HasAutoScore
        ? $"Auto-marked: {Source.AutoScore} / {Source.AutoScoreMax}"
        : string.Empty;

    public string AutoFeedback => Source.Feedback;

    public bool NeedsMarking => Source.NeedsTeacherMarking;

    /// <summary>Amber while it waits, green once a human has graded it.</summary>
    public Color StatusColor => NeedsMarking
        ? Color.FromArgb("#A85400")
        : Color.FromArgb("#2E6B33");

    public string StatusLabel
    {
        get
        {
            if (Source.SubmittedAt == default) return "Nothing handed in";
            if (Source.IsTeacherMarked) return $"Marked {Source.TeacherMarkedAt.ToLocalTime():d MMM}";
            if (HasAutoScore && !NeedsMarking) return "Auto-marked";
            return "Needs marking";
        }
    }

    /// <summary>Bound to the grade entry. Kept as text so a blank box is possible.</summary>
    [ObservableProperty] private string gradeText = string.Empty;

    [ObservableProperty] private string feedback = string.Empty;

    /// <summary>Set briefly after a successful save, so the teacher sees it took.</summary>
    [ObservableProperty] private bool justSaved;
}

/// <summary>
/// The teacher's marking screen for one assignment.
///
/// This exists because auto-marking only covers multiple choice. Written
/// answers and uploaded files need a person, and before this screen the only
/// way to record that was editing Firestore by hand — which no teacher would do.
/// </summary>
public partial class TeacherMarkingViewModel : ObservableObject
{
    private readonly IClassService _classes;

    private string _classId = string.Empty;
    private string _assignmentId = string.Empty;

    public TeacherMarkingViewModel(IClassService classes) => _classes = classes;

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isRefreshing;
    [ObservableProperty] private string statusMessage = string.Empty;

    [ObservableProperty] private string assignmentTitle = string.Empty;
    [ObservableProperty] private string className = string.Empty;
    [ObservableProperty] private string pointsLabel = string.Empty;

    /// <summary>Submissions needing a human, listed first.</summary>
    [ObservableProperty] private ObservableCollection<MarkingRow> submissions = [];

    [ObservableProperty] private bool hasSubmissions;
    [ObservableProperty] private string emptyMessage = string.Empty;

    /// <summary>"4 of 26 still need marking" — the teacher's actual question.</summary>
    [ObservableProperty] private string progressLabel = string.Empty;

    [ObservableProperty] private string needsMarkingCount = "0";
    [ObservableProperty] private bool hasUnmarked;

    private int _outOf = 100;

    public void Initialise(string classId, string assignmentId, string title, string className)
    {
        _classId = classId;
        _assignmentId = assignmentId;
        AssignmentTitle = title;
        ClassName = className;
    }

    public async Task LoadAsync()
    {
        if (IsBusy || string.IsNullOrEmpty(_classId)) return;

        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            // The assignment gives the mark total, so the teacher sees "out of
            // 50" rather than having to remember it.
            var assignments = await _classes.GetAssignmentsAsync(_classId);
            var assignment = assignments.FirstOrDefault(a => a.Id == _assignmentId);

            _outOf = assignment?.Points ?? 100;
            PointsLabel = assignment is null ? string.Empty : $"Out of {assignment.Points}";

            var items = await _classes.GetSubmissionsForAssignmentAsync(_classId, _assignmentId);

            Submissions = new ObservableCollection<MarkingRow>(
                items.Select(s => new MarkingRow(s, _outOf)));

            HasSubmissions = Submissions.Count > 0;

            var unmarked = Submissions.Count(r => r.NeedsMarking);
            NeedsMarkingCount = unmarked.ToString();
            HasUnmarked = unmarked > 0;

            ProgressLabel = Submissions.Count == 0
                ? string.Empty
                : unmarked == 0
                    ? $"All {Submissions.Count} marked."
                    : $"{unmarked} of {Submissions.Count} still need marking.";

            EmptyMessage = HasSubmissions
                ? string.Empty
                : "Nobody has handed this in yet.";
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            StatusMessage = "Only this class's teacher can see these submissions.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "That took too long. Check your connection and try again.";
        }
        catch (Exception ex)
        {
            CrashLogger.Log("TeacherMarking.Load", ex);
            StatusMessage = "We couldn't load the submissions. Pull down to try again.";
        }
        finally
        {
            IsBusy = false;
            IsRefreshing = false;
        }
    }

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync();

    /// <summary>
    /// Saves one student's mark. Per-row rather than a single save button: a
    /// teacher marks a class over several sittings, and a form that loses
    /// everything on a dropped connection is worse than useless.
    /// </summary>
    [RelayCommand]
    private async Task SaveAsync(MarkingRow? row)
    {
        if (row is null || IsBusy) return;

        if (!int.TryParse(row.GradeText?.Trim(), out var grade))
        {
            StatusMessage = $"Enter a number for {row.StudentName}'s mark.";
            return;
        }

        if (grade < 0 || grade > _outOf)
        {
            StatusMessage = $"{row.StudentName}'s mark must be between 0 and {_outOf}.";
            return;
        }

        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            await _classes.SetGradeAsync(
                _classId, _assignmentId, row.StudentId, grade, row.Feedback ?? string.Empty);

            // Confirm in place rather than reloading. A full reload would
            // reorder the list under the teacher's finger mid-marking.
            row.JustSaved = true;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            StatusMessage = "Only this class's teacher can enter marks.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "That took too long. The mark wasn't saved — try again.";
        }
        catch (Exception ex)
        {
            CrashLogger.Log("TeacherMarking.Save", ex);
            StatusMessage = $"We couldn't save {row.StudentName}'s mark. Please try again.";
        }
        finally { IsBusy = false; }
    }
}
