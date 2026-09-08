using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HighSchoolEcosystem.Models;
using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.Services.Interfaces;

namespace HighSchoolEcosystem.ViewModels;

/// <summary>One material inside a unit, as the teacher edits it.</summary>
public sealed record EditableMaterialRow(CourseMaterial Source, int Position, int TotalInUnit)
{
    public string Id => Source.Id;
    public string Title => Source.Title;
    public string MetaLine => Source.MetaLine;
    public bool IsChapter => Source.IsChapter;
    public string UnitId => Source.UnitId;

    public string Glyph => Source.Kind switch
    {
        MaterialKind.Chapter => "\U0001F4D6",
        MaterialKind.Link => "\U0001F517",
        _ => "\U0001F5CE"
    };

    /// <summary>Hides the move controls at the ends of the unit's list.</summary>
    public bool CanMoveUp => Position > 0;
    public bool CanMoveDown => Position < TotalInUnit - 1;

    public string PositionLabel => $"{Position + 1}";

    /// <summary>Theme heading, shown once above the first item of each theme.</summary>
    public string ThemeHeading { get; init; } = string.Empty;
    public bool ShowsThemeHeading => !string.IsNullOrWhiteSpace(ThemeHeading);
}

/// <summary>
/// A unit and everything in it, as one collapsible block.
///
/// This mirrors exactly what a student sees on the class page, which is the
/// point: a teacher editing a flat list of sixty items has no idea what the
/// course looks like to the person taking it.
/// </summary>
public sealed partial class CourseGroupRow : ObservableObject
{
    /// <summary>Empty for the loose "Module information" group.</summary>
    public string UnitId { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    /// <summary>Null for the loose group, which isn't a real document.</summary>
    public LearningUnit? Unit { get; init; }

    public ObservableCollection<EditableMaterialRow> Items { get; init; } = [];

    /// <summary>Loose material can't be edited or deleted as a unit.</summary>
    public bool IsRealUnit => Unit is not null;

    [ObservableProperty] private bool isExpanded = true;

    public string MetaLine
    {
        get
        {
            var parts = new List<string>
            {
                Items.Count == 1 ? "1 item" : $"{Items.Count} items"
            };

            if (Unit is not null)
            {
                if (Unit.ActivityCount > 0) parts.Add(Unit.ActivityCountLabel);

                var time = Unit.TimeOnTaskLabel;
                if (!string.IsNullOrWhiteSpace(time)) parts.Add(time);
            }

            return string.Join(" · ", parts);
        }
    }

    public bool IsEmpty => Items.Count == 0;

    /// <summary>Chevron direction, so the header reads as a control.</summary>
    public string ExpandGlyph => IsExpanded ? "▾" : "▸";

    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(ExpandGlyph));

    public Color BannerColor { get; init; } = Colors.Transparent;
}

/// <summary>
/// The course builder for one class.
///
/// Structured as unit groups rather than a flat list so the teacher edits the
/// same tree the student reads. Material can be a chapter written here, an
/// external link, or an uploaded file; the form asks only for what the chosen
/// kind actually needs.
/// </summary>
public partial class TeacherMaterialsViewModel : ObservableObject
{
    private readonly IClassService _classes;
    private readonly IStorageService _storage;

    private string _classId = string.Empty;
    private string _editingId = string.Empty;
    private string _editingUnitId = string.Empty;

    /// <summary>Cached so reordering and grouping don't need another round trip.</summary>
    private List<LearningUnit> _units = [];
    private List<CourseMaterial> _materials = [];

    /// <summary>Set by the file picker, consumed on save.</summary>
    private Stream? _pendingUpload;
    private string _pendingFileName = string.Empty;
    private string _pendingContentType = string.Empty;

    public TeacherMaterialsViewModel(IClassService classes, IStorageService storage)
    {
        _classes = classes;
        _storage = storage;
    }

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isRefreshing;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private bool hasStatus;

    partial void OnStatusMessageChanged(string value) => HasStatus = !string.IsNullOrWhiteSpace(value);

    [ObservableProperty] private string className = string.Empty;

