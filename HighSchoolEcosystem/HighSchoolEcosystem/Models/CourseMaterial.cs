namespace HighSchoolEcosystem.Models;

/// <summary>
/// How a piece of course material is stored, and therefore how a student opens
/// it. One collection covers all three so a teacher can interleave a written
/// chapter, a PDF and a YouTube link in a single ordered list.
/// </summary>
public enum MaterialKind
{
    /// <summary>Written in the app, read in the app. Body holds the text.</summary>
    Chapter,

    /// <summary>An external URL — Drive, OneDrive, YouTube, a public PDF.</summary>
    Link,

    /// <summary>Uploaded to Firebase Storage. Url is the download URL, StoragePath the object.</summary>
    File
}

/// <summary>
/// Mirrors /classes/{classId}/materials/{materialId}. Under the class, so the
/// rules gate it on class membership the same way assignments are — no extra
/// lookup, and unenrolling a student removes their access automatically.
///
/// Written by the class teacher (and admins), read by anyone enrolled.
/// </summary>
public sealed class CourseMaterial
{
    public string Id { get; init; } = string.Empty;
    public MaterialKind Kind { get; init; } = MaterialKind.Chapter;

    public string Title { get; init; } = string.Empty;

    /// <summary>One line describing it, shown under the title in the list.</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>
    /// The chapter text. Only meaningful for <see cref="MaterialKind.Chapter"/>
    /// and empty otherwise — a Link or File has nothing to store here.
    /// </summary>
    public string Body { get; init; } = string.Empty;

    /// <summary>
    /// Where to open it. The external URL for a Link, the Storage download URL
    /// for a File, empty for a Chapter.
    /// </summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>
    /// The Storage object path for a File, so deleting the material can also
    /// delete the object. Without this an orphaned file stays billable forever.
    /// Empty for Link and Chapter.
    /// </summary>
    public string StoragePath { get; init; } = string.Empty;

    /// <summary>Original filename, shown so a student knows what they're downloading.</summary>
    public string FileName { get; init; } = string.Empty;

    public long FileSizeBytes { get; init; }

    /// <summary>
    /// Position in the list. Sparse and teacher-assigned rather than a dense
    /// index, so inserting a chapter between two others doesn't rewrite every
    /// document after it.
    /// </summary>
    public int Order { get; init; }

    /// <summary>
    /// The learning unit this belongs to. Empty means loose material, which the
    /// UI groups under "Module information" — that's also where everything
    /// created before units existed ends up, so nothing disappears.
    /// </summary>
    public string UnitId { get; init; } = string.Empty;

    /// <summary>
    /// Optional grouping inside a unit — ARC calls these Themes ("Theme 1:
    /// Usability"). Free text rather than its own collection: a theme has no
    /// properties of its own beyond a name, and a third level of documents to
    /// keep in sync would buy nothing.
    /// </summary>
    public string Theme { get; init; } = string.Empty;

    public bool HasTheme => !string.IsNullOrWhiteSpace(Theme);

    public string AuthorId { get; init; } = string.Empty;
    public string AuthorName { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; }

    public bool IsChapter => Kind == MaterialKind.Chapter;
    public bool IsOpenable => Kind != MaterialKind.Chapter && !string.IsNullOrWhiteSpace(Url);

    /// <summary>"1.4 MB" — only meaningful for uploads.</summary>
    public string FileSizeLabel
    {
        get
        {
            if (FileSizeBytes <= 0) return string.Empty;
            if (FileSizeBytes < 1024) return $"{FileSizeBytes} B";
            if (FileSizeBytes < 1024 * 1024) return $"{FileSizeBytes / 1024.0:0.#} KB";
            return $"{FileSizeBytes / (1024.0 * 1024.0):0.#} MB";
        }
    }

    /// <summary>The grey line under the title in the materials list.</summary>
    public string MetaLine
    {
        get
        {
            var parts = new List<string>
            {
                Kind switch
                {
                    MaterialKind.Chapter => "Chapter",
                    MaterialKind.Link => "Link",
                    _ => "File"
                }
            };

            if (!string.IsNullOrWhiteSpace(Summary)) parts.Add(Summary);
            if (!string.IsNullOrWhiteSpace(FileName)) parts.Add(FileName);

            var size = FileSizeLabel;
            if (!string.IsNullOrWhiteSpace(size)) parts.Add(size);

            return string.Join(" · ", parts);
        }
    }
}
