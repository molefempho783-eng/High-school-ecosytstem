using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.ViewModels;
using HighSchoolEcosystem.Views;
using Microsoft.Extensions.DependencyInjection;

namespace HighSchoolEcosystem.Controls;

/// <summary>
/// The dashboard Newspaper section. Handles its own taps, so a dashboard only
/// has to place it and call LoadNewspaperAsync — no per-page handlers to keep
/// in step across five code-behinds.
/// </summary>
public partial class NewspaperWidget : ContentView
{
    public NewspaperWidget() => InitializeComponent();

    // Not named "Shell" - that would shadow Microsoft.Maui.Controls.Shell.
    private DashboardShellViewModel? Dashboard => BindingContext as DashboardShellViewModel;

    private async void OnOpenNewspaperTapped(object? sender, TappedEventArgs e)
        => await OpenNewspaperAsync(page => { });

    private async void OnWriteClicked(object? sender, EventArgs e)
        => await OpenNewspaperAsync(page => page.StartComposing());

    /// <summary>
    /// Reads the article in the shared detail sheet. Edit opens the Newspaper
    /// page on that article; Delete (a take-down, for teachers and admins, or
    /// the author removing their own) happens in place after a confirmation.
    /// </summary>
    private async void OnArticleTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not ArticleRow row) return;
        if (FindPage() is not { } page) return;

        try
        {
            var action = await DetailPopup.ShowAsync(
                page, row.Title, row.Source.DetailText,
                "Newspaper", NewspaperPage.NewspaperAccent, row.CanEdit, row.CanDelete);

            switch (action)
            {
                case DetailAction.Edit:
                    await OpenNewspaperAsync(p => p.StartEditing(row.Id));
                    break;

                case DetailAction.Delete:
                    if (!await NewspaperPage.ConfirmDeleteAsync(page, row.Title)) break;
                    if (Dashboard is { } dashboard && !await dashboard.DeleteArticleAsync(row))
                    {
                        await page.DisplayAlertAsync(
                            "Couldn't remove the article",
                            "Please check your connection and try again.", "OK");
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            CrashLogger.Log("NewspaperWidget.ArticleDetail", ex);
        }
    }

    private async Task OpenNewspaperAsync(Action<NewspaperPage> prepare)
    {
        try
        {
            var services = Handler?.MauiContext?.Services ?? IPlatformApplication.Current?.Services;
            if (services is null) return;

            var page = services.GetRequiredService<NewspaperPage>();
            prepare(page);
            await Navigation.PushAsync(page);
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Navigate:NewspaperPage", ex);
        }
    }

    // The detail sheet is injected into a page's root Grid, so it needs the
    // page itself rather than this control.
    private Page? FindPage()
    {
        Element? current = this;
        while (current is not null and not Page) current = current.Parent;
        return current as Page;
    }
}
