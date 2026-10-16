using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HighSchoolEcosystem.Models;
using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.Services.Interfaces;

namespace HighSchoolEcosystem.ViewModels;

/// <summary>
/// One Newspaper article as a tile.
///
/// Edit and delete are per-row and separate: an author may do both to their
/// own article, an admin both to any, and a teacher may only DELETE (take an
/// article down) — never rewrite a student's words under the student's name.
/// </summary>
public sealed record ArticleRow(Article Source, bool CanEdit, bool CanDelete)
{
    public string Id => Source.Id;
    public string Title => Source.Title;
    public string MetaLine => Source.MetaLine;
    public string Preview => Source.Preview;
    public bool HasPreview => !string.IsNullOrWhiteSpace(Source.Body);
    public string ImageUrl => Source.ImageUrl;
    public bool HasImage => Source.HasImage;
}

/// <summary>
/// The Newspaper page: every article, plus the form journalists write in.
///
/// Dashboards show the latest few articles and read them in place; this is
/// where writing happens, because an article needs a full-height editor and a
/// picture picker that don't fit in a widget. Same split as sport.
/// </summary>
public partial class NewspaperViewModel : ObservableObject
{
    private readonly IContentService _content;
    private readonly IStorageService _storage;

    public NewspaperViewModel(IContentService content, IStorageService storage)
    {
        _content = content;
        _storage = storage;
    }

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isRefreshing;
    [ObservableProperty] private string statusMessage = string.Empty;

    [ObservableProperty] private ObservableCollection<ArticleRow> articles = [];
    [ObservableProperty] private bool hasArticles;
    [ObservableProperty] private string articlesCount = "0";
    [ObservableProperty] private string emptyMessage = string.Empty;

    /// <summary>Journalist or Admin — shows the "Write an article" button.</summary>
    [ObservableProperty] private bool canWrite;

    // ---- compose form ----

    [ObservableProperty] private bool isFormVisible;
    [ObservableProperty] private string newTitle = string.Empty;
    [ObservableProperty] private string newBody = string.Empty;

