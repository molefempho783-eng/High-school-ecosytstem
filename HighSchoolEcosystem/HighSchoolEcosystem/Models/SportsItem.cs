namespace HighSchoolEcosystem.Models;

/// <summary>
/// What a sports post is. Drives which fields the UI asks for and how the row
/// is presented — a fixture leads with its date, a result with its score.
/// </summary>
public enum SportsItemKind
{
    /// <summary>An upcoming match. Has a date, opponent and venue.</summary>
    Fixture,

    /// <summary>A finished match. Has a score line.</summary>
    Result,

    /// <summary>Trials, training changes, tour news — anything not a match.</summary>
    News
}

/// <summary>
/// Mirrors /sports/{itemId}. Fixtures, results and sports news in one
/// collection: they share an audience, a sort order (by date) and a permission
/// model, and splitting them would mean three queries to build one screen.
///
/// Writable by Coach, Admin and Journalist. Journalist is included so students
/// on the newspaper can file match reports without being given a staff role —
/// the rules pin AuthorId to the creator, so they can't rewrite anyone else's.
/// </summary>
public sealed class SportsItem
{
    public string Id { get; init; } = string.Empty;
    public SportsItemKind Kind { get; init; } = SportsItemKind.News;

    public string Title { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;

    /// <summary>"Rugby", "Netball", "Athletics". Free text — schools invent codes.</summary>
    public string Sport { get; init; } = string.Empty;

    /// <summary>"U16A", "1st XI". Empty when the post isn't team-specific.</summary>
    public string Team { get; init; } = string.Empty;

    /// <summary>Kickoff for a fixture, or the date played for a result.</summary>
    public DateTimeOffset EventDate { get; init; } = DateTimeOffset.UtcNow;

    public string Opponent { get; init; } = string.Empty;
    public string Venue { get; init; } = string.Empty;

    /// <summary>
    /// Free text like "24 – 17" rather than two ints: some sports don't score
    /// that way, and athletics results aren't a score at all.
    /// </summary>
    public string Score { get; init; } = string.Empty;

    public string AuthorId { get; init; } = string.Empty;
    public string AuthorName { get; init; } = string.Empty;
    public DateTimeOffset PostedAt { get; init; } = DateTimeOffset.UtcNow;

    public bool IsUpcoming => Kind == SportsItemKind.Fixture && EventDate >= DateTimeOffset.Now;

    /// <summary>"Rugby · U16A" — whichever parts are set.</summary>
    public string SportAndTeam
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Sport)) parts.Add(Sport);
            if (!string.IsNullOrWhiteSpace(Team)) parts.Add(Team);
            return string.Join(" · ", parts);
        }
    }

    /// <summary>The grey line under the title, built from whatever the post has.</summary>
    public string MetaLine
    {
        get
        {
            var parts = new List<string>();

            if (Kind != SportsItemKind.News)
                parts.Add(EventDate.ToLocalTime().ToString("ddd d MMM, HH:mm"));

            if (!string.IsNullOrWhiteSpace(Opponent)) parts.Add($"vs {Opponent}");
            if (!string.IsNullOrWhiteSpace(Venue)) parts.Add(Venue);
            if (!string.IsNullOrWhiteSpace(Score)) parts.Add(Score);

            var sportAndTeam = SportAndTeam;
            if (!string.IsNullOrWhiteSpace(sportAndTeam)) parts.Add(sportAndTeam);

            return parts.Count == 0
                ? PostedAt.ToLocalTime().ToString("ddd d MMM")
                : string.Join(" · ", parts);
        }
    }
}
