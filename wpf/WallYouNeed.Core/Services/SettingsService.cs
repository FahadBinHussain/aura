using System.Text.Json;
using LiteDB;
using Microsoft.Extensions.Logging;
using WallYouNeed.Core.Models;
using WallYouNeed.Core.Services.Interfaces;
using Microsoft.Win32;
using System.IO;

namespace WallYouNeed.Core.Services;

/// <summary>
/// Implementation of the settings service
/// </summary>
public class SettingsService : ISettingsService
{
    private readonly ILogger<SettingsService> _logger;
    private readonly string _databasePath;
    private readonly string _settingsFilePath;
    private AppSettings _currentSettings;
    private const string AppRegistryKey = "WallYouNeed";

    // Event that fires when settings are changed
    public event EventHandler<AppSettings>? SettingsChanged;

    public SettingsService(ILogger<SettingsService> logger)
    {
        _logger = logger;
        
        // Define paths
        string appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WallYouNeed");
        
        // Ensure directories exist
        Directory.CreateDirectory(appDataPath);
        
        _databasePath = Path.Combine(appDataPath, "WallYouNeed.db");
        _settingsFilePath = Path.Combine(appDataPath, "settings.json");
        
        // Initialize with default settings
        _currentSettings = new AppSettings
        {
            StorageLocation = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                "WallYouNeed"
            ),
            RotationIntervalMinutes = 60,
            Theme = AppTheme.Dark
        };

        // Add null check before creating directory
        var dirPath = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrEmpty(dirPath))
        {
            Directory.CreateDirectory(dirPath);
        }
    }

    /// <inheritdoc />
    public async Task<AppSettings> LoadSettingsAsync()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                string json = await File.ReadAllTextAsync(_settingsFilePath);
                var loadedSettings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json);
                
                if (loadedSettings != null)
                {
                    _currentSettings = loadedSettings;
                    _logger.LogInformation("Settings loaded successfully");
                    
                    // Notify listeners that settings have been loaded
                    SettingsChanged?.Invoke(this, _currentSettings);
                }
            }
            else
            {
                _logger.LogInformation("Settings file not found, using defaults");
                await SaveSettingsAsync(_currentSettings);
            }

            // Ensure save location directory exists
            Directory.CreateDirectory(_currentSettings.StorageLocation);
            
            return _currentSettings;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading settings");
            return _currentSettings;
        }
    }

    /// <inheritdoc />
    public async Task SaveSettingsAsync(AppSettings settings)
    {
        try
        {
            _currentSettings = settings;
            string json = System.Text.Json.JsonSerializer.Serialize(settings, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            
            await File.WriteAllTextAsync(_settingsFilePath, json);
            _logger.LogInformation("Settings saved successfully");
            
            // Notify listeners that settings have changed
            SettingsChanged?.Invoke(this, _currentSettings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving settings");
        }
    }

    /// <inheritdoc />
    public async Task UpdateSettingsAsync(Action<AppSettings> updateAction)
    {
        // Apply the update action to current settings
        updateAction(_currentSettings);
        
        // Save the updated settings
        await SaveSettingsAsync(_currentSettings);
    }

    /// <inheritdoc />
    public async Task<AppSettings> GetSettingsAsync()
    {
        return await Task.FromResult(_currentSettings);
    }

    /// <inheritdoc />
    public async Task ResetToDefaultsAsync()
    {
        _currentSettings = new AppSettings
        {
            StorageLocation = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                "WallYouNeed"
            ),
            RotationIntervalMinutes = 60,
            Theme = AppTheme.Dark
        };
        
        await SaveSettingsAsync(_currentSettings);
        _logger.LogInformation("Settings reset to defaults");
    }

    /// <inheritdoc />
    public async Task<bool> SetRunAtStartupAsync(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", 
                true);
                
            if (key == null)
            {
                _logger.LogError("Failed to open registry key for startup");
                return false;
            }
            
            if (enabled)
            {
                string appPath = System.Reflection.Assembly.GetEntryAssembly()?.Location ?? "";
                if (string.IsNullOrEmpty(appPath)) return false;
                
                // Convert app.dll path to .exe
                if (appPath.EndsWith(".dll"))
                {
                    appPath = Path.Combine(
                        Path.GetDirectoryName(appPath) ?? "",
                        $"{Path.GetFileNameWithoutExtension(appPath)}.exe"
                    );
                }
                
                key.SetValue(AppRegistryKey, appPath);
                _logger.LogInformation("Application added to startup");
            }
            else
            {
                key.DeleteValue(AppRegistryKey, false);
                _logger.LogInformation("Application removed from startup");
            }
            
            // Update the current settings
            await UpdateSettingsAsync(s => s.RunAtStartup = enabled);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting startup status");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsRunAtStartupEnabledAsync()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
                
            if (key == null) return false;
            
            var value = key.GetValue(AppRegistryKey);
            bool isEnabled = value != null;
            
            // Sync settings with actual registry state
            if (_currentSettings.RunAtStartup != isEnabled)
            {
                await UpdateSettingsAsync(s => s.RunAtStartup = isEnabled);
            }
            
            return isEnabled;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking startup status");
            return false;
        }
    }
} 