    /// <summary>Unit groups, each with its material nested.</summary>
    [ObservableProperty] private ObservableCollection<CourseGroupRow> groups = [];

    [ObservableProperty] private bool hasGroups;
    [ObservableProperty] private string emptyMessage = string.Empty;

    /// <summary>"3 units · 14 items" — the course at a glance.</summary>
    [ObservableProperty] private string courseSummary = string.Empty;

    /// <summary>False when no Storage bucket is configured — hides the upload option.</summary>
    [ObservableProperty] private bool canUploadFiles;

    // ==================== material form ====================

    [ObservableProperty] private bool isFormVisible;

    public ObservableCollection<string> Kinds { get; } = ["Chapter", "Link", "File"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsBody))]
    [NotifyPropertyChangedFor(nameof(ShowsUrl))]
    [NotifyPropertyChangedFor(nameof(ShowsFilePicker))]
    [NotifyPropertyChangedFor(nameof(KindHint))]
    private string newKind = "Chapter";

    public bool ShowsBody => NewKind == "Chapter";
    public bool ShowsUrl => NewKind == "Link";
    public bool ShowsFilePicker => NewKind == "File";

    /// <summary>Explains the chosen kind, so the teacher doesn't have to guess.</summary>
    public string KindHint => NewKind switch
    {
        "Chapter" => "Written here and read inside the app. No file needed, and students can mark it complete.",
        "Link" => "Points at Google Drive, OneDrive, YouTube, or any public page. Opens in their browser.",
        _ => "Uploaded to the school's storage. PDF, Word, PowerPoint or an image, up to 25 MB."
    };

    [ObservableProperty] private string newTitle = string.Empty;
    [ObservableProperty] private string newSummary = string.Empty;
    [ObservableProperty] private string newBody = string.Empty;
    [ObservableProperty] private string newUrl = string.Empty;
    [ObservableProperty] private string newTheme = string.Empty;

    /// <summary>
    /// Unit names for the picker, with a sentinel at index 0 for "no unit".
    /// MAUI's Picker has no null item, so the sentinel carries that meaning.
    /// </summary>
    public ObservableCollection<string> UnitChoices { get; } = [LooseUnitLabel];

    public const string LooseUnitLabel = "Module information (no unit)";

    [ObservableProperty] private string selectedUnitChoice = LooseUnitLabel;

    [ObservableProperty] private string pickedFileLabel = string.Empty;
    [ObservableProperty] private bool hasPickedFile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormHeading))]
    [NotifyPropertyChangedFor(nameof(SaveButtonText))]
    private bool isEditing;

    public string FormHeading => IsEditing ? "Edit material" : "Add material";
    public string SaveButtonText => IsEditing ? "Save changes" : "Add to course";

    // ==================== unit form ====================

    [ObservableProperty] private bool isUnitFormVisible;
    [ObservableProperty] private string newUnitTitle = string.Empty;
    [ObservableProperty] private string newUnitDescription = string.Empty;
    [ObservableProperty] private string newUnitActivities = string.Empty;
    [ObservableProperty] private string newUnitMinutes = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnitFormHeading))]
    [NotifyPropertyChangedFor(nameof(UnitSaveText))]
    private bool isEditingUnit;

    public string UnitFormHeading => IsEditingUnit ? "Edit unit" : "New learning unit";
    public string UnitSaveText => IsEditingUnit ? "Save changes" : "Create unit";

    /// <summary>Maps picker labels back to unit IDs. Rebuilt on every load.</summary>
    private readonly Dictionary<string, string> _unitIdByLabel = new(StringComparer.Ordinal);

    public void Initialise(string classId, string className)
    {
        _classId = classId;
        ClassName = className;
    }

    // ==================== load ====================

    public async Task LoadAsync()
    {
        if (IsBusy || string.IsNullOrEmpty(_classId)) return;

        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            CanUploadFiles = _storage.IsConfigured;

            // No bucket means uploads would fail server-side, so don't offer
            // the option at all rather than letting a teacher hit a wall.
            if (!CanUploadFiles && Kinds.Contains("File")) Kinds.Remove("File");

            _units = (await _classes.GetUnitsAsync(_classId)).ToList();
            _materials = (await _classes.GetMaterialsAsync(_classId)).ToList();

            RebuildUnitPicker();
            BuildGroups();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            StatusMessage = "You don't have permission to manage this class's content.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "That took too long. Check your connection and try again.";
        }
        catch (Exception ex)
        {
            CrashLogger.Log("TeacherMaterials.Load", ex);
            StatusMessage = "We couldn't load the course content. Pull down to try again.";
        }
        finally
        {
            IsBusy = false;
            IsRefreshing = false;
        }
    }

    private void RebuildUnitPicker()
    {
        // Preserve the teacher's current choice across a reload — losing it
        // mid-edit would silently move the material they're working on.
        var previous = SelectedUnitChoice;

        UnitChoices.Clear();
        UnitChoices.Add(LooseUnitLabel);
        _unitIdByLabel.Clear();

        foreach (var unit in _units)
        {
            UnitChoices.Add(unit.Title);
            _unitIdByLabel[unit.Title] = unit.Id;
        }

        SelectedUnitChoice = UnitChoices.Contains(previous) ? previous : LooseUnitLabel;
    }

    /// <summary>
    /// Builds one group per unit, plus a "Module information" group for loose
    /// material. The loose group leads, as it does for students — it's the
    /// module admin they read before anything else.
    /// </summary>
    private void BuildGroups()
    {
        // Remember which groups were collapsed so a save doesn't expand
        // everything the teacher had deliberately folded away.
        var collapsed = Groups
            .Where(g => !g.IsExpanded)
            .Select(g => g.UnitId)
            .ToHashSet(StringComparer.Ordinal);

        var palette = TeacherPalette;
        var built = new List<CourseGroupRow>();

        List<EditableMaterialRow> RowsFor(string unitId)
        {
            var inUnit = _materials
                .Where(m => (string.IsNullOrWhiteSpace(m.UnitId) ? string.Empty : m.UnitId) == unitId)
                .OrderBy(m => m.Order)
                .ThenBy(m => m.CreatedAt)
                .ToList();

            var rows = new List<EditableMaterialRow>();
            var lastTheme = string.Empty;

            for (var i = 0; i < inUnit.Count; i++)
            {
                var material = inUnit[i];
                var theme = material.Theme ?? string.Empty;

                // The heading prints once per run of a theme, so consecutive
                // items in "Theme 1" don't repeat the label five times.
                var heading = !string.Equals(theme, lastTheme, StringComparison.Ordinal) && theme.Length > 0
                    ? theme
                    : string.Empty;
                lastTheme = theme;

                rows.Add(new EditableMaterialRow(material, i, inUnit.Count) { ThemeHeading = heading });
            }

            return rows;
        }

        var loose = RowsFor(string.Empty);
        if (loose.Count > 0)
        {
            built.Add(new CourseGroupRow
            {
                UnitId = string.Empty,
                Title = "Module information",
                Items = new ObservableCollection<EditableMaterialRow>(loose),
                BannerColor = palette[0],
                IsExpanded = !collapsed.Contains(string.Empty)
            });
        }

        for (var i = 0; i < _units.Count; i++)
        {
            var unit = _units[i];
            built.Add(new CourseGroupRow
            {
                UnitId = unit.Id,
                Title = unit.Title,
                Unit = unit,
                Items = new ObservableCollection<EditableMaterialRow>(RowsFor(unit.Id)),
                // Offset so the first real unit doesn't share a colour with the
                // loose group above it.
                BannerColor = palette[(i + 1) % palette.Count],
                IsExpanded = !collapsed.Contains(unit.Id)
            });
        }

        Groups = new ObservableCollection<CourseGroupRow>(built);
        HasGroups = built.Count > 0;

        CourseSummary = _units.Count == 0 && _materials.Count == 0
            ? string.Empty
            : $"{Plural(_units.Count, "unit")} · {Plural(_materials.Count, "item")}";

        EmptyMessage = HasGroups
            ? string.Empty
            : "Nothing here yet. Start by creating a unit, then add chapters, "
            + "links or files to it.";
    }

    private static string Plural(int count, string noun)
        => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync();

    [RelayCommand]
    private static void ToggleGroup(CourseGroupRow? group)
    {
        if (group is null) return;
        group.IsExpanded = !group.IsExpanded;
    }

    // ==================== material commands ====================

    /// <summary>
    /// Opens the form with a unit pre-selected. Called from a group's own "Add"
    /// button, so the teacher doesn't have to re-pick the unit they're already
    /// looking at — the single biggest friction in the old flat list.
    /// </summary>
    [RelayCommand]
    private void AddToGroup(CourseGroupRow? group)
    {
        ResetForm();

        if (group is not null)
        {
            SelectedUnitChoice = _unitIdByLabel
                .FirstOrDefault(kv => kv.Value == group.UnitId).Key ?? LooseUnitLabel;
        }

        IsFormVisible = true;
    }

    [RelayCommand]
    private void StartCreate()
    {
        ResetForm();
        IsFormVisible = true;
    }

    [RelayCommand]
    private void StartEdit(EditableMaterialRow? row)
    {
        if (row is null) return;

        var source = row.Source;
        _editingId = source.Id;
        IsEditing = true;
        NewKind = source.Kind.ToString();
        NewTitle = source.Title;
        NewSummary = source.Summary;
        NewBody = source.Body;
        NewUrl = source.Url;
        NewTheme = source.Theme;

        SelectedUnitChoice = _unitIdByLabel
            .FirstOrDefault(kv => kv.Value == source.UnitId).Key ?? LooseUnitLabel;

        // A queued upload from a previous edit must not leak into this one.
        ClearPendingUpload();

        IsFormVisible = true;
    }

    [RelayCommand]
    private void CancelEdit() => ResetForm();

    /// <summary>
    /// Called by the view once the platform file picker returns. The stream is
    /// held until save, so cancelling the form uploads nothing.
    /// </summary>
    public void AttachFile(Stream content, string fileName, string contentType)
    {
        _pendingUpload?.Dispose();
        _pendingUpload = content;
        _pendingFileName = fileName;
        _pendingContentType = contentType;

        PickedFileLabel = fileName;
        HasPickedFile = true;

        // A file usually IS the title, so fill it in rather than making the
        // teacher retype it — but only if they haven't written one themselves.
        if (string.IsNullOrWhiteSpace(NewTitle))
            NewTitle = Path.GetFileNameWithoutExtension(fileName);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy) return;

        var kind = Enum.TryParse<MaterialKind>(NewKind, out var parsed) ? parsed : MaterialKind.Chapter;

        // Validation up front, with messages that say what to do rather than
        // what went wrong.
        if (string.IsNullOrWhiteSpace(NewTitle))
        {
            StatusMessage = "Give the material a title — students see this in the contents list.";
            return;
        }

        if (kind == MaterialKind.Link && string.IsNullOrWhiteSpace(NewUrl))
        {
            StatusMessage = "Paste the link you want to share.";
            return;
        }

        if (kind == MaterialKind.Chapter && string.IsNullOrWhiteSpace(NewBody))
        {
            StatusMessage = "Write the chapter text, or switch the kind to Link or File.";
            return;
        }

        if (kind == MaterialKind.File && _pendingUpload is null && !IsEditing)
        {
            StatusMessage = "Choose a file to upload.";
            return;
        }

        IsBusy = true;
        StatusMessage = string.Empty;

        var url = NewUrl;
        var storagePath = string.Empty;
        var fileName = string.Empty;
        var fileSize = 0L;

        var targetUnitId = _unitIdByLabel.TryGetValue(SelectedUnitChoice ?? string.Empty, out var unitId)
            ? unitId
            : string.Empty;

        try
        {
            if (kind == MaterialKind.File && _pendingUpload is not null)
            {
                var result = await _storage.UploadClassMaterialAsync(
                    _classId, _pendingFileName, _pendingUpload, _pendingContentType);

                url = result.DownloadUrl;
                storagePath = result.StoragePath;
                fileName = _pendingFileName;
                fileSize = result.SizeBytes;
            }

            // New material goes to the end of ITS OWN unit, not the end of the
            // whole course — otherwise adding to unit 1 would sort it after
            // everything in unit 9.
            var order = IsEditing
                ? _materials.FirstOrDefault(m => m.Id == _editingId)?.Order ?? 0
                : _materials.Count(m => (m.UnitId ?? string.Empty) == targetUnitId);

            await _classes.SaveMaterialAsync(_classId, new CourseMaterial
            {
                Id = IsEditing ? _editingId : string.Empty,
                Kind = kind,
                Title = NewTitle,
                Summary = NewSummary,
                Body = kind == MaterialKind.Chapter ? NewBody : string.Empty,
                Url = kind == MaterialKind.Chapter ? string.Empty : url,
                StoragePath = storagePath,
                FileName = fileName,
                FileSizeBytes = fileSize,
                UnitId = targetUnitId,
                Theme = NewTheme?.Trim() ?? string.Empty,
                Order = order
            });

            ResetForm();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            StatusMessage = "Only this class's teacher can add material to it.";
            return;
        }
        catch (InvalidOperationException ex)
        {
            // Storage not configured, or the file was rejected. The message is
            // written for a teacher, so show it verbatim.
            StatusMessage = ex.Message;
            return;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "That took too long. Check your connection and try again.";
            return;
        }
        catch (Exception ex)
        {
            CrashLogger.Log("TeacherMaterials.Save", ex);
            StatusMessage = "We couldn't save that. Please try again.";
            return;
        }
        finally { IsBusy = false; }

        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteAsync(EditableMaterialRow? row)
    {
        if (row is null || IsBusy) return;

        IsBusy = true;
        try
        {
            await _classes.DeleteMaterialAsync(_classId, row.Source);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            StatusMessage = "Only this class's teacher can remove its material.";
            return;
        }
        catch (Exception ex)
        {
            CrashLogger.Log("TeacherMaterials.Delete", ex);
            StatusMessage = "We couldn't delete that. Please try again.";
            return;
        }
        finally { IsBusy = false; }

        await LoadAsync();
    }

    [RelayCommand]
    private Task MoveUpAsync(EditableMaterialRow? row) => MoveAsync(row, up: true);

    [RelayCommand]
    private Task MoveDownAsync(EditableMaterialRow? row) => MoveAsync(row, up: false);

    private async Task MoveAsync(EditableMaterialRow? row, bool up)
    {
        if (row is null || IsBusy) return;

        IsBusy = true;
        try
        {
            await _classes.MoveMaterialAsync(_classId, row.Source, up);
        }
        catch (Exception ex)
        {
            CrashLogger.Log("TeacherMaterials.Move", ex);
            StatusMessage = "We couldn't reorder that. Please try again.";
            return;
        }
        finally { IsBusy = false; }

        await LoadAsync();
    }

    // ==================== unit commands ====================

    [RelayCommand]
    private void StartCreateUnit()
    {
        ResetUnitForm();
        IsUnitFormVisible = true;
    }

    [RelayCommand]
    private void StartEditUnit(CourseGroupRow? group)
    {
        if (group?.Unit is null) return;

        _editingUnitId = group.UnitId;
        IsEditingUnit = true;
        NewUnitTitle = group.Unit.Title;
        NewUnitDescription = group.Unit.Description;
        NewUnitActivities = group.Unit.ActivityCount > 0 ? group.Unit.ActivityCount.ToString() : string.Empty;
        NewUnitMinutes = group.Unit.TimeOnTaskMinutes > 0 ? group.Unit.TimeOnTaskMinutes.ToString() : string.Empty;
        IsUnitFormVisible = true;
    }

    [RelayCommand]
    private void CancelUnitEdit() => ResetUnitForm();

    [RelayCommand]
    private async Task SaveUnitAsync()
    {
        if (IsBusy) return;

        if (string.IsNullOrWhiteSpace(NewUnitTitle))
        {
            StatusMessage = "Give the unit a title, for example \"LU 1: Introduction\".";
            return;
        }

        // Blank stays 0 rather than failing: both are optional, and refusing to
        // save a unit because the activity count is empty would be petty.
        _ = int.TryParse(NewUnitActivities, out var activities);
        _ = int.TryParse(NewUnitMinutes, out var minutes);

        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            await _classes.SaveUnitAsync(_classId, new LearningUnit
            {
                Id = IsEditingUnit ? _editingUnitId : string.Empty,
                Title = NewUnitTitle,
                Description = NewUnitDescription,
                ActivityCount = activities,
                TimeOnTaskMinutes = minutes,
                Order = IsEditingUnit
                    ? _units.FirstOrDefault(u => u.Id == _editingUnitId)?.Order ?? _units.Count
                    : _units.Count
            });

            ResetUnitForm();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            StatusMessage = "Only this class's teacher can manage its units.";
            return;
        }
        catch (Exception ex)
        {
            CrashLogger.Log("TeacherMaterials.SaveUnit", ex);
            StatusMessage = "We couldn't save that unit. Please try again.";
            return;
        }
        finally { IsBusy = false; }

        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteUnitAsync(CourseGroupRow? group)
    {
        if (group?.Unit is null || IsBusy) return;

        IsBusy = true;
        try
        {
            // The service detaches this unit's material rather than deleting
            // it, so nothing is lost — it reappears under "Module information".
            await _classes.DeleteUnitAsync(_classId, group.UnitId);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            StatusMessage = "Only this class's teacher can remove its units.";
            return;
        }
        catch (Exception ex)
        {
            CrashLogger.Log("TeacherMaterials.DeleteUnit", ex);
            StatusMessage = "We couldn't delete that unit. Please try again.";
            return;
        }
        finally { IsBusy = false; }

        StatusMessage = group.Items.Count > 0
            ? $"Unit removed. Its {Plural(group.Items.Count, "item")} moved to Module information."
            : string.Empty;

        await LoadAsync();
    }

    // ==================== helpers ====================

    private void ClearPendingUpload()
    {
        _pendingUpload?.Dispose();
        _pendingUpload = null;
        _pendingFileName = string.Empty;
        _pendingContentType = string.Empty;
        PickedFileLabel = string.Empty;
        HasPickedFile = false;
    }

    private void ResetForm()
    {
        _editingId = string.Empty;
        IsEditing = false;
        IsFormVisible = false;
        NewKind = "Chapter";
        NewTitle = string.Empty;
        NewSummary = string.Empty;
        NewBody = string.Empty;
        NewUrl = string.Empty;
        NewTheme = string.Empty;
        SelectedUnitChoice = LooseUnitLabel;
        ClearPendingUpload();
    }

    private void ResetUnitForm()
    {
        _editingUnitId = string.Empty;
        IsEditingUnit = false;
        IsUnitFormVisible = false;
        NewUnitTitle = string.Empty;
        NewUnitDescription = string.Empty;
        NewUnitActivities = string.Empty;
        NewUnitMinutes = string.Empty;
    }

    /// <summary>Banner colours, read from Colors.xaml so a recolour follows through.</summary>
    private static IReadOnlyList<Color> TeacherPalette
    {
        get
        {
            var keys = new[] { "Banner1", "Banner2", "Banner3", "Banner4", "Banner5", "Banner6" };
            var resources = Application.Current?.Resources;
            if (resources is null) return Fallback;

            var found = new List<Color>(keys.Length);
            foreach (var key in keys)
            {
                if (resources.TryGetValue(key, out var value) && value is Color colour)
                    found.Add(colour);
            }
            return found.Count > 0 ? found : Fallback;
        }
    }

    private static readonly Color[] Fallback =
    [
        Color.FromArgb("#0D5257"), Color.FromArgb("#178C8C"), Color.FromArgb("#4A6572"),
        Color.FromArgb("#7A5C3E"), Color.FromArgb("#2C5F8A"), Color.FromArgb("#6B7A3A")
    ];
}
