using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WallYouNeed.Core.Models;

namespace WallYouNeed.Core.Services.Interfaces;

/// <summary>
/// Interface for the wallpaper service
/// </summary>
public interface IWallpaperService
{
    /// <summary>
    /// Get all wallpapers
    /// </summary>
    /// <returns>All wallpapers</returns>
    Task<IEnumerable<Wallpaper>> GetAllWallpapersAsync();
    
    /// <summary>
    /// Get wallpapers by source
    /// </summary>
    /// <param name="source">The wallpaper source</param>
    /// <returns>Wallpapers from the specified source</returns>
    Task<IEnumerable<Wallpaper>> GetWallpapersBySourceAsync(WallpaperSource source);
    
    /// <summary>
    /// Get wallpapers by tag
    /// </summary>
    /// <param name="tag">The tag to search for</param>
    /// <returns>Wallpapers with the specified tag</returns>
    Task<IEnumerable<Wallpaper>> GetWallpapersByTagAsync(string tag);
    
    /// <summary>
    /// Get a wallpaper by ID
    /// </summary>
    /// <param name="id">The wallpaper ID</param>
    /// <returns>The wallpaper with the specified ID, or null if not found</returns>
    Task<Wallpaper?> GetWallpaperByIdAsync(string id);
    
    /// <summary>
    /// Save a wallpaper
    /// </summary>
    /// <param name="wallpaper">The wallpaper to save</param>
    /// <returns>True if the wallpaper was saved successfully, false otherwise</returns>
    Task<bool> SaveWallpaperAsync(Wallpaper wallpaper);
    
    /// <summary>
    /// Delete a wallpaper
    /// </summary>
    /// <param name="id">The ID of the wallpaper to delete</param>
    /// <returns>True if the wallpaper was deleted successfully, false otherwise</returns>
    Task<bool> DeleteWallpaperAsync(string id);
    
    /// <summary>
    /// Update a wallpaper
    /// </summary>
    /// <param name="wallpaper">The wallpaper to update</param>
    /// <returns>True if the wallpaper was updated successfully, false otherwise</returns>
    Task<bool> UpdateWallpaperAsync(Wallpaper wallpaper);
    
    /// <summary>
    /// Get recent wallpapers
    /// </summary>
    /// <param name="count">The number of wallpapers to return</param>
    /// <returns>The most recently used wallpapers</returns>
    Task<IEnumerable<Wallpaper>> GetRecentWallpapersAsync(int count = 10);
    
    /// <summary>
    /// Get favorite wallpapers
    /// </summary>
    /// <returns>Wallpapers that are marked as favorites</returns>
    Task<IEnumerable<Wallpaper>> GetFavoriteWallpapersAsync();
    
    /// <summary>
    /// Toggle favorite status for a wallpaper
    /// </summary>
    /// <param name="id">The ID of the wallpaper</param>
    /// <returns>True if the favorite status was toggled successfully, false otherwise</returns>
    Task<bool> ToggleFavoriteAsync(string id);
    
    /// <summary>
    /// Download a wallpaper from Unsplash
    /// </summary>
    /// <param name="query">The search query</param>
    /// <returns>The downloaded wallpaper</returns>
    Task<Wallpaper> DownloadFromUnsplashAsync(string query);
    
    /// <summary>
    /// Download a wallpaper from Pexels
    /// </summary>
    /// <param name="query">The search query</param>
    /// <returns>The downloaded wallpaper</returns>
    Task<Wallpaper> DownloadFromPexelsAsync(string query);
    
    /// <summary>
    /// Download a wallpaper from Wallpaper Engine
    /// </summary>
    /// <param name="workshopUrl">The Steam Workshop URL</param>
    /// <returns>The downloaded wallpaper</returns>
    Task<Wallpaper> DownloadFromWallpaperEngineAsync(string workshopUrl);
    
    /// <summary>
    /// Generate an AI wallpaper
    /// </summary>
    /// <param name="prompt">The prompt for the AI</param>
    /// <returns>The generated wallpaper</returns>
    Task<Wallpaper> GenerateAiWallpaperAsync(string prompt);
    
    /// <summary>
    /// Apply a wallpaper
    /// Apply a wallpaper to the desktop
    /// </summary>
    Task<bool> ApplyWallpaperAsync(string id, string? monitorId = null);
    
    /// <summary>
    /// Apply a wallpaper to the lock screen
    /// </summary>
    Task<bool> ApplyToLockScreenAsync(string id);
    
    /// <summary>
    /// Import a local wallpaper file
    /// </summary>
    Task<Wallpaper> ImportLocalWallpaperAsync(string filePath);
} 