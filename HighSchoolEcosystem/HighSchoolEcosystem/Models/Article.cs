namespace HighSchoolEcosystem.Models;

/// <summary>
/// Mirrors /newspaper/{articleId}: one article in the school Newspaper.
///
/// Written by Journalists (and Admins), read by the whole school community.
/// Articles go live as soon as they're saved — there is no draft or approval
/// step. Teachers and admins can take one down; only the author or an admin
/// can change its words. All of that is enforced in firestore.rules.
///
/// THREE projections build this object — ContentService.CreateArticleAsync,
/// UpdateArticleAsync and WithId. A new property must be added to all three or
/// it silently never round-trips (the SportsItem image fields and
/// Assignment.SubmissionMode both fell into that trap).
/// </summary>
public sealed class Article
{
    public string Id { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;

    /// <summary>
    /// A tokenised download URL for the article's picture, so an Image can
    /// bind straight to it. Empty when the article has no picture.
    /// </summary>
    public string ImageUrl { get; init; } = string.Empty;

    /// <summary>
    /// The Storage object path behind ImageUrl — newspaper/{articleId}/{file}.
    /// Kept so deleting the article can delete its picture; without it the
    /// object stays in the bucket, billable forever, with nothing pointing at it.
    /// </summary>
    public string ImageStoragePath { get; init; } = string.Empty;

    public string AuthorId { get; init; } = string.Empty;
    public string AuthorName { get; init; } = string.Empty;
    public DateTimeOffset PostedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset EditedAt { get; init; }

    public bool HasImage => !string.IsNullOrWhiteSpace(ImageUrl);

    public bool WasEdited => EditedAt > PostedAt;

    /// <summary>
    /// Falls back rather than showing a raw UID — a byline of "8fH2kQ..." is
    /// worse than no byline.
    /// </summary>
    public string ByLine => string.IsNullOrWhiteSpace(AuthorName)
        ? "By a school journalist"
        : $"By {AuthorName}";

    /// <summary>"By Amara Dlamini · Mon 6 Oct" — the grey line under a tile title.</summary>
    public string MetaLine => $"{ByLine} · {PostedAt.ToLocalTime():ddd d MMM}";

    /// <summary>
    /// The first couple of sentences, for a tile. Cut on a word boundary so a
    /// preview never ends mid-word.
    /// </summary>
    public string Preview
    {
        get
        {
            var text = Body.Trim().ReplaceLineEndings(" ");
            const int limit = 140;
            if (text.Length <= limit) return text;

            var cut = text.LastIndexOf(' ', limit);
            return (cut > 60 ? text[..cut] : text[..limit]).TrimEnd(',', '.', ';', ':') + "…";
        }
    }

    /// <summary>
    /// The whole article for the detail sheet: the body, then who wrote it and
    /// when. The title is the sheet's heading, so it isn't repeated here.
    /// </summary>
    public string DetailText
    {
        get
        {
            var lines = new List<string>();

            if (!string.IsNullOrWhiteSpace(Body))
            {
                lines.Add(Body.Trim());
                lines.Add(string.Empty);
            }

            lines.Add(ByLine);
            lines.Add($"on {PostedAt.ToLocalTime():d MMMM yyyy}");
            if (WasEdited) lines.Add($"Edited {EditedAt.ToLocalTime():d MMMM yyyy}");

            return string.Join(Environment.NewLine, lines);
        }
    }
}
