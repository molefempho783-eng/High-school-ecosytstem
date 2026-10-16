using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace HighSchoolEcosystem.Views;

public partial class MessagesPage : ContentPage
{
    private readonly MessagesViewModel _viewModel;
    private readonly IServiceProvider _services;

    public MessagesPage(MessagesViewModel viewModel, IServiceProvider services)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _services = services;
    }

    // Reloaded on every appearance, so coming back from a thread updates its
    // preview and clears its unread dot.
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    private async void OnConversationTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not ConversationRow row) return;
        await OpenAsync(row.Source.Id);
    }

    private async void OnContactTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not ContactRow row) return;
        var conversation = await _viewModel.StartAsync(row);
        if (conversation is not null) await OpenAsync(conversation.Id);
    }

    private async Task OpenAsync(string conversationId)
    {
        try
        {
            var page = _services.GetRequiredService<ConversationPage>();
            page.Load(conversationId);
            await Navigation.PushAsync(page);
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Navigate:Conversation", ex);
        }
    }
}
