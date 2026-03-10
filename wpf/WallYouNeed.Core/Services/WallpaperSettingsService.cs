using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using WallYouNeed.Core.Models;
using WallYouNeed.Core.Services.Interfaces;

namespace WallYouNeed.Core.Services;

/// <summary>
/// Implementation of the wallpaper settings service
/// </summary>
public class WallpaperSettingsService : IWallpaperSettingsService
{
    private readonly ILogger<WallpaperSettingsService> _logger;
    private readonly string _settingsPath;
    private AppSettings _settings;

    public event EventHandler<AppSettings>? SettingsChanged;

    public WallpaperSettingsService(ILogger<WallpaperSettingsService> logger)
    {
        _logger = logger;
        _settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WallYouNeed",
            "settings.json");
        
        _settings = new AppSettings();
    }

    /// <inheritdoc />
    public AppSettings LoadSettings()
    {
        try
        {
            _logger.LogInformation("Loading settings from: {Path}", _settingsPath);
            
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                _settings = JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
                _logger.LogInformation("Settings loaded successfully");
            }
            else
            {
                _logger.LogInformation("Settings file not found, using defaults");
                _settings = new AppSettings();
                SaveSettings(_settings);
            }
            
            return _settings;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading settings");
            _settings = new AppSettings();
            return _settings;
        }
    }

    /// <inheritdoc />
    public void SaveSettings(AppSettings settings)
    {
        try
        {
            _logger.LogInformation("Saving settings to: {Path}", _settingsPath);
            
            // Ensure directory exists
            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
            File.WriteAllText(_settingsPath, json);
            
            _settings = settings;
            OnSettingsChanged(settings);
            
            _logger.LogInformation("Settings saved successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving settings");
        }
    }

    /// <inheritdoc />
    public AppSettings GetSettings()
    {
        return _settings;
    }

    /// <inheritdoc />
    public void ResetToDefaults()
    {
        _logger.LogInformation("Resetting settings to defaults");
        
        _settings = new AppSettings();
        SaveSettings(_settings);
        
        _logger.LogInformation("Settings reset successfully");
    }

    private void OnSettingsChanged(AppSettings settings)
    {
        SettingsChanged?.Invoke(this, settings);
    }
} 