using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HighSchoolEcosystem.Models;
using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.Services.Interfaces;

namespace HighSchoolEcosystem.ViewModels;

/// <summary>
/// One assignment as it stands for one student, with the mark editable in
/// place.
///
/// An ObservableObject rather than a record because the whole point of this
/// screen is changing the numbers: the row has to show its own saved state
/// without the list being rebuilt and reordered under the teacher's finger.
/// </summary>
public sealed partial class StudentMarkRow : ObservableObject
{
    public StudentMarkRow(Assignment assignment, Submission? submission)
    {
        Assignment = assignment;
        Submission = submission;

        OutOf = submission is { IsAutoMarked: true, AutoScoreMax: > 0 }
            ? submission.AutoScoreMax
            : assignment.Points;

        GradeText = submission?.IsMarked == true
            ? submission.Grade.ToString(CultureInfo.InvariantCulture)
            : string.Empty;

        Feedback = submission?.Feedback ?? string.Empty;
    }

    public Assignment Assignment { get; }
    public Submission? Submission { get; }

    public string AssignmentId => Assignment.Id;
    public string Title => Assignment.Title;
    public AssignmentKind Kind => Assignment.Kind;
    public DateTimeOffset DueDate => Assignment.DueDate;

    /// <summary>The denominator. Auto-marked work is out of its own total.</summary>
    public int OutOf { get; }

    /// <summary>
    /// The mark, as text. Text rather than int? because the box has to be able
    /// to be empty — "not marked yet" is a real state, and an int would show it
    /// as a zero the student hasn't earned.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMark))]
    [NotifyPropertyChangedFor(nameof(Percentage))]
    [NotifyPropertyChangedFor(nameof(BandColor))]
    private string gradeText = string.Empty;

    [ObservableProperty] private string feedback = string.Empty;

    /// <summary>Set after a successful save, so the row confirms itself.</summary>
    [ObservableProperty] private bool justSaved;

    public bool HasSubmission => Submission is not null && Submission.SubmittedAt != default;

    public bool IsAutoMarked => Submission?.IsAutoMarked == true;

    public string StateLabel
    {
        get
        {
            if (!HasSubmission) return "Not handed in";
            var when = Submission!.SubmittedAt.ToLocalTime();
            return Submission.IsTeacherMarked
                ? $"Marked · handed in {when:d MMM}"
                : $"Handed in {when:d MMM}";
        }
    }

    public string MetaLine => $"{Kind} · due {DueDate.ToLocalTime():d MMM yyyy} · out of {OutOf}";

    public bool HasMark => TryParse(out _);

    public double? Percentage => TryParse(out var mark) && OutOf > 0
        ? (double)mark / OutOf * 100
        : null;

    /// <summary>
    /// Banded by percentage, matching the student's own grade tiles so the same
    /// mark isn't a different colour depending on who is looking at it.
    /// </summary>
    public Color BandColor => Percentage switch
    {
        null => Color.FromArgb("#C4C4C4"),
        >= 75 => Color.FromArgb("#2E6B33"),
        >= 50 => Color.FromArgb("#178C8C"),
        >= 40 => Color.FromArgb("#A85400"),
        _ => Color.FromArgb("#B3261E")
    };

    /// <summary>
    /// Parses the box. Rejects negatives and anything above the total: a mark of
    /// 200 out of 50 is a typo every time, and silently storing it would corrupt
    /// the student's average with no way to notice.
    /// </summary>
    public bool TryParse(out int mark)
    {
        mark = 0;
        var text = GradeText?.Trim();
        if (string.IsNullOrEmpty(text)) return false;
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out mark)) return false;
        return mark >= 0 && mark <= OutOf;
    }
}

/// <summary>
/// One student's full mark history for one class, editable, and exportable.
///
/// Distinct from TeacherMarkingViewModel, which is one assignment across every
/// student. This is the other axis: every assignment for one student, which is
/// what a parent meeting or a report needs.
/// </summary>
public partial class TeacherStudentMarksViewModel : ObservableObject
{
    private readonly IClassService _classes;

    private string _classId = string.Empty;
    private string _studentId = string.Empty;

    public TeacherStudentMarksViewModel(IClassService classes) => _classes = classes;

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isRefreshing;
    [ObservableProperty] private string statusMessage = string.Empty;

    [ObservableProperty] private string studentName = string.Empty;
    [ObservableProperty] private string className = string.Empty;

    [ObservableProperty] private ObservableCollection<StudentMarkRow> marks = [];

    [ObservableProperty] private bool hasMarks;
    [ObservableProperty] private string marksCount = "0";
    [ObservableProperty] private string emptyMessage = string.Empty;

    /// <summary>"64% average across 9 marks", or empty when nothing is marked.</summary>
    [ObservableProperty] private string averageLabel = string.Empty;

    [ObservableProperty] private bool hasAverage;

    public void Load(string classId, string className, string studentId, string studentName)
    {
        _classId = classId;
        _studentId = studentId;
        ClassName = className;
        StudentName = studentName;
    }

