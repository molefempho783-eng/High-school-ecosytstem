using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HighSchoolEcosystem.Models;
using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.Services.Interfaces;

namespace HighSchoolEcosystem.ViewModels;

/// <summary>
/// One row in the contents tree down the left of the unit page.
///
/// A single flat collection carries three visual levels — unit, theme, item —
/// distinguished by <see cref="Depth"/>. A genuine tree would need nested
/// BindableLayouts, and a CollectionView nested inside a ScrollView collapses
/// on mobile; flattening sidesteps that entirely and makes the whole tree one
/// scrollable list.
/// </summary>
public sealed record TocRow(
    string Id, string Label, int Depth, TocRowKind Kind)
{
    /// <summary>"1/4 Completed", or empty for a leaf.</summary>
    public string ProgressLabel { get; init; } = string.Empty;

    public bool HasProgress => !string.IsNullOrWhiteSpace(ProgressLabel);

    /// <summary>The currently open unit, highlighted with a border as ARC does.</summary>
    public bool IsSelected { get; init; }

    /// <summary>True for a completed leaf, which gets a tick.</summary>
    public bool IsCompleted { get; init; }

    /// <summary>
    /// The tick itself. A property rather than a converter in the view, so the
    /// compiled binding can resolve it — MAUIG2045 caught this missing, and the
    /// binding was silently falling back to runtime reflection on every row of
    /// the contents tree.
    ///
    /// Duplicated from UnitCardRow deliberately: the two are different row types
    /// showing the same idea, and a shared base for one string would cost more
    /// than it saves. If you change the glyph, change both.
    /// </summary>
    public string CompletionGlyph => IsCompleted ? "\u2713" : string.Empty;

    /// <summary>Indent in device units. 14 per level keeps three levels legible on a phone.</summary>
    public double Indent => Depth * 14;

    public bool IsUnit => Kind == TocRowKind.Unit;
    public bool IsTheme => Kind == TocRowKind.Theme;
    public bool IsItem => Kind == TocRowKind.Item;

    /// <summary>Only leaves open something.</summary>
    public bool IsTappable => Kind is TocRowKind.Item or TocRowKind.Unit;
}

public enum TocRowKind
{
    Unit,
    Theme,
    Item
}

/// <summary>
/// A single learning unit: the contents tree on the left, the unit's own
/// content on the right.
///
/// The tree lists every unit in the course, not just this one — that's how ARC
/// works, and it means a student can move between units without going back to
/// the class page. Only the selected unit is expanded.
/// </summary>
public partial class StudentUnitViewModel : ObservableObject
{
    private readonly IClassService _classes;

    private string _classId = string.Empty;
    private List<LearningUnit> _units = [];
    private List<CourseMaterial> _materials = [];
    private ClassProgress _progress = new();

    public StudentUnitViewModel(IClassService classes) => _classes = classes;

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isRefreshing;
    [ObservableProperty] private string statusMessage = string.Empty;

    [ObservableProperty] private string className = string.Empty;

    /// <summary>Which unit is open. Empty string means the loose "Module information" group.</summary>
    [ObservableProperty] private string selectedUnitId = string.Empty;

    // ---- content pane ----
    [ObservableProperty] private string unitTitle = string.Empty;
    [ObservableProperty] private string unitDescription = string.Empty;
    [ObservableProperty] private bool hasUnitDescription;

    /// <summary>"No. of activities: 1"</summary>
    [ObservableProperty] private string activityCountLabel = string.Empty;
    [ObservableProperty] private bool hasActivityCount;

    /// <summary>"Time on task for activities: 1 hour and 15 minutes"</summary>
    [ObservableProperty] private string timeOnTaskLabel = string.Empty;
    [ObservableProperty] private bool hasTimeOnTask;

    /// <summary>Material in the open unit, for the content pane's own list.</summary>
    [ObservableProperty] private ObservableCollection<MaterialRow> unitMaterials = [];
    [ObservableProperty] private bool hasUnitMaterials;
    [ObservableProperty] private string unitEmptyMessage = string.Empty;

    // ---- contents tree ----
    [ObservableProperty] private ObservableCollection<TocRow> contents = [];

    /// <summary>Filters the tree. ARC puts a search box above it.</summary>
    [ObservableProperty]
    private string searchText = string.Empty;

