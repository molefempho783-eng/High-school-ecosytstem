using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HighSchoolEcosystem.Models;
using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.Services.Interfaces;

namespace HighSchoolEcosystem.ViewModels;

/// <summary>
/// One answer choice, shown as a lettered radio button.
///
/// <see cref="Letter"/> is presentation only — it comes from the option's
/// position, so it changes if the teacher reorders the choices. Only
/// <see cref="Text"/> is ever stored on a submission.
/// </summary>
public partial class AnswerOption : ObservableObject
{
    public string Letter { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// The question's ID, used as the radio group name. Each question needs its
    /// own group or picking an answer in one would clear the answer in another.
    /// </summary>
    public string GroupName { get; init; } = string.Empty;

    public string Display => $"{Letter}.   {Text}";

    /// <summary>
    /// Two-way bound to RadioButton.IsChecked. MAUI unchecks the other buttons
    /// in the same group for us, and that flows back through the binding — so
    /// exactly one option in a group is ever true.
    /// </summary>
    [ObservableProperty] private bool isSelected;
}

/// <summary>
/// One multiple-choice question as the student answers it.
///
/// Deliberately carries no notion of a correct answer — the key lives in a
/// teacher-only collection the student's device cannot read at all.
/// </summary>
public partial class AnswerableQuestion : ObservableObject
{
    public string QuestionId { get; init; } = string.Empty;
    public int Number { get; init; }
    public string Text { get; init; } = string.Empty;
    public int Points { get; init; }

    public ObservableCollection<AnswerOption> Options { get; init; } = [];

    /// <summary>
    /// Derived from the options rather than stored, so it can't disagree with
    /// what the radio buttons are showing.
    /// </summary>
    public AnswerOption? SelectedOption => Options.FirstOrDefault(o => o.IsSelected);

    public bool NotAnswered => SelectedOption is null;

    public string Heading => $"{Number}. {Text}";
    public string PointsLabel => Points == 1 ? "1 point" : $"{Points} points";

    /// <summary>A, B, C, D … by position. Presentation only — never stored.</summary>
    public static string LetterFor(int index) => ((char)('A' + index)).ToString();

    /// <summary>
    /// Wires each option's IsSelected back to this question's derived state.
    /// Called once after Options is populated.
    /// </summary>
    public void WatchOptions()
    {
        foreach (var option in Options)
        {
            option.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(AnswerOption.IsSelected)) return;
                OnPropertyChanged(nameof(SelectedOption));
                OnPropertyChanged(nameof(NotAnswered));
            };
        }
    }
}

public partial class StudentAssignmentViewModel : ObservableObject
{
    private readonly IClassService _classes;
    private string _classId = string.Empty;
    private string _assignmentId = string.Empty;

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = string.Empty;

    [ObservableProperty] private string title = string.Empty;
    [ObservableProperty] private string className = string.Empty;
    [ObservableProperty] private string kindAndDue = string.Empty;
    [ObservableProperty] private string description = string.Empty;
    [ObservableProperty] private bool hasDescription;

    [ObservableProperty] private ObservableCollection<AnswerableQuestion> questions = [];
    [ObservableProperty] private bool hasQuestions;

    /// <summary>Free-text answer. Always available, alongside any questions.</summary>
    [ObservableProperty] private string writtenAnswer = string.Empty;

    [ObservableProperty] private string submitLabel = "Submit";
    [ObservableProperty] private string submissionState = string.Empty;

    /// <summary>Set once a teacher has marked the work.</summary>
    [ObservableProperty] private bool isMarked;
    [ObservableProperty] private string gradeLabel = string.Empty;
    [ObservableProperty] private string feedback = string.Empty;
    [ObservableProperty] private bool hasFeedback;

    // ---- how this is handed in ----