    public async Task LoadAsync()
    {
        if (IsBusy || string.IsNullOrEmpty(_classId) || string.IsNullOrEmpty(_studentId)) return;

        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            var assignments = await _classes.GetAssignmentsAsync(_classId);
            var submissions = await _classes.GetSubmissionsForStudentAsync(_classId, _studentId);

            var byAssignmentId = submissions.ToDictionary(
                x => x.AssignmentId, x => x.Submission, StringComparer.Ordinal);

            // Every assignment, not only the ones with a submission. Work that
            // was never handed in is the most important thing on this screen —
            // it's the gap a teacher is looking for — and it can still be
            // graded, for a paper test or a zero.
            var rows = assignments
                .OrderByDescending(a => a.DueDate)
                .Select(a => new StudentMarkRow(
                    a, byAssignmentId.TryGetValue(a.Id, out var s) ? s : null))
                .ToList();

            Marks = new ObservableCollection<StudentMarkRow>(rows);
            HasMarks = rows.Count > 0;
            MarksCount = rows.Count.ToString();
            EmptyMessage = HasMarks ? string.Empty : "No assignments have been set for this class yet.";

            RecalculateAverage();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            StatusMessage = "You can only see marks for classes you teach.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "That took too long. Check your connection and try again.";
        }
        catch (Exception ex)
        {
            CrashLogger.Log("TeacherStudentMarks.Load", ex);
            StatusMessage = "We couldn't load this student's marks. Please try again.";
        }
        finally { IsBusy = false; }
    }

    private void RecalculateAverage()
    {
        var scored = Marks.Where(m => m.Percentage is not null).ToList();
        HasAverage = scored.Count > 0;
        AverageLabel = HasAverage
            ? $"{scored.Average(m => m.Percentage!.Value):0}% average across " +
              (scored.Count == 1 ? "1 mark" : $"{scored.Count} marks")
            : string.Empty;
    }

    /// <summary>
    /// Saves one row.
    ///
    /// Per row rather than a single Save All: a dropped connection halfway
    /// through a class's marks should lose one edit, not all of them. Same
    /// reasoning as the marking screen.
    /// </summary>
    [RelayCommand]
    private async Task SaveAsync(StudentMarkRow? row)
    {
        if (row is null || IsBusy) return;

        if (!row.TryParse(out var mark))
        {
            StatusMessage = string.IsNullOrWhiteSpace(row.GradeText)
                ? "Enter a mark before saving."
                : $"The mark must be a whole number between 0 and {row.OutOf}.";
            return;
        }

        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            await _classes.SetGradeAsync(
                _classId, row.AssignmentId, _studentId, mark, row.Feedback ?? string.Empty, StudentName);

            // Confirmed in place. A reload would re-sort the list and lose the
            // teacher's place partway down a term's work.
            row.JustSaved = true;
            RecalculateAverage();
            StatusMessage = $"Saved {mark} / {row.OutOf} for {row.Title}.";
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            StatusMessage = "You don't have permission to change this mark.";
        }
        catch (Exception ex)
        {
            CrashLogger.Log("TeacherStudentMarks.Save", ex);
            StatusMessage = "We couldn't save that mark. Please try again.";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try { await LoadAsync(); }
        finally { IsRefreshing = false; }
    }

    /// <summary>
    /// Builds the export as CSV text. Returned rather than written, so the page
    /// owns the file and the share sheet and this stays testable.
    ///
    /// CSV rather than a real .xlsx because the project carries no spreadsheet
    /// library; Excel opens this directly. If that ever changes, this is the
    /// only method that needs replacing.
    /// </summary>
    public string BuildCsv()
    {
        var csv = new StringBuilder();
        csv.AppendLine("Student,Class,Assignment,Kind,Due,Mark,Out of,Percentage,Status,Feedback");

        foreach (var row in Marks)
        {
            var percentage = row.Percentage is { } pct
                ? pct.ToString("0.#", CultureInfo.InvariantCulture)
                : string.Empty;

            csv.AppendLine(string.Join(",", new[]
            {
                Escape(StudentName),
                Escape(ClassName),
                Escape(row.Title),
                Escape(row.Kind.ToString()),
                // Sortable and unambiguous. A local short date would be read as
                // month-first by an Excel set to a US locale.
                Escape(row.DueDate.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                Escape(row.HasMark ? row.GradeText.Trim() : string.Empty),
                Escape(row.OutOf.ToString(CultureInfo.InvariantCulture)),
                Escape(percentage),
                Escape(row.StateLabel),
                Escape(row.Feedback ?? string.Empty)
            }));
        }

        return csv.ToString();
    }

    /// <summary>The filename offered to the share sheet.</summary>
    public string CsvFileName
    {
        get
        {
            var safeStudent = Sanitise(StudentName, "student");
            var safeClass = Sanitise(ClassName, "class");
            return $"{safeStudent}-{safeClass}-marks-{DateTime.Now:yyyy-MM-dd}.csv";
        }
    }

    /// <summary>
    /// CSV escaping, in full. A comma in "Term 2, Paper 1" would otherwise shift
    /// every later column by one, and a quote in a teacher's feedback would end
    /// the field early — both corrupt the file silently rather than failing.
    /// </summary>
    private static string Escape(string value)
    {
        value ??= string.Empty;

        // Newlines matter as much as commas: feedback is a free-text box and
        // teachers press Enter in it.
        var needsQuoting = value.Contains(',') || value.Contains('"')
                        || value.Contains('\n') || value.Contains('\r');

        if (!needsQuoting) return value;

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    /// <summary>Strips anything a filesystem would object to.</summary>
    private static string Sanitise(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;

        var cleaned = new string(value
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray())
            .Trim('-');

        while (cleaned.Contains("--")) cleaned = cleaned.Replace("--", "-");

        return cleaned.Length == 0 ? fallback : cleaned;
    }
}
