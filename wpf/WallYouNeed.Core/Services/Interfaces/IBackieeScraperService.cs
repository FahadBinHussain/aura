using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WallYouNeed.Core.Models;

namespace WallYouNeed.Core.Services.Interfaces
{
    public interface IBackieeScraperService
    {
        /// <summary>
        /// Event that fires when new wallpapers are added
        /// </summary>
        event EventHandler<List<WallpaperModel>> NewWallpapersAdded;
        
        /// <summary>
        /// Starts the timer to periodically scrape wallpapers
        /// </summary>
        Task StartPeriodicUpdates();
        
        /// <summary>
        /// Stops the periodic updates
        /// </summary>
        void StopPeriodicUpdates();
        
        /// <summary>
        /// Scrapes the latest wallpapers from the homepage
        /// </summary>
        Task<List<WallpaperModel>> ScrapeLatestWallpapers();
        
        /// <summary>
        /// Scrapes wallpapers for a specific category
        /// </summary>
        /// <param name="category">The category to scrape</param>
        /// <param name="maxPages">Maximum number of pages to scrape</param>
        Task<List<WallpaperModel>> ScrapeWallpapersByCategory(string category, int maxPages = 3);
        
        /// <summary>
        /// Extracts wallpaper information from backiee_content.html
        /// </summary>
        /// <param name="htmlContent">The HTML content to extract wallpapers from</param>
        /// <returns>A list of wallpaper models extracted from the HTML content</returns>
        Task<List<WallpaperModel>> ExtractWallpapersFromContentHtml(string htmlContent);
        
        /// <summary>
        /// Extracts wallpapers from the local backiee_content.html file
        /// </summary>
        /// <returns>A list of extracted wallpapers</returns>
        Task<List<WallpaperModel>> ExtractWallpapersFromLocalFile();
        
        /// <summary>
        /// Gets hardcoded wallpapers directly from specified URLs
        /// </summary>
        /// <returns>List of wallpaper models from hardcoded URLs</returns>
        Task<List<WallpaperModel>> GetHardcodedWallpapers();
        
        /// <summary>
        /// Generates placeholder wallpaper models with backiee-specific formatting
        /// </summary>
        /// <param name="count">Number of placeholders to generate</param>
        /// <returns>List of placeholder wallpaper models</returns>
        List<WallpaperModel> GeneratePlaceholderWallpapers(int count = 10);
    }
} 