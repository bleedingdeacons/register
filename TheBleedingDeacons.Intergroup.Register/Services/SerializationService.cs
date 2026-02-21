using Serilog;
using System;
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

            var page = GetActivePage();
            if (page != null)
            {
                await page.DisplayAlert("Success",
                    $"File saved to: {filePath}", "OK");
            }
            else
            {
                Logger.Information("File saved to: {FilePath} (no active Page to display alert)", filePath);
            }
#endif
            return true;
        }
        catch (Exception ex)
        {
            var page = GetActivePage();
            if (page != null)
            {
                await page.DisplayAlert("Error",
                    $"Position export failed: {ex.Message}", "OK");
            }
            else
            {
                Logger.Error(ex, "Position export failed: {Message}", ex.Message);
            }

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
            
            var page = GetActivePage();
            if (page != null)
            {
                await page.DisplayAlert("Success", 
                    $"File saved to Downloads: {fileName}", "OK");
            }
            else
            {
                Logger.Information("File saved to Downloads: {FileName} (no active Page to display alert)", fileName);
            }
        }
        else
        {
            var filePath = Path.Combine(FileSystem.AppDataDirectory, fileName);
            await File.WriteAllBytesAsync(filePath, data);
            
            var page = GetActivePage();
            if (page != null)
            {
                await page.DisplayAlert("Success", 
                    $"File saved to app directory: {fileName}", "OK");
            }
            else
            {
                Logger.Information("File saved to app directory: {FileName} (no active Page to display alert)", fileName);
            }
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
        
        var page = GetActivePage();
        if (page != null)
        {
            await page.DisplayAlert("Success", 
                $"File saved and shared: {fileName}", "OK");
        }
        else
        {
            Logger.Information("File saved and shared: {FileName} (no active Page to display alert)", fileName);
        }
    }
#endif

#if WINDOWS
    private async Task SaveToFileWindows(byte[] data, string fileName)
    {
        var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var filePath = Path.Combine(documentsPath, fileName);
        await File.WriteAllBytesAsync(filePath, data);
        
        var page = GetActivePage();
        if (page != null)
        {
            await page.DisplayAlert("Success", 
                $"File saved to Documents: {fileName}", "OK");
        }
        else
        {
            Logger.Information("File saved to Documents: {FileName} (no active Page to display alert)", fileName);
        }
    }
#endif

    private Page? GetActivePage()
    {
        var app = Application.Current;
        if (app == null) return null;

        var windows = app.Windows;
        if (windows != null && windows.Count > 0)
        {
            return windows[0].Page;
        }

        return null;
    }
}