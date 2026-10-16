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

    /// <summary>
    /// A picture for the post — a match photo, or the image on an article.
    /// A tokenised download URL, so an Image can bind straight to it.
    /// </summary>
    public string ImageUrl { get; init; } = string.Empty;

    /// <summary>
    /// The Storage object path behind ImageUrl. Kept so deleting the post can
    /// delete its picture; without it the object stays in the bucket, billable
    /// forever, with nothing pointing at it.
    /// </summary>
    public string ImageStoragePath { get; init; } = string.Empty;

    public bool HasImage => !string.IsNullOrWhiteSpace(ImageUrl);

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

    /// <summary>
    /// Who posted it, spelled out. Falls back rather than showing a raw UID —
    /// a byline of "8fH2kQ..." is worse than no byline.
    /// </summary>
    public string ByLine => string.IsNullOrWhiteSpace(AuthorName)
        ? "Posted by a member of staff"
        : $"Posted by {AuthorName}";

    /// <summary>
    /// The full record, for the detail popup. A tile can only carry a title and
    /// one meta line; this is everything the post actually holds, each fact on
    /// its own line so it stays readable in an alert.
    /// </summary>
    public string DetailText
    {
        get
        {
            // Sport and team are the sheet's chip, so they're not repeated here.
            var lines = new List<string>();

            if (Kind != SportsItemKind.News)
                lines.Add($"When: {EventDate.ToLocalTime():dddd d MMMM yyyy, HH:mm}");

            if (!string.IsNullOrWhiteSpace(Opponent)) lines.Add($"Against: {Opponent}");
            if (!string.IsNullOrWhiteSpace(Venue)) lines.Add($"Venue: {Venue}");
            if (!string.IsNullOrWhiteSpace(Score)) lines.Add($"Score: {Score}");

            if (!string.IsNullOrWhiteSpace(Body))
            {
                lines.Add(string.Empty);
                lines.Add(Body.Trim());
            }

            lines.Add(string.Empty);
            lines.Add(ByLine);
            lines.Add($"on {PostedAt.ToLocalTime():d MMMM yyyy}");

            return string.Join(Environment.NewLine, lines);
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
