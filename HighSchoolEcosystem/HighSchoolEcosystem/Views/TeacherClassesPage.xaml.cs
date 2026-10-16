using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace HighSchoolEcosystem.Views;

public partial class TeacherClassesPage : ContentPage
{
    private readonly TeacherClassesViewModel _viewModel;
    private readonly IServiceProvider _services;

    public TeacherClassesPage(TeacherClassesViewModel viewModel, IServiceProvider services)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _services = services;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    /// <summary>
    /// The class list is a BindableLayout tile grid now, not a CollectionView,
    /// so there is no selection model — the item comes off the tapped tile's
    /// own BindingContext, the same way the teacher dashboard does it.
    /// </summary>
    private async void OnClassRowTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not TeacherClassRow row) return;

        try
        {
            var page = _services.GetRequiredService<TeacherClassDetailPage>();
            page.Load(row.Id);
            await Navigation.PushAsync(page);
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Navigate:TeacherClassDetail", ex);
        }
    }
}
