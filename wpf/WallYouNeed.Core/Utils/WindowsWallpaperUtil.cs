using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace WallYouNeed.Core.Utils
{
    public class WindowsWallpaperUtil
    {
        private readonly ILogger<WindowsWallpaperUtil> _logger;

        public WindowsWallpaperUtil(ILogger<WindowsWallpaperUtil> logger)
        {
            _logger = logger;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);

        private const int SPI_SETDESKWALLPAPER = 20;
        private const int SPIF_UPDATEINIFILE = 0x01;
        private const int SPIF_SENDCHANGE = 0x02;

        public bool SetWallpaper(string imagePath)
        {
            try
            {
                if (!File.Exists(imagePath))
                {
                    _logger.LogError("File does not exist: {FilePath}", imagePath);
                    return false;
                }

                SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, imagePath, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
                _logger.LogInformation("Wallpaper set successfully: {FilePath}", imagePath);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting wallpaper: {FilePath}", imagePath);
                return false;
            }
        }

        public bool SetWallpaperForMonitor(string imagePath, string monitorId)
        {
            try
            {
                // In a real implementation, this would use Windows API to set a wallpaper for a specific monitor
                // For demonstration purposes, we'll just set it for all monitors
                _logger.LogWarning("Setting wallpaper for specific monitor is not fully implemented. Using default implementation for all monitors.");
                return SetWallpaper(imagePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting wallpaper for monitor {MonitorId}: {FilePath}", monitorId, imagePath);
                return false;
            }
        }

        public bool SetLockScreenWallpaper(string imagePath)
        {
            try
            {
                if (!File.Exists(imagePath))
                {
                    _logger.LogError("File does not exist: {FilePath}", imagePath);
                    return false;
                }

                // On Windows 10/11, setting lock screen requires UWP APIs or registry modifications
                // This is a simplified implementation
                _logger.LogWarning("Setting lock screen wallpaper is not fully implemented.");
                
                // For demonstration purposes:
                var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Lock Screen", true);
                if (key != null)
                {
                    key.SetValue("LockScreenImagePath", imagePath);
                    key.Close();
                    _logger.LogInformation("Lock screen wallpaper registry key set: {FilePath}", imagePath);
                    return true;
                }
                
                _logger.LogWarning("Could not set lock screen wallpaper - registry key not found");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting lock screen wallpaper: {FilePath}", imagePath);
                return false;
            }
        }

        public List<MonitorInfo> GetConnectedMonitors()
        {
            // In a real implementation, this would enumerate all connected monitors using Windows API
            // For demonstration purposes, we'll return a mock list
            var monitors = new List<MonitorInfo>
            {
                new MonitorInfo { MonitorId = "monitor1", Name = "Primary Monitor", IsPrimary = true, Width = 1920, Height = 1080 }
            };
            
            _logger.LogInformation("Retrieved {Count} connected monitors", monitors.Count);
            return monitors;
        }
    }

    public class MonitorInfo
    {
        public string MonitorId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsPrimary { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }
} 