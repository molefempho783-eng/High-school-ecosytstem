using HighSchoolEcosystem.Models;

namespace HighSchoolEcosystem.ViewModels;

/// <summary>
/// One announcement as the list shows it.
///
/// <see cref="CanManage"/> is stamped per row from the caller's roles rather
/// than read off the row itself, so the same template serves a reader and an
/// admin without a second DataTemplate.
/// </summary>
public sealed record AnnouncementRow(Announcement Source, bool CanManage)
{
    public string Id => Source.Id;
    public string Title => Source.Title;
    public string Body => Source.Body;
    public bool IsPinned => Source.IsPinned;

    public string MetaLine
    {
        get
        {
            var parts = new List<string> { Source.PostedAt.ToLocalTime().ToString("ddd d MMM") };

            if (!string.IsNullOrWhiteSpace(Source.AuthorName)) parts.Add(Source.AuthorName);
            if (Source.WasEdited) parts.Add($"edited {Source.EditedAt.ToLocalTime():d MMM}");
            if (Source.IsPublic) parts.Add("Public");

            // "Until 12 Sep" while live, "Expired 3 Sep" after. Only authors
            // ever see the expired form — readers don't get the row at all.
            var expiry = Source.ExpiryLabel;
            if (!string.IsNullOrWhiteSpace(expiry)) parts.Add(expiry);

            return string.Join(" · ", parts);
        }
    }

    public bool IsExpired => Source.IsExpired;

    /// <summary>
    /// Greys out a lapsed notice in the author's list, so it reads as
    /// already-gone rather than as something students can still see.
    /// </summary>
    public double RowOpacity => IsExpired ? 0.55 : 1;

    public bool HasBody => !string.IsNullOrWhiteSpace(Source.Body);

    /// <summary>"Mathematics Grade 10A" or "Whole school" — shown as a chip so
    /// a merged feed makes clear who a notice came from.</summary>
    public string ScopeLabel => Source.ScopeLabel;

    public bool IsClassAnnouncement => Source.IsClassAnnouncement;

    /// <summary>
    /// Class notices get the class colour, school-wide ones the brand accent,
    /// so the two are distinguishable at a glance in one feed.
    /// </summary>
    public Color AccentColor { get; init; } = Color.FromArgb("#178C8C");
}
