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

    /// <summary>
    /// The team this belongs to, or empty. Same arrangement as ClassId: team
    /// notices live at /teams/{teamId}/notices, so this is the path talking,
    /// grafted on after deserialisation so a merged feed can tell the three
    /// sources apart.
    ///
    /// A notice is school-wide, OR a class's, OR a team's — never more than one.
    /// </summary>
    public string TeamId { get; init; } = string.Empty;

    /// <summary>Denormalised for the scope chip, like ClassName.</summary>
    public string TeamName { get; init; } = string.Empty;

    public bool IsClassAnnouncement => !string.IsNullOrWhiteSpace(ClassId);

    public bool IsTeamAnnouncement => !string.IsNullOrWhiteSpace(TeamId);

    /// <summary>"Mathematics Grade 10A", "U16A Rugby" or "Whole school" — the
    /// feed's scope chip, so a merged feed is never ambiguous about who a
    /// notice was actually addressed to.</summary>
    public string ScopeLabel => IsTeamAnnouncement
        ? (string.IsNullOrWhiteSpace(TeamName) ? "Team" : TeamName)
        : IsClassAnnouncement
            ? (string.IsNullOrWhiteSpace(ClassName) ? "Class" : ClassName)
            : "Whole school";

    public bool WasEdited => EditedAt > PostedAt;

    /// <summary>
    /// Who posted it. Falls back rather than showing a raw UID — a byline of
    /// "8fH2kQ..." is worse than no byline at all.
    /// </summary>
    public string ByLine => string.IsNullOrWhiteSpace(AuthorName)
        ? "Posted by the school"
        : $"Posted by {AuthorName}";

    /// <summary>
    /// The whole notice, for the detail popup.
    ///
    /// The feed truncates the body to keep tiles even; this is the only place a
    /// reader sees all of it, which matters when the important part of a notice
    /// is the third sentence.
    /// </summary>
    public string DetailText
    {
        get
        {
            // NOT the scope: the sheet shows that as a chip above the title,
            // and repeating it as the first line of the body reads as a stutter.
            var lines = new List<string>();

            if (!string.IsNullOrWhiteSpace(Body))
            {
                lines.Add(string.Empty);
                lines.Add(Body.Trim());
            }

            lines.Add(string.Empty);
            lines.Add(ByLine);
            lines.Add($"on {PostedAt.ToLocalTime():d MMMM yyyy}");

            if (WasEdited) lines.Add($"Edited {EditedAt.ToLocalTime():d MMMM yyyy}");

            if (HasExpiry)
            {
                lines.Add(IsExpired
                    ? $"Expired {ExpiresAt.ToLocalTime():d MMMM yyyy}"
                    : $"Showing until {ExpiresAt.ToLocalTime():d MMMM yyyy}");
            }

            return string.Join(Environment.NewLine, lines);
        }
    }
}
