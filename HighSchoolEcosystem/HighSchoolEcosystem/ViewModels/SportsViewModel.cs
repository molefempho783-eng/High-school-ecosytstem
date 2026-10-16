using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HighSchoolEcosystem.Models;
using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.Services.Interfaces;

namespace HighSchoolEcosystem.ViewModels;

/// <summary>
/// One sports post as a row.
///
/// <see cref="CanManage"/> is per-row rather than per-page: a coach may edit
/// anything, but a journalist may only edit their own posts, so two rows on the
/// same screen can legitimately differ.
/// </summary>
public sealed record SportsRow(SportsItem Source, bool CanManage)
{
    public string Id => Source.Id;
    public string Title => Source.Title;
    public string Body => Source.Body;
    public string MetaLine => Source.MetaLine;
    public bool HasBody => !string.IsNullOrWhiteSpace(Source.Body);

    /// <summary>"Fixture", "Result", "News" — shown as a chip on the row.</summary>
    public string KindLabel => Source.Kind.ToString();

    public bool IsUpcoming => Source.IsUpcoming;

    public string ImageUrl => Source.ImageUrl;
    public bool HasImage => Source.HasImage;
}

public partial class SportsViewModel : ObservableObject
{
    private readonly IContentService _content;

    private readonly IStorageService _storage;

    public SportsViewModel(IContentService content, IStorageService storage)
    {
        _content = content;
        _storage = storage;
    }

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isRefreshing;
    [ObservableProperty] private string statusMessage = string.Empty;

    [ObservableProperty] private ObservableCollection<SportsRow> items = [];
    [ObservableProperty] private string emptyMessage = string.Empty;

    /// <summary>Upcoming fixtures, split out so they can carry a count badge.</summary>
    [ObservableProperty] private ObservableCollection<SportsRow> upcoming = [];
    [ObservableProperty] private bool hasUpcoming;
    [ObservableProperty] private string upcomingCount = "0";

    /// <summary>Results and news — everything that isn't a future fixture.</summary>
    [ObservableProperty] private ObservableCollection<SportsRow> recent = [];
    [ObservableProperty] private bool hasRecent;
    [ObservableProperty] private string recentCount = "0";

    /// <summary>True for Coach, Admin and Journalist — shows the compose button.</summary>
    [ObservableProperty] private bool canPost;

    // ---- compose form ----

    [ObservableProperty] private bool isFormVisible;
    [ObservableProperty] private string newTitle = string.Empty;
    [ObservableProperty] private string newBody = string.Empty;
    [ObservableProperty] private string newSport = string.Empty;
    [ObservableProperty] private string newTeam = string.Empty;
    [ObservableProperty] private string newOpponent = string.Empty;
    [ObservableProperty] private string newVenue = string.Empty;
    [ObservableProperty] private string newScore = string.Empty;
    [ObservableProperty] private DateTime newEventDate = DateTime.Today;
    [ObservableProperty] private TimeSpan newEventTime = new(14, 0, 0);

