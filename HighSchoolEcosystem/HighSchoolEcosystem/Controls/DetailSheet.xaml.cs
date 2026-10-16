using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.Views;

namespace HighSchoolEcosystem.Controls;

public partial class DetailSheet : ContentView
{
    // Completed exactly once, by whichever of the buttons or the scrim is hit
    // first. TrySetResult, not SetResult, because a fast double-tap really does
    // fire two handlers and the second would throw on an already-completed task.
    private readonly TaskCompletionSource<DetailAction> _result =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private bool _closing;

    public DetailSheet() => InitializeComponent();

    public Task<DetailAction> Result => _result.Task;

    /// <summary>
    /// Fills the sheet in. <paramref name="scope"/> is the chip — "Whole school",
    /// a class name, a team name — and is hidden when there's nothing to say.
    /// </summary>
    public void Fill(string title, string details, string scope, Color accent, bool canManage)
        => Fill(title, details, scope, accent, canEdit: canManage, canDelete: canManage);

    /// <summary>
    /// Edit and Delete shown separately. The Newspaper needs this: a teacher
    /// may take an article down but not rewrite a student's words.
    /// </summary>
    public void Fill(string title, string details, string scope, Color accent, bool canEdit, bool canDelete)
    {
        TitleLabel.Text = string.IsNullOrWhiteSpace(title) ? "Details" : title;
        BodyLabel.Text = details ?? string.Empty;

        ScopeLabel.Text = scope ?? string.Empty;
        ScopeChip.IsVisible = !string.IsNullOrWhiteSpace(scope);

        Accent.Color = accent;

        EditButton.IsVisible = canEdit;
        DeleteButton.IsVisible = canDelete;
    }

    /// <summary>
    /// Slides the card up and fades the scrim in.
    ///
    /// The card starts translated by its own height, so the animation works
    /// whatever the content's height turns out to be — a fixed offset would
    /// leave a short sheet starting halfway up the screen.
    /// </summary>
    public async Task AnimateInAsync()
    {
        Scrim.Opacity = 0;
        var height = Card.Height > 0 ? Card.Height : 320;
        Card.TranslationY = height;

        await Task.WhenAll(
            Scrim.FadeTo(0.45, 160, Easing.CubicOut),
            Card.TranslateTo(0, 0, 220, Easing.CubicOut));
    }

    private async Task CloseAsync(DetailAction action)
    {
        // Guarded: the scrim sits under the card but a tap near the edge can
        // race a button press, and animating out twice looks broken.
        if (_closing) return;
        _closing = true;

        var height = Card.Height > 0 ? Card.Height : 320;

        await Task.WhenAll(
            Scrim.FadeTo(0, 140, Easing.CubicIn),
            Card.TranslateTo(0, height, 180, Easing.CubicIn));

        _result.TrySetResult(action);
    }

    private async void OnScrimTapped(object? sender, TappedEventArgs e)
    {
        try { await CloseAsync(DetailAction.None); }
        catch (Exception ex) { CrashLogger.Log("DetailSheet.Scrim", ex); }
    }

    private async void OnCloseClicked(object? sender, EventArgs e)
    {
        try { await CloseAsync(DetailAction.None); }
        catch (Exception ex) { CrashLogger.Log("DetailSheet.Close", ex); }
    }

    private async void OnEditClicked(object? sender, EventArgs e)
    {
        try { await CloseAsync(DetailAction.Edit); }
        catch (Exception ex) { CrashLogger.Log("DetailSheet.Edit", ex); }
    }

    private async void OnDeleteClicked(object? sender, EventArgs e)
    {
        try { await CloseAsync(DetailAction.Delete); }
        catch (Exception ex) { CrashLogger.Log("DetailSheet.Delete", ex); }
    }
}
