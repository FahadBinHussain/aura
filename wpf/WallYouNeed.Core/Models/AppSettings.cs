using System;
using System.Collections.Generic;

namespace WallYouNeed.Core.Models
{
    public enum AppTheme
    {
        Light,
        Dark
    }
    
    /// <summary>
    /// Application settings and configuration
    /// </summary>
    public class AppSettings
    {
        /// <summary>
        /// Application version
        /// </summary>
        public string AppVersion { get; set; } = "1.0.0";
        
        /// <summary>
        /// Theme of the application
        /// </summary>
        public AppTheme Theme { get; set; } = AppTheme.Dark;
        
        /// <summary>
        /// Current wallpaper ID
        /// </summary>
        public string CurrentWallpaperId { get; set; } = string.Empty;
        
        /// <summary>
        /// Whether to automatically change wallpapers
        /// </summary>
        public bool AutoChangeWallpaper { get; set; } = false;
        
        /// <summary>
        /// Rotation interval index
        /// </summary>
        public int RotationIntervalIndex { get; set; } = 2; // Default to 1 hour (index 2)
        
        /// <summary>
        /// Rotation interval in minutes
        /// </summary>
        public int RotationIntervalMinutes { get; set; } = 60; // Default to 1 hour
        
        /// <summary>
        /// Whether to use local wallpapers
        /// </summary>
        public bool UseLocalWallpapers { get; set; } = true;
        
        /// <summary>
        /// Whether to use Unsplash wallpapers
        /// </summary>
        public bool UseUnsplash { get; set; } = false;
        
        /// <summary>
        /// Whether to use Pexels wallpapers
        /// </summary>
        public bool UsePexels { get; set; } = false;
        
        /// <summary>
        /// API key for Unsplash
        /// </summary>
        public string UnsplashApiKey { get; set; } = string.Empty;
        
        /// <summary>
        /// API key for Pexels
        /// </summary>
        public string PexelsApiKey { get; set; } = string.Empty;
        
        /// <summary>
        /// Storage location
        /// </summary>
        public string StorageLocation { get; set; } = string.Empty;
        
        /// <summary>
        /// Whether to run the application at system startup
        /// </summary>
        public bool RunAtStartup { get; set; } = false;
        
        /// <summary>
        /// Whether to minimize the application to the tray
        /// </summary>
        public bool MinimizeToTray { get; set; } = true;
        
        /// <summary>
        /// Included tags for auto wallpaper change
        /// </summary>
        public List<string> IncludedTags { get; set; } = new List<string>();
        
        /// <summary>
        /// Excluded tags for auto wallpaper change
        /// </summary>
        public List<string> ExcludedTags { get; set; } = new List<string>();
        
        /// <summary>
        /// Whether to apply wallpapers to all monitors
        /// </summary>
        public bool ApplyToAllMonitors { get; set; } = true;
        
        /// <summary>
        /// Monitor-specific wallpapers
        /// </summary>
        public Dictionary<string, string> MonitorWallpapers { get; set; } = new Dictionary<string, string>();
        
        /// <summary>
        /// Whether to apply wallpapers to the lock screen
        /// </summary>
        public bool ApplyToLockScreen { get; set; } = false;
        
        /// <summary>
        /// Lock screen wallpaper ID
        /// </summary>
        public string LockScreenWallpaperId { get; set; } = string.Empty;
        
        /// <summary>
        /// Whether to enable detailed logging for troubleshooting
        /// </summary>
        public bool VerboseLogging { get; set; } = false;
        
        /// <summary>
        /// Window width
        /// </summary>
        public double WindowWidth { get; set; } = 1200;
        
        /// <summary>
        /// Window height
        /// </summary>
        public double WindowHeight { get; set; } = 800;
        
        /// <summary>
        /// Window left position
        /// </summary>
        public double WindowLeft { get; set; } = 100;
        
        /// <summary>
        /// Window top position
        /// </summary>
        public double WindowTop { get; set; } = 100;
        
        /// <summary>
        /// Window state (e.g., normal, maximized, minimized)
        /// </summary>
        public string WindowState { get; set; } = "Normal";
        
        /// <summary>
        /// LatestWallpapersPage scroll position
        /// </summary>
        public double LatestWallpapersScrollPosition { get; set; } = 0;
        
        /// <summary>
        /// LatestWallpapersPage item width
        /// </summary>
        public double LatestWallpapersItemWidth { get; set; } = 300;
        
        /// <summary>
        /// LatestWallpapersPage item height
        /// </summary>
        public double LatestWallpapersItemHeight { get; set; } = 180;
    }
} 