    /// <summary>Bound to a Picker; index maps to <see cref="SportsItemKind"/>.</summary>
    public ObservableCollection<string> Kinds { get; } = ["Fixture", "Result", "News"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsMatchFields))]
    [NotifyPropertyChangedFor(nameof(ShowsScore))]
    private string newKind = "Fixture";

    /// <summary>Opponent, venue and date only make sense for a match.</summary>
    public bool ShowsMatchFields => NewKind != "News";

    /// <summary>A score is only meaningful once the match has been played.</summary>
    public bool ShowsScore => NewKind == "Result";

    // ---- picture ----

    /// <summary>
    /// The picked file, held until Save. Uploading on pick would leave orphaned
    /// objects in the bucket every time someone changes their mind, and for a
    /// NEW post there's no ID to build the path from until the document exists.
    /// </summary>
    private byte[]? _pickedImageBytes;
    private string _pickedImageName = string.Empty;
    private string _pickedImageContentType = string.Empty;

    /// <summary>
    /// The picture already on the post being edited. Held as plain fields as
    /// well as an observable one because the save path has to write them back —
    /// the update patches the image columns, so not carrying them forward would
    /// wipe the picture off a post every time someone fixed a typo.
    /// </summary>
    private string _existingImageStoragePath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExistingImage))]
    private string existingImageUrl = string.Empty;

    public bool HasExistingImage => !string.IsNullOrWhiteSpace(ExistingImageUrl);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPickedImage))]
    private string pickedImageLabel = string.Empty;

    public bool HasPickedImage => !string.IsNullOrWhiteSpace(PickedImageLabel);

    /// <summary>
    /// False when no Storage bucket is configured. Everything else about a post
    /// still works, so the picture option is hidden rather than the whole form
    /// being disabled.
    /// </summary>
    public bool CanAttachImage => _storage.IsConfigured;

    /// <summary>
    /// Called by the page once it has picked a file. The bytes are read there
    /// and handed over, because FilePicker is a platform UI API and the stream
    /// it returns doesn't survive the trip.
    /// </summary>
    public void SetPickedImage(byte[] bytes, string fileName, string contentType)
    {
        _pickedImageBytes = bytes;
        _pickedImageName = fileName;
        _pickedImageContentType = contentType;
        PickedImageLabel = $"{fileName} ({bytes.Length / 1024} KB)";
    }

    [RelayCommand]
    private void ClearPickedImage()
    {
        _pickedImageBytes = null;
        _pickedImageName = string.Empty;
        _pickedImageContentType = string.Empty;
        PickedImageLabel = string.Empty;
    }

    private string _editingId = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormHeading))]
    [NotifyPropertyChangedFor(nameof(SaveButtonText))]
    private bool isEditing;

    public string FormHeading => IsEditing ? "Edit post" : "New sports post";
    public string SaveButtonText => IsEditing ? "Save changes" : "Post";

    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            await _content.PrimeAsync();
            CanPost = _content.CanPostSports;

            var all = await _content.GetSportsAsync();

            // CanEditSportsItem is evaluated per item, because a journalist's
            // permission depends on who wrote it.
            var rows = all.Select(i => new SportsRow(i, _content.CanEditSportsItem(i))).ToList();

            Items = new ObservableCollection<SportsRow>(rows);

            var upcomingRows = rows.Where(r => r.IsUpcoming).ToList();
            var recentRows = rows.Where(r => !r.IsUpcoming).ToList();

            Upcoming = new ObservableCollection<SportsRow>(upcomingRows);
            Recent = new ObservableCollection<SportsRow>(recentRows);

            HasUpcoming = upcomingRows.Count > 0;
            HasRecent = recentRows.Count > 0;
            UpcomingCount = upcomingRows.Count.ToString();
            RecentCount = recentRows.Count.ToString();

            EmptyMessage = rows.Count == 0
                ? "No fixtures, results or sports news yet."
                : string.Empty;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "That took too long. Check your connection and try again.";
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Sports.Load", ex);
            StatusMessage = "We couldn't load the sports page. Pull down to try again.";
        }
        finally
        {
            IsBusy = false;
            IsRefreshing = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        // try/finally, not `=> LoadAsync()`: LoadAsync opens with
        // `if (IsBusy) return`, so pulling while a load is already running
        // would return without clearing IsRefreshing and hang the spinner.
        try { await LoadAsync(); }
        finally { IsRefreshing = false; }
    }

    [RelayCommand]
    private void StartCreate()
    {
        ResetForm();
        IsFormVisible = true;
    }

    [RelayCommand]
    private void StartEdit(SportsRow? row)
    {
        if (row is null || !row.CanManage) return;

        var source = row.Source;
        _editingId = source.Id;
        IsEditing = true;
        NewKind = source.Kind.ToString();
        NewTitle = source.Title;
        NewBody = source.Body;
        NewSport = source.Sport;
        NewTeam = source.Team;
        NewOpponent = source.Opponent;
        NewVenue = source.Venue;
        NewScore = source.Score;

        var local = source.EventDate.ToLocalTime();
        NewEventDate = local.Date;
        NewEventTime = local.TimeOfDay;

        // Carried so the form can show the current picture, and so saving
        // writes it back rather than blanking it.
        ExistingImageUrl = source.ImageUrl;
        _existingImageStoragePath = source.ImageStoragePath;
        ClearPickedImage();

        IsFormVisible = true;
    }

    [RelayCommand]
    private void CancelEdit() => ResetForm();

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy) return;

        if (string.IsNullOrWhiteSpace(NewTitle))
        {
            StatusMessage = "Give the post a title.";
            return;
        }

        // Combined as local time then converted, so a coach entering 14:00
        // means 14:00 where the school is, not UTC.
        var eventAt = new DateTimeOffset(NewEventDate.Date + NewEventTime, DateTimeOffset.Now.Offset);

        var item = new SportsItem
        {
            Kind = Enum.TryParse<SportsItemKind>(NewKind, out var kind) ? kind : SportsItemKind.News,
            Title = NewTitle,
            Body = NewBody,
            Sport = NewSport,
            Team = NewTeam,
            Opponent = NewOpponent,
            Venue = NewVenue,
            Score = NewScore,
            EventDate = eventAt.ToUniversalTime(),

            // Carried explicitly. UpdateSportsItemAsync patches ImageUrl and
            // ImageStoragePath, so leaving these empty would wipe the picture
            // off a post every time someone fixed a typo in its title.
            ImageUrl = ExistingImageUrl,
            ImageStoragePath = _existingImageStoragePath
        };

        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            string id;
            if (IsEditing)
            {
                await _content.UpdateSportsItemAsync(_editingId, item);
                id = _editingId;
            }
            else
            {
                id = await _content.CreateSportsItemAsync(item);
            }

            // Upload AFTER the document exists: the object path contains the
            // post's ID. A failed upload therefore leaves a post without a
            // picture, which is recoverable by editing — the other order would
            // leave an orphaned object in the bucket that nothing points at.
            await AttachPickedImageAsync(id);

            ResetForm();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            StatusMessage = IsEditing
                ? "You can only edit posts you wrote."
                : "You don't have permission to post sports updates.";
            return;
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Sports.Save", ex);
            StatusMessage = "We couldn't save that. Please try again.";
            return;
        }
        finally { IsBusy = false; }

        await LoadAsync();
    }

    /// <summary>
    /// Uploads the picked picture and attaches it to the post.
    ///
    /// Failure is reported but does NOT fail the save: the post itself is
    /// already written, and telling a journalist their article vanished because
    /// a photo didn't upload would be both wrong and alarming.
    /// </summary>
    private async Task AttachPickedImageAsync(string id)
    {
        if (_pickedImageBytes is null || _pickedImageBytes.Length == 0) return;
        if (!_storage.IsConfigured || string.IsNullOrWhiteSpace(id)) return;

        try
        {
            using var stream = new MemoryStream(_pickedImageBytes);

            var result = await _storage.UploadSportsImageAsync(
                id, _pickedImageName, stream, _pickedImageContentType);

            await _content.SetSportsImageAsync(id, result.DownloadUrl, result.StoragePath);

            // The old picture is only removed once the new one is safely
            // recorded — the other order risks a post with no image at all.
            if (!string.IsNullOrWhiteSpace(_existingImageStoragePath)
                && _existingImageStoragePath != result.StoragePath)
            {
                await _storage.DeleteAsync(_existingImageStoragePath);
            }
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            StatusMessage = "The post was saved, but the picture was refused. "
                          + "Check that storage.rules has been deployed.";
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Sports.UploadImage", ex);
            StatusMessage = "The post was saved, but the picture didn't upload. "
                          + "Edit the post to try again.";
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(SportsRow? row)
    {
        if (row is null || IsBusy || !row.CanManage) return;

        IsBusy = true;
        try
        {
            await _content.DeleteSportsItemAsync(row.Id);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            StatusMessage = "You can only delete posts you wrote.";
            return;
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Sports.Delete", ex);
            StatusMessage = "We couldn't delete that. Please try again.";
            return;
        }
        finally { IsBusy = false; }

        await LoadAsync();
    }

    private void ResetForm()
    {
        _editingId = string.Empty;
        IsEditing = false;
        IsFormVisible = false;
        NewKind = "Fixture";
        NewTitle = string.Empty;
        NewBody = string.Empty;
        NewSport = string.Empty;
        NewTeam = string.Empty;
        NewOpponent = string.Empty;
        NewVenue = string.Empty;
        NewScore = string.Empty;
        NewEventDate = DateTime.Today;
        NewEventTime = new TimeSpan(14, 0, 0);

        ExistingImageUrl = string.Empty;
        _existingImageStoragePath = string.Empty;
        ClearPickedImage();
    }
}
