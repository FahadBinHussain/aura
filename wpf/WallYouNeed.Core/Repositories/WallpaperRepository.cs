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
    public class WallpaperRepository : IWallpaperRepository
    {
        private readonly string _dataPath;
        private readonly ILogger<WallpaperRepository> _logger;
        private List<WallpaperModel> _wallpapers;
        private readonly object _lock = new object();

        public WallpaperRepository(string dataPath, ILogger<WallpaperRepository> logger)
        {
            _dataPath = dataPath;
            _logger = logger;
            _wallpapers = new List<WallpaperModel>();
            
            // Fix directory creation logic
            try 
            {
                // Get the directory path without filename
                var wallpaperDir = Path.GetDirectoryName(_dataPath);
                
                if (wallpaperDir != null)
                {
                    Directory.CreateDirectory(wallpaperDir);
                    _logger.LogInformation("Created wallpaper storage directory: {Dir}", wallpaperDir);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating wallpaper storage directory");
            }
            
            // Load wallpapers
            LoadWallpapers();
        }

        private void LoadWallpapers()
        {
            try
            {
                if (File.Exists(_dataPath))
                {
                    string json = File.ReadAllText(_dataPath);
                    _wallpapers = JsonSerializer.Deserialize<List<WallpaperModel>>(json) ?? new List<WallpaperModel>();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading wallpapers");
                _wallpapers = new List<WallpaperModel>();
            }
        }

        private void SaveWallpapers()
        {
            try
            {
                string json = JsonSerializer.Serialize(_wallpapers, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_dataPath, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving wallpapers");
            }
        }

        public Task<List<WallpaperModel>> GetAllWallpapersAsync()
        {
            return Task.FromResult(_wallpapers.ToList());
        }

        public Task<WallpaperModel?> GetWallpaperByIdAsync(string id)
        {
            return Task.FromResult(_wallpapers.FirstOrDefault(w => w.Id == id));
        }

        public Task<List<WallpaperModel>> GetWallpapersByCategoryAsync(string category)
        {
            return Task.FromResult(_wallpapers.Where(w => w.Category == category).ToList());
        }

        public Task<List<WallpaperModel>> GetWallpapersByResolutionCategoryAsync(string resolutionCategory)
        {
            return Task.FromResult(_wallpapers.Where(w => w.ResolutionCategory == resolutionCategory).ToList());
        }

        public Task<WallpaperModel?> GetWallpapersBySourceUrlAsync(string sourceUrl)
        {
            return Task.FromResult(_wallpapers.FirstOrDefault(w => w.SourceUrl == sourceUrl));
        }

        public Task AddWallpaperAsync(WallpaperModel wallpaper)
        {
            lock (_lock)
            {
                if (_wallpapers.Any(w => w.Id == wallpaper.Id))
                {
                    throw new InvalidOperationException($"Wallpaper with ID {wallpaper.Id} already exists");
                }

                _wallpapers.Add(wallpaper);
                SaveWallpapers();
            }

            return Task.CompletedTask;
        }

        public Task UpdateWallpaperAsync(WallpaperModel wallpaper)
        {
            lock (_lock)
            {
                int index = _wallpapers.FindIndex(w => w.Id == wallpaper.Id);
                if (index == -1)
                {
                    throw new InvalidOperationException($"Wallpaper with ID {wallpaper.Id} not found");
                }

                _wallpapers[index] = wallpaper;
                SaveWallpapers();
            }

            return Task.CompletedTask;
        }

        public Task DeleteWallpaperAsync(string id)
        {
            lock (_lock)
            {
                int index = _wallpapers.FindIndex(w => w.Id == id);
                if (index == -1)
                {
                    throw new InvalidOperationException($"Wallpaper with ID {id} not found");
                }

                _wallpapers.RemoveAt(index);
                SaveWallpapers();
            }

            return Task.CompletedTask;
        }
    }
} 