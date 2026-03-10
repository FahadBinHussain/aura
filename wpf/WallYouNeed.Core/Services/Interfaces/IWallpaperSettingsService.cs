using System;
using WallYouNeed.Core.Models;

namespace WallYouNeed.Core.Services.Interfaces;

/// <summary>
/// Interface for wallpaper settings service
/// </summary>
public interface IWallpaperSettingsService
{
    /// <summary>
    /// Event that fires when settings are changed
    /// </summary>
    event EventHandler<AppSettings>? SettingsChanged;

    /// <summary>
    /// Load settings from storage
    /// </summary>
    AppSettings LoadSettings();
    
    /// <summary>
    /// Save settings to storage
    /// </summary>
    void SaveSettings(AppSettings settings);
    
    /// <summary>
    /// Get current settings
    /// </summary>
    AppSettings GetSettings();
    
    /// <summary>
    /// Reset settings to defaults
    /// </summary>
    void ResetToDefaults();
} 