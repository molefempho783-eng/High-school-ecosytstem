using HighSchoolEcosystem.Models;
using HighSchoolEcosystem.Services.Interfaces;

namespace HighSchoolEcosystem.Services;

/// <summary>
/// Teams and their notice boards, over Firestore REST like everything else.
///
/// Mirrors the class half of <see cref="ClassService"/> closely and on purpose:
/// same ownership rule, same roster-as-a-list, same notices subcollection. If
/// you're changing something here, check whether the class version needs the
/// same change — they are the same idea twice.
/// </summary>
public sealed class TeamService : ITeamService
{
    private const string TeamsPath = "teams";

    private readonly IFirestoreClient _firestore;
    private readonly IFirebaseAuthService _auth;
    private readonly IUserProfileService _profiles;

    private UserProfile? _cachedProfile;

    public TeamService(IFirestoreClient firestore, IFirebaseAuthService auth, IUserProfileService profiles)
    {
        _firestore = firestore;
        _auth = auth;
        _profiles = profiles;
    }

    /// <summary>
    /// Fails CLOSED until PrimeAsync has run. A button that appears and then is
    /// refused is worse than one that appears a moment late.
    /// </summary>
    public bool CanManageTeams =>
        _cachedProfile?.HasRole(Role.Coach) == true || _cachedProfile?.HasRole(Role.Admin) == true;

