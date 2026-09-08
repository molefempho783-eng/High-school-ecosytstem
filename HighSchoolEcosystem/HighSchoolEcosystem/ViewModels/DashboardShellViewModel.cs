using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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
    /// </summary>
    protected static IReadOnlyList<Color> BannerPalette
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
