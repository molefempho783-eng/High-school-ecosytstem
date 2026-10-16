using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HighSchoolEcosystem.Models;
using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.Services.Interfaces;

namespace HighSchoolEcosystem.ViewModels;

/// <summary>One message as a bubble in a teacher-parent thread.</summary>
public sealed class DirectMessageRow
{
    public DirectMessageRow(DirectMessage source, bool isMine)
    {
        Source = source;
        IsMine = isMine;
    }

    public DirectMessage Source { get; }
    public string Id => Source.Id;
    public string Text => Source.Text;
    public bool IsMine { get; }

    public string TimeLabel
    {
        get
        {
            var local = Source.SentAt.ToLocalTime();
            if (local.Date == DateTime.Today) return local.ToString("HH:mm");
            if (local.Date > DateTime.Today.AddDays(-7)) return local.ToString("ddd HH:mm");
            return local.ToString("d MMM HH:mm");
        }
    }
}

/// <summary>
/// A private teacher-parent thread. Polled while open, like the class chat —
/// the app reaches Firestore over REST, which has no live listener.
/// </summary>
public partial class ConversationViewModel : ObservableObject
{
    private const int HistoryCount = 80;

    private readonly IConversationService _conversations;
    private string _conversationId = string.Empty;
    private Conversation? _conversation;
    private bool _loaded;

    public ConversationViewModel(IConversationService conversations) => _conversations = conversations;

    [ObservableProperty] private string title = "Messages";
    [ObservableProperty] private string subtitle = string.Empty;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isRefreshing;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private ObservableCollection<DirectMessageRow> messages = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string draft = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private bool isSending;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private bool canSend;

    public int MaxLength => IConversationService.MaxLength;

    public event EventHandler? MessagesAdded;

    public void Initialise(string conversationId)
    {
        _conversationId = conversationId;
        _conversation = null;
        _loaded = false;
    }

    public async Task LoadAsync()
    {
        if (IsBusy || string.IsNullOrEmpty(_conversationId)) return;
        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            _conversation = await _conversations.GetAsync(_conversationId);
            if (_conversation is null)
            {
                StatusMessage = "This conversation no longer exists.";
                CanSend = false;
                return;
            }

            var me = _conversations.CurrentUserId;
            var iAmTeacher = _conversation.TeacherId == me;
            Title = iAmTeacher ? _conversation.ParentName : _conversation.TeacherName;
            Subtitle = iAmTeacher
                ? $"Parent of {_conversation.StudentName} · {_conversation.ClassName}"
                : $"{_conversation.StudentName}'s {_conversation.ClassName} teacher";
            // Admins can read threads (moderation) but aren't a party to them.
            CanSend = _conversation.ParticipantIds.Contains(me);

            var recent = await _conversations.GetRecentMessagesAsync(_conversationId, HistoryCount);
            Messages = new ObservableCollection<DirectMessageRow>(recent.Select(m => new DirectMessageRow(m, m.AuthorId == me)));
            _loaded = true;
            _conversations.MarkRead(_conversationId);
            MessagesAdded?.Invoke(this, EventArgs.Empty);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            StatusMessage = "You don't have access to this conversation.";
            CanSend = false;
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Conversation.Load", ex);
            StatusMessage = "We couldn't load this conversation. Pull down to try again.";
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
        try { await LoadAsync(); }
        finally { IsRefreshing = false; }
    }

    /// <summary>
    /// New messages since a little before the newest shown; senders' clocks
    /// differ, so a slightly older timestamp mustn't be missed. Deduped by ID.
    /// </summary>
    public async Task PollAsync(CancellationToken cancellationToken)
    {
        if (!_loaded || IsBusy) return;
        var since = Messages.Count > 0 ? Messages[^1].Source.SentAt.AddMinutes(-2) : DateTimeOffset.UtcNow.AddMinutes(-2);
        var newer = await _conversations.GetMessagesSinceAsync(_conversationId, since, cancellationToken);
        AddNew(newer);
    }

    private void AddNew(IEnumerable<DirectMessage> incoming)
    {
        var me = _conversations.CurrentUserId;
        var known = Messages.Select(m => m.Id).ToHashSet();
        var added = false;
        foreach (var message in incoming.OrderBy(m => m.SentAt))
        {
            if (!known.Add(message.Id)) continue;
            var index = Messages.Count;
            while (index > 0 && Messages[index - 1].Source.SentAt > message.SentAt) index--;
            Messages.Insert(index, new DirectMessageRow(message, message.AuthorId == me));
            added = true;
        }
        if (!added) return;
        _conversations.MarkRead(_conversationId);
        MessagesAdded?.Invoke(this, EventArgs.Empty);
    }

    public void MarkRead()
    {
        if (_loaded) _conversations.MarkRead(_conversationId);
    }

    private bool CanSendNow() => CanSend && !IsSending && !string.IsNullOrWhiteSpace(Draft);

    [RelayCommand(CanExecute = nameof(CanSendNow))]
    private async Task SendAsync()
    {
        if (_conversation is null) return;
        IsSending = true;
        StatusMessage = string.Empty;
        try
        {
            var sent = await _conversations.SendAsync(_conversation, Draft);
            Draft = string.Empty;
            AddNew([sent]);
        }
        catch (InvalidOperationException ex)
        {
            StatusMessage = ex.Message;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            StatusMessage = "That message was refused. Only the teacher and parent in this conversation can write here.";
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Conversation.Send", ex);
            StatusMessage = "We couldn't send that. Check your connection and try again.";
        }
        finally
        {
            IsSending = false;
        }
    }
}
