using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels;

public partial class EmailSettingsViewModel : ObservableObject
{
    private static readonly ILogger Logger = AppLogger.ForContext<EmailSettingsViewModel>();

    private readonly IMailService _mailService;
    
    [ObservableProperty]
    private string _smtpHost = "smtp.gmail.com";

    [ObservableProperty]
    private int _smtpPort = 587;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private bool _enableSsl = true;

    [ObservableProperty]
    private int _timeoutSeconds = 30;

    [ObservableProperty]
    private int _maxRetries = 10;

    [ObservableProperty]
    private bool _isOfflineMode;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private bool _isTesting;

    [ObservableProperty]
    private bool _isSaving;

    public EmailSettingsViewModel(IMailService mailService)
    {
        _mailService = mailService ?? throw new ArgumentNullException(nameof(mailService));

        IsOfflineMode = _mailService.IsOfflineMode;
        MaxRetries = _mailService.MaxRetries;

        LoadSettings();
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        try
        {
            IsSaving = true;
            StatusMessage = "Saving settings...";

            var config = new SmtpConfiguration
            {
                Host = SmtpHost,
                Port = SmtpPort,
                Username = Username,
                Password = Password,
                EnableSsl = EnableSsl,
                TimeoutSeconds = TimeoutSeconds,
                MaxRetries = MaxRetries
            };

            if (!config.IsValid())
            {
                StatusMessage = "Please fill in all required fields";
                return;
            }

            await _mailService.UpdateConfigurationAsync(config);

            // Update offline mode if changed
            if (IsOfflineMode != _mailService.IsOfflineMode)
            {
                if (IsOfflineMode)
                    _mailService.EnableOfflineMode();
                else
                    _mailService.DisableOfflineMode();
            }

            // Save to preferences for persistence
            await SaveToPreferences(config);

            StatusMessage = "Settings saved successfully";
            Logger.Information("SMTP configuration updated successfully");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error saving SMTP settings");
            StatusMessage = $"Error saving settings: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        try
        {
            IsTesting = true;
            StatusMessage = "Testing connection...";

            var config = new SmtpConfiguration
            {
                Host = SmtpHost,
                Port = SmtpPort,
                Username = Username,
                Password = Password,
                EnableSsl = EnableSsl,
                TimeoutSeconds = TimeoutSeconds,
                MaxRetries = MaxRetries
            };

            if (!config.IsValid())
            {
                StatusMessage = "Please fill in all required fields";
                return;
            }

            var result = await _mailService.TestSmtpConnectionAsync(config);

            if (result)
            {
                StatusMessage = "Connection test successful! Test email sent.";
            }
            else
            {
                StatusMessage = "Connection test failed. Check your settings.";
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error testing SMTP connection");
            StatusMessage = $"Connection test error: {ex.Message}";
        }
        finally
        {
            IsTesting = false;
        }
    }

    [RelayCommand]
    private void LoadDefaults()
    {
        SmtpHost = "smtp.gmail.com";
        SmtpPort = 587;
        EnableSsl = true;
        TimeoutSeconds = 30;
        MaxRetries = 10;
        StatusMessage = "Default settings loaded";
    }

    [RelayCommand]
    private void ToggleOfflineMode()
    {
        IsOfflineMode = !IsOfflineMode;

        if (IsOfflineMode)
        {
            _mailService.EnableOfflineMode();
            StatusMessage = "Offline mode enabled";
        }
        else
        {
            _mailService.DisableOfflineMode();
            StatusMessage = "Online mode enabled";
        }
    }

    private void LoadSettings()
    {
        try
        {
            // Load from preferences
            SmtpHost = Preferences.Get("smtp_host", "smtp.gmail.com");
            SmtpPort = Preferences.Get("smtp_port", 587);
            Username = Preferences.Get("smtp_username", string.Empty);
            // Note: In a real app, use SecureStorage for sensitive data like passwords
            Password = Preferences.Get("smtp_password", string.Empty);
            EnableSsl = Preferences.Get("smtp_enable_ssl", true);
            TimeoutSeconds = Preferences.Get("smtp_timeout", 30);
            MaxRetries = Preferences.Get("smtp_max_retries", 10);

            StatusMessage = "Settings loaded";
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error loading settings");
            StatusMessage = "Error loading settings, using defaults";
        }
    }

    private async Task SaveToPreferences(SmtpConfiguration config)
    {
        try
        {
            Preferences.Set("smtp_host", config.Host);
            Preferences.Set("smtp_port", config.Port);
            Preferences.Set("smtp_username", config.Username);
            // Note: In a real app, use SecureStorage for sensitive data
            Preferences.Set("smtp_password", config.Password);
            Preferences.Set("smtp_enable_ssl", config.EnableSsl);
            Preferences.Set("smtp_timeout", config.TimeoutSeconds);
            Preferences.Set("smtp_max_retries", config.MaxRetries);

            // For sensitive data like passwords, use SecureStorage instead:
            // await SecureStorage.SetAsync("smtp_password", config.Password);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error saving settings to preferences");
            throw;
        }
    }

    // Property validation
    partial void OnSmtpPortChanged(int value)
    {
        if (value <= 0 || value > 65535)
        {
            StatusMessage = "Port must be between 1 and 65535";
        }
    }

    partial void OnTimeoutSecondsChanged(int value)
    {
        if (value <= 0)
        {
            StatusMessage = "Timeout must be greater than 0";
        }
    }

    partial void OnMaxRetriesChanged(int value)
    {
        if (value <= 0)
        {
            StatusMessage = "Max retries must be greater than 0";
        }
        else
        {
            // Update the mail service immediately
            _mailService.MaxRetries = value;
        }
    }
}