using HighSchoolEcosystem.Models;
using HighSchoolEcosystem.Services.Interfaces;

namespace HighSchoolEcosystem.Services;

/// <summary>
/// Announcements, sport and the Newspaper. See <see cref="IContentService"/> for why the
/// permission properties here are a UI convenience rather than a boundary.
/// </summary>
public sealed class ContentService : IContentService
{
    private const string AnnouncementsPath = "announcements";
    private const string SportsPath = "sports";
    private const string NewspaperPath = "newspaper";

    private readonly IFirestoreClient _firestore;
    private readonly IFirebaseAuthService _auth;
    private readonly IUserProfileService _profiles;

    /// <summary>
    /// Cached for the lifetime of the service. Roles only change when an admin
    /// edits them, which forces a re-login anyway, and re-reading the profile
    /// on every permission check would mean a network round trip per button.
    /// </summary>
    private UserProfile? _cachedProfile;

    private readonly IStorageService _storage;

    public ContentService(
        IFirestoreClient firestore, IFirebaseAuthService auth,
        IUserProfileService profiles, IStorageService storage)
    {
        _firestore = firestore;
        _auth = auth;
        _profiles = profiles;
        _storage = storage;
    }

    /// <summary>
    /// Must be awaited once before the Can* properties mean anything. The
    /// view models call it during load; before that the properties report
    /// false, which fails closed.
    /// </summary>
    public async Task PrimeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _cachedProfile = await _profiles.GetOrCreateCurrentProfileAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // A failed profile read must not grant permissions. Leaving the
            // cache null means every Can* stays false.
            CrashLogger.Log("ContentService.Prime", ex);
            _cachedProfile = null;
        }
    }

    private bool Holds(Role role) => _cachedProfile?.HasRole(role) == true;

    private string CurrentUserId => _auth.CurrentSession?.UserId ?? string.Empty;

    // ==================== announcements ====================

    public bool CanManageAnnouncements => Holds(Role.Admin);

    public async Task<IReadOnlyList<Announcement>> GetAnnouncementsAsync(
        bool includeExpired = false, CancellationToken cancellationToken = default)
    {
        var documents = await _firestore.ListDocumentsAsync<Announcement>(
            AnnouncementsPath, cancellationToken);

        return documents
            .Select(d => WithId(d.Value, d.Id))
            // Expired notices are dropped for readers and kept for authors.
            // Filtered here rather than in the query: Firestore can't order by
            // one field and range-filter another without a composite index, and
            // the per-school volume doesn't justify one.
            .Where(a => includeExpired || !a.IsExpired)
            // Pinned first, then newest.
            .OrderByDescending(a => a.IsPinned)
            .ThenByDescending(a => a.PostedAt)
            .ToList();
    }

    public async Task<string> CreateAnnouncementAsync(
        string title, string body, bool isPinned, bool isPublic,
        DateTimeOffset expiresAt = default,
        CancellationToken cancellationToken = default)
    {
        var session = _auth.CurrentSession
            ?? throw new InvalidOperationException("You need to be signed in to post an announcement.");

        var id = Guid.NewGuid().ToString("n");

        await _firestore.SetDocumentAsync($"{AnnouncementsPath}/{id}", new Announcement
        {
            Title = title.Trim(),
            Body = body.Trim(),
            IsPinned = isPinned,
            IsPublic = isPublic,
            ExpiresAt = expiresAt,
            // Must match request.auth.uid or the rule denies the write.
            AuthorId = session.UserId,
            AuthorName = _cachedProfile?.DisplayName ?? session.Email ?? string.Empty,
            PostedAt = DateTimeOffset.UtcNow
        }, cancellationToken);

        return id;
    }

    public Task UpdateAnnouncementAsync(
        string id, string title, string body, bool isPinned, bool isPublic,
        DateTimeOffset expiresAt = default,
        CancellationToken cancellationToken = default)
    {
        // PatchFieldsAsync, not SetDocumentAsync: the converter would serialise
        // AuthorId and PostedAt too, and the rule requires AuthorId to be
        // unchanged. Patching an explicit field list can't disturb either.
        return _firestore.PatchFieldsAsync($"{AnnouncementsPath}/{id}",
            new Dictionary<string, object?>
            {
                ["Title"] = title.Trim(),
                ["Body"] = body.Trim(),
                ["IsPinned"] = isPinned,
                ["IsPublic"] = isPublic,
                ["ExpiresAt"] = expiresAt,
                ["EditedAt"] = DateTimeOffset.UtcNow
            }, cancellationToken);
    }

    public Task DeleteAnnouncementAsync(string id, CancellationToken cancellationToken = default)
        => _firestore.DeleteDocumentAsync($"{AnnouncementsPath}/{id}", cancellationToken);

    // ==================== sport ====================

    public bool CanPostSports => Holds(Role.Coach) || Holds(Role.Admin) || Holds(Role.Journalist);

    public bool CanEditSportsItem(SportsItem item)
    {
        if (Holds(Role.Coach) || Holds(Role.Admin)) return true;

        // Mirrors the rule exactly: a journalist owns only what they wrote.
        return Holds(Role.Journalist)
            && !string.IsNullOrEmpty(CurrentUserId)
            && string.Equals(item.AuthorId, CurrentUserId, StringComparison.Ordinal);
    }

    public async Task<IReadOnlyList<SportsItem>> GetSportsAsync(
        CancellationToken cancellationToken = default)
    {
        var documents = await _firestore.ListDocumentsAsync<SportsItem>(SportsPath, cancellationToken);

        var all = documents.Select(d => WithId(d.Value, d.Id)).ToList();

        // Upcoming fixtures lead, soonest first — that's what someone opening
        // the sports page is actually looking for. Everything else follows,
        // newest first.
        var upcoming = all.Where(i => i.IsUpcoming).OrderBy(i => i.EventDate);
        var rest = all.Where(i => !i.IsUpcoming)
                      .OrderByDescending(i => i.Kind == SportsItemKind.Result ? i.EventDate : i.PostedAt);

        return upcoming.Concat(rest).ToList();
    }

    public async Task<string> CreateSportsItemAsync(
        SportsItem item, CancellationToken cancellationToken = default)
    {
        var session = _auth.CurrentSession
            ?? throw new InvalidOperationException("You need to be signed in to post.");

        var id = Guid.NewGuid().ToString("n");

        await _firestore.SetDocumentAsync($"{SportsPath}/{id}", new SportsItem
        {
            Kind = item.Kind,
            Title = item.Title.Trim(),
            Body = item.Body.Trim(),
            Sport = item.Sport.Trim(),
            Team = item.Team.Trim(),
            EventDate = item.EventDate,
            Opponent = item.Opponent.Trim(),
            Venue = item.Venue.Trim(),
            Score = item.Score.Trim(),
            ImageUrl = item.ImageUrl.Trim(),
            ImageStoragePath = item.ImageStoragePath.Trim(),
            AuthorId = session.UserId,
            AuthorName = _cachedProfile?.DisplayName ?? session.Email ?? string.Empty,
            PostedAt = DateTimeOffset.UtcNow
        }, cancellationToken);

        return id;
    }

    public Task UpdateSportsItemAsync(
        string id, SportsItem item, CancellationToken cancellationToken = default)
    {
        // Explicit field list again: AuthorId and PostedAt must survive the
        // edit untouched, and the rule checks AuthorId specifically.
        return _firestore.PatchFieldsAsync($"{SportsPath}/{id}",
            new Dictionary<string, object?>
            {
                ["Kind"] = item.Kind,
                ["Title"] = item.Title.Trim(),
                ["Body"] = item.Body.Trim(),
                ["Sport"] = item.Sport.Trim(),
                ["Team"] = item.Team.Trim(),
                ["EventDate"] = item.EventDate,
                ["Opponent"] = item.Opponent.Trim(),
                ["Venue"] = item.Venue.Trim(),
                ["Score"] = item.Score.Trim(),
                ["ImageUrl"] = item.ImageUrl.Trim(),
                ["ImageStoragePath"] = item.ImageStoragePath.Trim()
            }, cancellationToken);
    }

    public Task SetSportsScoreAsync(
        string id, string score, CancellationToken cancellationToken = default)
        => _firestore.PatchFieldsAsync($"{SportsPath}/{id}",
            new Dictionary<string, object?>
            {
                // A match with a score on it is a result, not a fixture. Setting
                // both together is what moves it out of the coach's "needs a
                // result" list, so they can't drift apart.
                ["Kind"] = SportsItemKind.Result,
                ["Score"] = score.Trim()
            }, cancellationToken);

    /// <summary>
    /// Attaches an uploaded picture to a post.
    ///
    /// Separate from create because the object path contains the post's ID,
    /// which doesn't exist until the document has been written. Patches only
    /// the two image fields, so it can't clobber an edit made in between.
    /// </summary>
    public Task SetSportsImageAsync(
        string id, string imageUrl, string storagePath, CancellationToken cancellationToken = default)
        => _firestore.PatchFieldsAsync($"{SportsPath}/{id}",
            new Dictionary<string, object?>
            {
                ["ImageUrl"] = imageUrl.Trim(),
                ["ImageStoragePath"] = storagePath.Trim()
            }, cancellationToken);

    public async Task DeleteSportsItemAsync(string id, CancellationToken cancellationToken = default)
    {
        // The picture goes FIRST, while the document that points at it still
        // exists. Deleting the document first and then failing would leave an
        // object in the bucket that nothing references and nothing can find —
        // billable forever. Same ordering as course material files.
        try
        {
            var existing = await _firestore.GetDocumentAsync<SportsItem>(
                $"{SportsPath}/{id}", cancellationToken);

            if (!string.IsNullOrWhiteSpace(existing?.ImageStoragePath))
                await _storage.DeleteAsync(existing!.ImageStoragePath, cancellationToken);
        }
        catch (Exception ex)
        {
            // A picture we couldn't remove must not block removing the post.
            CrashLogger.Log($"ContentService.DeleteSportsImage:{id}", ex);
        }

        await _firestore.DeleteDocumentAsync($"{SportsPath}/{id}", cancellationToken);
    }

    // ==================== newspaper ====================

    // Mirrors `isSchoolCommunity() || isJournalist()` in firestore.rules.
    public bool CanReadArticles =>
        Holds(Role.Student) || Holds(Role.Teacher) || Holds(Role.Parent)
        || Holds(Role.Admin) || Holds(Role.Coach) || Holds(Role.Journalist);

    public bool CanWriteArticles => Holds(Role.Journalist) || Holds(Role.Admin);

    private bool IsAuthor(Article article)
        => !string.IsNullOrEmpty(CurrentUserId)
        && string.Equals(article.AuthorId, CurrentUserId, StringComparison.Ordinal);

    public bool CanEditArticle(Article article)
        => Holds(Role.Admin) || (Holds(Role.Journalist) && IsAuthor(article));

    // Wider than edit on purpose: a teacher can take an article down but not
    // rewrite a student's words under the student's name.
    public bool CanDeleteArticle(Article article)
        => Holds(Role.Admin) || Holds(Role.Teacher)
        || (Holds(Role.Journalist) && IsAuthor(article));

    public async Task<IReadOnlyList<Article>> GetArticlesAsync(
        CancellationToken cancellationToken = default)
    {
        var documents = await _firestore.ListDocumentsAsync<Article>(NewspaperPath, cancellationToken);

        return documents
            .Select(d => WithId(d.Value, d.Id))
            .OrderByDescending(a => a.PostedAt)
            .ToList();
    }

    public async Task<string> CreateArticleAsync(
        string title, string body, CancellationToken cancellationToken = default)
    {
        var session = _auth.CurrentSession
            ?? throw new InvalidOperationException("You need to be signed in to write an article.");

        var id = Guid.NewGuid().ToString("n");

        await _firestore.SetDocumentAsync($"{NewspaperPath}/{id}", new Article
        {
            Title = title.Trim(),
            Body = body.Trim(),
            // The picture is attached afterwards by SetArticleImageAsync: its
            // object path needs this ID, which only exists once this is written.
            ImageUrl = string.Empty,
            ImageStoragePath = string.Empty,
            // Must match request.auth.uid or the rule denies the write.
            AuthorId = session.UserId,
            AuthorName = _cachedProfile?.DisplayName ?? session.Email ?? string.Empty,
            PostedAt = DateTimeOffset.UtcNow
        }, cancellationToken);

        return id;
    }

    public Task UpdateArticleAsync(
        string id, string title, string body, CancellationToken cancellationToken = default)
        // Explicit field list: AuthorId and PostedAt must survive untouched, and
        // the picture columns are left out so fixing a typo can't wipe the image.
        => _firestore.PatchFieldsAsync($"{NewspaperPath}/{id}",
            new Dictionary<string, object?>
            {
                ["Title"] = title.Trim(),
                ["Body"] = body.Trim(),
                ["EditedAt"] = DateTimeOffset.UtcNow
            }, cancellationToken);

    public Task SetArticleImageAsync(
        string id, string imageUrl, string storagePath, CancellationToken cancellationToken = default)
        => _firestore.PatchFieldsAsync($"{NewspaperPath}/{id}",
            new Dictionary<string, object?>
            {
                ["ImageUrl"] = imageUrl.Trim(),
                ["ImageStoragePath"] = storagePath.Trim()
            }, cancellationToken);

    public async Task DeleteArticleAsync(string id, CancellationToken cancellationToken = default)
    {
        // Picture FIRST, while the document that points at it still exists —
        // the other order can leave an object nothing references. Same
        // ordering as sport and course material.
        try
        {
            var existing = await _firestore.GetDocumentAsync<Article>(
                $"{NewspaperPath}/{id}", cancellationToken);

            if (!string.IsNullOrWhiteSpace(existing?.ImageStoragePath))
                await _storage.DeleteAsync(existing!.ImageStoragePath, cancellationToken);
        }
        catch (Exception ex)
        {
            // A picture we couldn't remove must not block taking the article down.
            CrashLogger.Log($"ContentService.DeleteArticleImage:{id}", ex);
        }

        await _firestore.DeleteDocumentAsync($"{NewspaperPath}/{id}", cancellationToken);
    }

    // ==================== helpers ====================

    // The document ID lives in the path, not the body, so it has to be grafted
    // on after deserialisation.
    private static Announcement WithId(Announcement value, string id) => new()
    {
        Id = id,
        Title = value.Title,
        Body = value.Body,
        AuthorId = value.AuthorId,
        AuthorName = value.AuthorName,
        PostedAt = value.PostedAt,
        EditedAt = value.EditedAt,
        IsPinned = value.IsPinned,
        IsPublic = value.IsPublic,
        ExpiresAt = value.ExpiresAt
    };

    private static SportsItem WithId(SportsItem value, string id) => new()
    {
        Id = id,
        Kind = value.Kind,
        Title = value.Title,
        Body = value.Body,
        Sport = value.Sport,
        Team = value.Team,
        EventDate = value.EventDate,
        Opponent = value.Opponent,
        Venue = value.Venue,
        Score = value.Score,
        ImageUrl = value.ImageUrl,
        ImageStoragePath = value.ImageStoragePath,
        AuthorId = value.AuthorId,
        AuthorName = value.AuthorName,
        PostedAt = value.PostedAt
    };

    private static Article WithId(Article value, string id) => new()
    {
        Id = id,
        Title = value.Title,
        Body = value.Body,
        ImageUrl = value.ImageUrl,
        ImageStoragePath = value.ImageStoragePath,
        AuthorId = value.AuthorId,
        AuthorName = value.AuthorName,
        PostedAt = value.PostedAt,
        EditedAt = value.EditedAt
    };
}
