using HighSchoolEcosystem.Models;

namespace HighSchoolEcosystem.Services.Interfaces;

/// <summary>
/// School-wide content that isn't tied to a class: announcements and sport.
///
/// The Can* properties exist so the UI can hide controls the rules would
/// reject. They are a convenience, never a security boundary — every one of
/// them is enforced again in firestore.rules, which is the only thing an
/// attacker can't skip.
/// </summary>
public interface IContentService
{
    /// <summary>
    /// Loads the caller's roles so the Can* properties below become meaningful.
    /// Await this before reading them — until it completes they all report
    /// false, which fails closed rather than briefly showing an admin button to
    /// a student.
    /// </summary>
    Task PrimeAsync(CancellationToken cancellationToken = default);

    // ---- announcements ----

    /// <summary>True when the signed-in user may create, edit or delete announcements.</summary>
    bool CanManageAnnouncements { get; }

    /// <summary>
    /// Pinned first, then newest first.
    ///
    /// Expired notices are dropped by default. Pass includeExpired for a
    /// management view: an author needs to see one that has lapsed, or they
    /// can't tell why it vanished and can't extend it.
    ///
    /// Filtering happens here rather than in the rules — an expired notice
    /// isn't secret, just stale, and a rule that denied them would fail the
    /// whole list query rather than trimming it.
    /// </summary>
    Task<IReadOnlyList<Announcement>> GetAnnouncementsAsync(bool includeExpired = false, CancellationToken cancellationToken = default);

    /// <summary>Creates an announcement. Returns its new ID.</summary>
    /// <param name="expiresAt">Default for "never expires".</param>
    Task<string> CreateAnnouncementAsync(
        string title, string body, bool isPinned, bool isPublic,
        DateTimeOffset expiresAt = default,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the editable fields only. AuthorId and PostedAt are never sent,
    /// because the rules pin both and a full-object write would be denied.
    /// </summary>
    Task UpdateAnnouncementAsync(
        string id, string title, string body, bool isPinned, bool isPublic,
        DateTimeOffset expiresAt = default,
        CancellationToken cancellationToken = default);

    Task DeleteAnnouncementAsync(string id, CancellationToken cancellationToken = default);

    // ---- sport ----

    /// <summary>True when the user may post sport at all (Coach, Admin or Journalist).</summary>
    bool CanPostSports { get; }

    /// <summary>
    /// Whether this user may edit or delete a specific post. Coaches and admins
    /// may touch anything; a journalist may only touch their own, which mirrors
    /// the rule exactly.
    /// </summary>
    bool CanEditSportsItem(SportsItem item);

    /// <summary>Upcoming fixtures first by date, then everything else newest first.</summary>
    Task<IReadOnlyList<SportsItem>> GetSportsAsync(CancellationToken cancellationToken = default);

    Task<string> CreateSportsItemAsync(SportsItem item, CancellationToken cancellationToken = default);

    Task UpdateSportsItemAsync(string id, SportsItem item, CancellationToken cancellationToken = default);

    Task DeleteSportsItemAsync(string id, CancellationToken cancellationToken = default);
}