    /// <summary>The teacher's choice: written, file, or either.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnswerBox))]
    private bool acceptsWritten = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnswerBox))]
    private bool acceptsFile;

    /// <summary>
    /// Whether there is anywhere to put an answer. False on a multiple-choice
    /// paper, where the whole "Your answer" card is hidden — a card headed
    /// "Your answer" containing nothing to answer with is worse than no card.
    /// </summary>
    public bool HasAnswerBox => AcceptsWritten || AcceptsFile;

    /// <summary>One line telling the student what's expected.</summary>
    [ObservableProperty] private string submissionHint = string.Empty;

    /// <summary>Name of the file they've picked but not yet submitted.</summary>
    [ObservableProperty] private string pickedFileLabel = string.Empty;
    [ObservableProperty] private bool hasPickedFile;

    /// <summary>A file already submitted, so they can see what's on record.</summary>
    [ObservableProperty] private string submittedFileLabel = string.Empty;
    [ObservableProperty] private bool hasSubmittedFile;

    /// <summary>False when the assignment has been deleted — submitting would orphan a document.</summary>
    [ObservableProperty] private bool canSubmit = true;

    private readonly IStorageService _storage;

    public StudentAssignmentViewModel(IClassService classes, IStorageService storage)
    {
        _classes = classes;
        _storage = storage;
    }

    /// <summary>Queued by the file picker, consumed on submit.</summary>
    private Stream? _pendingFile;
    private string _pendingFileName = string.Empty;
    private string _pendingContentType = string.Empty;

    public async Task LoadAsync(string classId, string assignmentId, string title, string className)
    {
        if (IsBusy) return;
        _classId = classId;
        _assignmentId = assignmentId;
        Title = title;
        ClassName = className;

        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            // The assignment itself, for instructions and the due date.
            var assignments = await _classes.GetAssignmentsAsync(classId);
            var assignment = assignments.FirstOrDefault(a => a.Id == assignmentId);

            CanSubmit = assignment is not null;
            if (assignment is null)
            {
                StatusMessage = "This assignment has been removed by the teacher.";
            }
            else
            {
                Description = assignment.Description;
                HasDescription = !string.IsNullOrWhiteSpace(assignment.Description);

                // What the teacher asked for. Uploads also need Storage
                // configured — if it isn't, fall back to written so the student
                // isn't shown a picker that can't work.
                // Multiple-choice papers have nothing to type and nothing to
                // attach. Checked FIRST, because the storage fallback below
                // would otherwise hand a questions-only paper a written answer
                // box on any build where Storage isn't configured.
                if (assignment.IsQuestionsOnly)
                {
                    AcceptsWritten = false;
                    AcceptsFile = false;
                    SubmissionHint = assignment.SubmissionHint;
                }
                else
                {
                    AcceptsWritten = assignment.AcceptsWritten || !_storage.IsConfigured;
                    AcceptsFile = assignment.AcceptsFile && _storage.IsConfigured;
                    SubmissionHint = AcceptsFile ? assignment.SubmissionHint : "Type your answer to hand this in.";
                }
                KindAndDue = $"{assignment.Kind} · due {assignment.DueDate.ToLocalTime():ddd d MMM, HH:mm}"
                           + $" · {assignment.Points} pts";
            }

            var questions = await _classes.GetQuestionsAsync(classId, assignmentId);
            var existing = await _classes.GetMySubmissionAsync(classId, assignmentId);

            Questions = new ObservableCollection<AnswerableQuestion>(
                questions.Select((q, i) =>
                {
                    var row = new AnswerableQuestion
                    {
                        QuestionId = q.Id,
                        Number = i + 1,
                        Text = q.Text,
                        Points = q.Points,
                        Options = new ObservableCollection<AnswerOption>(
                            q.Options.Select((text, index) => new AnswerOption
                            {
                                Letter = AnswerableQuestion.LetterFor(index),
                                Text = text,
                                GroupName = q.Id
                            }))
                    };

                    // Matched on text, not letter: the letter is derived from
                    // position, so a teacher reordering the choices would
                    // otherwise silently change what the student answered.
                    var picked = existing?.AnswerFor(q.Id);
                    if (picked is not null)
                    {
                        var match = row.Options.FirstOrDefault(
                            o => string.Equals(o.Text, picked, StringComparison.Ordinal));
                        if (match is not null) match.IsSelected = true;
                    }

                    row.WatchOptions();
                    return row;
                }));

            HasQuestions = Questions.Count > 0;

            if (existing is not null)
            {
                WrittenAnswer = existing.Text;
                SubmitLabel = "Update submission";
                SubmissionState = $"Submitted {existing.SubmittedAt.ToLocalTime():ddd d MMM, HH:mm}";

                IsMarked = existing.IsMarked;

                // An auto-marked score is out of the multiple-choice total, not
                // the assignment total — those differ whenever there's also a
                // written answer still awaiting the teacher.
                GradeLabel = existing switch
                {
                    { IsMarked: false } => string.Empty,
                    { IsAutoMarked: true } => $"Auto-marked: {existing.Grade} / {existing.AutoScoreMax}",
                    _ => $"Marked: {existing.Grade}"
                         + (assignment is not null ? $" / {assignment.Points}" : string.Empty)
                };

                Feedback = existing.Feedback;
                HasFeedback = !string.IsNullOrWhiteSpace(existing.Feedback);

                HasSubmittedFile = existing.HasFile;
                SubmittedFileLabel = existing.FileLabel;
            }
            else
            {
                SubmitLabel = "Submit";
                SubmissionState = "Not submitted yet.";
            }
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            StatusMessage = "You don't have access to this assignment. "
                          + "You may no longer be enrolled in the class.";
        }
        catch (Exception ex)
        {
            CrashLogger.Log("StudentAssignment.Load", ex);
            StatusMessage = "We couldn't load this assignment. Please go back and try again.";
        }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// Called by the view once the platform picker returns. The stream is held
    /// until submit, so backing out uploads nothing.
    /// </summary>
    public void AttachFile(Stream content, string fileName, string contentType)
    {
        _pendingFile?.Dispose();
        _pendingFile = content;
        _pendingFileName = fileName;
        _pendingContentType = contentType;

        PickedFileLabel = fileName;
        HasPickedFile = true;
    }

    [RelayCommand]
    private void ClearFile()
    {
        _pendingFile?.Dispose();
        _pendingFile = null;
        _pendingFileName = string.Empty;
        _pendingContentType = string.Empty;
        PickedFileLabel = string.Empty;
        HasPickedFile = false;
    }

    [RelayCommand]
    private async Task SubmitAsync()
    {
        if (IsBusy) return;
        if (!CanSubmit)
        {
            StatusMessage = "This assignment has been removed, so it can't be submitted to.";
            return;
        }

        var unanswered = Questions.Count(q => q.SelectedOption is null);
        var written = WrittenAnswer?.Trim() ?? string.Empty;

        // Nothing at all to hand in. The wording follows what the teacher
        // actually asked for, so a student told to attach a file isn't nagged
        // about typing an answer.
        if (Questions.Count == 0 && written.Length == 0 && !HasPickedFile && !HasSubmittedFile)
        {
            StatusMessage = !AcceptsWritten && !AcceptsFile
                // A multiple-choice paper with no questions on it. The teacher
                // created it and never finished; say so rather than asking the
                // student for an answer there's nowhere to put.
                ? "Your teacher hasn't added the questions for this yet."
                : AcceptsFile && !AcceptsWritten
                    ? "Attach a file before submitting."
                    : AcceptsFile
                        ? "Type an answer or attach a file before submitting."
                        : "Write your answer before submitting.";
            return;
        }

        // Warn but don't block: partial work is better than none, and a student
        // may be coming back to finish later.
        var warning = unanswered > 0
            ? $" {unanswered} question(s) left unanswered."
            : string.Empty;

        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            var answers = Questions
                .Where(q => q.SelectedOption is not null)
                // .Text, not the lettered display string — see AnswerOption.
                .Select(q => Submission.Encode(q.QuestionId, q.SelectedOption!.Text))
                .ToList();

            if (_pendingFile is not null)
            {
                // Uploads first inside the service, so a failed upload leaves
                // the student's work intact rather than recording a submission
                // that points at nothing.
                await _classes.SubmitWithFileAsync(
                    _classId, _assignmentId, written, answers,
                    _pendingFile, _pendingFileName, _pendingContentType);

                SubmittedFileLabel = _pendingFileName;
                HasSubmittedFile = true;
                ClearFile();
            }
            else
            {
                await _classes.SubmitAsync(_classId, _assignmentId, written, answers);
            }

            SubmitLabel = "Update submission";
            SubmissionState = $"Submitted {DateTimeOffset.Now:ddd d MMM, HH:mm}";

            // Any previous mark described the OLD answers, so stop showing it
            // rather than implying this new submission has already been graded.
            IsMarked = false;
            HasFeedback = false;
            GradeLabel = string.Empty;

            // Marking happens server-side a moment later, so there's nothing to
            // show yet. Say so rather than leaving the student wondering whether
            // it worked.
            StatusMessage = HasQuestions
                ? "Submitted." + warning + " Your multiple-choice answers are marked "
                  + "automatically — reopen this page shortly to see your score."
                : "Submitted." + warning;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            StatusMessage = "You don't have permission to submit to this assignment. "
                          + "Check with your teacher that you're enrolled in the class.";
        }
        catch (InvalidOperationException ex)
        {
            // Storage problems arrive here with a message written for a person.
            StatusMessage = ex.Message;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "That took too long. Check your connection and try again.";
        }
        catch (Exception ex)
        {
            CrashLogger.Log("StudentAssignment.Submit", ex);
            StatusMessage = "We couldn't submit that. Please try again.";
        }
        finally { IsBusy = false; }
    }
}
