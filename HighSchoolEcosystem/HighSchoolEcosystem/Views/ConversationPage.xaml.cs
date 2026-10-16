using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.ViewModels;

namespace HighSchoolEcosystem.Views;

public partial class ConversationPage : ContentPage
{
    /// <summary>Same cadence as the class chat; stops as soon as the page closes.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly ConversationViewModel _viewModel;
    private CancellationTokenSource? _polling;

    public ConversationPage(ConversationViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _viewModel.MessagesAdded += (_, _) => ScrollToNewest();
    }

    /// <summary>Called before pushing; DI can't supply the thread.</summary>
    public void Load(string conversationId) => _viewModel.Initialise(conversationId);

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
        _polling?.Cancel();
        _polling = new CancellationTokenSource();
        _ = PollLoopAsync(_polling.Token);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _polling?.Cancel();
        _polling = null;
        _viewModel.MarkRead();
    }

    private async Task PollLoopAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(PollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                try { await _viewModel.PollAsync(token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
                catch (Exception ex) { CrashLogger.Log("Conversation.Poll", ex); }
            }
        }
        catch (OperationCanceledException) { }
    }

    private void ScrollToNewest()
    {
        if (_viewModel.Messages.Count == 0) return;
        try { MessageList.ScrollTo(_viewModel.Messages.Count - 1, position: ScrollToPosition.End, animate: false); }
        catch (Exception ex) { CrashLogger.Log("Conversation.Scroll", ex); }
    }
}