    partial void OnSearchTextChanged(string value) => BuildContents();

    /// <summary>Collapses the tree on narrow screens, where it would crowd the content.</summary>
    [ObservableProperty] private bool isContentsVisible = true;

    // ---- chapter reader ----
    [ObservableProperty] private bool isReaderVisible;
    [ObservableProperty] private string readerTitle = string.Empty;
    [ObservableProperty] private string readerBody = string.Empty;
    [ObservableProperty] private bool isReaderComplete;

    private string _readerMaterialId = string.Empty;

    public void Initialise(string classId, string className, string unitId)
    {
        _classId = classId;
        ClassName = className;
        SelectedUnitId = unitId;
    }

    public async Task LoadAsync()
    {
        if (IsBusy || string.IsNullOrEmpty(_classId)) return;

        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            _units = (await _classes.GetUnitsAsync(_classId)).ToList();
            _materials = (await _classes.GetMaterialsAsync(_classId)).ToList();
            _progress = await _classes.GetMyProgressAsync(_classId);

            ApplySelection();
            BuildContents();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            StatusMessage = "You don't have access to this class. You may no longer be enrolled.";
        }
        catch (Exception ex)
        {
            CrashLogger.Log("StudentUnit.Load", ex);
            StatusMessage = "We couldn't load this unit. Pull down to try again.";
        }
        finally
        {
            IsBusy = false;
            IsRefreshing = false;
        }
    }

    /// <summary>Fills the content pane from whichever unit is selected.</summary>
    private void ApplySelection()
    {
        var unit = _units.FirstOrDefault(u => u.Id == SelectedUnitId);

        if (unit is null)
        {
            // No match means the loose group — either because SelectedUnitId is
            // empty, or because the unit was deleted while the page was open.
            UnitTitle = "Module information";
            UnitDescription = string.Empty;
            HasUnitDescription = false;
            ActivityCountLabel = string.Empty;
            HasActivityCount = false;
            TimeOnTaskLabel = string.Empty;
            HasTimeOnTask = false;
        }
        else
        {
            UnitTitle = unit.Title;
            UnitDescription = unit.Description;
            HasUnitDescription = unit.HasDescription;

            HasActivityCount = unit.ActivityCount > 0;
            ActivityCountLabel = HasActivityCount ? $"No. of activities: {unit.ActivityCount}" : string.Empty;

            var time = unit.TimeOnTaskLabel;
            HasTimeOnTask = !string.IsNullOrWhiteSpace(time);
            TimeOnTaskLabel = HasTimeOnTask ? $"Time on task for activities: {time}" : string.Empty;
        }

        var inUnit = MaterialsIn(SelectedUnitId);
        UnitMaterials = new ObservableCollection<MaterialRow>(
            inUnit.Select(m => new MaterialRow(m, _progress.IsComplete(m.Id))));

        HasUnitMaterials = inUnit.Count > 0;
        UnitEmptyMessage = HasUnitMaterials
            ? string.Empty
            : "There's nothing in this unit yet.";
    }

    private List<CourseMaterial> MaterialsIn(string unitId)
        => _materials
            .Where(m => string.Equals(
                string.IsNullOrWhiteSpace(m.UnitId) ? string.Empty : m.UnitId,
                unitId, StringComparison.Ordinal))
            .OrderBy(m => m.Order)
            .ThenBy(m => m.CreatedAt)
            .ToList();

    /// <summary>
    /// Rebuilds the flattened tree. Every unit appears; only the selected one
    /// is expanded into themes and items.
    /// </summary>
    private void BuildContents()
    {
        var rows = new List<TocRow>();
        var filter = SearchText?.Trim() ?? string.Empty;

        void AddGroup(string unitId, string title)
        {
            var items = MaterialsIn(unitId);
            var done = items.Count(m => _progress.IsComplete(m.Id));
            var isSelected = string.Equals(unitId, SelectedUnitId, StringComparison.Ordinal);

            // When searching, a unit is only shown if it or something inside it
            // matches — otherwise the tree stays full-length and the search
            // achieves nothing.
            var unitMatches = Matches(title, filter);
            var childMatches = items.Where(m => Matches(m.Title, filter) || Matches(m.Theme, filter)).ToList();

            if (filter.Length > 0 && !unitMatches && childMatches.Count == 0) return;

            rows.Add(new TocRow(unitId, title, 0, TocRowKind.Unit)
            {
                ProgressLabel = items.Count > 0 ? $"{done}/{items.Count} Completed" : string.Empty,
                IsSelected = isSelected
            });

            // Collapsed units contribute only their header, unless a search is
            // active — then matching children are worth surfacing.
            if (!isSelected && filter.Length == 0) return;

            var visible = filter.Length > 0 && !unitMatches ? childMatches : items;

            foreach (var themeGroup in visible.GroupBy(m => m.Theme ?? string.Empty))
            {
                if (!string.IsNullOrWhiteSpace(themeGroup.Key))
                {
                    var themeItems = themeGroup.ToList();
                    var themeDone = themeItems.Count(m => _progress.IsComplete(m.Id));

                    rows.Add(new TocRow($"{unitId}::{themeGroup.Key}", themeGroup.Key, 1, TocRowKind.Theme)
                    {
                        ProgressLabel = $"{themeDone}/{themeItems.Count} Completed"
                    });
                }

                foreach (var material in themeGroup)
                {
                    rows.Add(new TocRow(material.Id, material.Title,
                        string.IsNullOrWhiteSpace(themeGroup.Key) ? 1 : 2, TocRowKind.Item)
                    {
                        IsCompleted = _progress.IsComplete(material.Id)
                    });
                }
            }
        }

        if (_materials.Any(m => string.IsNullOrWhiteSpace(m.UnitId)))
            AddGroup(string.Empty, "Module information");

        foreach (var unit in _units)
            AddGroup(unit.Id, unit.Title);

        Contents = new ObservableCollection<TocRow>(rows);
    }

    private static bool Matches(string? value, string filter)
        => filter.Length == 0
           || (value is not null && value.Contains(filter, StringComparison.CurrentCultureIgnoreCase));

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync();

    [RelayCommand]
    private void ToggleContents() => IsContentsVisible = !IsContentsVisible;

    /// <summary>Switches the content pane to another unit without leaving the page.</summary>
    [RelayCommand]
    private void SelectUnit(string? unitId)
    {
        SelectedUnitId = unitId ?? string.Empty;
        ApplySelection();
        BuildContents();
    }

    [RelayCommand]
    private void OpenChapter(MaterialRow? row)
    {
        if (row is null || !row.IsChapter) return;

        _readerMaterialId = row.Id;
        ReaderTitle = row.Title;
        ReaderBody = string.IsNullOrWhiteSpace(row.Body) ? "This chapter is empty." : row.Body;
        IsReaderComplete = row.IsCompleted;
        IsReaderVisible = true;
    }

    [RelayCommand]
    private void CloseReader()
    {
        IsReaderVisible = false;
        _readerMaterialId = string.Empty;
        ReaderTitle = string.Empty;
        ReaderBody = string.Empty;
    }

    /// <summary>
    /// Marks the open chapter complete, which is what moves the progress bars.
    /// Optimistic: the local state updates first so the tick is immediate, and
    /// reverts if the write fails.
    /// </summary>
    [RelayCommand]
    private async Task ToggleCompleteAsync()
    {
        if (string.IsNullOrEmpty(_readerMaterialId)) return;

        var target = !IsReaderComplete;
        IsReaderComplete = target;

        var ids = _progress.CompletedMaterialIds.ToList();
        if (target)
        {
            if (!ids.Contains(_readerMaterialId, StringComparer.Ordinal)) ids.Add(_readerMaterialId);
        }
        else
        {
            ids.Remove(_readerMaterialId);
        }
        _progress = new ClassProgress { CompletedMaterialIds = ids };

        ApplySelection();
        BuildContents();

        try
        {
            await _classes.SetMaterialCompleteAsync(_classId, _readerMaterialId, target);
        }
        catch (Exception ex)
        {
            CrashLogger.Log("StudentUnit.ToggleComplete", ex);

            // Put it back: showing a tick that didn't save is worse than not
            // showing one, because the student stops tracking it themselves.
            IsReaderComplete = !target;
            _progress = await _classes.GetMyProgressAsync(_classId);
            ApplySelection();
            BuildContents();

            StatusMessage = "We couldn't save your progress. Check your connection.";
        }
    }
}
