using CommunityToolkit.Mvvm.ComponentModel;
using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.Services.Interfaces;

namespace HighSchoolEcosystem.ViewModels;

/// <summary>
/// The unread-messages circle on a class card. A separate observable object
/// rather than a property on the (immutable) card row, so the count can fill in
/// after the cards are already on screen — counting costs a read per class and
/// shouldn't hold the dashboard up.
/// </summary>
public sealed partial class ChatUnread : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnread))]
    [NotifyPropertyChangedFor(nameof(Label))]
    [NotifyPropertyChangedFor(nameof(Description))]
    private int count;

    /// <summary>Zero hides the circle — an empty badge reads as "something's wrong".</summary>
    public bool HasUnread => Count > 0;

    public string Label => Count > 99 ? "99+" : Count.ToString();

    /// <summary>For screen readers, which can't see a coloured circle.</summary>
    public string Description => Count == 1 ? "1 unread message" : $"{Count} unread messages";

    /// <summary>
    /// Fills in a set of badges in parallel. A class that fails to count just
    /// shows no circle — never an error on the dashboard.
    /// </summary>
    public static async Task LoadAllAsync(IClassChatService chat, IEnumerable<(string ClassId, ChatUnread Badge)> items)
    {
        await Task.WhenAll(items.Select(async item =>
        {
            try { item.Badge.Count = await chat.GetUnreadCountAsync(item.ClassId); }
            catch (Exception ex) { CrashLogger.Log($"ChatUnread:{item.ClassId}", ex); }
        }));
    }
}
