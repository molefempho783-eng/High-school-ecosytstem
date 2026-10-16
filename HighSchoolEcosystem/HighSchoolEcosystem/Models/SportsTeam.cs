namespace HighSchoolEcosystem.Models;

/// <summary>
/// Mirrors /teams/{teamId} — a squad a coach runs.
///
/// Deliberately shaped like <see cref="SchoolClass"/> rather than like a tag on
/// /sports: a team has a roster, an owner and its own notice board, which is
/// exactly what a class has. Reusing that shape means the rules, the service and
/// the screens all work the same way, and a coach who has seen a class page can
/// read a team page without being taught it.
///
/// It is NOT a class, though — no timetable, no marks, no course content. A
/// student can be in a team without being in any of that coach's classes, and
/// usually is.
/// </summary>
public sealed class SportsTeam
{
    public string Id { get; init; } = string.Empty;

    /// <summary>"U16A Rugby", "1st XI Netball". Free text — schools invent these.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>"Rugby", "Netball". Kept separate from the name so teams can be grouped.</summary>
    public string Sport { get; init; } = string.Empty;

    /// <summary>"2026", "Winter 2026". Optional; lets last year's squad be kept without confusion.</summary>
    public string Season { get; init; } = string.Empty;

    /// <summary>
    /// The coach who owns it. Set from the signed-in user at create time and
    /// immutable afterwards — the rules pin it, exactly like a class's TeacherId.
    /// </summary>
    public string CoachId { get; init; } = string.Empty;

    /// <summary>Denormalised so a squad list doesn't need a user lookup per row.</summary>
    public string CoachName { get; init; } = string.Empty;

    /// <summary>
    /// The squad. Student UIDs, and the reason this is a list on the team
    /// document rather than a subcollection: the rules need to answer "is this
    /// person in this team?" in ONE document read, on every notice they try to
    /// read. A subcollection would cost a read per check.
    /// </summary>
    public List<string> MemberIds { get; init; } = [];

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public int MemberCount => MemberIds.Count;

    /// <summary>"Rugby · 2026" — whichever parts are set.</summary>
    public string SportAndSeason
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Sport)) parts.Add(Sport);
            if (!string.IsNullOrWhiteSpace(Season)) parts.Add(Season);
            return string.Join(" · ", parts);
        }
    }

    public string MemberLabel => MemberIds.Count == 1 ? "1 in the squad" : $"{MemberIds.Count} in the squad";
}
