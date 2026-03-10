using System.Threading.Tasks;

namespace WallYouNeed.Core.Services.Interfaces;

/// <summary>
/// Interface for wallpaper rotation service
/// </summary>
public interface IWallpaperRotationService
{
    /// <summary>
    /// Apply a random wallpaper based on settings
    /// </summary>
    Task<bool> ApplyRandomWallpaperAsync();
    
    /// <summary>
    /// Start the automatic wallpaper rotation
    /// </summary>
    Task StartRotationAsync(int intervalMinutes);
    
    /// <summary>
    /// Stop the automatic wallpaper rotation
    /// </summary>
    Task StopRotationAsync();
} 