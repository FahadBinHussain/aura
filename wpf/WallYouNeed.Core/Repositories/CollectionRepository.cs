using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WallYouNeed.Core.Models;

namespace WallYouNeed.Core.Repositories
{
    public class CollectionRepository : ICollectionRepository
    {
        private readonly string _dataPath;
        private readonly ILogger<CollectionRepository> _logger;
        private List<Collection> _collections;
        private readonly object _lock = new object();

        public CollectionRepository(string dataPath, ILogger<CollectionRepository> logger)
        {
            _dataPath = dataPath;
            _logger = logger;
            _collections = new List<Collection>();
            
            // Ensure directory exists
            Directory.CreateDirectory(Path.GetDirectoryName(dataPath));
            
            // Load collections
            LoadCollections();
        }

        private void LoadCollections()
        {
            try
            {
                if (File.Exists(_dataPath))
                {
                    string json = File.ReadAllText(_dataPath);
                    _collections = JsonSerializer.Deserialize<List<Collection>>(json) ?? new List<Collection>();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading collections");
                _collections = new List<Collection>();
            }
        }

        private void SaveCollections()
        {
            try
            {
                string json = JsonSerializer.Serialize(_collections, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_dataPath, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving collections");
            }
        }

        public Task<List<Collection>> GetAllCollectionsAsync()
        {
            return Task.FromResult(_collections.ToList());
        }

        public Task<Collection> GetCollectionByIdAsync(string id)
        {
            return Task.FromResult(_collections.FirstOrDefault(c => c.Id == id));
        }

        public Task<Collection> GetCollectionByNameAsync(string name)
        {
            return Task.FromResult(_collections.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));
        }

        public Task<List<WallpaperModel>> GetWallpapersInCollectionAsync(string collectionId)
        {
            // This method requires a WallpaperRepository to get the actual wallpaper models
            // For now, we return an empty list
            return Task.FromResult(new List<WallpaperModel>());
        }

        public Task AddCollectionAsync(Collection collection)
        {
            lock (_lock)
            {
                if (_collections.Any(c => c.Id == collection.Id))
                {
                    throw new InvalidOperationException($"Collection with ID {collection.Id} already exists");
                }

                _collections.Add(collection);
                SaveCollections();
            }

            return Task.CompletedTask;
        }

        public Task UpdateCollectionAsync(Collection collection)
        {
            lock (_lock)
            {
                int index = _collections.FindIndex(c => c.Id == collection.Id);
                if (index == -1)
                {
                    throw new InvalidOperationException($"Collection with ID {collection.Id} not found");
                }

                _collections[index] = collection;
                SaveCollections();
            }

            return Task.CompletedTask;
        }

        public Task DeleteCollectionAsync(string id)
        {
            lock (_lock)
            {
                int index = _collections.FindIndex(c => c.Id == id);
                if (index == -1)
                {
                    throw new InvalidOperationException($"Collection with ID {id} not found");
                }

                _collections.RemoveAt(index);
                SaveCollections();
            }

            return Task.CompletedTask;
        }

        public Task AddWallpaperToCollectionAsync(string collectionId, string wallpaperId)
        {
            lock (_lock)
            {
                var collection = _collections.FirstOrDefault(c => c.Id == collectionId);
                if (collection == null)
                {
                    throw new InvalidOperationException($"Collection with ID {collectionId} not found");
                }

                if (!collection.WallpaperIds.Contains(wallpaperId))
                {
                    collection.WallpaperIds.Add(wallpaperId);
                    SaveCollections();
                }
            }

            return Task.CompletedTask;
        }

        public Task RemoveWallpaperFromCollectionAsync(string collectionId, string wallpaperId)
        {
            lock (_lock)
            {
                var collection = _collections.FirstOrDefault(c => c.Id == collectionId);
                if (collection == null)
                {
                    throw new InvalidOperationException($"Collection with ID {collectionId} not found");
                }

                if (collection.WallpaperIds.Contains(wallpaperId))
                {
                    collection.WallpaperIds.Remove(wallpaperId);
                    SaveCollections();
                }
            }

            return Task.CompletedTask;
        }
    }
} 