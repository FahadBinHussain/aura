using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using WallYouNeed.Core.Models;
using WallYouNeed.Core.Repositories;
using WallYouNeed.Core.Services.Interfaces;
using WallYouNeed.Core.Utils;

namespace WallYouNeed.Core.Services;

/// <summary>
/// Implementation of the wallpaper service
/// </summary>
public class WallpaperService : IWallpaperService
{
    private readonly ILogger<WallpaperService> _logger;
    private readonly ISettingsService _settingsService;
    private readonly HttpClient _unsplashClient;
    private readonly HttpClient _pexelsClient;
    private readonly WindowsWallpaperUtil _wallpaperUtil;
    private readonly IWallpaperRepository _wallpaperRepository;
    private readonly ICollectionRepository _collectionRepository;
    private readonly IWallpaperConfigurationService _configurationService;

    public WallpaperService(
        ILogger<WallpaperService> logger,
        ISettingsService settingsService,
        IHttpClientFactory httpClientFactory,
        WindowsWallpaperUtil wallpaperUtil,
        IWallpaperRepository wallpaperRepository,
        ICollectionRepository collectionRepository,
        IWallpaperConfigurationService configurationService)
    {
        _logger = logger;
        _settingsService = settingsService;
        _unsplashClient = httpClientFactory.CreateClient("UnsplashApi");
        _pexelsClient = httpClientFactory.CreateClient("PexelsApi");
        _wallpaperUtil = wallpaperUtil;
        _wallpaperRepository = wallpaperRepository;
        _collectionRepository = collectionRepository;
        _configurationService = configurationService;
    }

