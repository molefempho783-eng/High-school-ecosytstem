using HighSchoolEcosystem.Models;

namespace HighSchoolEcosystem.Services.Interfaces;

/// <summary>
/// Sports teams and their notice boards.
///
/// Separate from IContentService (which owns /sports — fixtures, results and
/// articles, all public) because a team is the opposite: a roster and a private
/// notice board. Mixing them would put "readable by the world" and "readable by
/// the squad" behind one service and invite the wrong default.
///
/// The shape deliberately mirrors the class half of IClassService: a team has an
/// owner, a roster and its own announcements, exactly like a class.
/// </summary>
public interface ITeamService
{
    /// <summary>True for Coach and Admin. UI convenience; the rules decide.</summary>
    bool CanManageTeams { get; }

    /// <summary>Must be awaited once before CanManageTeams means anything.</summary>
    Task PrimeAsync(CancellationToken cancellationToken = default);

    /// <summary>Every team in the school. Readable by the whole community.</summary>
    Task<IReadOnlyList<SportsTeam>> GetAllTeamsAsync(CancellationToken cancellationToken = default);

    /// <summary>Teams this coach owns.</summary>
    Task<IReadOnlyList<SportsTeam>> GetMyTeamsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Teams a given student is in. Used for the student's own feed and, with a
    /// child's UID, for the parent's.
    /// </summary>
    Task<IReadOnlyList<SportsTeam>> GetTeamsForStudentAsync(string studentId, CancellationToken cancellationToken = default);

    Task<SportsTeam?> GetTeamAsync(string teamId, CancellationToken cancellationToken = default);

    /// <summary>Creates a team owned by the signed-in coach. Returns its ID.</summary>
    Task<string> CreateTeamAsync(string name, string sport, string season, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames a team. CoachId is deliberately not a parameter — the rules
    /// reject any write that changes it, so offering it would be a lie.
    /// </summary>
    Task UpdateTeamAsync(string teamId, string name, string sport, string season, CancellationToken cancellationToken = default);

    /// <summary>Replaces the squad wholesale, the same way class enrolment works.</summary>
    Task SetSquadAsync(string teamId, IReadOnlyList<string> memberIds, CancellationToken cancellationToken = default);

    Task DeleteTeamAsync(string teamId, CancellationToken cancellationToken = default);

    // ---- team notices ----

    /// <summary>
    /// Notices for one team. <paramref name="includeExpired"/> is for the coach
    /// who wrote them: a reader stops seeing a lapsed notice, the author keeps
    /// seeing it dimmed so they can tell it lapsed and extend it.
    /// </summary>
    Task<IReadOnlyList<Announcement>> GetTeamNoticesAsync(string teamId, bool includeExpired = false, CancellationToken cancellationToken = default);

    /// <summary>Creates or edits a notice. Returns its ID.</summary>
    Task<string> SaveTeamNoticeAsync(string teamId, string teamName, Announcement notice, CancellationToken cancellationToken = default);

    Task DeleteTeamNoticeAsync(string teamId, string noticeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Live notices across several teams, for a merged reader feed. Always
    /// excludes expired ones — this is only ever called for readers.
    /// </summary>
    Task<IReadOnlyList<Announcement>> GetNoticesForTeamsAsync(IReadOnlyList<SportsTeam> teams, CancellationToken cancellationToken = default);
}
