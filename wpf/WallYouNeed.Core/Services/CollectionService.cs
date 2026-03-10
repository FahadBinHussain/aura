using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WallYouNeed.Core.Models;
using WallYouNeed.Core.Repositories;
using WallYouNeed.Core.Services.Interfaces;

namespace WallYouNeed.Core.Services;

/// <summary>
/// Implementation of the collection service
/// </summary>
public class CollectionService : ICollectionService
{
    private readonly ILogger<CollectionService> _logger;
    private readonly ICollectionRepository _repository;
    private readonly IWallpaperRepository _wallpaperRepository;
    private readonly IWallpaperService _wallpaperService;

    public CollectionService(
        ILogger<CollectionService> logger,
        ICollectionRepository repository,
        IWallpaperRepository wallpaperRepository,
        IWallpaperService wallpaperService)
    {
        _logger = logger;
        _repository = repository;
        _wallpaperRepository = wallpaperRepository;
        _wallpaperService = wallpaperService;
    }

    /// <inheritdoc />
    public async Task<List<Collection>> GetAllCollectionsAsync()
    {
        try
        {
            _logger.LogInformation("Getting all collections");
            return await _repository.GetAllCollectionsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all collections");
            return new List<Collection>();
        }
    }

    /// <inheritdoc />
    public async Task<Collection> GetCollectionByIdAsync(string id)
    {
        try
        {
            _logger.LogInformation("Getting collection by ID: {Id}", id);
            var collection = await _repository.GetCollectionByIdAsync(id);
            
            if (collection == null)
            {
                throw new KeyNotFoundException($"Collection not found with ID: {id}");
            }
            
            return collection;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting collection by ID: {Id}", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Collection> CreateCollectionAsync(string name, string description = "")
    {
        try
        {
            _logger.LogInformation("Creating collection: {Name}", name);
            
            var collection = new Collection
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                Description = description,
                CreatedDate = DateTime.Now,
                WallpaperIds = new List<string>()
            };
            
            await _repository.AddCollectionAsync(collection);
            
            _logger.LogInformation("Collection created with ID: {Id}", collection.Id);
            
            return collection;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating collection: {Name}", name);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task UpdateCollectionAsync(Collection collection)
    {
        try
        {
            _logger.LogInformation("Updating collection: {Id}", collection.Id);
            
            var existing = await _repository.GetCollectionByIdAsync(collection.Id);
            if (existing == null)
            {
                throw new KeyNotFoundException($"Collection not found with ID: {collection.Id}");
            }
            
            collection.ModifiedDate = DateTime.Now;
            await _repository.UpdateCollectionAsync(collection);
            
            _logger.LogInformation("Collection updated: {Id}", collection.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating collection: {Id}", collection.Id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task DeleteCollectionAsync(string id)
    {
        try
        {
            _logger.LogInformation("Deleting collection: {Id}", id);
            
            var collection = await _repository.GetCollectionByIdAsync(id);
            if (collection == null)
            {
                throw new KeyNotFoundException($"Collection not found with ID: {id}");
            }
            
            await _repository.DeleteCollectionAsync(id);
            
            _logger.LogInformation("Collection deleted: {Id}", id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting collection: {Id}", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task AddWallpaperToCollectionAsync(string collectionId, string wallpaperId)
    {
        try
        {
            _logger.LogInformation("Adding wallpaper {WallpaperId} to collection {CollectionId}", wallpaperId, collectionId);
            
            var collection = await _repository.GetCollectionByIdAsync(collectionId);
            if (collection == null)
            {
                throw new KeyNotFoundException($"Collection not found with ID: {collectionId}");
            }
            
            var wallpaper = await _wallpaperService.GetWallpaperByIdAsync(wallpaperId);
            if (wallpaper == null)
            {
                throw new KeyNotFoundException($"Wallpaper not found with ID: {wallpaperId}");
            }
            
            if (collection.WallpaperIds.Contains(wallpaperId))
            {
                _logger.LogInformation("Wallpaper already in collection");
                return;
            }
            
            await _repository.AddWallpaperToCollectionAsync(collectionId, wallpaperId);
            
            _logger.LogInformation("Wallpaper added to collection");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding wallpaper {WallpaperId} to collection {CollectionId}", wallpaperId, collectionId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task RemoveWallpaperFromCollectionAsync(string collectionId, string wallpaperId)
    {
        try
        {
            _logger.LogInformation("Removing wallpaper {WallpaperId} from collection {CollectionId}", wallpaperId, collectionId);
            
            var collection = await _repository.GetCollectionByIdAsync(collectionId);
            if (collection == null)
            {
                throw new KeyNotFoundException($"Collection not found with ID: {collectionId}");
            }
            
            if (!collection.WallpaperIds.Contains(wallpaperId))
            {
                _logger.LogInformation("Wallpaper not in collection");
                return;
            }
            
            await _repository.RemoveWallpaperFromCollectionAsync(collectionId, wallpaperId);
            
            _logger.LogInformation("Wallpaper removed from collection");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing wallpaper {WallpaperId} from collection {CollectionId}", wallpaperId, collectionId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<List<Wallpaper>> GetWallpapersInCollectionAsync(string collectionId)
    {
        try
        {
            _logger.LogInformation("Getting wallpapers in collection: {Id}", collectionId);
            
            var collection = await _repository.GetCollectionByIdAsync(collectionId);
            if (collection == null)
            {
                throw new KeyNotFoundException($"Collection not found with ID: {collectionId}");
            }
            
            var wallpapers = new List<Wallpaper>();
            var wallpaperModels = await _repository.GetWallpapersInCollectionAsync(collectionId);
            
            // Convert WallpaperModel to Wallpaper
            foreach (var model in wallpaperModels)
            {
                var wallpaper = await _wallpaperService.GetWallpaperByIdAsync(model.Id);
                if (wallpaper != null)
                {
                    wallpapers.Add(wallpaper);
                }
            }
            
            return wallpapers;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting wallpapers in collection: {Id}", collectionId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Wallpaper?> GetWallpaperFromCollectionAsync(string collectionId, string wallpaperId)
    {
        try
        {
            _logger.LogInformation("Getting wallpaper {WallpaperId} from collection {CollectionId}", wallpaperId, collectionId);
            
            var collection = await _repository.GetCollectionByIdAsync(collectionId);
            if (collection == null)
            {
                throw new KeyNotFoundException($"Collection not found with ID: {collectionId}");
            }
            
            if (!collection.WallpaperIds.Contains(wallpaperId))
            {
                _logger.LogInformation("Wallpaper not in collection");
                return null;
            }
            
            return await _wallpaperService.GetWallpaperByIdAsync(wallpaperId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting wallpaper {WallpaperId} from collection {CollectionId}", wallpaperId, collectionId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> SetCollectionCoverAsync(string collectionId, string wallpaperId)
    {
        try
        {
            _logger.LogInformation("Setting collection cover: {CollectionId}, {WallpaperId}", collectionId, wallpaperId);
            
            var collection = await _repository.GetCollectionByIdAsync(collectionId);
            if (collection == null)
            {
                throw new KeyNotFoundException($"Collection not found with ID: {collectionId}");
            }
            
            var wallpaper = await _wallpaperService.GetWallpaperByIdAsync(wallpaperId);
            if (wallpaper == null)
            {
                throw new KeyNotFoundException($"Wallpaper not found with ID: {wallpaperId}");
            }
            
            if (!collection.WallpaperIds.Contains(wallpaperId))
            {
                throw new InvalidOperationException("Wallpaper not in collection");
            }
            
            // Get the wallpaper's local path to set as cover
            if (string.IsNullOrEmpty(wallpaper.FilePath))
            {
                _logger.LogWarning("Wallpaper has no local path");
                return false;
            }
            
            collection.CoverImagePath = wallpaper.FilePath;
            collection.ModifiedDate = DateTime.Now;
            
            await _repository.UpdateCollectionAsync(collection);
            
            _logger.LogInformation("Collection cover set: {CollectionId}", collectionId);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting collection cover: {CollectionId}, {WallpaperId}", collectionId, wallpaperId);
            throw;
        }
    }
} 