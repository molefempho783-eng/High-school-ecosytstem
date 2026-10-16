using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HighSchoolEcosystem.Models;
using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.Services.Interfaces;

namespace HighSchoolEcosystem.ViewModels;

/// <summary>
/// One button on the navigation rail.
///
/// Data rather than markup, so the rail is declared once in XAML and every
/// dashboard supplies its own items. Adding a destination is a line of C#, not
/// twelve lines of duplicated XAML in five files.
/// </summary>
public sealed partial class RailItem : ObservableObject
{
    public RailItem(string glyph, string label, ICommand command)
    {
        Glyph = glyph;
        Label = label;
        Command = command;
    }

    public string Glyph { get; }

    /// <summary>
    /// The rail draws this IMAGE rather than the glyph: images are bundled per
    /// platform at build time, so the rail can't go blank if a font fails to
    /// load. Derived from the glyph so no caller has to change.
    /// </summary>
    public string IconImage => Icons.RailImage(Glyph);
    public string Label { get; }
    public ICommand Command { get; }

    /// <summary>Draws a divider above this item, for grouping.</summary>
    public bool StartsGroup { get; init; }

    /// <summary>
    /// Whether this item shows its label. Held per item rather than read from
    /// the parent view model through a RelativeSource binding: AncestorType
    /// against a view-model type is fragile under compiled bindings, and this
    /// is a plain property the XAML compiler can resolve.
    ///
    /// Kept in step by DashboardShellViewModel when the rail is toggled.
    /// </summary>
    [ObservableProperty] private bool isExpanded;
}

/// <summary>
/// Shared chrome for every role's home screen: the rail, the header identity,
/// and the busy/error state that all five dashboards render identically.
///
/// This exists so student, teacher, admin, parent and coach can't drift apart.
/// Before it, the rail lived only on the student dashboard and the others had
/// a different header — the same product looked like three products depending
/// on who signed in.
/// </summary>
public abstract partial class DashboardShellViewModel : ObservableObject
{
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isRefreshing;
    [ObservableProperty] private string statusMessage = string.Empty;

    // ---- header identity ----

    [ObservableProperty] private string schoolName = string.Empty;
    [ObservableProperty] private string displayName = string.Empty;
    [ObservableProperty] private string roleLabel = string.Empty;

    /// <summary>Two letters for the avatar tile, top right.</summary>
    [ObservableProperty] private string initials = "?";

    // ---- navigation rail ----

    /// <summary>
    /// The rail's buttons. Populated by each dashboard during load, once its
    /// roles are known — a teacher who is also an admin gets both sets.
    /// </summary>
    [ObservableProperty] private ObservableCollection<RailItem> railItems = [];

    /// <summary>
    /// Whether the rail shows labels beside its icons. Collapsed by default: on
    /// a phone the labels cost width the content needs more.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RailWidth))]
    private bool isRailExpanded;

    /// <summary>
    /// 44 collapsed, 190 expanded.
    ///
    /// The rail FLOATS over the content rather than occupying a column, so
    /// expanding it covers the page instead of squeezing it. 44 is the platform
    /// minimum touch target — any narrower and the icons stop being reliably
    /// tappable, which matters more than the handful of pixels it would save.
    /// </summary>
    public double RailWidth => IsRailExpanded ? 190 : CollapsedRailWidth;

    /// <summary>
    /// The inset the content keeps on its left so nothing hides under the
    /// collapsed rail. Pages bind their padding to this.
    /// </summary>
    public const double CollapsedRailWidth = 44;

    [RelayCommand]
    private void ToggleRail() => IsRailExpanded = !IsRailExpanded;

    // Each item carries its own IsExpanded so the label binding stays simple.
    partial void OnIsRailExpandedChanged(bool value)
    {
        foreach (var item in RailItems) item.IsExpanded = value;
    }

    // A rail rebuilt while expanded must not come back collapsed.
    partial void OnRailItemsChanged(ObservableCollection<RailItem> value)
    {
        foreach (var item in value) item.IsExpanded = IsRailExpanded;
    }

    // ---- newspaper ----
    //
    // Shared here rather than copied into five dashboards: the Newspaper
    // section is identical for every role, and it's rendered by one control
    // (Controls/NewspaperWidget) that reads these through the inherited
    // BindingContext. Each dashboard only calls LoadNewspaperAsync.

    [ObservableProperty] private ObservableCollection<ArticleRow> newspaperArticles = [];
    [ObservableProperty] private bool hasNewspaperArticles;
    [ObservableProperty] private string newspaperCount = "0";
    [ObservableProperty] private string newspaperEmptyMessage = string.Empty;

    /// <summary>
    /// Whether the section exists at all. False for visitors and guests, who
    /// the rules don't let read the Newspaper — a section that could only ever
    /// show an error is worse than none. The rail item follows the same test.
    /// </summary>
    [ObservableProperty] private bool showsNewspaper;

    /// <summary>Journalist or Admin — shows "Write an article".</summary>
    [ObservableProperty] private bool canWriteArticles;

    private IContentService? _newspaperContent;

    /// <summary>
    /// How many articles a dashboard shows before "Read the Newspaper".
    /// </summary>
    private const int NewspaperPreviewCount = 4;

