using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WallYouNeed.Core.Models;
using WallYouNeed.Core.Services.Interfaces;

namespace WallYouNeed.Core
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // Setup services
            var services = new ServiceCollection();
            
            // Add logging
            services.AddLogging(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Debug);
            });
            
            // Add core services
            string dataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WallYouNeed");
                
            services.AddWallYouNeedCore(dataPath);
            
            // Build service provider
            var serviceProvider = services.BuildServiceProvider();
            
            // Get the BackieeScraperService
            var backieeScraper = serviceProvider.GetRequiredService<IBackieeScraperService>();
            
            Console.WriteLine("Testing Backiee Scraper Service");
            Console.WriteLine("------------------------------");
            
            try
            {
                // Scrape latest wallpapers
                Console.WriteLine("Scraping latest wallpapers...");
                var latestWallpapers = await backieeScraper.ScrapeLatestWallpapers();
                
                Console.WriteLine($"Found {latestWallpapers.Count} latest wallpapers:");
                foreach (var wallpaper in latestWallpapers)
                {
                    Console.WriteLine($"- {wallpaper.Title} ({wallpaper.ResolutionCategory})");
                }
                
                Console.WriteLine();
                
                // Scrape a specific category
                string category = "nature";
                Console.WriteLine($"Scraping wallpapers for category '{category}'...");
                var categoryWallpapers = await backieeScraper.ScrapeWallpapersByCategory(category, 1);
                
                Console.WriteLine($"Found {categoryWallpapers.Count} wallpapers in category '{category}':");
                foreach (var wallpaper in categoryWallpapers)
                {
                    Console.WriteLine($"- {wallpaper.Title} ({wallpaper.ResolutionCategory})");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
            
            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }
    }
} 