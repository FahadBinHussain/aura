using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.Runtime.Versioning;
using System.IO;

namespace WallYouNeed.Core.Utilities;

/// <summary>
/// Utility class for Windows wallpaper operations
/// </summary>
[SupportedOSPlatform("windows")]
public class WindowsWallpaperUtil
{
    private readonly ILogger<WindowsWallpaperUtil> _logger;

    // Windows API constants
    private const int SPI_SETDESKWALLPAPER = 0x0014;
    private const int SPI_GETDESKWALLPAPER = 0x0073;
    private const int SPIF_UPDATEINIFILE = 0x01;
    private const int SPIF_SENDCHANGE = 0x02;
    private const int MAX_PATH = 260;
    private const string WALLPAPER_STYLE_PATH = @"Control Panel\Desktop";
    private const string LOCK_SCREEN_PATH = @"SOFTWARE\Microsoft\Windows\CurrentVersion\PersonalizationCSP";

    // Wallpaper styles
    public const int WALLPAPER_STYLE_CENTER = 0;
    public const int WALLPAPER_STYLE_TILE = 1;
    public const int WALLPAPER_STYLE_STRETCH = 2;
    public const int WALLPAPER_STYLE_FIT = 3;
    public const int WALLPAPER_STYLE_FILL = 4;
    public const int WALLPAPER_STYLE_SPAN = 5;

    // P/Invoke declarations for Windows API calls
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int SystemParametersInfo(int uAction, int uParam, StringBuilder lpvParam, int fuWinIni);

    public WindowsWallpaperUtil(ILogger<WindowsWallpaperUtil> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Set wallpaper for the Windows desktop
    /// </summary>
    /// <param name="filePath">Path to the wallpaper image</param>
    /// <param name="style">Wallpaper style (center, stretch, etc.)</param>
    /// <returns>True if successful</returns>
    public bool SetDesktopWallpaper(string filePath, int style = WALLPAPER_STYLE_FILL)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                _logger.LogError($"Wallpaper file not found: {filePath}");
                return false;
            }

            // Set the wallpaper style in registry
            using (var key = Registry.CurrentUser.OpenSubKey(WALLPAPER_STYLE_PATH, true))
            {
                if (key == null)
                {
                    _logger.LogError("Could not open registry key for wallpaper style");
                    return false;
                }

                // Set the wallpaper style and tile mode
                switch (style)
                {
                    case WALLPAPER_STYLE_CENTER:
                        key.SetValue("WallpaperStyle", "0");
                        key.SetValue("TileWallpaper", "0");
                        break;
                    case WALLPAPER_STYLE_TILE:
                        key.SetValue("WallpaperStyle", "0");
                        key.SetValue("TileWallpaper", "1");
                        break;
                    case WALLPAPER_STYLE_STRETCH:
                        key.SetValue("WallpaperStyle", "2");
                        key.SetValue("TileWallpaper", "0");
                        break;
                    case WALLPAPER_STYLE_FIT:
                        key.SetValue("WallpaperStyle", "6");
                        key.SetValue("TileWallpaper", "0");
                        break;
                    case WALLPAPER_STYLE_FILL:
                        key.SetValue("WallpaperStyle", "10");
                        key.SetValue("TileWallpaper", "0");
                        break;
                    case WALLPAPER_STYLE_SPAN:
                        key.SetValue("WallpaperStyle", "22");
                        key.SetValue("TileWallpaper", "0");
                        break;
                }
            }

            // Apply the wallpaper
            var result = SystemParametersInfo(
                SPI_SETDESKWALLPAPER,
                0,
                filePath,
                SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);

            if (result == 0)
            {
                _logger.LogError($"Failed to set wallpaper: {Marshal.GetLastWin32Error()}");
                return false;
            }

            _logger.LogInformation($"Wallpaper set successfully: {filePath}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting desktop wallpaper");
            return false;
        }
    }

    /// <summary>
    /// Get the current desktop wallpaper path
    /// </summary>
    public string GetCurrentWallpaper()
    {
        try
        {
            StringBuilder wallpaperPath = new StringBuilder(MAX_PATH);

            var result = SystemParametersInfo(
                SPI_GETDESKWALLPAPER,
                wallpaperPath.Capacity,
                wallpaperPath,
                0);

            if (result == 0)
            {
                _logger.LogError($"Failed to get wallpaper: {Marshal.GetLastWin32Error()}");
                return string.Empty;
            }

            return wallpaperPath.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting current wallpaper");
            return string.Empty;
        }
    }

    /// <summary>
    /// Set the lock screen wallpaper for Windows 10+
    /// </summary>
    /// <param name="filePath">Path to the wallpaper image</param>
    /// <returns>True if successful</returns>
    public bool SetLockScreenWallpaper(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                _logger.LogError($"Lock screen wallpaper file not found: {filePath}");
                return false;
            }

            using (var key = Registry.LocalMachine.CreateSubKey(LOCK_SCREEN_PATH))
            {
                if (key == null)
                {
                    _logger.LogError("Could not create registry key for lock screen");
                    return false;
                }

                key.SetValue("LockScreenImagePath", filePath);
                key.SetValue("LockScreenImageStatus", 1);
                
                _logger.LogInformation($"Lock screen wallpaper set: {filePath}");
            }

            return true;
        }
        catch (UnauthorizedAccessException)
        {
            _logger.LogError("Admin rights required to set lock screen wallpaper");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting lock screen wallpaper");
            return false;
        }
    }

    /// <summary>
    /// Get information about connected monitors
    /// </summary>
    /// <returns>List of monitor information</returns>
    public List<MonitorInfo> GetConnectedMonitors()
    {
        // This is a simplified implementation that only returns one monitor
        // A full implementation would use Windows API to enumerate all monitors
        var monitors = new List<MonitorInfo>
        {
            new MonitorInfo
            {
                MonitorId = "primary",
                Name = "Primary Monitor",
                IsPrimary = true
            }
        };

        return monitors;
    }

    /// <summary>
    /// Set a different wallpaper for each monitor (Windows 10+)
    /// </summary>
    /// <param name="monitorWallpapers">Dictionary with monitor IDs and wallpaper paths</param>
    /// <returns>True if successful</returns>
    public bool SetMultiMonitorWallpapers(Dictionary<string, string> monitorWallpapers)
    {
        try
        {
            // In a real implementation, this would use the Windows 10+ multi-monitor API
            // For now, this is a simplified version that just sets the main wallpaper
            
            if (monitorWallpapers.Count == 0)
            {
                _logger.LogWarning("No wallpapers provided for multi-monitor setup");
                return false;
            }
            
            // For now, just use the first wallpaper
            var firstWallpaper = monitorWallpapers.Values.First();
            return SetDesktopWallpaper(firstWallpaper, WALLPAPER_STYLE_FILL);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting multi-monitor wallpapers");
            return false;
        }
    }
}

/// <summary>
/// Information about a monitor
/// </summary>
public class MonitorInfo
{
    public string MonitorId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
} 