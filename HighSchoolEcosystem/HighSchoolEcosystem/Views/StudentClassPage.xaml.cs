using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace HighSchoolEcosystem.Views;

public partial class StudentClassPage : ContentPage
{
    private readonly StudentClassViewModel _viewModel;
    private readonly IServiceProvider _services;

    public StudentClassPage(StudentClassViewModel viewModel, IServiceProvider services)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _services = services;
    }

    /// <summary>
    /// Called before pushing. Separate from the constructor because the page is
    /// resolved from DI, which can't supply the class ID.
    /// </summary>
    public void Load(string classId, string className)
        => _viewModel.Initialise(classId, className);

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // Reloaded each time so progress ticked on the unit page is reflected
        // in the cards when the student comes back.
        await _viewModel.LoadAsync();
    }

    private async void OnClassChatTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            var page = _services.GetRequiredService<ClassChatPage>();
            page.Load(_viewModel.ClassId, _viewModel.ClassName);
            await Navigation.PushAsync(page);
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Navigate:ClassChat", ex);
        }
    }

    private async void OnUnitTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not UnitCardRow card) return;

        try
        {
            var page = _services.GetRequiredService<StudentUnitPage>();
            // An empty UnitId is legitimate — it's the loose "Module
            // information" group, which the unit page renders like any other.
            page.Load(_viewModel.ClassId, _viewModel.ClassName, card.UnitId);
            await Navigation.PushAsync(page);
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Navigate:StudentUnit", ex);
        }
    }

    private async void OnWorkTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not StudentWorkRow row) return;

        try
        {
            var page = _services.GetRequiredService<StudentAssignmentPage>();
            page.Load(row.ClassId, row.AssignmentId, row.Title, row.ClassName);
            await Navigation.PushAsync(page);
        }
        catch (Exception ex)
        {
            CrashLogger.Log("Navigate:StudentAssignment", ex);
        }
    }
}
