using CommunityToolkit.Mvvm.Input;
using Serilog;
using TheBleedingDeacons.Intergroup.Register.Services;
using TheBleedingDeacons.Intergroup.Register.Support;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels;

public partial class ImportExportViewModel : BaseViewModel
{
    private static readonly ILogger Logger = AppLogger.ForContext<ImportExportViewModel>();

    private readonly SerializationService _externalService;

    public ImportExportViewModel(SerializationService externalService)
    {
        _externalService = externalService;
        Title = "Data";
    }

    public string SystemInfo => GetAppInfo();

    private string GetAppInfo()
    {        
        string info = Path.Combine(FileSystem.AppDataDirectory, MauiProgram.UNITY_DATABASE_NAME);
        System.Diagnostics.Debug.WriteLine(info);
        return info;
    }

    [RelayCommand]
    async Task ExportExcel()
    {
        if (IsBusy) return;

        IsBusy = true;
        try
        {
            await _externalService.ExportExcelFile();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    async Task ViewAllGroups()
    {
        await Shell.Current.GoToAsync("GroupListPage");
    }

    [RelayCommand]
    async Task GoBack()
    {
        await Shell.Current.GoToAsync("..");
    }
}