    public async Task PrimeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _cachedProfile = await _profiles.GetOrCreateCurrentProfileAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // A failed prime leaves CanManageTeams false, which is the safe
            // direction — the rules would refuse the write anyway.
            CrashLogger.Log("TeamService.Prime", ex);
        }
    }

    // ---- teams ----

    public async Task<IReadOnlyList<SportsTeam>> GetAllTeamsAsync(CancellationToken cancellationToken = default)
    {
        var documents = await _firestore.ListDocumentsAsync<SportsTeam>(TeamsPath, cancellationToken);

        return documents
            .Select(d => WithId(d.Value, d.Id))
            .OrderBy(t => t.Sport, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<SportsTeam>> GetMyTeamsAsync(CancellationToken cancellationToken = default)
    {
        var uid = _auth.CurrentSession?.UserId;
        if (string.IsNullOrEmpty(uid)) return [];

        // Filtered client-side rather than with a where-query: the rules make
        // every team readable to the school community, so listing is one call
        // and a query would need its own composite index for no benefit at
        // school scale.
        var all = await GetAllTeamsAsync(cancellationToken);
        return all.Where(t => string.Equals(t.CoachId, uid, StringComparison.Ordinal)).ToList();
    }

    public async Task<IReadOnlyList<SportsTeam>> GetTeamsForStudentAsync(
        string studentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(studentId)) return [];

        var all = await GetAllTeamsAsync(cancellationToken);
        return all.Where(t => t.MemberIds.Contains(studentId, StringComparer.Ordinal)).ToList();
    }

    public async Task<SportsTeam?> GetTeamAsync(string teamId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(teamId)) return null;

        var team = await _firestore.GetDocumentAsync<SportsTeam>($"{TeamsPath}/{teamId}", cancellationToken);
        return team is null ? null : WithId(team, teamId);
    }

    public async Task<string> CreateTeamAsync(
        string name, string sport, string season, CancellationToken cancellationToken = default)
    {
        var session = _auth.CurrentSession
            ?? throw new InvalidOperationException("You need to be signed in to create a team.");

        var profile = await _profiles.GetOrCreateCurrentProfileAsync(cancellationToken);
        var id = Guid.NewGuid().ToString("n");

        await _firestore.SetDocumentAsync($"{TeamsPath}/{id}", new SportsTeam
        {
            Name = name.Trim(),
            Sport = sport.Trim(),
            Season = season.Trim(),
            // Must equal request.auth.uid or the create is denied — and it's
            // what makes this coach the owner from the first moment.
            CoachId = session.UserId,
            CoachName = profile?.DisplayName ?? session.Email ?? string.Empty,
            MemberIds = [],
            CreatedAt = DateTimeOffset.UtcNow
        }, cancellationToken);

        return id;
    }

    public Task UpdateTeamAsync(
        string teamId, string name, string sport, string season, CancellationToken cancellationToken = default)
        // Explicit field list, and CoachId is NOT in it. The rules reject any
        // write that changes ownership, and a whole-object write would resend
        // it — which also means it would resend an empty MemberIds and wipe the
        // squad. This is the SetDocumentAsync trap, and it costs a roster here.
        => _firestore.PatchFieldsAsync($"{TeamsPath}/{teamId}",
            new Dictionary<string, object?>
            {
                ["Name"] = name.Trim(),
                ["Sport"] = sport.Trim(),
                ["Season"] = season.Trim()
            }, cancellationToken);

    public Task SetSquadAsync(
        string teamId, IReadOnlyList<string> memberIds, CancellationToken cancellationToken = default)
        => _firestore.PatchFieldsAsync($"{TeamsPath}/{teamId}",
            new Dictionary<string, object?>
            {
                ["MemberIds"] = memberIds.Distinct(StringComparer.Ordinal).ToList()
            }, cancellationToken);

    public async Task DeleteTeamAsync(string teamId, CancellationToken cancellationToken = default)
    {
        // Notices first, while the team they belong to still exists. Deleting
        // the team first would orphan them: Firestore does not cascade, so the
        // subcollection would survive with nothing able to reach or delete it.
        try
        {
            var notices = await _firestore.ListDocumentsAsync<Announcement>(
                NoticesPath(teamId), cancellationToken);

            foreach (var notice in notices)
                await _firestore.DeleteDocumentAsync($"{NoticesPath(teamId)}/{notice.Id}", cancellationToken);
        }
        catch (Exception ex)
        {
            // A notice we couldn't remove must not block removing the team.
            CrashLogger.Log($"TeamService.DeleteNotices:{teamId}", ex);
        }

        await _firestore.DeleteDocumentAsync($"{TeamsPath}/{teamId}", cancellationToken);
    }

    // ---- notices ----

    private static string NoticesPath(string teamId) => $"{TeamsPath}/{teamId}/notices";

    public async Task<IReadOnlyList<Announcement>> GetTeamNoticesAsync(
        string teamId, bool includeExpired = false, CancellationToken cancellationToken = default)
    {
        var documents = await _firestore.ListDocumentsAsync<Announcement>(
            NoticesPath(teamId), cancellationToken);

        return documents
            .Select(d => WithTeamContext(d.Value, d.Id, teamId, string.Empty))
            // Readers don't see lapsed notices; the coach who wrote them does.
            .Where(a => includeExpired || !a.IsExpired)
            .OrderByDescending(a => a.IsPinned)
            .ThenByDescending(a => a.PostedAt)
            .ToList();
    }

    public async Task<string> SaveTeamNoticeAsync(
        string teamId, string teamName, Announcement notice, CancellationToken cancellationToken = default)
    {
        var session = _auth.CurrentSession
            ?? throw new InvalidOperationException("You need to be signed in to post a notice.");

        var isNew = string.IsNullOrWhiteSpace(notice.Id);
        var id = isNew ? Guid.NewGuid().ToString("n") : notice.Id;

        if (isNew)
        {
            var profile = await _profiles.GetOrCreateCurrentProfileAsync(cancellationToken);

            await _firestore.SetDocumentAsync($"{NoticesPath(teamId)}/{id}", new Announcement
            {
                Title = notice.Title.Trim(),
                Body = notice.Body.Trim(),
                IsPinned = notice.IsPinned,
                ExpiresAt = notice.ExpiresAt,
                // Denormalised so a merged feed can label the notice without a
                // team lookup per row.
                TeamName = teamName,
                // Must equal request.auth.uid or the rule denies the create.
                AuthorId = session.UserId,
                AuthorName = profile?.DisplayName ?? session.Email ?? string.Empty,
                PostedAt = DateTimeOffset.UtcNow
            }, cancellationToken);

            return id;
        }

        // Explicit field list: the rule requires AuthorId unchanged, and a
        // whole-object write would resend it.
        await _firestore.PatchFieldsAsync($"{NoticesPath(teamId)}/{id}",
            new Dictionary<string, object?>
            {
                ["Title"] = notice.Title.Trim(),
                ["Body"] = notice.Body.Trim(),
                ["IsPinned"] = notice.IsPinned,
                ["ExpiresAt"] = notice.ExpiresAt,
                ["EditedAt"] = DateTimeOffset.UtcNow
            }, cancellationToken);

        return id;
    }

    public Task DeleteTeamNoticeAsync(string teamId, string noticeId, CancellationToken cancellationToken = default)
        => _firestore.DeleteDocumentAsync($"{NoticesPath(teamId)}/{noticeId}", cancellationToken);

    public async Task<IReadOnlyList<Announcement>> GetNoticesForTeamsAsync(
        IReadOnlyList<SportsTeam> teams, CancellationToken cancellationToken = default)
    {
        var all = new List<Announcement>();

        // One read per team. Fine at school scale — a pupil is in two or three —
        // and it avoids a collection-group query, which would need its own index
        // and a rule that can't see the parent team to check membership.
        foreach (var team in teams)
        {
            try
            {
                var documents = await _firestore.ListDocumentsAsync<Announcement>(
                    NoticesPath(team.Id), cancellationToken);

                // The live team name wins over the stored copy, so a rename
                // shows through immediately.
                all.AddRange(documents
                    .Select(d => WithTeamContext(d.Value, d.Id, team.Id, team.Name))
                    // This feed is always for readers, so lapsed notices never
                    // belong in it.
                    .Where(a => !a.IsExpired));
            }
            catch (Exception ex)
            {
                // One unreadable team shouldn't empty the whole feed.
                CrashLogger.Log($"TeamService.NoticesForTeam:{team.Id}", ex);
            }
        }

        return all;
    }

    // ---- helpers ----

    // The document ID lives in the path, not the body.
    private static SportsTeam WithId(SportsTeam value, string id) => new()
    {
        Id = id,
        Name = value.Name,
        Sport = value.Sport,
        Season = value.Season,
        CoachId = value.CoachId,
        CoachName = value.CoachName,
        MemberIds = value.MemberIds,
        CreatedAt = value.CreatedAt
    };

    // The ID and the team both live in the path. Grafted on after
    // deserialisation so a merged feed can tell a team notice from a class one.
    private static Announcement WithTeamContext(
        Announcement value, string id, string teamId, string teamName) => new()
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
        ExpiresAt = value.ExpiresAt,
        TeamId = teamId,
        TeamName = string.IsNullOrWhiteSpace(teamName) ? value.TeamName : teamName
    };
}
