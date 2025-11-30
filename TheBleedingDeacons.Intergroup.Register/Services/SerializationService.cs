using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using TheBleedingDeacons.Intergroup.Register.Support;

namespace TheBleedingDeacons.Intergroup.Register.Services;

public class SerializationService
{
    private static readonly ILogger Logger = AppLogger.ForContext<SerializationService>();

    private readonly DataService _registrationService;

    public SerializationService(DataService registrationService)
    {
        _registrationService = registrationService;
    }

    public async Task<bool> ImportExcelFile()
    {
        try
        {
            var result = await FilePicker.PickAsync(new PickOptions
            {
                PickerTitle = "Select Excel file",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.iOS, new[] { "org.openxmlformats.spreadsheetml.sheet" } },
                    { DevicePlatform.Android, new[] { "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" } },
                    { DevicePlatform.WinUI, new[] { ".xlsx" } },
                    { DevicePlatform.macOS, new[] { "xlsx" } }
                })
            });

            if (result != null)
            {
                using var stream = await result.OpenReadAsync();

                await _registrationService.ImportFromExcel(stream);

                await Application.Current!.MainPage!.DisplayAlert("Success",
                    "Position Excel file imported successfully!", "OK");
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            await Application.Current!.MainPage!.DisplayAlert("Error",
                $"Data import failed: {ex.Message}", "OK");
            return false;
        }
    }

    public async Task<bool> ExportExcelFile()
    {
        try
        {
            var excelData = await _registrationService.ExportToExcel();
            var fileName = $"positions_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

#if ANDROID
            await SaveToDownloadsAndroid(excelData, fileName);
#elif IOS
            await SaveToDocumentsIOS(excelData, fileName);
#elif WINDOWS
            await SaveToFileWindows(excelData, fileName);
#else
            var filePath = Path.Combine(FileSystem.AppDataDirectory, fileName);
            await File.WriteAllBytesAsync(filePath, excelData);

            await Application.Current!.MainPage!.DisplayAlert("Success",
                $"File saved to: {filePath}", "OK");
#endif
            return true;
        }
        catch (Exception ex)
        {
            await Application.Current!.MainPage!.DisplayAlert("Error",
                $"Position export failed: {ex.Message}", "OK");
            return false;
        }
    }

#if ANDROID
    private async Task SaveToDownloadsAndroid(byte[] data, string fileName)
    {
        var downloadsPath = Android.OS.Environment.GetExternalStoragePublicDirectory(Android.OS.Environment.DirectoryDownloads)?.AbsolutePath;
        if (downloadsPath != null)
        {
            var filePath = Path.Combine(downloadsPath, fileName);
            await File.WriteAllBytesAsync(filePath, data);
            
            await Application.Current!.MainPage!.DisplayAlert("Success", 
                $"File saved to Downloads: {fileName}", "OK");
        }
        else
        {
            var filePath = Path.Combine(FileSystem.AppDataDirectory, fileName);
            await File.WriteAllBytesAsync(filePath, data);
            
            await Application.Current!.MainPage!.DisplayAlert("Success", 
                $"File saved to app directory: {fileName}", "OK");
        }
    }
#endif

#if IOS
    private async Task SaveToDocumentsIOS(byte[] data, string fileName)
    {
        var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var filePath = Path.Combine(documentsPath, fileName);
        await File.WriteAllBytesAsync(filePath, data);
        
        await Share.RequestAsync(new ShareFileRequest
        {
            Title = "Share Excel File",
            File = new ShareFile(filePath)
        });
        
        await Application.Current!.MainPage!.DisplayAlert("Success", 
            $"File saved and shared: {fileName}", "OK");
    }
#endif

#if WINDOWS
    private async Task SaveToFileWindows(byte[] data, string fileName)
    {
        var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var filePath = Path.Combine(documentsPath, fileName);
        await File.WriteAllBytesAsync(filePath, data);
        
        await Application.Current!.MainPage!.DisplayAlert("Success", 
            $"File saved to Documents: {fileName}", "OK");
    }
#endif
}