    /// <summary>
    /// Every role except Visitor reads the Newspaper — mirrors
    /// `isSchoolCommunity() || isJournalist()` in firestore.rules. Used to
    /// decide the rail item before the content service has been primed.
    /// </summary>
    protected static bool ReadsNewspaper(UserProfile? profile)
        => profile is not null && profile.AllRoles().Any(r => r != Role.Visitor);

    /// <summary>
    /// Loads the latest articles. Call AFTER IContentService.PrimeAsync, which
    /// every dashboard already does for its announcements. Never throws: a
    /// broken Newspaper mustn't make the rest of the dashboard look broken.
    /// </summary>
    protected async Task LoadNewspaperAsync(IContentService content)
    {
        _newspaperContent = content;
        ShowsNewspaper = content.CanReadArticles;
        CanWriteArticles = content.CanWriteArticles;

        if (!ShowsNewspaper)
        {
            NewspaperArticles = [];
            HasNewspaperArticles = false;
            NewspaperCount = "0";
            NewspaperEmptyMessage = string.Empty;
            return;
        }

        try
        {
            var all = await content.GetArticlesAsync();
            var shown = all.Take(NewspaperPreviewCount)
                .Select(a => new ArticleRow(a, content.CanEditArticle(a), content.CanDeleteArticle(a)))
                .ToList();

            NewspaperArticles = new ObservableCollection<ArticleRow>(shown);
            HasNewspaperArticles = shown.Count > 0;
            NewspaperCount = all.Count.ToString();
            NewspaperEmptyMessage = HasNewspaperArticles
                ? string.Empty
                : "No articles yet. Check back soon.";
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Dashboard.Newspaper", ex);
            NewspaperArticles = [];
            HasNewspaperArticles = false;
            NewspaperEmptyMessage = "We couldn't load the Newspaper right now.";
        }
    }

    /// <summary>
    /// Removes an article from the dashboard's detail sheet. The caller has
    /// already confirmed. Returns false if it was refused or failed.
    /// </summary>
    public async Task<bool> DeleteArticleAsync(ArticleRow row)
    {
        if (_newspaperContent is null || !row.CanDelete) return false;

        try
        {
            await _newspaperContent.DeleteArticleAsync(row.Id);
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Dashboard.DeleteArticle", ex);
            return false;
        }

        await LoadNewspaperAsync(_newspaperContent);
        return true;
    }

    // ---- teacher-parent messages ----
    //
    // Shared for the same reason as the Newspaper: teacher and parent
    // dashboards render one control (Controls/MessagesWidget) from these.

    [ObservableProperty] private ObservableCollection<ConversationRow> recentConversations = [];
    [ObservableProperty] private bool hasRecentConversations;
    [ObservableProperty] private bool showsMessages;
    [ObservableProperty] private string messagesSummary = string.Empty;

    /// <summary>Threads with something new from the other person.</summary>
    [ObservableProperty] private ChatUnread unreadConversations = new();

    /// <summary>Never throws: messages failing mustn't make the dashboard look broken.</summary>
    protected async Task LoadMessagesAsync(IConversationService conversations)
    {
        ShowsMessages = true;
        try
        {
            var me = conversations.CurrentUserId;
            var all = await conversations.GetMyConversationsAsync();
            var rows = all.Select(c => new ConversationRow(c, me, conversations.IsUnread(c))).ToList();

            RecentConversations = new ObservableCollection<ConversationRow>(rows.Take(3));
            HasRecentConversations = rows.Count > 0;
            UnreadConversations.Count = rows.Count(r => r.IsUnread);
            MessagesSummary = rows.Count == 0
                ? "No conversations yet. Open Messages to start one."
                : string.Empty;
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Dashboard.Messages", ex);
            RecentConversations = [];
            HasRecentConversations = false;
            MessagesSummary = "We couldn't load your messages right now.";
        }
    }

    /// <summary>
    /// Up to two letters for the avatar. Takes the first and last word so
    /// "Mpho Molefe" gives MM and a middle name doesn't displace the surname.
    /// </summary>
    protected static string InitialsFor(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return "?";

        var words = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return "?";
        if (words.Length == 1) return words[0][..1].ToUpperInvariant();

        return (words[0][..1] + words[^1][..1]).ToUpperInvariant();
    }

    /// <summary>
    /// Banner colours, read from Colors.xaml so recolouring a client school
    /// doesn't leave cards on the old palette. Falls back to a hardcoded set
    /// when there's no Application — which is the case in unit tests.
    ///
    /// Public rather than protected: TeacherClassesViewModel isn't a dashboard
    /// but shows the same class tiles, and a second copy of the palette is how
    /// the same class ends up two different colours on two screens.
    /// </summary>
    public static IReadOnlyList<Color> BannerPalette
    {
        get
        {
            var keys = new[] { "Banner1", "Banner2", "Banner3", "Banner4", "Banner5", "Banner6" };
            var resources = Application.Current?.Resources;
            if (resources is null) return FallbackPalette;

            var found = new List<Color>(keys.Length);
            foreach (var key in keys)
            {
                if (resources.TryGetValue(key, out var value) && value is Color colour)
                    found.Add(colour);
            }
            return found.Count > 0 ? found : FallbackPalette;
        }
    }

    private static readonly Color[] FallbackPalette =
    [
        Color.FromArgb("#0D5257"), Color.FromArgb("#178C8C"), Color.FromArgb("#4A6572"),
        Color.FromArgb("#7A5C3E"), Color.FromArgb("#2C5F8A"), Color.FromArgb("#6B7A3A")
    ];
}
