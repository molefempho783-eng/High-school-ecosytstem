using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HighSchoolEcosystem.Models;
using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.Services.Interfaces;

namespace HighSchoolEcosystem.ViewModels;

/// <summary>One authored question, with its answer revealed (teachers only).</summary>
public sealed record QuestionRow(string Id, int Number, int Order, string Text, string OptionSummary, string CorrectAnswer, int Points)
{
    public string Title => $"{Number}. {Text}";
    public string Subtitle => $"{OptionSummary} · answer: {CorrectAnswer} · {Points} pt(s)";
}

/// <summary>
/// Authoring screen for an assignment's multiple-choice questions.
///
/// Teachers see the correct answers here because the rules let them read
/// /answerKeys. Students never can — the same screen would come back empty of
/// answers for them, which is why the key is a separate collection.
///
/// The form is dual-mode: blank it adds a new question, or tapping a row loads
/// that question in for editing and saves back to the same document ID.
/// </summary>
public partial class TeacherQuestionsViewModel : ObservableObject
{
    private readonly IClassService _classes;
    private readonly IUserProfileService _profiles;
    private string _classId = string.Empty;
    private string _assignmentId = string.Empty;
    private Role? _myRole;

    // Kept so editing can repopulate the form without another round trip.
    private List<HomeworkQuestion> _loaded = [];
    private IReadOnlyDictionary<string, int> _answerKey = new Dictionary<string, int>();

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private string assignmentTitle = string.Empty;
    [ObservableProperty] private ObservableCollection<QuestionRow> questions = [];

    /// <summary>
    /// True when the answer key couldn't be read. Every answer then shows as
    /// "not set", so the UI has to say why rather than implying they're missing.
    /// </summary>
    [ObservableProperty] private bool answerKeyUnavailable;

    // ---- form ----

    /// <summary>Document ID being edited; null means the form will add a new question.</summary>
    private string? _editingId;

