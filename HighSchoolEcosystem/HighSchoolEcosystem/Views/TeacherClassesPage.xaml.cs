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

    private async void OnClassSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not TeacherClassRow row) return;
        if (sender is CollectionView collectionView) collectionView.SelectedItem = null;

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
