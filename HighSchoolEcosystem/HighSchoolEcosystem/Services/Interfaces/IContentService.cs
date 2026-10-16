using HighSchoolEcosystem.Models;

namespace HighSchoolEcosystem.Services.Interfaces;

/// <summary>
/// School-wide content that isn't tied to a class: announcements, sport and
/// the Newspaper.
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

    /// <summary>
    /// Records the score on a match and turns it into a result.
    ///
    /// Narrower than UpdateSportsItemAsync on purpose: it touches Kind and
    /// Score and nothing else, so a coach entering a score from a tile that
    /// loaded ten minutes ago can't overwrite a venue or kickoff time someone
    /// corrected in the meantime.
    /// </summary>
    Task SetSportsScoreAsync(string id, string score, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attaches an uploaded picture to a post. Separate from create because the
    /// object path needs the post's ID, which doesn't exist until it's written.
    /// </summary>
    Task SetSportsImageAsync(string id, string imageUrl, string storagePath, CancellationToken cancellationToken = default);

    /// <summary>Deletes the post, and its picture first.</summary>
    Task DeleteSportsItemAsync(string id, CancellationToken cancellationToken = default);

    // ---- newspaper ----

    /// <summary>
    /// True when the user may read the Newspaper at all: the school community
    /// plus journalists. Visitors and guests may not, so dashboards hide the
    /// section for them rather than showing a feed that would be denied.
    /// </summary>
    bool CanReadArticles { get; }

    /// <summary>True when the user may write articles (Journalist or Admin).</summary>
    bool CanWriteArticles { get; }

    /// <summary>
    /// Whether this user may change the words of a specific article: its author
    /// (if still a journalist) or an admin. Mirrors the update rule.
    /// </summary>
    bool CanEditArticle(Article article);

    /// <summary>
    /// Whether this user may delete a specific article: its author, an admin,
    /// or any teacher (take-down). Mirrors the delete rule.
    /// </summary>
    bool CanDeleteArticle(Article article);

    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<Article>> GetArticlesAsync(CancellationToken cancellationToken = default);

    /// <summary>Publishes an article immediately. Returns its new ID.</summary>
    Task<string> CreateArticleAsync(string title, string body, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the words only. AuthorId and PostedAt are never sent, and the
    /// picture fields aren't either — a typo fix must not touch the picture.
    /// </summary>
    Task UpdateArticleAsync(string id, string title, string body, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attaches an uploaded picture. Separate from create because the object
    /// path needs the article's ID, which doesn't exist until it's written.
    /// Pass empty strings to remove the picture from the article.
    /// </summary>
    Task SetArticleImageAsync(string id, string imageUrl, string storagePath, CancellationToken cancellationToken = default);

    /// <summary>Deletes the article, and its picture first.</summary>
    Task DeleteArticleAsync(string id, CancellationToken cancellationToken = default);
}