    private void EnsureBackieeContentFileExists()
    {
        try
        {
            string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string targetPath = Path.Combine(appDirectory, "backiee_content.html");
            
            // Check if the file already exists in the app directory
            if (File.Exists(targetPath))
            {
                _logger.LogInformation("backiee_content.html file already exists at: {Path}", targetPath);
                return;
            }
            
            // Check for the file in the working directory
            string workingDirPath = Path.Combine(Directory.GetCurrentDirectory(), "backiee_content.html");
            if (File.Exists(workingDirPath))
            {
                _logger.LogInformation("Found backiee_content.html in working directory, copying to app directory");
                File.Copy(workingDirPath, targetPath, true);
                return;
            }
            
            // Check for the file relative to the executable
            var executablePath = Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
            string execPath = Path.Combine(executablePath, "backiee_content.html");
            if (File.Exists(execPath))
            {
                _logger.LogInformation("Found backiee_content.html relative to executable, copying to app directory");
                File.Copy(execPath, targetPath, true);
                return;
            }
            
            _logger.LogWarning("Could not find backiee_content.html file in any expected location");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ensuring backiee_content.html file exists");
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<Wallpaper>> GetAllWallpapersAsync()
    {
        try
        {
            _logger.LogInformation("Getting all wallpapers");
            var wallpaperModels = await _wallpaperRepository.GetAllWallpapersAsync();
            // Convert WallpaperModel to Wallpaper if needed
            return ConvertModelsToWallpapers(wallpaperModels);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all wallpapers");
            return new List<Wallpaper>();
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<Wallpaper>> GetWallpapersBySourceAsync(WallpaperSource source)
    {
        try
        {
            _logger.LogInformation("Getting wallpapers by source: {Source}", source);
            var wallpaperModels = await _wallpaperRepository.GetAllWallpapersAsync();
            // Filter by source and convert
            return ConvertModelsToWallpapers(wallpaperModels.Where(w => w.Source == source.ToString()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting wallpapers by source: {Source}", source);
            return new List<Wallpaper>();
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<Wallpaper>> GetWallpapersByTagAsync(string tag)
    {
        try
        {
            _logger.LogInformation("Getting wallpapers by tag: {Tag}", tag);
            // Implementation will vary based on how tags are stored
            var wallpaperModels = await _wallpaperRepository.GetAllWallpapersAsync();
            // This assumes tags are stored in the Category property
            return ConvertModelsToWallpapers(wallpaperModels.Where(w => w.Category == tag));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting wallpapers by tag: {Tag}", tag);
            return new List<Wallpaper>();
        }
    }

    /// <inheritdoc />
    public async Task<Wallpaper?> GetWallpaperByIdAsync(string id)
    {
        try
        {
            _logger.LogInformation("Getting wallpaper by ID: {Id}", id);
            var wallpaperModel = await _wallpaperRepository.GetWallpaperByIdAsync(id);
            if (wallpaperModel == null)
                return null;
            
            return ConvertModelToWallpaper(wallpaperModel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting wallpaper by ID: {Id}", id);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> SaveWallpaperAsync(Wallpaper wallpaper)
    {
        try
        {
            _logger.LogInformation("Saving wallpaper: {Id}", wallpaper.Id);
            
            // Convert Wallpaper to WallpaperModel
            var wallpaperModel = new WallpaperModel
            {
                Id = wallpaper.Id,
                Title = wallpaper.Name,
                Category = wallpaper.Tags.FirstOrDefault() ?? "",
                Source = wallpaper.Source.ToString(),
                SourceUrl = wallpaper.SourceUrl,
                LocalPath = wallpaper.FilePath,
                UploadDate = wallpaper.CreatedAt
            };
            
            await _wallpaperRepository.AddWallpaperAsync(wallpaperModel);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving wallpaper: {Id}", wallpaper.Id);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteWallpaperAsync(string id)
    {
        try
        {
            _logger.LogInformation("Deleting wallpaper: {Id}", id);
            
            // Get wallpaper to check if it has a local file that needs to be deleted
            var wallpaperModel = await _wallpaperRepository.GetWallpaperByIdAsync(id);
            if (wallpaperModel != null && !string.IsNullOrEmpty(wallpaperModel.LocalPath) && File.Exists(wallpaperModel.LocalPath))
            {
                // Delete the file
                File.Delete(wallpaperModel.LocalPath);
                _logger.LogInformation("Deleted wallpaper file: {FilePath}", wallpaperModel.LocalPath);
            }
            
            await _wallpaperRepository.DeleteWallpaperAsync(id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting wallpaper: {Id}", id);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateWallpaperAsync(Wallpaper wallpaper)
    {
        try
        {
            _logger.LogInformation("Updating wallpaper: {Id}", wallpaper.Id);
            
            // Get existing wallpaper
            var existingWallpaper = await _wallpaperRepository.GetWallpaperByIdAsync(wallpaper.Id);
            if (existingWallpaper == null)
                return false;
                
            // Update properties
            existingWallpaper.Title = wallpaper.Name;
            existingWallpaper.Category = wallpaper.Tags.FirstOrDefault() ?? "";
            existingWallpaper.LocalPath = wallpaper.FilePath;
            
            await _wallpaperRepository.UpdateWallpaperAsync(existingWallpaper);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating wallpaper: {Id}", wallpaper.Id);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<Wallpaper>> GetRecentWallpapersAsync(int count = 10)
    {
        try
        {
            _logger.LogInformation("Getting {Count} recent wallpapers", count);
            var wallpapers = await _wallpaperRepository.GetAllWallpapersAsync();
            // Sort by upload date and take specified count
            return ConvertModelsToWallpapers(wallpapers.OrderByDescending(w => w.UploadDate).Take(count));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting recent wallpapers");
            return new List<Wallpaper>();
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<Wallpaper>> GetFavoriteWallpapersAsync()
    {
        try
        {
            _logger.LogInformation("Getting favorite wallpapers");
            // Since we don't have a specific method for favorites in IWallpaperRepository,
            // we can filter by rating or any other property that indicates favorites
            var wallpapers = await _wallpaperRepository.GetAllWallpapersAsync();
            return ConvertModelsToWallpapers(wallpapers.Where(w => w.Rating >= 4)); // Assuming rating >= 4 means favorite
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting favorite wallpapers");
            return new List<Wallpaper>();
        }
    }

    /// <inheritdoc />
    public async Task<bool> ToggleFavoriteAsync(string id)
    {
        try
        {
            _logger.LogInformation("Toggling favorite for wallpaper: {Id}", id);
            
            var wallpaper = await _wallpaperRepository.GetWallpaperByIdAsync(id);
            if (wallpaper == null)
                return false;
                
            // Toggle favorite status (using Rating as a proxy for favorite)
            wallpaper.Rating = wallpaper.Rating >= 4 ? 0 : 5;
            
            await _wallpaperRepository.UpdateWallpaperAsync(wallpaper);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error toggling favorite for wallpaper: {Id}", id);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<Wallpaper> DownloadFromUnsplashAsync(string query)
    {
        try
        {
            _logger.LogInformation("Downloading wallpaper from Unsplash with query: {Query}", query);
            var settings = await _settingsService.LoadSettingsAsync();
            
            // In a real app, we would use the Unsplash API to search for images
            // For this demo, we'll create a dummy wallpaper
            var wallpaper = new Wallpaper
            {
                Id = Guid.NewGuid().ToString(),
                Title = $"Unsplash Wallpaper - {query}",
                Name = $"Unsplash Wallpaper - {query}",
                Description = $"Downloaded from Unsplash with query: {query}",
                Source = WallpaperSource.Unsplash,
                Author = "Unsplash Author",
                SourceUrl = "https://unsplash.com",
                Tags = new List<string> { query },
                CreatedAt = DateTime.Now,
                LastUsedAt = DateTime.Now
            };
            
            // Create storage directory if it doesn't exist
            var storageDir = settings.StorageLocation;
            if (string.IsNullOrEmpty(storageDir))
            {
                storageDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WallYouNeed", 
                    "Wallpapers");
            }
            
            Directory.CreateDirectory(storageDir);
            
            // In a real app, we would download the image here
            // For this demo, we'll just create a dummy file path
            wallpaper.FilePath = Path.Combine(storageDir, $"{wallpaper.Id}.jpg");
            
            // Save the wallpaper to the repository
            await SaveWallpaperAsync(wallpaper);
            
            return wallpaper;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading wallpaper from Unsplash");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<Wallpaper> DownloadFromPexelsAsync(string query)
    {
        try
        {
            _logger.LogInformation("Downloading wallpaper from Pexels with query: {Query}", query);
            var settings = await _settingsService.LoadSettingsAsync();
            
            // In a real app, we would use the Pexels API to search for images
            // For this demo, we'll create a dummy wallpaper
            var wallpaper = new Wallpaper
            {
                Id = Guid.NewGuid().ToString(),
                Title = $"Pexels Wallpaper - {query}",
                Name = $"Pexels Wallpaper - {query}",
                Description = $"Downloaded from Pexels with query: {query}",
                Source = WallpaperSource.Pexels,
                Author = "Pexels Author",
                SourceUrl = "https://pexels.com",
                Tags = new List<string> { query },
                CreatedAt = DateTime.Now,
                LastUsedAt = DateTime.Now
            };
            
            // Create storage directory if it doesn't exist
            var storageDir = settings.StorageLocation;
            if (string.IsNullOrEmpty(storageDir))
            {
                storageDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WallYouNeed", 
                    "Wallpapers");
            }
            
            Directory.CreateDirectory(storageDir);
            
            // In a real app, we would download the image here
            // For this demo, we'll just create a dummy file path
            wallpaper.FilePath = Path.Combine(storageDir, $"{wallpaper.Id}.jpg");
            
            // Save the wallpaper to the repository
            await SaveWallpaperAsync(wallpaper);
            
            return wallpaper;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading wallpaper from Pexels");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<Wallpaper> DownloadFromWallpaperEngineAsync(string workshopUrl)
    {
        // This is a placeholder implementation. The actual implementation would use
        // the DepotDownloader library to download from Steam Workshop
        throw new NotImplementedException("Wallpaper Engine download not implemented yet");
    }

    /// <inheritdoc />
    public async Task<Wallpaper> GenerateAiWallpaperAsync(string prompt)
    {
        // This is a placeholder implementation. The actual implementation would use
        // a service like Stable Diffusion API to generate images
        throw new NotImplementedException("AI wallpaper generation not implemented yet");
    }

    /// <inheritdoc />
    public async Task<bool> ApplyWallpaperAsync(string id, string? monitorId = null)
    {
        try
        {
            _logger.LogInformation("Applying wallpaper: {Id}", id);
            var wallpaperModel = await _wallpaperRepository.GetWallpaperByIdAsync(id);
            
            if (wallpaperModel == null)
            {
                _logger.LogWarning("Wallpaper not found: {Id}", id);
                return false;
            }
            
            if (string.IsNullOrEmpty(wallpaperModel.LocalPath) || !File.Exists(wallpaperModel.LocalPath))
            {
                _logger.LogWarning("Wallpaper file does not exist: {FilePath}", wallpaperModel.LocalPath);
                return false;
            }
            
            // Apply the wallpaper
            bool success;
            if (string.IsNullOrEmpty(monitorId))
            {
                // Apply to all monitors
                success = _wallpaperUtil.SetWallpaper(wallpaperModel.LocalPath);
            }
            else
            {
                // Apply to specific monitor
                success = _wallpaperUtil.SetWallpaperForMonitor(wallpaperModel.LocalPath, monitorId);
            }
            
            if (success)
            {
                // Update the wallpaper's LastUsedAt time (track this in our model)
                wallpaperModel.UploadDate = DateTime.Now; // We're using UploadDate for LastUsedAt
                await _wallpaperRepository.UpdateWallpaperAsync(wallpaperModel);
                
                // Update current wallpaper ID in settings
                var settings = await _settingsService.LoadSettingsAsync();
                settings.CurrentWallpaperId = id;
                await _settingsService.SaveSettingsAsync(settings);
                
                _logger.LogInformation("Successfully applied wallpaper: {Id}", id);
            }
            else
            {
                _logger.LogWarning("Failed to apply wallpaper: {Id}", id);
            }
            
            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying wallpaper: {Id}", id);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> ApplyToLockScreenAsync(string id)
    {
        try
        {
            _logger.LogInformation("Applying wallpaper to lock screen: {Id}", id);
            var wallpaperModel = await _wallpaperRepository.GetWallpaperByIdAsync(id);
            
            if (wallpaperModel == null)
            {
                _logger.LogWarning("Wallpaper not found: {Id}", id);
                return false;
            }
            
            if (string.IsNullOrEmpty(wallpaperModel.LocalPath) || !File.Exists(wallpaperModel.LocalPath))
            {
                _logger.LogWarning("Wallpaper file does not exist: {FilePath}", wallpaperModel.LocalPath);
                return false;
            }
            
            // Apply the wallpaper to the lock screen
            var success = _wallpaperUtil.SetLockScreenWallpaper(wallpaperModel.LocalPath);
            
            if (success)
            {
                // Update the wallpaper's LastUsedAt time
                wallpaperModel.UploadDate = DateTime.Now; // We're using UploadDate for LastUsedAt
                await _wallpaperRepository.UpdateWallpaperAsync(wallpaperModel);
                
                // Update lock screen wallpaper ID in settings
                var settings = await _settingsService.LoadSettingsAsync();
                settings.LockScreenWallpaperId = id;
                await _settingsService.SaveSettingsAsync(settings);
                
                _logger.LogInformation("Successfully applied wallpaper to lock screen: {Id}", id);
            }
            else
            {
                _logger.LogWarning("Failed to apply wallpaper to lock screen: {Id}", id);
            }
            
            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying wallpaper to lock screen: {Id}", id);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<Wallpaper> ImportLocalWallpaperAsync(string filePath)
    {
        try
        {
            _logger.LogInformation("Importing local wallpaper: {FilePath}", filePath);
            
            if (!File.Exists(filePath))
            {
                _logger.LogWarning("File does not exist: {FilePath}", filePath);
                return null;
            }
            
            var settings = await _settingsService.LoadSettingsAsync();
            var fileName = Path.GetFileName(filePath);
            
            // Create a new wallpaper
            var wallpaper = new Wallpaper
            {
                Id = Guid.NewGuid().ToString(),
                Title = Path.GetFileNameWithoutExtension(fileName),
                Name = Path.GetFileNameWithoutExtension(fileName),
                Source = WallpaperSource.Local,
                CreatedAt = DateTime.Now,
                LastUsedAt = DateTime.Now
            };
            
            // Create storage directory if it doesn't exist
            var storageDir = settings.StorageLocation;
            if (string.IsNullOrEmpty(storageDir))
            {
                storageDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WallYouNeed", 
                    "Wallpapers");
            }
            
            Directory.CreateDirectory(storageDir);
            
            // Copy the file to the storage directory
            var destPath = Path.Combine(storageDir, $"{wallpaper.Id}{Path.GetExtension(fileName)}");
            File.Copy(filePath, destPath);
            
            // Update wallpaper with file path
            wallpaper.FilePath = destPath;
            
            // Save the wallpaper to the repository
            await SaveWallpaperAsync(wallpaper);
            
            return wallpaper;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error importing local wallpaper: {FilePath}", filePath);
            return null;
        }
    }

    // Helper methods for converting between types
    private Wallpaper ConvertModelToWallpaper(WallpaperModel model)
    {
        return new Wallpaper
        {
            Id = model.Id,
            Name = model.Title,
            Title = model.Title,
            Source = Enum.TryParse<WallpaperSource>(model.Source, out var source) ? source : WallpaperSource.Local,
            SourceUrl = model.SourceUrl,
            FilePath = model.LocalPath,
            CreatedAt = model.UploadDate,
            Tags = new List<string> { model.Category },
            // Add other properties as needed
        };
    }
    
    private List<Wallpaper> ConvertModelsToWallpapers(IEnumerable<WallpaperModel> models)
    {
        return models.Select(ConvertModelToWallpaper).ToList();
    }
}

// API response models for third-party services

public class UnsplashPhotoResponse
{
    public string Id { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? AltDescription { get; set; }
    public UnsplashUrls Urls { get; set; } = new();
    public UnsplashLinks Links { get; set; } = new();
    public UnsplashUser User { get; set; } = new();
    public int Width { get; set; }
    public int Height { get; set; }
}

public class UnsplashUrls
{
    public string Raw { get; set; } = string.Empty;
    public string Full { get; set; } = string.Empty;
    public string Regular { get; set; } = string.Empty;
    public string Small { get; set; } = string.Empty;
    public string Thumb { get; set; } = string.Empty;
}

public class UnsplashLinks
{
    public string Self { get; set; } = string.Empty;
    public string Html { get; set; } = string.Empty;
    public string Download { get; set; } = string.Empty;
}

public class UnsplashUser
{
    public string Id { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public class PexelsSearchResponse
{
    public int TotalResults { get; set; }
    public int Page { get; set; }
    public int PerPage { get; set; }
    public List<PexelsPhoto> Photos { get; set; } = new();
    public string NextPage { get; set; } = string.Empty;
}

public class PexelsPhoto
{
    public int Id { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string Url { get; set; } = string.Empty;
    public string Photographer { get; set; } = string.Empty;
    public string PhotographerUrl { get; set; } = string.Empty;
    public string? Alt { get; set; }
    public PexelsPhotoSources Src { get; set; } = new();
}

public class PexelsPhotoSources
{
    public string Original { get; set; } = string.Empty;
    public string Large { get; set; } = string.Empty;
    public string Medium { get; set; } = string.Empty;
    public string Small { get; set; } = string.Empty;
    public string Portrait { get; set; } = string.Empty;
    public string Landscape { get; set; } = string.Empty;
    public string Tiny { get; set; } = string.Empty;
} 