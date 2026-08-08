using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Storage;
using Serilog;
using System.Collections.ObjectModel;
using TheBleedingDeacons.Intergroup.Register.Support;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels
{
    public partial class DatabaseBackupViewModel : ObservableObject
    {
        [ObservableProperty]
        private bool isBackupInProgress;

        [ObservableProperty]
        private string statusMessage = "Ready to backup database";

        [ObservableProperty]
        private string lastBackupDate = string.Empty;

        [ObservableProperty]
        private string selectedDatabaseFile = string.Empty;

        [ObservableProperty]
        private DatabaseFileInfo selectedDatabaseFileInfo;

        public ObservableCollection<DatabaseFileInfo> DatabaseFiles { get; } = new();

        public DatabaseBackupViewModel()
        {
            LoadLastBackupDate();
            RefreshDatabaseListAsync().SafeFireAndForget("RefreshDatabaseList"); // Load database files on startup
        }

        [RelayCommand]
        private async Task RefreshDatabaseListAsync()
        {
            try
            {
                DatabaseFiles.Clear();
                StatusMessage = "Scanning for database files...";

                await Task.Run(() =>
                {
                    var appDataPath = FileSystem.AppDataDirectory;
                    var cacheDataPath = FileSystem.CacheDirectory;

                    // Common database file extensions
                    var dbExtensions = new[] { "*.db", "*.sqlite", "*.sqlite3", "*.db3" };

                    var dbFiles = new List<DatabaseFileInfo>();

                    // Search in App Data Directory
                    foreach (var extension in dbExtensions)
                    {
                        var files = Directory.GetFiles(appDataPath, extension, SearchOption.AllDirectories);
                        foreach (var file in files)
                        {
                            var fileInfo = new FileInfo(file);
                            dbFiles.Add(new DatabaseFileInfo
                            {
                                FileName = Path.GetFileName(file),
                                FullPath = file,
                                Size = fileInfo.Length,
                                LastModified = fileInfo.LastWriteTime,
                                Location = "App Data"
                            });
                        }
                    }

                    // Search in Cache Directory
                    if (Directory.Exists(cacheDataPath))
                    {
                        foreach (var extension in dbExtensions)
                        {
                            var files = Directory.GetFiles(cacheDataPath, extension, SearchOption.AllDirectories);
                            foreach (var file in files)
                            {
                                var fileInfo = new FileInfo(file);
                                dbFiles.Add(new DatabaseFileInfo
                                {
                                    FileName = Path.GetFileName(file),
                                    FullPath = file,
                                    Size = fileInfo.Length,
                                    LastModified = fileInfo.LastWriteTime,
                                    Location = "Cache"
                                });
                            }
                        }
                    }

                    // Update UI on main thread
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        foreach (var dbFile in dbFiles.OrderBy(f => f.FileName, StringComparer.CurrentCulture))
                        {
                            DatabaseFiles.Add(dbFile);
                        }

                        if (DatabaseFiles.Count > 0)
                        {
                            StatusMessage = $"Found {DatabaseFiles.Count} database file(s)";
                            // Auto-select the first database if none selected
                            if (string.IsNullOrEmpty(SelectedDatabaseFile))
                            {
                                var firstDb = DatabaseFiles[0];
                                firstDb.IsSelected = true;
                                SelectedDatabaseFile = firstDb.FullPath;
                                SelectedDatabaseFileInfo = firstDb;
                            }
                        }
                        else
                        {
                            StatusMessage = "No database files found in application directories";
                        }
                    });
                });
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error scanning for databases: {ex.Message}";
            }
        }

        [RelayCommand(CanExecute = nameof(CanBackupDatabase))]
        private async Task BackupDatabaseAsync()
        {
            IsBackupInProgress = true;
            StatusMessage = "Starting backup...";

            try
            {
                if (string.IsNullOrEmpty(SelectedDatabaseFile))
                {
                    StatusMessage = "Please select a database file to backup";
                    return;
                }

                if (!File.Exists(SelectedDatabaseFile))
                {
                    StatusMessage = "Selected database file not found!";
                    return;
                }

                StatusMessage = "Copying database file...";

                // Create backup filename with timestamp
                var originalFileName = Path.GetFileName(SelectedDatabaseFile);
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var backupFileName = $"backup_{Path.GetFileNameWithoutExtension(originalFileName)}_{timestamp}{Path.GetExtension(originalFileName)}";

                // Get downloads folder path
                var downloadsPath = GetDownloadsPath();
                var backupFilePath = Path.Combine(downloadsPath, backupFileName);

                // Ensure downloads directory exists
                Directory.CreateDirectory(downloadsPath);

                // Copy the database file
                await Task.Run(() => File.Copy(SelectedDatabaseFile, backupFilePath, true));

                StatusMessage = $"Backup completed successfully!\nSaved to: {backupFilePath}";

                // Save the backup date
                SaveLastBackupDate();
                LoadLastBackupDate();
            }
            catch (UnauthorizedAccessException)
            {
                StatusMessage = "Access denied. Unable to write to downloads folder.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Backup failed: {ex.Message}";
            }
            finally
            {
                IsBackupInProgress = false;
            }
        }

        [RelayCommand]
        private void SelectDatabase(string path)
        {
            // Clear previous selection
            foreach (var dbFile in DatabaseFiles)
            {
                dbFile.IsSelected = false;
            }

            // Set new selection
            var selectedFile = DatabaseFiles.FirstOrDefault(f => f.FullPath == path);
            if (selectedFile != null)
            {
                selectedFile.IsSelected = true;
                SelectedDatabaseFile = path;
                SelectedDatabaseFileInfo = selectedFile;
            }
        }

        private bool CanBackupDatabase()
        {
            return !IsBackupInProgress && !string.IsNullOrEmpty(SelectedDatabaseFile);
        }

        partial void OnIsBackupInProgressChanged(bool value)
        {
            BackupDatabaseCommand.NotifyCanExecuteChanged();
        }

        partial void OnSelectedDatabaseFileChanged(string value)
        {
            BackupDatabaseCommand.NotifyCanExecuteChanged();
        }

        partial void OnSelectedDatabaseFileInfoChanged(DatabaseFileInfo value)
        {
            // Update the selected database file path when the info changes
            if (value != null)
            {
                SelectedDatabaseFile = value.FullPath;
            }
        }

        private static string GetDownloadsPath()
        {
#if ANDROID
            // For Android, use the public Downloads directory
            var downloadsPath = Android.OS.Environment.GetExternalStoragePublicDirectory(Android.OS.Environment.DirectoryDownloads)?.AbsolutePath;
            return downloadsPath ?? Path.Combine(FileSystem.AppDataDirectory, "Downloads");
#elif WINDOWS
            // For Windows, use the user's Downloads folder
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
#elif IOS
            // For iOS, use the app's Documents directory (iOS doesn't have a shared Downloads folder)
            return FileSystem.AppDataDirectory;
#else
            // Fallback to app data directory
            return Path.Combine(FileSystem.AppDataDirectory, "Downloads");
#endif
        }

        private static void SaveLastBackupDate()
        {
            Preferences.Set("LastBackupDate", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        }

        private void LoadLastBackupDate()
        {
            var lastBackup = Preferences.Get("LastBackupDate", string.Empty);
            if (!string.IsNullOrEmpty(lastBackup))
            {
                LastBackupDate = $"Last backup: {lastBackup}";
            }
            else
            {
                LastBackupDate = "No previous backups";
            }
        }
    }

    public partial class DatabaseFileInfo : ObservableObject
    {
        [ObservableProperty]
        private string fileName = string.Empty;

        [ObservableProperty]
        private string fullPath = string.Empty;

        [ObservableProperty]
        private long size;

        [ObservableProperty]
        private DateTime lastModified;

        [ObservableProperty]
        private string location = string.Empty;

        [ObservableProperty]
        private bool isSelected;

        public string DisplayText => $"{FileName} ({FormatFileSize(Size)}) - {Location}";
        public string SizeText => FormatFileSize(Size);
        public string LastModifiedText => LastModified.ToString("yyyy-MM-dd HH:mm:ss");

        private static string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
            return $"{bytes / (1024.0 * 1024.0 * 1024.0):F1} GB";
        }
    }
}
