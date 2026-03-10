using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WallYouNeed.Core.Configuration;

namespace WallYouNeed.Core.Services
{
    public class WallpaperConfigurationService : IWallpaperConfigurationService
    {
        private readonly string _configPath;
        private readonly ILogger<WallpaperConfigurationService> _logger;
        private BackieeScraperConfig _backieeConfig;
        private readonly object _lock = new object();

        public WallpaperConfigurationService(string configPath, ILogger<WallpaperConfigurationService> logger)
        {
            _configPath = configPath;
            _logger = logger;
            
            // Ensure directory exists
            Directory.CreateDirectory(Path.GetDirectoryName(configPath));
            
            // Load or create default configuration
            _backieeConfig = LoadBackieeConfig();
        }

        private BackieeScraperConfig LoadBackieeConfig()
        {
            string backieeConfigPath = Path.Combine(_configPath, "backiee_config.json");
            
            try
            {
                if (File.Exists(backieeConfigPath))
                {
                    string json = File.ReadAllText(backieeConfigPath);
                    return JsonSerializer.Deserialize<BackieeScraperConfig>(json) ?? new BackieeScraperConfig();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading Backiee configuration");
            }
            
            // Return default configuration
            return new BackieeScraperConfig();
        }

        private async Task SaveBackieeConfigAsync(BackieeScraperConfig config)
        {
            string backieeConfigPath = Path.Combine(_configPath, "backiee_config.json");
            
            try
            {
                string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(backieeConfigPath, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving Backiee configuration");
                throw;
            }
        }

        public Task<BackieeScraperConfig> GetBackieeConfigAsync()
        {
            lock (_lock)
            {
                return Task.FromResult(_backieeConfig);
            }
        }

        public async Task UpdateBackieeConfigAsync(BackieeScraperConfig config)
        {
            lock (_lock)
            {
                _backieeConfig = config;
            }
            
            await SaveBackieeConfigAsync(config);
        }
    }
} 