namespace HighSchoolEcosystem.Models;

/// <summary>
/// A multiple-choice question, stored at
/// /classes/{classId}/assignments/{assignmentId}/questions/{questionId}.
///
/// IMPORTANT: this document contains NO answer. The correct option lives in a
/// sibling collection (<see cref="QuestionAnswerKey"/>) that the security rules
/// keep teacher/admin-only. Students can read the questions — so if the answer
/// were a field here, every student could read the answer key.
/// </summary>
public sealed class HomeworkQuestion
{
    public string Id { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;

    /// <summary>Answer choices in display order. Two to four entries.</summary>
    public List<string> Options { get; init; } = [];

    public int Points { get; init; } = 1;

    /// <summary>Display position within the assignment, 1-based.</summary>
    public int Order { get; init; }
}

/// <summary>
/// The correct answer for one question, stored at
/// /classes/{classId}/assignments/{assignmentId}/answerKeys/{questionId} —
/// same document ID as the question it belongs to.
///
/// Separated purely so the security rules can deny students read access. Never
/// merge this back into <see cref="HomeworkQuestion"/>.
/// </summary>
public sealed class QuestionAnswerKey
{
    /// <summary>Zero-based index into the question's Options list.</summary>
    public int CorrectIndex { get; init; }
}
