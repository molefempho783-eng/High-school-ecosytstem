using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HighSchoolEcosystem.Models;
using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.Services.Interfaces;

namespace HighSchoolEcosystem.ViewModels;

/// <summary>One message as a chat bubble.</summary>
public sealed class ChatMessageRow
{
    public ChatMessageRow(ClassMessage source, bool isMine, bool canDelete)
    {
        Source = source;
        IsMine = isMine;
        CanDelete = canDelete;
    }

    public ClassMessage Source { get; }
    public string Id => Source.Id;
    public string Text => Source.Text;
    public bool HasText => !string.IsNullOrWhiteSpace(Source.Text);

    // ---- attachment ----

    public bool HasAttachment => Source.HasAttachment;
    public bool IsImage => Source.IsImage;
    public bool IsFile => Source.IsFile;
    public string AttachmentUrl => Source.AttachmentUrl;
    public string AttachmentName => string.IsNullOrWhiteSpace(Source.AttachmentName) ? "Attachment" : Source.AttachmentName;
    public string AttachmentSizeLabel => SizeLabel(Source.AttachmentSize);

    /// <summary>"Open picture" / "Open file" — what tapping the bubble offers.</summary>
    public string OpenLabel => IsImage ? "Open picture" : "Open file";

    internal static string SizeLabel(long bytes) => bytes switch
    {
        <= 0 => string.Empty,
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0} KB",
        _ => $"{bytes / (1024.0 * 1024.0):0.#} MB"
    };

    /// <summary>Your own messages sit on the right, everyone else's on the left.</summary>
    public bool IsMine { get; }
    public bool IsOthers => !IsMine;

    /// <summary>Your own, or anyone's if you're the teacher — mirrors the delete rule.</summary>
    public bool CanDelete { get; }

    public bool IsTeacher => Source.AuthorRole == "Teacher";

    /// <summary>"Naledi Khumalo · Teacher", or just the name for a student.</summary>
    public string AuthorLabel
    {
        get
        {
            var name = string.IsNullOrWhiteSpace(Source.AuthorName) ? "Someone" : Source.AuthorName;
            return IsTeacher ? $"{name} · Teacher" : name;
        }
    }

    /// <summary>"14:32" today, "Mon 14:32" this week, "3 Oct 14:32" before that.</summary>
    public string TimeLabel
    {
        get
        {
            var local = Source.SentAt.ToLocalTime();
            var today = DateTime.Today;
            if (local.Date == today) return local.ToString("HH:mm");
            if (local.Date > today.AddDays(-7)) return local.ToString("ddd HH:mm");
            return local.ToString("d MMM HH:mm");
        }
    }
}

/// <summary>
/// A class's group chat. Everyone in it sees every message; there is no
/// private messaging. New messages are fetched by polling while the page is
/// open (the app talks to Firestore over REST, which has no live listener), so
/// the page drives <see cref="PollAsync"/> on a timer.
/// </summary>
public partial class ClassChatViewModel : ObservableObject
{
    private const int HistoryCount = 60;

    private readonly IClassChatService _chat;
    private readonly IClassService _classes;
    private readonly IFirebaseAuthService _auth;
    private readonly IUserProfileService _profiles;
    private readonly IStorageService _storage;

    private string _classId = string.Empty;
    private bool _isTeacher;
    private bool _isAdmin;
    private bool _loaded;

    public ClassChatViewModel(IClassChatService chat, IClassService classes,
        IFirebaseAuthService auth, IUserProfileService profiles, IStorageService storage)
    {
        _chat = chat;
        _classes = classes;
        _auth = auth;
        _profiles = profiles;
        _storage = storage;
    }

    [ObservableProperty] private string className = string.Empty;
    [ObservableProperty] private string subtitle = string.Empty;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isRefreshing;
    [ObservableProperty] private string statusMessage = string.Empty;

    [ObservableProperty] private ObservableCollection<ChatMessageRow> messages = [];
    [ObservableProperty] private bool hasMessages;

