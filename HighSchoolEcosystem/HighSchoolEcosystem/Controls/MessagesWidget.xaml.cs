using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.ViewModels;
using HighSchoolEcosystem.Views;
using Microsoft.Extensions.DependencyInjection;

namespace HighSchoolEcosystem.Controls;

/// <summary>Dashboard Messages section. Opens the inbox, or a thread directly.</summary>
public partial class MessagesWidget : ContentView
{
    public MessagesWidget() => InitializeComponent();

    private IServiceProvider? Services => Handler?.MauiContext?.Services ?? IPlatformApplication.Current?.Services;

    private async void OnOpenClicked(object? sender, EventArgs e)
    {
        try
        {
            if (Services is { } services)
                await Navigation.PushAsync(services.GetRequiredService<MessagesPage>());
        }
        catch (Exception ex) { CrashLogger.Log("Navigate:Messages", ex); }
    }

    private async void OnConversationTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not ConversationRow row) return;
        try
        {
            if (Services is not { } services) return;
            var page = services.GetRequiredService<ConversationPage>();
            page.Load(row.Source.Id);
            await Navigation.PushAsync(page);
        }
        catch (Exception ex) { CrashLogger.Log("Navigate:Conversation", ex); }
    }
}
