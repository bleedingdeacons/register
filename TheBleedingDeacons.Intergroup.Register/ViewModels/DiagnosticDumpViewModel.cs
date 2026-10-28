using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Storage;
using Serilog;
using System.Collections.ObjectModel;
using System.Globalization;
using TheBleedingDeacons.Intergroup.Register.Support;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels
{
    public partial class DiagnosticDumpViewModel : ObservableObject
    {
        /// <summary>
        /// The one folder every export goes to, under the user-visible
        /// Downloads directory: database backups and logs alike, so there is
        /// a single place to look for anything the app has handed over.
        /// Public on purpose. Kept in one place because the Android
        /// MediaStore path and the direct path both need it.
        /// </summary>
        private const string ExportSubfolder = "Intergroup Register";

        private static string ExportFolderLabel => $"Download/{ExportSubfolder}";

        [ObservableProperty]
        private bool isBackupInProgress;

        [ObservableProperty]
        private string statusMessage = "Ready to backup database";

        [ObservableProperty]
        private string lastBackupDate = string.Empty;

        [ObservableProperty]
        private string lastLogExportDate = string.Empty;

        [ObservableProperty]
        private string selectedDatabaseFile = string.Empty;

        [ObservableProperty]
        private DatabaseFileInfo? selectedDatabaseFileInfo;

        public ObservableCollection<DatabaseFileInfo> DatabaseFiles { get; } = new();

        public DiagnosticDumpViewModel()
        {
            LoadLastBackupDate();
            LoadLastLogExportDate();
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
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                var backupFileName = $"backup_{Path.GetFileNameWithoutExtension(originalFileName)}_{timestamp}{Path.GetExtension(originalFileName)}";

                // Through the same path as the logs: on Android 10+ a raw
                // File.Copy into public Downloads is not allowed at all.
                await PublishToDownloadsAsync(SelectedDatabaseFile, backupFileName, "application/vnd.sqlite3");

                StatusMessage = $"Backup completed successfully!\nSaved to {ExportFolderLabel}/{backupFileName}";

                // Save the backup date
                SaveLastBackupDate();
                LoadLastBackupDate();
            }
            catch (UnauthorizedAccessException)
            {
                StatusMessage = "Access denied. Unable to write to the Downloads folder.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Backup failed: {ex.Message}";
                Log.Warning(ex, "Database backup to Downloads failed");
            }
            finally
            {
                IsBackupInProgress = false;
            }
        }

        [RelayCommand(CanExecute = nameof(CanExportLogs))]
        private async Task ExportLogsAsync()
        {
            IsBackupInProgress = true;
            StatusMessage = "Exporting logs...";

            try
            {
                var logsPath = Path.Combine(FileSystem.AppDataDirectory, "logs");

                if (!Directory.Exists(logsPath))
                {
                    StatusMessage = "No logs folder found — nothing to export.";
                    return;
                }

                // The Better Stack buffer, its bookmark, and (on DEBUG builds)
                // the rolling file-sink logs. All of these live inside the app's
                // sandbox, which on Android is unreadable by any other app, so
                // the only way to inspect them on a production device is to
                // publish a copy somewhere user-visible.
                var logFiles = Directory.GetFiles(logsPath, "*", SearchOption.AllDirectories);

                if (logFiles.Length == 0)
                {
                    StatusMessage = "Logs folder is empty — nothing to export.";
                    return;
                }

                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                var exported = 0;

                foreach (var logFile in logFiles)
                {
                    // Flatten the tree into one folder — the buffer sits in a
                    // subdirectory, and MediaStore's RELATIVE_PATH makes nested
                    // folders more trouble than they're worth for a diagnostic
                    // dump. Prefixing with the timestamp keeps repeat exports
                    // apart and stops MediaStore appending "(1)" to the name.
                    var targetName = $"{timestamp}_{Path.GetFileName(logFile)}";
                    await PublishToDownloadsAsync(logFile, targetName, "text/plain");
                    exported++;
                }

                StatusMessage =
                    $"Exported {exported} log file(s) to {ExportFolderLabel}.";

                SaveLastLogExportDate();
                LoadLastLogExportDate();
            }
            catch (UnauthorizedAccessException)
            {
                StatusMessage = "Access denied. Unable to write to the Downloads folder.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Log export failed: {ex.Message}";
                Log.Warning(ex, "Log export to Downloads failed");
            }
            finally
            {
                IsBackupInProgress = false;
            }
        }

        /// <summary>
        /// Copies one file into the export folder, <see cref="ExportSubfolder"/>
        /// under the user-visible Downloads directory. Every export goes
        /// through here, so they all land in the same place.
        ///
        /// <para>On Android API 29+ the public Downloads folder is not writable
        /// by raw path — scoped storage requires going through MediaStore, which
        /// hands back a stream rather than a filename. Older releases keep the
        /// direct-path behaviour, gated on the legacy storage permission.</para>
        /// </summary>
        private static async Task PublishToDownloadsAsync(string sourcePath, string targetFileName, string mimeType)
        {
#if ANDROID
            if (OperatingSystem.IsAndroidVersionAtLeast(29))
            {
                var resolver = Android.App.Application.Context.ContentResolver
                    ?? throw new IOException("No ContentResolver available.");

                var values = new Android.Content.ContentValues();
                values.Put(Android.Provider.MediaStore.IMediaColumns.DisplayName, targetFileName);
                values.Put(Android.Provider.MediaStore.IMediaColumns.MimeType, mimeType);
                values.Put(
                    Android.Provider.MediaStore.IMediaColumns.RelativePath,
                    $"{Android.OS.Environment.DirectoryDownloads}/{ExportSubfolder}");

                // IS_PENDING hides the row until the write completes, so a file
                // browser never shows a half-copied file.
                values.Put(Android.Provider.MediaStore.IMediaColumns.IsPending, 1);

                var collection = Android.Provider.MediaStore.Downloads.ExternalContentUri
                    ?? throw new IOException("Could not resolve the MediaStore Downloads collection.");

                var uri = resolver.Insert(collection, values)
                    ?? throw new IOException($"MediaStore refused to create {targetFileName}.");

                using (var target = resolver.OpenOutputStream(uri, "w")
                    ?? throw new IOException($"Could not open an output stream for {targetFileName}."))
                using (var source = OpenSharedRead(sourcePath))
                {
                    await source.CopyToAsync(target);
                }

                values.Clear();
                values.Put(Android.Provider.MediaStore.IMediaColumns.IsPending, 0);
                resolver.Update(uri, values, null, null);
                return;
            }

            // API 21–28: direct path, but WRITE_EXTERNAL_STORAGE is a runtime
            // permission from API 23 onward.
            var status = await Permissions.RequestAsync<Permissions.StorageWrite>();
            if (status != PermissionStatus.Granted)
            {
                throw new UnauthorizedAccessException("Storage permission was not granted.");
            }

            var publicDownloads = Android.OS.Environment
                .GetExternalStoragePublicDirectory(Android.OS.Environment.DirectoryDownloads)?.AbsolutePath
                ?? throw new IOException("Could not resolve the public Downloads folder.");

            var targetDirectory = Path.Combine(publicDownloads, ExportSubfolder);
#elif WINDOWS
            _ = mimeType; // Only MediaStore asks for one.
            var targetDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads",
                ExportSubfolder);
#else
            _ = mimeType; // Only MediaStore asks for one.
            // iOS and Mac have no shared Downloads folder an app can write to;
            // the app's own Documents directory is what the Files app shows.
            var targetDirectory = Path.Combine(FileSystem.AppDataDirectory, ExportSubfolder);
#endif

            Directory.CreateDirectory(targetDirectory);

            using var input = OpenSharedRead(sourcePath);
            using var output = File.Create(Path.Combine(targetDirectory, targetFileName));
            await input.CopyToAsync(output);
        }

        /// <summary>
        /// Opens a file for reading while tolerating concurrent writers. The
        /// Better Stack shipper appends to the buffer and deletes rolled files
        /// on its own schedule, so a plain <see cref="File.Copy(string, string, bool)"/>
        /// here races with it and throws a sharing violation.
        /// </summary>
        private static FileStream OpenSharedRead(string path)
        {
            return new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
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

        private bool CanExportLogs()
        {
            return !IsBackupInProgress;
        }

        partial void OnIsBackupInProgressChanged(bool value)
        {
            BackupDatabaseCommand.NotifyCanExecuteChanged();
            ExportLogsCommand.NotifyCanExecuteChanged();
        }

        partial void OnSelectedDatabaseFileChanged(string value)
        {
            BackupDatabaseCommand.NotifyCanExecuteChanged();
        }

        partial void OnSelectedDatabaseFileInfoChanged(DatabaseFileInfo? value)
        {
            // Update the selected database file path when the info changes
            if (value != null)
            {
                SelectedDatabaseFile = value.FullPath;
            }
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

        private static void SaveLastLogExportDate()
        {
            Preferences.Set(
                "LastLogExportDate",
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        }

        private void LoadLastLogExportDate()
        {
            var lastExport = Preferences.Get("LastLogExportDate", string.Empty);
            if (!string.IsNullOrEmpty(lastExport))
            {
                LastLogExportDate = $"Last log export: {lastExport}";
            }
            else
            {
                LastLogExportDate = "No previous log exports";
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