    private string _editingId = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormHeading))]
    [NotifyPropertyChangedFor(nameof(SaveButtonText))]
    private bool isEditing;

    public string FormHeading => IsEditing ? "Edit article" : "New article";
    public string SaveButtonText => IsEditing ? "Save changes" : "Publish";

    /// <summary>
    /// Set by a dashboard before this page appears: open the form straight
    /// away, either blank or on a specific article. Applied once, after the
    /// first load, because editing needs the article and the permissions.
    /// </summary>
    private bool _composeOnLoad;
    private string _editOnLoadId = string.Empty;

    public void RequestCompose() => _composeOnLoad = true;
    public void RequestEdit(string articleId) => _editOnLoadId = articleId ?? string.Empty;

    // ---- picture ----

    /// <summary>
    /// The picked file, held until Publish. Uploading on pick would leave an
    /// orphaned object every time someone changed their mind, and a new
    /// article has no ID to build the path from until it's been written.
    /// </summary>
    private byte[]? _pickedImageBytes;
    private string _pickedImageName = string.Empty;
    private string _pickedImageContentType = string.Empty;

    private string _existingImageStoragePath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExistingImage))]
    private string existingImageUrl = string.Empty;

    public bool HasExistingImage => !string.IsNullOrWhiteSpace(ExistingImageUrl);

    /// <summary>The author tapped "Remove picture" on the article being edited.</summary>
    private bool _removeExistingImage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPickedImage))]
    private string pickedImageLabel = string.Empty;

    public bool HasPickedImage => !string.IsNullOrWhiteSpace(PickedImageLabel);

    /// <summary>
    /// False when no Storage bucket is configured. Articles still work, so the
    /// picture option is hidden rather than the whole form disabled.
    /// </summary>
    public bool CanAttachImage => _storage.IsConfigured;

    /// <summary>
    /// Called by the page once it has picked a file. The bytes are read there,
    /// because FilePicker is a platform UI API and its stream doesn't reliably
    /// survive being held until Publish.
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

    [RelayCommand]
    private void RemoveExistingImage()
    {
        _removeExistingImage = true;
        ExistingImageUrl = string.Empty;
    }

    // ---- load ----

    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            await _content.PrimeAsync();
            CanWrite = _content.CanWriteArticles;

            if (!_content.CanReadArticles)
            {
                Articles = [];
                HasArticles = false;
                ArticlesCount = "0";
                EmptyMessage = "The Newspaper is for members of the school.";
                return;
            }

            var all = await _content.GetArticlesAsync();

            // Evaluated per article: who wrote it decides what this user may do.
            var rows = all
                .Select(a => new ArticleRow(a, _content.CanEditArticle(a), _content.CanDeleteArticle(a)))
                .ToList();

            Articles = new ObservableCollection<ArticleRow>(rows);
            HasArticles = rows.Count > 0;
            ArticlesCount = rows.Count.ToString();
            EmptyMessage = HasArticles
                ? string.Empty
                : CanWrite
                    ? "No articles yet. Yours could be the first."
                    : "No articles yet. Check back soon.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "That took too long. Check your connection and try again.";
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Newspaper.Load", ex);
            StatusMessage = "We couldn't load the Newspaper. Pull down to try again.";
        }
        finally
        {
            IsBusy = false;
            IsRefreshing = false;
        }

        ApplyPendingRequest();
    }

    private void ApplyPendingRequest()
    {
        if (_composeOnLoad)
        {
            _composeOnLoad = false;
            if (CanWrite) StartCreate();
        }

        if (!string.IsNullOrEmpty(_editOnLoadId))
        {
            var row = Articles.FirstOrDefault(r => r.Id == _editOnLoadId);
            _editOnLoadId = string.Empty;
            if (row is not null) StartEdit(row);
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        // try/finally, not `=> LoadAsync()`: LoadAsync opens with
        // `if (IsBusy) return`, so pulling during a load would otherwise
        // leave the spinner hanging.
        try { await LoadAsync(); }
        finally { IsRefreshing = false; }
    }

    // ---- write ----

    [RelayCommand]
    private void StartCreate()
    {
        ResetForm();
        IsFormVisible = true;
    }

    public void StartEdit(ArticleRow? row)
    {
        if (row is null || !row.CanEdit) return;

        ResetForm();
        _editingId = row.Id;
        IsEditing = true;
        NewTitle = row.Source.Title;
        NewBody = row.Source.Body;

        ExistingImageUrl = row.Source.ImageUrl;
        _existingImageStoragePath = row.Source.ImageStoragePath;

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
            StatusMessage = "Give the article a headline.";
            return;
        }

        if (string.IsNullOrWhiteSpace(NewBody))
        {
            StatusMessage = "The article needs some words before it can go out.";
            return;
        }

        IsBusy = true;
        StatusMessage = string.Empty;
        var editing = IsEditing;
        try
        {
            string id;
            if (editing)
            {
                await _content.UpdateArticleAsync(_editingId, NewTitle, NewBody);
                id = _editingId;
            }
            else
            {
                id = await _content.CreateArticleAsync(NewTitle, NewBody);
            }

            // Picture AFTER the article exists: the object path contains its ID.
            // A failed upload leaves an article without a picture, fixable by
            // editing; the other order would orphan an object in the bucket.
            await ApplyPictureChangesAsync(id);

            ResetForm();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            StatusMessage = editing
                ? "You can only edit articles you wrote."
                : "Only journalists can write for the Newspaper.";
            return;
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Newspaper.Save", ex);
            StatusMessage = "We couldn't save that. Please try again.";
            return;
        }
        finally { IsBusy = false; }

        await LoadAsync();
    }

    /// <summary>
    /// Uploads a newly picked picture, or removes the old one.
    ///
    /// Failure is reported but does NOT fail the save — the article is already
    /// published, and telling a journalist it vanished because a photo didn't
    /// upload would be wrong and alarming.
    /// </summary>
    private async Task ApplyPictureChangesAsync(string id)
    {
        if (!_storage.IsConfigured || string.IsNullOrWhiteSpace(id)) return;

        var hasNew = _pickedImageBytes is { Length: > 0 };
        if (!hasNew && !_removeExistingImage) return;

        try
        {
            if (hasNew)
            {
                using var stream = new MemoryStream(_pickedImageBytes!);
                var result = await _storage.UploadArticleImageAsync(
                    id, _pickedImageName, stream, _pickedImageContentType);

                await _content.SetArticleImageAsync(id, result.DownloadUrl, result.StoragePath);

                // The old picture only goes once the new one is safely recorded.
                if (!string.IsNullOrWhiteSpace(_existingImageStoragePath)
                    && _existingImageStoragePath != result.StoragePath)
                {
                    await _storage.DeleteAsync(_existingImageStoragePath);
                }
            }
            else
            {
                // Removing: clear the pointer first, then the object. The other
                // order would briefly leave an article pointing at nothing.
                await _content.SetArticleImageAsync(id, string.Empty, string.Empty);
                if (!string.IsNullOrWhiteSpace(_existingImageStoragePath))
                    await _storage.DeleteAsync(_existingImageStoragePath);
            }
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            StatusMessage = "The article was saved, but the picture was refused. "
                          + "Check that storage.rules has been deployed.";
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Newspaper.Picture", ex);
            StatusMessage = "The article was saved, but the picture didn't upload. "
                          + "Edit the article to try again.";
        }
    }

    /// <summary>
    /// Deletes an article. The page confirms first — nothing here has undo, and
    /// an article disappears for the whole school at once.
    /// </summary>
    public async Task DeleteAsync(ArticleRow? row)
    {
        if (row is null || IsBusy || !row.CanDelete) return;

        IsBusy = true;
        try
        {
            await _content.DeleteArticleAsync(row.Id);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            StatusMessage = "You don't have permission to remove that article.";
            return;
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Newspaper.Delete", ex);
            StatusMessage = "We couldn't remove that. Please try again.";
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
        NewTitle = string.Empty;
        NewBody = string.Empty;

        ExistingImageUrl = string.Empty;
        _existingImageStoragePath = string.Empty;
        _removeExistingImage = false;
        ClearPickedImage();
    }
}
