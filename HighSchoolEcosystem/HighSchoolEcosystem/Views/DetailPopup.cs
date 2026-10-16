using HighSchoolEcosystem.Controls;
using HighSchoolEcosystem.Services;

namespace HighSchoolEcosystem.Views;

/// <summary>What the reader chose to do after seeing the details.</summary>
public enum DetailAction
{
    None,
    Edit,
    Delete
}

/// <summary>
/// The "tap it to see all of it" sheet, shared by announcements, sports posts
/// and assessments on every dashboard.
///
/// This USED to be DisplayAlert + DisplayActionSheet. Those work, but they are
/// OS dialogs and cannot be styled at all — so a branded app opened a grey
/// system box, and an author had to go through a two-step "Manage, then pick"
/// because an alert carries at most two buttons.
///
/// It's now an in-app sheet (Controls/DetailSheet), which means the app's own
/// colours and type, a scrollable body for a long notice, and every action on
/// one screen. The signature is unchanged, so no call site moved.
/// </summary>
public static class DetailPopup
{
    public static Task<DetailAction> ShowAsync(
        Page page, string title, string details, bool canManage)
        => ShowAsync(page, title, details, scope: string.Empty, accent: null, canManage);

    /// <summary>
    /// The full form. <paramref name="scope"/> shows as a chip — "Whole school",
    /// a class or a team — and <paramref name="accent"/> tints the strip so a
    /// team notice and a school one aren't the same object at a glance.
    /// </summary>
    public static Task<DetailAction> ShowAsync(
        Page page, string title, string details, string scope, Color? accent, bool canManage)
        => ShowAsync(page, title, details, scope, accent, canEdit: canManage, canDelete: canManage);

    /// <summary>
    /// Edit and Delete offered separately — for the Newspaper, where a teacher
    /// may take an article down but not edit a student's words.
    /// </summary>
    public static async Task<DetailAction> ShowAsync(
        Page page, string title, string details, string scope, Color? accent,
        bool canEdit, bool canDelete)
    {
        // The sheet is injected into the page's root Grid, which every page that
        // calls this has. If one ever doesn't, fall back to the OS dialog rather
        // than throwing — an unstyled sheet beats no sheet.
        if (page is not ContentPage { Content: Grid root })
        {
            await page.DisplayAlertAsync(
                string.IsNullOrWhiteSpace(title) ? "Details" : title, details, "Close");
            return DetailAction.None;
        }

        var sheet = new DetailSheet();
        sheet.Fill(title, details, scope, accent ?? Color.FromArgb("#178C8C"), canEdit, canDelete);

        // Span the whole grid. The dashboards are single-cell, but the pages
        // that carry HomeRail have a 44,* column layout — without the span the
        // sheet would be confined to the 44-wide rail column.
        Grid.SetRow(sheet, 0);
        Grid.SetColumn(sheet, 0);
        Grid.SetRowSpan(sheet, Math.Max(1, root.RowDefinitions.Count));
        Grid.SetColumnSpan(sheet, Math.Max(1, root.ColumnDefinitions.Count));

        // Added LAST so it draws over everything, including the floating rail.
        root.Children.Add(sheet);

        try
        {
            await sheet.AnimateInAsync();
            return await sheet.Result;
        }
        catch (Exception ex)
        {
            CrashLogger.Log("DetailPopup.Show", ex);
            return DetailAction.None;
        }
        finally
        {
            // Always removed, even if the await above threw — a stranded sheet
            // would leave the page permanently covered by a dimmed scrim with
            // no way to dismiss it.
            root.Children.Remove(sheet);
        }
    }
}