    /// <summary>Preserved across an edit so reordering doesn't happen on save.</summary>
    private int _editingOrder;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormHeading))]
    [NotifyPropertyChangedFor(nameof(SaveButtonText))]
    private bool isEditing;

    public string FormHeading => IsEditing ? "Edit question" : "New question";
    public string SaveButtonText => IsEditing ? "Save changes" : "Add question";

    [ObservableProperty] private string newText = string.Empty;
    [ObservableProperty] private string optionA = string.Empty;
    [ObservableProperty] private string optionB = string.Empty;
    [ObservableProperty] private string optionC = string.Empty;
    [ObservableProperty] private string optionD = string.Empty;
    [ObservableProperty] private string newPoints = "1";

    /// <summary>Labels A–D; only those with text filled in are actually saved.</summary>
    [ObservableProperty] private ObservableCollection<string> answerChoices =
        new(new[] { "A", "B", "C", "D" });
    [ObservableProperty] private string correctChoice = "A";

    public TeacherQuestionsViewModel(IClassService classes, IUserProfileService profiles)
    {
        _classes = classes;
        _profiles = profiles;
    }

    /// <summary>
    /// Builds an actionable message for a PERMISSION_DENIED. A 403 here has only
    /// three realistic causes, and a generic "no permission" leaves the teacher
    /// with no way to tell which — so name all three and their current role.
    /// </summary>
    private string PermissionMessage(string action)
    {
        var role = _myRole?.ToString() ?? "unknown";
        return $"Permission denied trying to {action}. You're signed in as {role}. "
             + "Check that firestore.rules has been deployed to this Firebase project "
             + "(firebase deploy --only firestore:rules), that you're the teacher who "
             + "owns this class, and that your role is Teacher or Admin.";
    }

    public async Task LoadAsync(string classId, string assignmentId, string assignmentTitle)
    {
        if (IsBusy) return;
        _classId = classId;
        _assignmentId = assignmentId;
        AssignmentTitle = assignmentTitle;

        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            // Cached so a later permission failure can report the actual role
            // rather than leaving the teacher guessing.
            try { _myRole = (await _profiles.GetOrCreateCurrentProfileAsync())?.Role; }
            catch { _myRole = null; }

            await ReloadAsync();

            if (AnswerKeyUnavailable)
                StatusMessage = PermissionMessage("read the answer key")
                    + " Answers show as \"not set\" until this is fixed.";
            else if (Questions.Count == 0)
                StatusMessage = "No questions yet. Add the first one below.";
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            StatusMessage = PermissionMessage("open this assignment's questions");
        }
        catch (Exception ex)
        {
            CrashLogger.Log("TeacherQuestions.Load", ex);
            StatusMessage = "We couldn't load the questions. Please try again.";
        }
        finally { IsBusy = false; }
    }

    /// <summary>Loads an existing question into the form for editing.</summary>
    public void BeginEdit(QuestionRow row)
    {
        var question = _loaded.FirstOrDefault(q => q.Id == row.Id);
        if (question is null)
        {
            StatusMessage = "That question is no longer available. Try refreshing.";
            return;
        }

        _editingId = question.Id;
        _editingOrder = question.Order;
        IsEditing = true;

        NewText = question.Text;

        // Pad back out to the four fixed option slots.
        OptionA = question.Options.ElementAtOrDefault(0) ?? string.Empty;
        OptionB = question.Options.ElementAtOrDefault(1) ?? string.Empty;
        OptionC = question.Options.ElementAtOrDefault(2) ?? string.Empty;
        OptionD = question.Options.ElementAtOrDefault(3) ?? string.Empty;

        NewPoints = question.Points.ToString();

        // Bounds-checked against the picker's own list: an out-of-range index
        // would produce a letter the Picker doesn't contain, and it would then
        // write null back into CorrectChoice and silently blank itself.
        CorrectChoice = _answerKey.TryGetValue(question.Id, out var index)
                        && index >= 0 && index < AnswerChoices.Count
            ? Letter(index)
            : "A";

        StatusMessage = $"Editing question {row.Number}.";
    }

    [RelayCommand]
    private void CancelEdit()
    {
        ResetForm();
        StatusMessage = "Edit cancelled.";
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy) return;

        if (string.IsNullOrWhiteSpace(NewText))
        {
            StatusMessage = "Type the question first.";
            return;
        }

        // Blank trailing options are simply not offered — a question can have
        // two, three or four choices.
        var options = new[] { OptionA, OptionB, OptionC, OptionD }
            .Select(o => o?.Trim() ?? string.Empty)
            .ToList();

        // Reject gaps (A, blank, C) rather than silently reshuffling, which
        // would move the correct answer out from under the teacher's choice.
        var filled = options.TakeWhile(o => o.Length > 0).Count();
        if (options.Skip(filled).Any(o => o.Length > 0))
        {
            StatusMessage = "Fill the options in order — don't leave a gap between them.";
            return;
        }
        if (filled < 2)
        {
            StatusMessage = "Give at least two options.";
            return;
        }

        var correctIndex = AnswerChoices.IndexOf(CorrectChoice);
        if (correctIndex < 0 || correctIndex >= filled)
        {
            StatusMessage = $"The correct answer must be one of the {filled} options you filled in.";
            return;
        }

        if (!int.TryParse(NewPoints, out var points) || points < 1)
        {
            StatusMessage = "Points must be a whole number of 1 or more.";
            return;
        }

        var wasEditing = IsEditing;
        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            await _classes.SaveQuestionAsync(_classId, _assignmentId, new HomeworkQuestion
            {
                // A non-empty Id makes this an update of the same document
                // rather than a new question.
                Id = _editingId ?? string.Empty,
                Text = NewText,
                Options = options.Take(filled).ToList(),
                Points = points,
                // Editing keeps the original position; new questions go last.
                // Max+1, not Count+1: deleting a question from the middle would
                // make Count+1 collide with an existing Order, and Firestore
                // lists by document ID, so the collision would sort arbitrarily.
                Order = wasEditing
                    ? _editingOrder
                    : (Questions.Count == 0 ? 1 : Questions.Max(q => q.Order) + 1)
            }, correctIndex);
        }
        catch (InvalidOperationException ex)
        {
            StatusMessage = ex.Message;
            IsBusy = false;
            return;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            StatusMessage = PermissionMessage(wasEditing ? "update this question" : "add a question");
            IsBusy = false;
            return;
        }
        catch (Exception ex)
        {
            CrashLogger.Log("TeacherQuestions.Save", ex);
            StatusMessage = "We couldn't save that question. Please try again.";
            IsBusy = false;
            return;
        }

        // Past this point the write succeeded. Refreshing the list is a separate
        // concern and must not be reported as a save failure — clearing the form
        // and then telling the teacher to retry would make their next save
        // create a duplicate instead of updating.
        ResetForm();
        StatusMessage = wasEditing ? "Question updated." : "Question added.";

        try
        {
            await ReloadAsync();
            if (AnswerKeyUnavailable)
                StatusMessage = "Saved, but the answer key couldn't be read back.";
        }
        catch (Exception ex)
        {
            CrashLogger.Log("TeacherQuestions.Save.Reload", ex);
            StatusMessage = "Saved, but the list couldn't be refreshed. Pull down to reload.";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task DeleteAsync(QuestionRow? row)
    {
        if (row is null || IsBusy) return;
        IsBusy = true;
        try
        {
            await _classes.DeleteQuestionAsync(_classId, _assignmentId, row.Id);

            // Don't leave the form editing a document that no longer exists —
            // saving would silently recreate it.
            if (_editingId == row.Id) ResetForm();

            await ReloadAsync();
            StatusMessage = "Question removed.";
        }
        catch (Exception ex)
        {
            CrashLogger.Log("TeacherQuestions.Delete", ex);
            StatusMessage = "We couldn't remove that question. Please try again.";
        }
        finally { IsBusy = false; }
    }

    private void ResetForm()
    {
        _editingId = null;
        _editingOrder = 0;
        IsEditing = false;
        NewText = string.Empty;
        OptionA = OptionB = OptionC = OptionD = string.Empty;
        NewPoints = "1";
        CorrectChoice = "A";
    }

    private async Task ReloadAsync()
    {
        _loaded = [.. await _classes.GetQuestionsAsync(_classId, _assignmentId)];

        // Teacher-only read; a student hitting this would get PERMISSION_DENIED.
        // Only that case is tolerated — swallowing everything would render every
        // answer as "not set", which looks exactly like the answers being lost.
        AnswerKeyUnavailable = false;
        try
        {
            _answerKey = await _classes.GetAnswerKeyAsync(_classId, _assignmentId);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            AnswerKeyUnavailable = true;
            _answerKey = new Dictionary<string, int>();
        }

        var rows = new List<QuestionRow>();
        var number = 1;
        foreach (var q in _loaded)
        {
            var correct = _answerKey.TryGetValue(q.Id, out var index) && index >= 0 && index < q.Options.Count
                ? $"{Letter(index)} — {q.Options[index]}"
                : "not set";

            rows.Add(new QuestionRow(q.Id, number++, q.Order, q.Text,
                $"{q.Options.Count} options", correct, q.Points));
        }

        Questions = new ObservableCollection<QuestionRow>(rows);
    }

    private static string Letter(int index) => ((char)('A' + index)).ToString();
}
