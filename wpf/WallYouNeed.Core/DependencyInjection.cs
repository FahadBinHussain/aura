using System;
using System.IO;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WallYouNeed.Core.Configuration;
using WallYouNeed.Core.Repositories;
using WallYouNeed.Core.Services;
using WallYouNeed.Core.Services.Interfaces;
using WallYouNeed.Core.Utils;

namespace WallYouNeed.Core
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddWallYouNeedCore(this IServiceCollection services, string dataPath)
        {
            // Ensure data directory exists
            Directory.CreateDirectory(dataPath);
            
            // Register repositories
            services.AddSingleton<IWallpaperRepository>(provider => 
                new WallpaperRepository(
                    Path.Combine(dataPath, "wallpapers.json"),
                    provider.GetRequiredService<ILogger<WallpaperRepository>>()));
                
            services.AddSingleton<ICollectionRepository>(provider => 
                new CollectionRepository(
                    Path.Combine(dataPath, "collections.json"),
                    provider.GetRequiredService<ILogger<CollectionRepository>>()));
            
            // Register configuration service
            services.AddSingleton<IWallpaperConfigurationService>(provider => 
                new WallpaperConfigurationService(
                    dataPath,
                    provider.GetRequiredService<ILogger<WallpaperConfigurationService>>()));
            
            // Register HTTP clients
            services.AddHttpClient("Backiee", client =>
            {
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
            });
            
            services.AddHttpClient("UnsplashApi", client =>
            {
                client.BaseAddress = new Uri("https://api.unsplash.com/");
                // You would add your API key here in a real application
                client.DefaultRequestHeaders.Add("Authorization", "Client-ID YOUR_UNSPLASH_API_KEY");
            });
            
            services.AddHttpClient("PexelsApi", client =>
            {
                client.BaseAddress = new Uri("https://api.pexels.com/v1/");
                // You would add your API key here in a real application
                client.DefaultRequestHeaders.Add("Authorization", "YOUR_PEXELS_API_KEY");
            });
            
            // Register utility services
            services.AddTransient<HtmlDownloader>();
            
            // Register other services
            services.AddSingleton<ISettingsService, SettingsService>();
            services.AddSingleton<IWallpaperService, WallpaperService>();
            services.AddSingleton<WindowsWallpaperUtil>();
            
            return services;
        }
    }
} 