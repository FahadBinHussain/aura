using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WallYouNeed.Core.Models;

namespace WallYouNeed.Core.Services.Interfaces;

/// <summary>
/// Interface for managing application settings
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// Event that fires when settings are changed
    /// </summary>
    event EventHandler<AppSettings>? SettingsChanged;

    /// <summary>
    /// Load application settings
    /// </summary>
    Task<AppSettings> LoadSettingsAsync();
    
    /// <summary>
    /// Save application settings
    /// </summary>
    Task SaveSettingsAsync(AppSettings settings);
    
    /// <summary>
    /// Update specific settings, merging with existing settings
    /// </summary>
    Task UpdateSettingsAsync(Action<AppSettings> updateAction);
    
    /// <summary>
    /// Get the current settings without loading from disk
    /// </summary>
    Task<AppSettings> GetSettingsAsync();
    
    /// <summary>
    /// Reset settings to defaults
    /// </summary>
    Task ResetToDefaultsAsync();
    
    /// <summary>
    /// Set the application to run at Windows startup
    /// </summary>
    Task<bool> SetRunAtStartupAsync(bool enabled);
    
    /// <summary>
    /// Check if the application is configured to run at startup
    /// </summary>
    Task<bool> IsRunAtStartupEnabledAsync();
} 