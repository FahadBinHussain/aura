using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WallYouNeed.Core.Models;
using WallYouNeed.Core.Services.Interfaces;

namespace WallYouNeed.Core.Services;

/// <summary>
/// Background service for automatic wallpaper rotation
/// </summary>
public class WallpaperRotationService : BackgroundService, IWallpaperRotationService
{
    private readonly ILogger<WallpaperRotationService> _logger;
    private readonly ISettingsService _settingsService;
    private readonly IWallpaperService _wallpaperService;
    private readonly Random _random = new Random();
    private Timer? _timer;

    public WallpaperRotationService(
        ILogger<WallpaperRotationService> logger,
        ISettingsService settingsService,
        IWallpaperService wallpaperService)
    {
        try
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _wallpaperService = wallpaperService ?? throw new ArgumentNullException(nameof(wallpaperService));
            
            _logger.LogInformation("WallpaperRotationService constructed successfully at: {Time}", DateTime.Now);
        }
        catch (Exception ex)
        {
            // Fallback to console logging if logger fails
            Console.WriteLine($"CRITICAL ERROR in WallpaperRotationService constructor: {ex}");
            throw; // Re-throw to ensure the service fails to start properly
        }
    }

    /// <inheritdoc />
    public async Task<bool> ApplyRandomWallpaperAsync()
    {
        try
        {
            _logger.LogInformation("Applying random wallpaper");

            var settings = await _settingsService.LoadSettingsAsync();
            
            // Get available wallpapers based on settings
            List<Wallpaper> wallpapers;
            try
            {
                wallpapers = await GetWallpapersBasedOnSettings(settings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting wallpapers for random selection");
                return false;
            }
            
            if (wallpapers.Count == 0)
            {
                _logger.LogWarning("No wallpapers available for random selection");
                return false;
            }

            // Select a random wallpaper
            Wallpaper selectedWallpaper;
            try
            {
                var randomIndex = _random.Next(wallpapers.Count);
                selectedWallpaper = wallpapers[randomIndex];
                
                if (string.IsNullOrEmpty(selectedWallpaper.FilePath) || !File.Exists(selectedWallpaper.FilePath))
                {
                    _logger.LogWarning("Selected wallpaper file does not exist: {FilePath}", selectedWallpaper.FilePath);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error selecting random wallpaper");
                return false;
            }
            
            // Apply to all monitors (single wallpaper)
            bool success;
            try
            {
                success = await _wallpaperService.ApplyWallpaperAsync(selectedWallpaper.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ApplyWallpaperAsync call: {Id}", selectedWallpaper.Id);
                return false;
            }
            
            if (success)
            {
                _logger.LogInformation("Applied random wallpaper '{Title}' (ID: {Id}) successfully", 
                    selectedWallpaper.Title, selectedWallpaper.Id);
            }
            else
            {
                _logger.LogWarning("Failed to apply random wallpaper '{Title}' (ID: {Id})",
                    selectedWallpaper.Title, selectedWallpaper.Id);
            }
            
            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying random wallpaper");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task StartRotationAsync(int intervalMinutes)
    {
        _logger.LogInformation("Starting wallpaper rotation with interval: {Interval} minutes", intervalMinutes);
        
        var settings = await _settingsService.LoadSettingsAsync();
        settings.AutoChangeWallpaper = true;
        settings.RotationIntervalMinutes = intervalMinutes;
        await _settingsService.SaveSettingsAsync(settings);
        
        StartRotationTimer(intervalMinutes);
    }

    /// <inheritdoc />
    public async Task StopRotationAsync()
    {
        _logger.LogInformation("Stopping wallpaper rotation");
        
        var settings = await _settingsService.LoadSettingsAsync();
        settings.AutoChangeWallpaper = false;
        await _settingsService.SaveSettingsAsync(settings);
        
        StopRotationTimer();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("WallpaperRotationService starting at: {Time}", DateTime.Now);
        
        try
        {
            // Initialize settings
            var settings = await _settingsService.LoadSettingsAsync();
            
            // Subscribe to settings changes
            _settingsService.SettingsChanged += OnSettingsChanged;
            
            // Start the timer if enabled
            if (settings.AutoChangeWallpaper)
            {
                StartRotationTimer(settings.RotationIntervalMinutes);
            }
            
            // Wait for cancellation
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in WallpaperRotationService");
        }
        finally
        {
            _timer?.Dispose();
            _logger.LogInformation("WallpaperRotationService stopped at: {Time}", DateTime.Now);
        }
    }

    private void OnSettingsChanged(object? sender, AppSettings newSettings)
    {
        if (newSettings.AutoChangeWallpaper)
        {
            _logger.LogInformation("Auto-change wallpaper enabled with interval: {Interval} minutes", 
                newSettings.RotationIntervalMinutes);
            
            StartRotationTimer(newSettings.RotationIntervalMinutes);
        }
        else
        {
            _logger.LogInformation("Auto-change wallpaper disabled");
            StopRotationTimer();
        }
    }

    private void StartRotationTimer(int intervalMinutes)
    {
        // Stop existing timer if any
        StopRotationTimer();

        // Create a new timer with the specified interval
        var intervalMs = intervalMinutes * 60 * 1000;
        _timer = new Timer(RotateWallpaper, null, 0, intervalMs);

        _logger.LogInformation("Wallpaper rotation timer started with interval: {Interval} minutes", intervalMinutes);
    }

    private void StopRotationTimer()
    {
        if (_timer != null)
        {
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            _timer.Dispose();
            _timer = null;
            _logger.LogInformation("Wallpaper rotation timer stopped");
        }
    }

    private async void RotateWallpaper(object? state)
    {
        try
        {
            _logger.LogInformation("Rotating wallpaper automatically");

            var settings = await _settingsService.LoadSettingsAsync();
            if (!settings.AutoChangeWallpaper)
            {
                _logger.LogWarning("Auto-change disabled but timer fired - stopping timer");
                StopRotationTimer();
                return;
            }

            await ApplyRandomWallpaperAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rotating wallpaper automatically");
        }
    }

    private async Task<List<Wallpaper>> GetWallpapersBasedOnSettings(AppSettings settings)
    {
        var wallpapers = new List<Wallpaper>();
        
        // Filter by enabled sources
        if (settings.UseLocalWallpapers)
        {
            var localWallpapers = await _wallpaperService.GetWallpapersBySourceAsync(WallpaperSource.Local);
            wallpapers.AddRange(localWallpapers);
        }
        
        if (settings.UseUnsplash)
        {
            var unsplashWallpapers = await _wallpaperService.GetWallpapersBySourceAsync(WallpaperSource.Unsplash);
            wallpapers.AddRange(unsplashWallpapers);
        }
        
        if (settings.UsePexels)
        {
            var pexelsWallpapers = await _wallpaperService.GetWallpapersBySourceAsync(WallpaperSource.Pexels);
            wallpapers.AddRange(pexelsWallpapers);
        }

        // Filter by tags if needed
        if (settings.IncludedTags.Count > 0)
        {
            wallpapers = wallpapers.Where(w => 
                w.Tags != null && 
                w.Tags.Any(t => settings.IncludedTags.Contains(t))).ToList();
        }
        
        if (settings.ExcludedTags.Count > 0)
        {
            wallpapers = wallpapers.Where(w => 
                w.Tags == null || 
                !w.Tags.Any(t => settings.ExcludedTags.Contains(t))).ToList();
        }

        _logger.LogInformation("Found {Count} wallpapers available for rotation", wallpapers.Count);
        
        return wallpapers;
    }

    public override async Task StopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Wallpaper Rotation Service is stopping");
        
        _settingsService.SettingsChanged -= OnSettingsChanged;
        StopRotationTimer();
        
        await base.StopAsync(stoppingToken);
    }
} 