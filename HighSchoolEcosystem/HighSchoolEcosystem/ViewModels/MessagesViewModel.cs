using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HighSchoolEcosystem.Models;
using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.Services.Interfaces;

namespace HighSchoolEcosystem.ViewModels;

/// <summary>One thread in the inbox, from the signed-in user's point of view.</summary>
public sealed class ConversationRow
{
    public ConversationRow(Conversation source, string myId, bool isUnread)
    {
        Source = source;
        IsUnread = isUnread;
        IAmTeacher = source.TeacherId == myId;
        LastWasMine = source.LastSenderId == myId;
    }

    public Conversation Source { get; }
    public bool IsUnread { get; }
    private bool IAmTeacher { get; }
    private bool LastWasMine { get; }

    /// <summary>The other person: the parent if I'm the teacher, and vice versa.</summary>
    public string OtherName => IAmTeacher
        ? (string.IsNullOrWhiteSpace(Source.ParentName) ? "Parent" : Source.ParentName)
        : (string.IsNullOrWhiteSpace(Source.TeacherName) ? "Teacher" : Source.TeacherName);

    /// <summary>"Parent of Amara · Mathematics" / "Amara's Mathematics teacher".</summary>
    public string About => IAmTeacher
        ? $"Parent of {Source.StudentName} · {Source.ClassName}"
        : $"{Source.StudentName}'s {Source.ClassName} teacher";

    public string Preview => string.IsNullOrWhiteSpace(Source.LastMessagePreview)
        ? "No messages yet"
        : LastWasMine ? $"You: {Source.LastMessagePreview}" : Source.LastMessagePreview;

    public string TimeLabel
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Source.LastMessagePreview)) return string.Empty;
            var local = Source.LastMessageAt.ToLocalTime();
            if (local.Date == DateTime.Today) return local.ToString("HH:mm");
            if (local.Date > DateTime.Today.AddDays(-7)) return local.ToString("ddd");
            return local.ToString("d MMM");
        }
    }
}

/// <summary>Someone the user hasn't messaged yet but may.</summary>
public sealed class ContactRow
{
    public ContactRow(ConversationContact contact, string myId)
    {
        Contact = contact;
        IAmTeacher = contact.TeacherId == myId;
    }

    public ConversationContact Contact { get; }
    private bool IAmTeacher { get; }

    public string Name => IAmTeacher ? Contact.ParentName : Contact.TeacherName;

    public string About => IAmTeacher
        ? $"Parent of {Contact.StudentName} · {Contact.ClassName}"
        : $"{Contact.StudentName}'s {Contact.ClassName} teacher";
}

/// <summary>
/// The inbox: existing threads, newest first, and the people the user can
/// start one with. Parents see their children's teachers; teachers see the
/// parents of their students.
/// </summary>
public partial class MessagesViewModel : ObservableObject
{
    private readonly IConversationService _conversations;

    public MessagesViewModel(IConversationService conversations) => _conversations = conversations;

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isRefreshing;
    [ObservableProperty] private string statusMessage = string.Empty;

    [ObservableProperty] private ObservableCollection<ConversationRow> conversations = [];
    [ObservableProperty] private bool hasConversations;

    [ObservableProperty] private ObservableCollection<ContactRow> contacts = [];
    [ObservableProperty] private bool hasContacts;
    [ObservableProperty] private string contactsEmptyMessage = string.Empty;

    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            var me = _conversations.CurrentUserId;

            var threads = await _conversations.GetMyConversationsAsync();
            Conversations = new ObservableCollection<ConversationRow>(
                threads.Select(c => new ConversationRow(c, me, _conversations.IsUnread(c))));
            HasConversations = Conversations.Count > 0;

            // People to start a thread with — minus anyone already talked to
            // about that child.
            var started = threads.Select(t => t.Id).ToHashSet();
            var all = await _conversations.GetContactsAsync();
            Contacts = new ObservableCollection<ContactRow>(
                all.Where(c => !started.Contains(c.ConversationId)).Select(c => new ContactRow(c, me)));
            HasContacts = Contacts.Count > 0;

            ContactsEmptyMessage = all.Count == 0
                ? "Nobody to message yet. Parents can message the teachers of their linked children's classes, "
                  + "and teachers the parents of their students. Ask an admin to link parents to their children."
                : string.Empty;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "That took too long. Pull down to try again.";
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Messages.Load", ex);
            StatusMessage = "We couldn't load your messages. Pull down to try again.";
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

    /// <summary>Gets (or creates) the thread for a contact. The page then opens it.</summary>
    public async Task<Conversation?> StartAsync(ContactRow row)
    {
        try
        {
            return await _conversations.OpenAsync(row.Contact);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            StatusMessage = "That conversation isn't allowed any more — the student may have moved class, "
                          + "or the parent link may have changed.";
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Messages.Start", ex);
            StatusMessage = "We couldn't start that conversation. Please try again.";
        }
        return null;
    }
}
