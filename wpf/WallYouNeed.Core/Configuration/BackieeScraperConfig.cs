using System;
using System.Collections.Generic;

namespace WallYouNeed.Core.Configuration
{
    public class BackieeScraperConfig
    {
        /// <summary>
        /// The base URL for Backiee.com
        /// </summary>
        public string BaseUrl { get; set; } = "https://www.backiee.com";

        /// <summary>
        /// Alternative URLs to try if the main URL doesn't work
        /// </summary>
        public List<string> AlternativeUrls { get; set; } = new List<string>
        {
            "https://backiee.com",
            "https://www.wallpaperbackiee.com",
            "https://backiee.com/wallpapers",
            "https://backiee.com/latest",
            "https://www.backiee.com/wallpapers"
        };

        /// <summary>
        /// The interval in milliseconds between scraping operations (default: 1 hour)
        /// </summary>
        public int ScrapingInterval { get; set; } = 3600000;

        /// <summary>
        /// Maximum number of pages to scrape per category
        /// </summary>
        public int MaxPagesPerCategory { get; set; } = 3;

        /// <summary>
        /// Maximum number of concurrent requests
        /// </summary>
        public int MaxConcurrentRequests { get; set; } = 2;

        /// <summary>
        /// Delay between requests in milliseconds to avoid rate limiting
        /// </summary>
        public int RequestDelayMs { get; set; } = 1000;

        /// <summary>
        /// Whether to automatically start scraping on initialization
        /// </summary>
        public bool AutoStartScraping { get; set; } = true;

        /// <summary>
        /// Categories to scrape
        /// </summary>
        public string[] CategoriesToScrape { get; set; } = new string[]
        {
            "abstract",
            "animals",
            "anime",
            "cars",
            "city",
            "fantasy",
            "flowers",
            "food",
            "holidays",
            "landscape",
            "minimalistic",
            "motorcycles",
            "movies",
            "nature",
            "space",
            "sport"
        };
        
        /// <summary>
        /// Maximum consecutive failures before enabling circuit breaker
        /// </summary>
        public int MaxConsecutiveFailures { get; set; } = 5;
        
        /// <summary>
        /// Circuit breaker cool-down period in minutes
        /// </summary>
        public int CircuitBreakerCooldownMinutes { get; set; } = 15;
        
        /// <summary>
        /// Whether to use headless browser as fallback when standard scraping fails
        /// </summary>
        public bool UseHeadlessBrowserFallback { get; set; } = true;
        
        /// <summary>
        /// Number of failures before triggering headless browser fallback
        /// </summary>
        public int FailuresBeforeHeadlessFallback { get; set; } = 3;
        
        /// <summary>
        /// Maximum retries for each scraping attempt before failing
        /// </summary>
        public int MaxRetries { get; set; } = 3;
        
        /// <summary>
        /// Whether to monitor HTML structure changes for detecting website updates
        /// </summary>
        public bool MonitorHtmlStructureChanges { get; set; } = true;
        
        /// <summary>
        /// Maximum number of wallpapers to scrape in a single operation
        /// </summary>
        public int MaxWallpapersPerScrape { get; set; } = 20;
        
        /// <summary>
        /// Hours before re-scraping the same wallpaper ID
        /// </summary>
        public int WallpaperRescrapeCooldownHours { get; set; } = 12;
    }
} 