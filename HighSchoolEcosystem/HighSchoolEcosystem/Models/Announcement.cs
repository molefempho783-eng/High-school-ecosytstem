namespace HighSchoolEcosystem.Models;

/// <summary>
/// Mirrors /announcements/{announcementId}. School-wide, not per-class — the
/// whole point is reaching everyone at once.
///
/// Only admins may write these; the rules enforce that, and
/// <c>IContentService.CanManageAnnouncements</c> mirrors it so the UI can hide
/// controls that would be rejected anyway. The client check is convenience, not
/// security — see firestore.rules.
/// </summary>
public sealed class Announcement
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;

    /// <summary>
    /// Set from the signed-in user at create time and never sent again on
    /// update. The rules pin it to the creating account, so an edit that tried
    /// to change it would be denied.
    /// </summary>
    public string AuthorId { get; init; } = string.Empty;

    /// <summary>Denormalised so listing announcements doesn't need a user lookup per row.</summary>
    public string AuthorName { get; init; } = string.Empty;

    public DateTimeOffset PostedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Last edit time, or default when never edited. Shown as "edited 3 Sep" so
    /// readers can tell a corrected notice from the original.
    /// </summary>
    public DateTimeOffset EditedAt { get; init; }

    /// <summary>
    /// Pins the announcement to the top of the list. Deliberately not a
    /// priority number — schools use "important" as a binary, and a numeric
    /// scale invites everything drifting to 1.
    /// </summary>
    public bool IsPinned { get; init; }

    /// <summary>
    /// Visible to signed-in Visitors as well as the school. Default false, so
    /// a notice is internal unless an admin deliberately opens it up — the
    /// safer direction for anything about students.
    ///
    /// Meaningless on a class announcement, which is scoped by enrolment.
    /// </summary>
    public bool IsPublic { get; init; }

    /// <summary>
    /// The class this belongs to, or empty for a school-wide notice.
    ///
    /// Class announcements live at /classes/{classId}/announcements, so this is
    /// really the path talking — it's grafted on after deserialisation so a
    /// merged feed can tell the two apart without tracking where each came from.
    /// </summary>
    public string ClassId { get; init; } = string.Empty;

    /// <summary>
    /// Denormalised at write time so a merged feed can label a notice with its
    /// class without a lookup per row. A class renamed afterwards keeps the old
    /// name here — acceptable for a label, and the alternative is a read per
    /// announcement on every dashboard load.
    /// </summary>
    public string ClassName { get; init; } = string.Empty;

    /// <summary>
    /// When this notice stops being shown, or default for "never".
    ///
    /// Optional on purpose: most school notices have no natural end date, and
    /// forcing one would either produce arbitrary dates or make posting slower
    /// for the common case. A trials date or a deadline reminder has one; "the
    /// library has moved" doesn't.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; init; }

    public bool HasExpiry => ExpiresAt != default;

    /// <summary>
    /// True once the expiry has passed. Readers stop seeing it; the author
    /// still does, so they can tell why it vanished and extend it if the date
    /// was wrong.
    /// </summary>
    public bool IsExpired => HasExpiry && ExpiresAt < DateTimeOffset.Now;

    /// <summary>"Until 12 Sep" or "Expired 3 Sep", for the meta line.</summary>
    public string ExpiryLabel
    {
        get
        {
            if (!HasExpiry) return string.Empty;

            var local = ExpiresAt.ToLocalTime();
            return IsExpired ? $"Expired {local:d MMM}" : $"Until {local:d MMM}";
        }
    }

    public bool IsClassAnnouncement => !string.IsNullOrWhiteSpace(ClassId);

    /// <summary>"Mathematics Grade 10A" or "Whole school" — the feed's scope chip.</summary>
    public string ScopeLabel => IsClassAnnouncement
        ? (string.IsNullOrWhiteSpace(ClassName) ? "Class" : ClassName)
        : "Whole school";

    public bool WasEdited => EditedAt > PostedAt;
}
