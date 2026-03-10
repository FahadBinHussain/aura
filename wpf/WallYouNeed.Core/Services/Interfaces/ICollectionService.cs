using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WallYouNeed.Core.Models;

namespace WallYouNeed.Core.Services.Interfaces;

/// <summary>
/// Interface for managing wallpaper collections
/// </summary>
public interface ICollectionService
{
    /// <summary>
    /// Get all collections
    /// </summary>
    Task<List<Collection>> GetAllCollectionsAsync();
    
    /// <summary>
    /// Get a collection by ID
    /// </summary>
    Task<Collection> GetCollectionByIdAsync(string id);
    
    /// <summary>
    /// Create a new collection
    /// </summary>
    Task<Collection> CreateCollectionAsync(string name, string description = "");
    
    /// <summary>
    /// Update an existing collection
    /// </summary>
    Task UpdateCollectionAsync(Collection collection);
    
    /// <summary>
    /// Delete a collection
    /// </summary>
    Task DeleteCollectionAsync(string id);
    
    /// <summary>
    /// Add a wallpaper to a collection
    /// </summary>
    Task AddWallpaperToCollectionAsync(string collectionId, string wallpaperId);
    
    /// <summary>
    /// Remove a wallpaper from a collection
    /// </summary>
    Task RemoveWallpaperFromCollectionAsync(string collectionId, string wallpaperId);
    
    /// <summary>
    /// Get all wallpapers in a collection
    /// </summary>
    Task<List<Wallpaper>> GetWallpapersInCollectionAsync(string collectionId);
    
    /// <summary>
    /// Get a specific wallpaper from a collection
    /// </summary>
    Task<Wallpaper?> GetWallpaperFromCollectionAsync(string collectionId, string wallpaperId);
    
    /// <summary>
    /// Set a wallpaper as the cover image for a collection
    /// </summary>
    Task<bool> SetCollectionCoverAsync(string collectionId, string wallpaperId);
} 