    /// <summary>Students and the class teacher. Admins can read and moderate only.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAttach))]
    private bool canSend;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CharactersLeft))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string draft = string.Empty;

    // ---- attachment waiting to be sent ----

    /// <summary>
    /// The picked picture or file. Held until Send rather than uploaded on pick,
    /// so changing your mind never leaves an orphaned file in the bucket.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingAttachment))]
    [NotifyPropertyChangedFor(nameof(PendingLabel))]
    [NotifyPropertyChangedFor(nameof(PendingIsImage))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private ChatAttachment? pendingAttachment;

    public bool HasPendingAttachment => PendingAttachment is not null;
    public bool PendingIsImage => PendingAttachment?.IsImage == true;

    public string PendingLabel => PendingAttachment is { } a
        ? $"{a.FileName} ({ChatMessageRow.SizeLabel(a.Bytes.LongLength)})"
        : string.Empty;

    /// <summary>A preview for a picked picture, before it's sent.</summary>
    public ImageSource? PendingPreview => PendingAttachment is { IsImage: true } a
        ? ImageSource.FromStream(() => new MemoryStream(a.Bytes))
        : null;

    /// <summary>False when no Storage bucket is configured: text still works.</summary>
    public bool CanAttach => CanSend && _storage.IsConfigured;

    public void SetPendingAttachment(ChatAttachment attachment)
    {
        PendingAttachment = attachment;
        OnPropertyChanged(nameof(PendingPreview));
    }

    [RelayCommand]
    private void ClearAttachment()
    {
        PendingAttachment = null;
        OnPropertyChanged(nameof(PendingPreview));
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private bool isSending;

    public int MaxLength => IClassChatService.MaxLength;

    /// <summary>Shown only near the limit, so it isn't noise the rest of the time.</summary>
    public string CharactersLeft
    {
        get
        {
            var left = MaxLength - (Draft?.Length ?? 0);
            return left <= 100 ? $"{left} characters left" : string.Empty;
        }
    }

    /// <summary>Raised when messages are added, so the page can scroll to the newest.</summary>
    public event EventHandler? MessagesAdded;

    public void Initialise(string classId, string className)
    {
        _classId = classId;
        ClassName = className;
        _loaded = false;
    }

    public async Task LoadAsync()
    {
        if (IsBusy || string.IsNullOrEmpty(_classId)) return;
        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            var uid = _auth.CurrentSession?.UserId ?? string.Empty;
            var schoolClass = await _classes.GetClassAsync(_classId);
            var profile = await _profiles.GetOrCreateCurrentProfileAsync();

            _isTeacher = schoolClass?.TeacherId == uid && profile?.HasRole(Role.Teacher) == true;
            _isAdmin = profile?.HasRole(Role.Admin) == true;
            var isStudent = schoolClass?.StudentIds.Contains(uid) == true && profile?.HasRole(Role.Student) == true;

            CanSend = _isTeacher || isStudent;
            if (!CanSend && !_isAdmin)
            {
                StatusMessage = "This chat is for the students in this class and their teacher.";
                return;
            }

            var count = schoolClass?.StudentIds.Count ?? 0;
            Subtitle = $"{(count == 1 ? "1 student" : $"{count} students")} and your teacher · everyone sees every message";
            if (_isTeacher) Subtitle = $"{(count == 1 ? "1 student" : $"{count} students")} · you can remove any message";
            else if (!CanSend) Subtitle = "Read-only for admins · you can remove any message";

            var recent = await _chat.GetRecentAsync(_classId, HistoryCount);
            Messages = new ObservableCollection<ChatMessageRow>(recent.Select(ToRow));
            HasMessages = Messages.Count > 0;
            _loaded = true;
            MarkRead();
            MessagesAdded?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "That took too long. Check your connection and pull down to try again.";
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            StatusMessage = "You don't have access to this class's chat.";
            CanSend = false;
        }
        catch (Exception ex)
        {
            CrashLogger.Log("ClassChat.Load", ex);
            StatusMessage = "We couldn't load the chat. Pull down to try again.";
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
        // A full reload, which also drops messages someone deleted — polling
        // only ever adds.
        try { await LoadAsync(); }
        finally { IsRefreshing = false; }
    }

    /// <summary>
    /// Fetches anything new. Asks from two minutes before the newest message
    /// shown, not exactly after it: timestamps come from each sender's clock,
    /// so a slightly slow phone could otherwise post a message that sorts just
    /// before ours and never gets picked up. Duplicates are dropped by ID.
    /// </summary>
    public async Task PollAsync(CancellationToken cancellationToken)
    {
        if (!_loaded || IsBusy) return;

        var since = Messages.Count > 0
            ? Messages[^1].Source.SentAt.AddMinutes(-2)
            : DateTimeOffset.UtcNow.AddMinutes(-2);

        var newer = await _chat.GetSinceAsync(_classId, since, cancellationToken);
        AddNew(newer);
    }

    private void AddNew(IEnumerable<ClassMessage> incoming)
    {
        var known = Messages.Select(m => m.Id).ToHashSet();
        var added = false;
        foreach (var message in incoming.OrderBy(m => m.SentAt))
        {
            if (!known.Add(message.Id)) continue;

            // Keep time order even if a late-arriving message is slightly older.
            var row = ToRow(message);
            var index = Messages.Count;
            while (index > 0 && Messages[index - 1].Source.SentAt > message.SentAt) index--;
            Messages.Insert(index, row);
            added = true;
        }

        if (!added) return;
        HasMessages = true;
        // The page is open, so anything that just arrived has been seen.
        MarkRead();
        MessagesAdded?.Invoke(this, EventArgs.Empty);
    }

    private bool CanSendNow() => CanSend && !IsSending
        && (!string.IsNullOrWhiteSpace(Draft) || PendingAttachment is not null);

    [RelayCommand(CanExecute = nameof(CanSendNow))]
    private async Task SendAsync()
    {
        var text = Draft.Trim();
        var attachment = PendingAttachment;
        if (text.Length == 0 && attachment is null) return;

        IsSending = true;
        StatusMessage = string.Empty;
        try
        {
            var sent = await _chat.SendAsync(_classId, text, asTeacher: _isTeacher, attachment: attachment);
            Draft = string.Empty;
            ClearAttachment();
            AddNew([sent]);
        }
        catch (InvalidOperationException ex)
        {
            StatusMessage = ex.Message;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            StatusMessage = attachment is null
                ? "That message was refused. Only this class's students and teacher can post here."
                : "That was refused. Check the file is a picture, PDF, Office document or text file under 15 MB, "
                  + "and that storage.rules has been deployed.";
        }
        catch (Exception ex)
        {
            CrashLogger.Log("ClassChat.Send", ex);
            StatusMessage = "We couldn't send that. Check your connection and try again.";
        }
        finally
        {
            IsSending = false;
        }
    }

    /// <summary>The page confirms first; this just removes it.</summary>
    public async Task DeleteAsync(ChatMessageRow row)
    {
        if (!row.CanDelete) return;
        try
        {
            await _chat.DeleteAsync(_classId, row.Source);
            Messages.Remove(row);
            HasMessages = Messages.Count > 0;
        }
        catch (Exception ex)
        {
            CrashLogger.Log("ClassChat.Delete", ex);
            StatusMessage = "We couldn't remove that message. Please try again.";
        }
    }

    /// <summary>Clears this class's unread circle, up to now.</summary>
    public void MarkRead()
    {
        if (_loaded && !string.IsNullOrEmpty(_classId)) _chat.MarkRead(_classId);
    }

    private ChatMessageRow ToRow(ClassMessage message)
    {
        var mine = message.AuthorId == (_auth.CurrentSession?.UserId ?? string.Empty);
        return new ChatMessageRow(message, mine, canDelete: mine || _isTeacher || _isAdmin);
    }
}
