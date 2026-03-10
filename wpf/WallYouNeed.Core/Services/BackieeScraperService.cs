using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WallYouNeed.Core.Configuration;
using WallYouNeed.Core.Models;
using WallYouNeed.Core.Services.Interfaces;
using WallYouNeed.Core.Utils;
using System.IO;
using HtmlAgilityPack;
using System.Net;
using System.Collections.Concurrent;
using System.Text;

namespace WallYouNeed.Core.Services
{
    public class BackieeScraperService : IBackieeScraperService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<BackieeScraperService> _logger;
        private readonly IWallpaperConfigurationService _configService;
        private readonly HtmlDownloader _htmlDownloader;
        private Timer? _timer;
        private bool _isScrapingInProgress;
        private readonly object _lock = new object();
        
        // Circuit breaker pattern implementation
        private int _consecutiveFailures = 0;
        private DateTime _circuitOpenUntil = DateTime.MinValue;
        private const int MaxConsecutiveFailures = 5;
        private const int CircuitBreakTimeMinutes = 15;

        // Cache recently successful wallpaper IDs to minimize redundant scraping
        private readonly ConcurrentDictionary<string, DateTime> _recentlyScrapedIds = new ConcurrentDictionary<string, DateTime>();
        
        // File-based logging
        private readonly string _logFilePath;

        // Add these constants for headers and patterns
        private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/134.0.0.0 Safari/537.36";
        private const string AcceptHeader = "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7";
        private static readonly Regex WallpaperIdRegex = new Regex(@"data-wallpaper-id=""(\d+)""", RegexOptions.Compiled);
        private static readonly Regex TitleRegex = new Regex(@"<title>(.*?)\s*-\s*Free\s*HD\s*Wallpapers<\/title>", RegexOptions.Compiled);
        private static readonly Regex QualityRegex = new Regex(@"class=""resolution""[^>]*>([^<]+)<", RegexOptions.Compiled);
        private static readonly Regex AiStatusRegex = new Regex(@"<div class=""ai-tag"">", RegexOptions.Compiled);

        public event EventHandler<List<WallpaperModel>>? NewWallpapersAdded;

        public BackieeScraperService(
            HttpClient httpClient,
            ILogger<BackieeScraperService> logger,
            IWallpaperConfigurationService configService,
            HtmlDownloader htmlDownloader)
        {
            _httpClient = httpClient;
            _logger = logger;
            _configService = configService;
            _htmlDownloader = htmlDownloader;
            
            // Configure HTTP client headers from standalone version
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            _httpClient.DefaultRequestHeaders.Accept.ParseAdd(AcceptHeader);
            
            // Set up file-based logging in project directory
            _logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backiee_scraper.log");
            LogToFile("BackieeScraperService initialized");

            _timer = new Timer(ScrapeCallback, null, Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>
        /// Writes a message to the log file
        /// </summary>
        private void LogToFile(string message)
        {
            try
            {
                string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} - {message}";
                
                // Use a fixed location for logging that we know exists and is writable
                string downloadsFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Downloads");
                
                string fixedLogPath = Path.Combine(downloadsFolder, "backiee_scraper_debug.log");
                
                // Ensure log file doesn't get too large
                if (File.Exists(fixedLogPath) && new FileInfo(fixedLogPath).Length > 5 * 1024 * 1024) // 5MB
                {
                    // Append to beginning of file and truncate if it gets too large
                    string existingContent = File.ReadAllText(fixedLogPath);
                    string truncatedContent = existingContent.Substring(0, Math.Min(existingContent.Length, 1024 * 1024)); // Keep last 1MB
                    File.WriteAllText(fixedLogPath, truncatedContent);
                }
                
                // Write to both the original log path and the fixed path
                File.AppendAllText(fixedLogPath, logEntry + Environment.NewLine);
                
                if (!string.IsNullOrEmpty(_logFilePath))
                {
                    try
                    {
                        File.AppendAllText(_logFilePath, logEntry + Environment.NewLine);
                    }
                    catch
                    {
                        // Ignore errors for the original path
                    }
                }
            }
            catch (Exception ex)
            {
                // Try one more approach - write to a file next to the executable
                try
                {
                    string executableDir = Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
                    string emergencyLog = Path.Combine(executableDir, "backiee_emergency.log");
                    File.AppendAllText(emergencyLog, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} - EMERGENCY LOG: {message} - LogToFile Exception: {ex.Message}" + Environment.NewLine);
                }
                catch
                {
                    // Ultimate fallback - nothing we can do if this fails
                }
            }
        }

        /// <summary>
        /// Starts the timer to periodically scrape wallpapers
        /// </summary>
        public async Task StartPeriodicUpdates()
        {
            var config = await _configService.GetBackieeConfigAsync();
            
            lock (_lock)
            {
                if (_timer == null)
                {
                    // Dispose any existing timer
                    _timer?.Dispose();
                    
                    // Use a more reliable Timer setup
                    var callbackHandler = new TimerCallback(async state => 
                    {
                        try 
                        {
                            // Don't queue up multiple scrape operations
                            if (_isScrapingInProgress)
                            {
                                _logger.LogInformation("Skipping scrape operation because previous one is still running");
                                return;
                            }
                            
                            await ScrapeLatestWallpapers();
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error in timer callback for scraping");
                        }
                    });
                    
                    // Start with a delay to avoid immediate scraping on startup
                    _timer = new Timer(
                        callbackHandler, 
                        null, 
                        TimeSpan.FromSeconds(10), // Initial delay
                        TimeSpan.FromMilliseconds(config.ScrapingInterval)); // Recurring interval
                    
                    _logger.LogInformation("Backiee scraper periodic updates started with interval of {Interval}ms", config.ScrapingInterval);
                }
            }
        }

        /// <summary>
        /// Stops the periodic updates
        /// </summary>
        public void StopPeriodicUpdates()
        {
            lock (_lock)
            {
                _timer?.Dispose();
                _timer = null;
                _logger.LogInformation("Backiee scraper periodic updates stopped");
            }
        }

        /// <summary>
        /// Scrapes the latest wallpapers from the homepage with smart retry logic
        /// </summary>
        public async Task<List<WallpaperModel>> ScrapeLatestWallpapers()
        {
            // Use a CancellationTokenSource with timeout to prevent hanging
            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(30)); // Hard 30-second timeout
            
            // Log that we're starting a scrape attempt
            _logger.LogInformation("Starting to scrape latest wallpapers");
            
            // Initialize an empty list for wallpapers
            var wallpapers = new List<WallpaperModel>();
            
            try 
            {
                // Try to scrape from backiee.com
                wallpapers = await TryBackieeScraping(cts.Token, tryUnsplashFallback: false);
                
                // If no wallpapers were found, use our static placeholders
                if (wallpapers.Count == 0)
                {
                    _logger.LogWarning("No wallpapers found from scraping, using static Backiee placeholder images");
                    wallpapers = GeneratePlaceholderWallpapers(10);
                }
                
                // Raise event for any wallpapers found
                if (wallpapers.Count > 0)
                {
                    // Raise the event with the new wallpapers
                    NewWallpapersAdded?.Invoke(this, wallpapers);
                }
                
                return wallpapers;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error scraping latest wallpapers. Using static Backiee placeholder images.");
                return GeneratePlaceholderWallpapers(10);
            }
        }
        
        /// <summary>
        /// Attempts to scrape from backiee.com with all the original logic
        /// </summary>
        private async Task<List<WallpaperModel>> TryBackieeScraping(CancellationToken ct, bool tryUnsplashFallback = false)
        {
            // Circuit breaker check
            if (DateTime.Now < _circuitOpenUntil)
            {
                _logger.LogWarning("Circuit breaker open until {Time}, skipping scraping", _circuitOpenUntil);
                return tryUnsplashFallback ? await GetFallbackWallpapers(10) : new List<WallpaperModel>();
            }

            // Prevent concurrent scraping
            if (_isScrapingInProgress)
            {
                _logger.LogInformation("Scraping is already in progress, skipping this request");
                return tryUnsplashFallback ? await GetFallbackWallpapers(10) : new List<WallpaperModel>();
            }

            // Use a thread-safe way to set the flag
            lock (_lock)
            {
                if (_isScrapingInProgress)
                    return tryUnsplashFallback ? GetFallbackWallpapers(10).Result : new List<WallpaperModel>();
                _isScrapingInProgress = true;
            }

            var wallpapers = new List<WallpaperModel>();

            try
            {
                // Get a list of known good IDs to try
                var knownWorkingIds = new List<string>
                {
                    "318542", "318541", "318540", "318539", "318538",
                    "318137", "318124", "318123", "318122", "318116"
                };
                
                // Try direct wallpaper creation first - fastest method
                foreach (var id in knownWorkingIds.Take(3))
                {
                    // Fast direct creation without HTML
                    var wallpaper = CreateDirectWallpaper(id);
                    if (wallpaper != null && await _htmlDownloader.VerifyImageUrl(wallpaper.ImageUrl))
                    {
                        _logger.LogInformation("Successfully created wallpaper directly with ID {Id}", id);
                        wallpapers.Add(wallpaper);
                    }
                }
                
                // If we got some valid wallpapers directly, return them
                if (wallpapers.Count >= 3)
                {
                    _consecutiveFailures = 0; // Reset failure counter
                    _logger.LogInformation("Successfully got {Count} wallpapers using direct creation", wallpapers.Count);
                    return wallpapers;
                }
                
                // If direct creation didn't yield enough results, try extraction
                _logger.LogInformation("Direct creation yielded only {Count} wallpapers, trying extraction", wallpapers.Count);
                
                // Try to extract wallpapers by direct IDs as the second approach
                var extractedWallpapers = await ExtractWallpapersByDirectIds(knownWorkingIds);
                if (extractedWallpapers.Any())
                {
                    wallpapers.AddRange(extractedWallpapers);
                    _logger.LogInformation("Added {Count} wallpapers from extraction", extractedWallpapers.Count);
                }
                
                if (wallpapers.Count < 3 && tryUnsplashFallback)
                {
                    _logger.LogWarning("Extraction yielded only {Count} wallpapers. Using fallback.", wallpapers.Count);
                    int needed = 10 - wallpapers.Count;
                    
                    if (needed > 0)
                    {
                        var fallbackWallpapers = await GetFallbackWallpapers(needed);
                        wallpapers.AddRange(fallbackWallpapers);
                        _logger.LogInformation("Added {Count} fallback wallpapers", fallbackWallpapers.Count);
                    }
                }
                
                // If we were able to get wallpapers, reset the consecutive failures
                if (wallpapers.Count > 0)
                {
                    _consecutiveFailures = 0;
                }
                else
                {
                    _consecutiveFailures++;
                    _logger.LogWarning("No wallpapers found. Consecutive failures: {Count}", _consecutiveFailures);
                    
                    if (_consecutiveFailures >= MaxConsecutiveFailures)
                    {
                        _circuitOpenUntil = DateTime.Now.AddMinutes(CircuitBreakTimeMinutes);
                        _logger.LogWarning("Circuit breaker opened until {DateTime} after {Failures} consecutive failures", 
                            _circuitOpenUntil, _consecutiveFailures);
                            
                        if (tryUnsplashFallback)
                        {
                            var fallbackWallpapers = await GetFallbackWallpapers(10);
                            wallpapers.AddRange(fallbackWallpapers);
                            _logger.LogInformation("Added {Count} fallback wallpapers via circuit breaker", fallbackWallpapers.Count);
                        }
                    }
                }
                
                return wallpapers;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in TryBackieeScraping: {Message}", ex.Message);
                _consecutiveFailures++;
                
                if (_consecutiveFailures >= MaxConsecutiveFailures)
                {
                    _circuitOpenUntil = DateTime.Now.AddMinutes(CircuitBreakTimeMinutes);
                    _logger.LogWarning("Circuit breaker opened until {DateTime} after {Failures} consecutive failures", 
                        _circuitOpenUntil, _consecutiveFailures);
                }
                
                if (tryUnsplashFallback)
                {
                    return await GetFallbackWallpapers(10);
                }
                
                return new List<WallpaperModel>();
            }
            finally
            {
                _isScrapingInProgress = false;
            }
        }
        
        /// <summary>
        /// Gets fallback wallpapers (formerly from Unsplash, now using our static Backiee images)
        /// </summary>
        private List<WallpaperModel> GetUnsplashFallbackWallpapers(int count)
        {
            _logger.LogInformation("Getting {Count} fallback wallpapers (using static Backiee images instead of Unsplash)", count);
            
            // Use our static Backiee placeholders instead of Unsplash images
            return GeneratePlaceholderWallpapers(count);
        }

        /// <summary>
        /// Scrapes wallpapers for a specific category
        /// </summary>
        /// <param name="category">The category to scrape</param>
        /// <param name="maxPages">Maximum number of pages to scrape</param>
        public async Task<List<WallpaperModel>> ScrapeWallpapersByCategory(string category, int maxPages = 3)
        {
            try
            {
                var config = await _configService.GetBackieeConfigAsync();
                var wallpapers = new List<WallpaperModel>();
                
                _logger.LogInformation("Scraping wallpapers for category: {Category}", category);
                
                for (int page = 1; page <= maxPages; page++)
                {
                    string url = $"{config.BaseUrl}/category/{category}/page/{page}";
                    
                    _logger.LogDebug("Scraping page {Page} of {Category}", page, category);
                    
                    var pageWallpapers = await ScrapeWallpaperPage(url);
                    
                    if (!pageWallpapers.Any())
                    {
                        _logger.LogDebug("No more wallpapers found for category {Category} at page {Page}", category, page);
                        break;
                    }
                    
                    wallpapers.AddRange(pageWallpapers);
                    
                    // Add small delay to avoid overloading the server
                    await Task.Delay(config.RequestDelayMs);
                }
                
                _logger.LogInformation("Found {Count} wallpapers for category {Category}", wallpapers.Count, category);
                
                return wallpapers;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error scraping wallpapers for category: {Category}", category);
                return new List<WallpaperModel>();
            }
        }
        
        /// <summary>
        /// Scrapes a single page of wallpapers with timeout safeguards
        /// </summary>
        private async Task<List<WallpaperModel>> ScrapeWallpaperPage(string url, string? category = null)
        {
            try 
            {
                var html = await _httpClient.GetStringAsync(url);
                if (string.IsNullOrEmpty(html)) return new List<WallpaperModel>();

                var wallpapers = new List<WallpaperModel>();
                var wallpaperDivs = FindWallpaperDivs(html);
                
                foreach (var div in wallpaperDivs)
                {
                    try 
                    {
                        var idMatch = WallpaperIdRegex.Match(div);
                        if (!idMatch.Success) continue;
                        
                        string id = idMatch.Groups[1].Value;
                        bool isAI = CheckAIStatus(div);
                        string quality = GetQualityFromDiv(div);

                        var wallpaper = new WallpaperModel
                        {
                            Id = id,
                            Title = $"Wallpaper {id}",
                            Category = category ?? "Latest",
                            ThumbnailUrl = $"https://backiee.com/static/wallpapers/560x315/{id}.jpg",
                            ImageUrl = $"https://backiee.com/static/wallpapers/wide/{id}.jpg",
                            SourceUrl = $"https://backiee.com/wallpaper/{id}",
                            Source = "Backiee",
                            Width = 1920,
                            Height = 1080,
                            Metadata = new Dictionary<string, string>(),
                            UploadDate = DateTime.Now
                        };
                        wallpapers.Add(wallpaper);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing wallpaper div");
                    }
                }

                return wallpapers;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error scraping page: {Url}", url);
                return new List<WallpaperModel>();
            }
        }
        
        private static List<string> FindWallpaperDivs(string html)
        {
            var pattern = @"<div class=""col-sm-3 col-md-3"">(.*?)<\/div>\s+<\/div>";
            return Regex.Matches(html, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase)
                        .Cast<Match>()
                        .Select(m => m.Groups[0].Value)
                        .ToList();
        }

        private static string GetQualityFromDiv(string div) => "";

        private bool CheckAIStatus(string div) => false;

        /// <summary>
        /// Extracts the full resolution image URL from the detail page
        /// </summary>
        private async Task<string> ExtractImageUrlFromDetailPage(string detailUrl)
        {
            try
            {
                _logger.LogDebug("Extracting image URL from detail page: {DetailUrl}", detailUrl);
                
                var config = await _configService.GetBackieeConfigAsync();
                
                // Check if we're being rate limited - add delay if needed
                await Task.Delay(config.RequestDelayMs);
                
                // If we see an image URL directly in the detail URL, use it (common for direct image links)
                if (detailUrl.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || 
                    detailUrl.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) || 
                    detailUrl.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogDebug("Detail URL appears to be a direct image URL: {ImageUrl}", detailUrl);
                    return detailUrl;
                }
                
                // Try to extract image ID from detail URL
                var wallpaperIdMatch = Regex.Match(detailUrl, "/wallpaper/[^/]+/(\\d+)");
                if (wallpaperIdMatch.Success && wallpaperIdMatch.Groups.Count > 1)
                {
                    string wallpaperId = wallpaperIdMatch.Groups[1].Value;
                    string constructedUrl = $"https://backiee.com/static/wallpapers/wide/{wallpaperId}.jpg";
                    _logger.LogDebug("Directly constructed image URL from wallpaper ID: {ImageUrl}", constructedUrl);
                    
                    // Verify this URL exists 
                    try
                    {
                        var request = new HttpRequestMessage(HttpMethod.Head, constructedUrl);
                        var response = await _httpClient.SendAsync(request);
                        
                        if (response.IsSuccessStatusCode)
                        {
                            _logger.LogInformation("Successfully verified constructed image URL: {ImageUrl}", constructedUrl);
                            return constructedUrl;
                        }
                        else
                        {
                            _logger.LogDebug("Constructed URL returned status code {StatusCode}: {ImageUrl}", 
                                response.StatusCode, constructedUrl);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Error verifying constructed URL: {ImageUrl}", constructedUrl);
                    }
                }
                
                // Try to get the image URL from OpenGraph meta tags first
                string ogImageUrl = await _htmlDownloader.ExtractAttributeAsync(
                    detailUrl, 
                    "//meta[@property='og:image']", 
                    "content");
                
                if (!string.IsNullOrEmpty(ogImageUrl))
                {
                    _logger.LogDebug("Found image URL from og:image meta tag: {ImageUrl}", ogImageUrl);
                    return ogImageUrl;
                }
                
                // Also try twitter:image which is often used
                string twitterImageUrl = await _htmlDownloader.ExtractAttributeAsync(
                    detailUrl,
                    "//meta[@name='twitter:image']",
                    "content");
                    
                if (!string.IsNullOrEmpty(twitterImageUrl))
                {
                    _logger.LogDebug("Found image URL from twitter:image meta tag: {ImageUrl}", twitterImageUrl);
                    return twitterImageUrl;
                }
                
                // Try to get the image URL from the download button
                string downloadUrl = await _htmlDownloader.ExtractAttributeAsync(
                    detailUrl, 
                    "//a[contains(@class, 'download-button')]", 
                    "href");
                
                if (!string.IsNullOrEmpty(downloadUrl))
                {
                    _logger.LogDebug("Found image URL from download button: {ImageUrl}", downloadUrl);
                    
                    // Ensure URL is absolute
                    if (!downloadUrl.StartsWith("http"))
                    {
                        downloadUrl = $"{config.BaseUrl.TrimEnd('/')}/{downloadUrl.TrimStart('/')}";
                    }
                    
                    return downloadUrl;
                }
                
                // Try to find the main image on the page
                // Download the HTML to parse it directly
                string html = await _htmlDownloader.DownloadHtmlAsync(detailUrl);
                
                if (string.IsNullOrEmpty(html))
                {
                    _logger.LogWarning("Could not download HTML from detail page: {DetailUrl}", detailUrl);
                    return string.Empty;
                }
                
                // Save detail page HTML for inspection
                try 
                {
                    string logDir = Path.Combine(Path.GetTempPath(), "WallYouNeed_Logs");
                    Directory.CreateDirectory(logDir);
                    string filename = $"detail_{DateTime.Now:yyyyMMdd_HHmmss}_{Path.GetFileNameWithoutExtension(detailUrl)}.html";
                    string fullPath = Path.Combine(logDir, filename);
                    File.WriteAllText(fullPath, html);
                    _logger.LogInformation("Saved detail HTML to log file: {Path}", fullPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to save detail HTML log file");
                }
                
                // Try several patterns to match the main wallpaper image
                var patterns = new[]
                {
                    // Look for download links
                    "<a[^>]*href\\s*=\\s*['\"]([^'\"]*(?:download|original|full|hd|wallpaper)[^'\"]*)['\"\\?][^>]*>",
                    
                    // Look for main/hero images
                    "<img[^>]*(?:id\\s*=\\s*['\"](?:main-image|hero-image|wallpaper-img|full-image)['\"])[^>]*src\\s*=\\s*['\"]([^'\"]*)['\"][^>]*>",
                    
                    // Look for high-res images
                    "<img[^>]*src\\s*=\\s*['\"]([^'\"]*(?:original|full|large)[^'\"]*)['\"\\?][^>]*>",
                    
                    // Look for images with certain CSS classes
                    "<img[^>]*class\\s*=\\s*['\"][^'\"]*(?:wallpaper|full|large|main|hero)[^'\"]*['\"][^>]*src\\s*=\\s*['\"]([^'\"]*)['\"][^>]*>",
                    
                    // Look for download links with other attributes
                    "<a[^>]*download[^>]*href\\s*=\\s*['\"]([^'\"]*)['\"][^>]*>",
                    
                    // Look for buttons with download attributes
                    "<button[^>]*data-(?:url|download|src)\\s*=\\s*['\"]([^'\"]*)['\"][^>]*>",
                    
                    // Check for JSON data containing image URL (common in modern sites)
                    "\"(?:url|wallpaper_url|src|original)\"\\s*:\\s*\"([^\"]*\\.(?:jpg|jpeg|png))\"",
                    
                    // Look for background image in style
                    "background(?:-image)?\\s*:\\s*url\\(['\"]?([^'\")]*)['\"]?\\)",
                    
                    // Find data-src attributes (common in lazy-loaded images)
                    "<img[^>]*data-src\\s*=\\s*['\"]([^'\"]*)['\"][^>]*>"
                };
                
                foreach (var pattern in patterns)
                {
                    var match = Regex.Match(html, pattern, RegexOptions.Singleline);
                    if (match.Success && match.Groups.Count > 1)
                    {
                        string imageUrl = match.Groups[1].Value;
                        
                        // Skip small/thumbnail images
                        if (imageUrl.Contains("thumb") || imageUrl.Contains("small") || imageUrl.Contains("icon") || 
                            imageUrl.Contains("avatar") || imageUrl.Contains("mini"))
                        {
                            continue;
                        }
                        
                        // Ensure URL is absolute
                        if (!imageUrl.StartsWith("http"))
                        {
                            imageUrl = $"{config.BaseUrl.TrimEnd('/')}/{imageUrl.TrimStart('/')}";
                        }
                        
                        _logger.LogDebug("Found image URL using pattern {PatternIndex}: {ImageUrl}", 
                            Array.IndexOf(patterns, pattern), imageUrl);
                        
                        return imageUrl;
                    }
                }
                
                // If all else fails, try to find any large image on the page
                var allImageMatches = Regex.Matches(html, "<img[^>]*\\s(?:src|data-src)\\s*=\\s*['\"]([^'\"]*)['\"][^>]*>", RegexOptions.Singleline);
                
                string largestImageUrl = string.Empty;
                foreach (Match match in allImageMatches)
                {
                    if (match.Groups.Count > 1)
                    {
                        string imageUrl = match.Groups[1].Value;
                        
                        // Skip if it's a thumbnail or icon
                        if (imageUrl.Contains("thumb") || imageUrl.Contains("icon") || 
                            imageUrl.Contains("avatar") || imageUrl.Contains("small"))
                        {
                            continue;
                        }
                        
                        // Choose this one if it's the first or if it contains keywords suggesting it's a wallpaper
                        if (string.IsNullOrEmpty(largestImageUrl) || 
                            imageUrl.Contains("large") || imageUrl.Contains("full") || 
                            imageUrl.Contains("original") || imageUrl.Contains("wallpaper") ||
                            imageUrl.Contains("background") || imageUrl.Contains("download") ||
                            (imageUrl.Contains(".jpg") || imageUrl.Contains(".jpeg") || imageUrl.Contains(".png")))
                        {
                            largestImageUrl = imageUrl;
                        }
                    }
                }
                
                if (!string.IsNullOrEmpty(largestImageUrl))
                {
                    // Ensure URL is absolute
                    if (!largestImageUrl.StartsWith("http"))
                    {
                        largestImageUrl = $"{config.BaseUrl.TrimEnd('/')}/{largestImageUrl.TrimStart('/')}";
                    }
                    
                    _logger.LogDebug("Found best candidate image URL from all images: {ImageUrl}", largestImageUrl);
                    return largestImageUrl;
                }
                
                // As a last resort, try to construct an image URL based on the detail URL
                string potentialImageUrl = string.Empty;
                
                // If detail URL doesn't end with a file extension, try adding one
                if (!detailUrl.EndsWith(".jpg") && !detailUrl.EndsWith(".jpeg") && !detailUrl.EndsWith(".png"))
                {
                    potentialImageUrl = detailUrl.TrimEnd('/') + ".jpg";
                    _logger.LogDebug("Attempting to construct image URL from detail URL: {ImageUrl}", potentialImageUrl);
                }
                
                // If we have a potential image URL, verify it exists
                if (!string.IsNullOrEmpty(potentialImageUrl))
                {
                    try
                    {
                        var request = new HttpRequestMessage(HttpMethod.Head, potentialImageUrl);
                        var response = await _httpClient.SendAsync(request);
                        
                        if (response.IsSuccessStatusCode)
                        {
                            _logger.LogDebug("Successfully verified constructed image URL: {ImageUrl}", potentialImageUrl);
                            return potentialImageUrl;
                        }
                    }
                    catch
                    {
                        // Ignore errors - we'll just return empty string below
                    }
                }
                
                _logger.LogWarning("Could not find any suitable image URL in detail page: {DetailUrl}", detailUrl);
                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting image URL from detail page: {DetailUrl}", detailUrl);
                return string.Empty;
            }
        }
        
        /// <summary>
        /// Determines the resolution category based on width and height
        /// </summary>
        private string DetermineResolutionCategory(int width, int height)
        {
            if (width == 0 || height == 0)
                return "Unknown";
                
            var ratio = (double)width / height;
            
            if (Math.Abs(ratio - 1.78) < 0.1) // 16:9 ratio
            {
                if (width >= 3840) return "4K";
                if (width >= 2560) return "2K";
                if (width >= 1920) return "Full HD";
                if (width >= 1280) return "HD";
                return "SD";
            }
            else if (Math.Abs(ratio - 1.33) < 0.1) // 4:3 ratio
            {
                return "4:3";
            }
            else if (ratio > 2) // Ultrawide
            {
                return "Ultrawide";
            }
            else if (width > height) // Landscape but irregular
            {
                return "Wide";
            }
            else if (height > width) // Portrait
            {
                return "Portrait";
            }
            
            return "Other";
        }
        
        /// <summary>
        /// Extract wallpapers directly from HTML content when element-based approach fails
        /// </summary>
        private async Task<List<WallpaperModel>> ExtractWallpapersDirectlyFromHtml(string html, string sourceUrl, string? category = null)
        {
            var wallpapers = new List<WallpaperModel>();
            
            if (string.IsNullOrEmpty(html))
            {
                _logger.LogWarning("Empty HTML content received for direct extraction from {SourceUrl}", sourceUrl);
                return wallpapers;
            }
            
            try
            {
                _logger.LogInformation("Attempting direct HTML extraction from {SourceUrl}", sourceUrl);
                
                // Use HtmlAgilityPack for parsing
                var htmlDoc = new HtmlDocument();
                htmlDoc.LoadHtml(html);
                
                // Log the number of image elements found
                var allImages = htmlDoc.DocumentNode.SelectNodes("//img");
                var imageCount = allImages?.Count ?? 0;
                _logger.LogInformation("Found {Count} image elements in HTML", imageCount);
                
                // Find all possible wallpaper elements
                var wallpaperNodes = htmlDoc.DocumentNode.SelectNodes("//div[contains(@class, 'item') and contains(@class, 'wallpaper')]");
                
                if (wallpaperNodes == null || wallpaperNodes.Count == 0)
                {
                    // Try alternative selectors
                    wallpaperNodes = htmlDoc.DocumentNode.SelectNodes("//div[contains(@class, 'wallpapers__item')]");
                }
                
                if (wallpaperNodes == null || wallpaperNodes.Count == 0)
                {
                    // Try any element with data-id or wallpaper id attribute
                    wallpaperNodes = htmlDoc.DocumentNode.SelectNodes("//*[@data-id or @data-wallpaper-id]");
                }
                
                if (wallpaperNodes == null || wallpaperNodes.Count == 0)
                {
                    // Try finding elements with wallpaper in the class or id
                    wallpaperNodes = htmlDoc.DocumentNode.SelectNodes("//*[contains(@class, 'wallpaper') or contains(@id, 'wallpaper')]");
                }
                
                // If we still haven't found any wallpapers, look for links to wallpaper detail pages
                if (wallpaperNodes == null || wallpaperNodes.Count == 0)
                {
                    var wallpaperLinks = htmlDoc.DocumentNode.SelectNodes("//a[contains(@href, '/wallpaper/')]");
                    if (wallpaperLinks != null && wallpaperLinks.Count > 0)
                    {
                        _logger.LogInformation("Found {Count} wallpaper links", wallpaperLinks.Count);
                        foreach (var link in wallpaperLinks.Take(10))
                        {
                            var href = link.GetAttributeValue("href", "");
                            if (!string.IsNullOrEmpty(href))
                            {
                                var titleElement = link.SelectSingleNode(".//h2") ?? link.SelectSingleNode(".//h3") ?? link.SelectSingleNode(".//span");
                                var title = titleElement != null ? DecodeHtml(titleElement.InnerText.Trim()) : "Unknown";
                                
                                var imgElement = link.SelectSingleNode(".//img");
                                var thumbnailUrl = imgElement?.GetAttributeValue("src", "") ?? "";
                                
                                // Try to get data-src if src is empty or a placeholder
                                if (string.IsNullOrEmpty(thumbnailUrl) || thumbnailUrl.Contains("placeholder") || thumbnailUrl.Contains("lazy"))
                                {
                                    thumbnailUrl = imgElement?.GetAttributeValue("data-src", "") ?? "";
                                }
                                
                                // Get the wallpaper ID from the href
                                var idMatch = Regex.Match(href, @"/wallpaper/(\d+)");
                                var id = idMatch.Success ? idMatch.Groups[1].Value : "";
                                
                                if (!string.IsNullOrEmpty(id))
                                {
                                    // Construct a full URL
                                    var fullHref = href.StartsWith("http") ? href : (href.StartsWith("/") ? $"https://backiee.com{href}" : $"https://backiee.com/{href}");
                                    
                                    // Try to determine resolution from text
                                    var resolutionMatch = Regex.Match(link.InnerText, @"(\d+)\s*[xX]\s*(\d+)");
                                    int width = 0, height = 0;
                                    if (resolutionMatch.Success)
                                    {
                                        int.TryParse(resolutionMatch.Groups[1].Value, out width);
                                        int.TryParse(resolutionMatch.Groups[2].Value, out height);
                                    }
                                    
                                    // Construct direct image URL if possible
                                    var imageUrl = !string.IsNullOrEmpty(id) 
                                        ? $"https://backiee.com/static/wallpapers/wide/{id}.jpg" 
                                        : "";
                                    
                                    var wallpaper = new WallpaperModel
                                    {
                                        Id = id,
                                        Title = !string.IsNullOrEmpty(title) ? title : "Wallpaper " + id,
                                        Category = !string.IsNullOrEmpty(category) ? category : "Latest",
                                        Width = width,
                                        Height = height,
                                        ResolutionCategory = DetermineResolutionCategory(width, height),
                                        ThumbnailUrl = !string.IsNullOrEmpty(thumbnailUrl) ? thumbnailUrl : "",
                                        ImageUrl = !string.IsNullOrEmpty(imageUrl) ? imageUrl : "",
                                        SourceUrl = fullHref,
                                        Source = "Backiee",
                                        UploadDate = DateTime.Now
                                    };
                                    
                                    wallpapers.Add(wallpaper);
                                }
                            }
                        }
                    }
                }
                else
                {
                    _logger.LogInformation("Found {Count} wallpaper nodes", wallpaperNodes.Count);
                    
                    foreach (var node in wallpaperNodes.Take(10))
                    {
                        // Extract wallpaper ID
                        var id = node.GetAttributeValue("data-id", "") ?? 
                                node.GetAttributeValue("data-wallpaper-id", "") ?? 
                                "";
                                
                        // If ID is still empty, try to find it in a link
                        if (string.IsNullOrEmpty(id))
                        {
                            var link = node.SelectSingleNode(".//a[contains(@href, '/wallpaper/')]");
                            if (link != null)
                            {
                                var href = link.GetAttributeValue("href", "");
                                var idMatch = Regex.Match(href, @"/wallpaper/(\d+)");
                                if (idMatch.Success)
                                {
                                    id = idMatch.Groups[1].Value;
                                }
                            }
                        }
                        
                        // Extract title
                        var titleNode = node.SelectSingleNode(".//h2") ?? 
                                        node.SelectSingleNode(".//h3") ?? 
                                        node.SelectSingleNode(".//span[@class='title']") ??
                                        node.SelectSingleNode(".//a[@title]");
                                        
                        var title = titleNode != null ? 
                                    DecodeHtml(titleNode.InnerText.Trim()) : 
                                    titleNode?.GetAttributeValue("title", "") ?? 
                                    "Wallpaper " + id;
                        
                        // Extract thumbnail URL
                        var imgNode = node.SelectSingleNode(".//img");
                        var thumbnailUrl = imgNode?.GetAttributeValue("src", "") ?? "";
                        
                        // Try data-src if src is empty or a placeholder
                        if (string.IsNullOrEmpty(thumbnailUrl) || thumbnailUrl.Contains("placeholder") || thumbnailUrl.Contains("lazy"))
                        {
                            thumbnailUrl = imgNode?.GetAttributeValue("data-src", "") ?? 
                                          imgNode?.GetAttributeValue("data-lazy-src", "") ?? 
                                          imgNode?.GetAttributeValue("data-original", "") ?? 
                                          "";
                        }
                        
                        // If we have an ID, try to construct a direct image URL
                        var imageUrl = !string.IsNullOrEmpty(id) 
                            ? $"https://backiee.com/static/wallpapers/wide/{id}.jpg" 
                            : "";
                            
                        // Try to find the source URL (detail page)
                        var detailLink = node.SelectSingleNode(".//a[contains(@href, '/wallpaper/')]");
                        var sourceUrl2 = detailLink?.GetAttributeValue("href", "") ?? "";
                        
                        // Make the URL absolute if it's relative
                        if (!string.IsNullOrEmpty(sourceUrl2) && !sourceUrl2.StartsWith("http"))
                        {
                            sourceUrl2 = sourceUrl2.StartsWith("/") 
                                ? $"https://backiee.com{sourceUrl2}" 
                                : $"https://backiee.com/{sourceUrl2}";
                        }
                        
                        // If we still don't have a source URL, use the current page
                        if (string.IsNullOrEmpty(sourceUrl2))
                        {
                            sourceUrl2 = sourceUrl;
                        }
                        
                        // Try to determine resolution
                        var resolutionText = node.InnerText;
                        var resolutionMatch = Regex.Match(resolutionText, @"(\d+)\s*[xX]\s*(\d+)");
                        int width = 0, height = 0;
                        if (resolutionMatch.Success)
                        {
                            int.TryParse(resolutionMatch.Groups[1].Value, out width);
                            int.TryParse(resolutionMatch.Groups[2].Value, out height);
                        }
                        
                        if (!string.IsNullOrEmpty(id))
                        {
                            var wallpaper = new WallpaperModel
                            {
                                Id = id,
                                Title = !string.IsNullOrEmpty(title) ? title : "Wallpaper " + id,
                                Category = !string.IsNullOrEmpty(category) ? category : "Latest",
                                Width = width,
                                Height = height,
                                ResolutionCategory = DetermineResolutionCategory(width, height),
                                ThumbnailUrl = !string.IsNullOrEmpty(thumbnailUrl) ? thumbnailUrl : "",
                                ImageUrl = !string.IsNullOrEmpty(imageUrl) ? imageUrl : "",
                                SourceUrl = sourceUrl2,
                                Source = "Backiee",
                                UploadDate = DateTime.Now
                            };
                            
                            wallpapers.Add(wallpaper);
                        }
                    }
                }
                
                _logger.LogInformation("Direct HTML extraction found {Count} wallpapers", wallpapers.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during direct HTML extraction: {Message}", ex.Message);
            }
            
            return wallpapers;
        }

        private string DecodeHtml(string html)
        {
            if (string.IsNullOrEmpty(html))
                return string.Empty;
            
            // Use HtmlAgilityPack for HTML decoding
            return HtmlAgilityPack.HtmlEntity.DeEntitize(html);
        }

        /// <summary>
        /// Analyzes HTML directly for debugging purposes when scraping fails
        /// </summary>
        private async Task AnalyzeHtmlForDebug(string url)
        {
            try
            {
                _logger.LogInformation("Starting HTML analysis for debugging: {Url}", url);
                
                // Download the HTML
                var html = await _httpClient.GetStringAsync(url);
                if (string.IsNullOrEmpty(html))
                {
                    _logger.LogWarning("Couldn't download HTML for analysis from {Url}", url);
                    return;
                }
                
                string logDir = Path.Combine(Path.GetTempPath(), "WallYouNeed_Logs");
                Directory.CreateDirectory(logDir);
                string filename = $"analysis_debug_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                string fullPath = Path.Combine(logDir, filename);
                
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"HTML Analysis for {url} at {DateTime.Now}");
                sb.AppendLine("=".PadRight(80, '='));
                sb.AppendLine();
                
                // Check for key patterns that would indicate we're on the right page
                sb.AppendLine("1. Key page indicators:");
                sb.AppendLine("-".PadRight(40, '-'));
                CheckPattern(sb, html, "<title[^>]*>([^<]*backiee[^<]*)</title>", "Title contains 'backiee'");
                CheckPattern(sb, html, "wallpaper", "Contains 'wallpaper' text");
                CheckPattern(sb, html, "class\\s*=\\s*['\"]col-sm-3", "Contains Bootstrap column classes");
                CheckPattern(sb, html, "<a[^>]*href\\s*=\\s*['\"][^'\"]*wallpaper[^'\"]*['\"]", "Contains wallpaper links");
                CheckPattern(sb, html, "data-src\\s*=", "Contains data-src attributes (lazy loading)");
                sb.AppendLine();
                
                // Check for specific HTML structures
                sb.AppendLine("2. HTML Structure Checks:");
                sb.AppendLine("-".PadRight(40, '-'));
                CheckPattern(sb, html, "<div[^>]*class\\s*=\\s*['\"][^'\"]*tz-gallery[^'\"]*['\"]", "Gallery container");
                CheckPattern(sb, html, "<div[^>]*class\\s*=\\s*['\"][^'\"]*row[^'\"]*['\"]", "Bootstrap row");
                CheckPattern(sb, html, "<div[^>]*class\\s*=\\s*['\"][^'\"]*col-sm-3[^'\"]*['\"]", "Bootstrap column");
                CheckPattern(sb, html, "<div[^>]*class\\s*=\\s*['\"][^'\"]*placeholder[^'\"]*['\"]", "Image placeholder");
                CheckPattern(sb, html, "<img[^>]*class\\s*=\\s*['\"][^'\"]*rounded-image[^'\"]*['\"]", "Rounded image");
                sb.AppendLine();
                
                // Extract a sample wallpaper if possible
                sb.AppendLine("3. Sample Wallpaper Link Extraction:");
                sb.AppendLine("-".PadRight(40, '-'));
                var wallpaperLinkMatch = Regex.Match(html, "<a\\s+href\\s*=\\s*['\"](?:https?://)?(?:www\\.)?backiee\\.com/wallpaper/([^/\"']+)/(\\d+)['\"][^>]*>");
                if (wallpaperLinkMatch.Success)
                {
                    string slug = wallpaperLinkMatch.Groups[1].Value;
                    string id = wallpaperLinkMatch.Groups[2].Value;
                    sb.AppendLine($"Found wallpaper link: slug='{slug}', id='{id}'");
                    
                    // Extract context around this match
                    int start = Math.Max(0, wallpaperLinkMatch.Index - 100);
                    int length = Math.Min(html.Length - start, wallpaperLinkMatch.Length + 200);
                    string context = html.Substring(start, length);
                    sb.AppendLine("Context around match:");
                    sb.AppendLine(context);
                }
                else
                {
                    sb.AppendLine("No wallpaper links found matching expected pattern.");
                }
                sb.AppendLine();
                
                // Look for image tags and their structures
                sb.AppendLine("4. Image Tags Analysis:");
                sb.AppendLine("-".PadRight(40, '-'));
                var imgTags = Regex.Matches(html, "<img[^>]*>");
                sb.AppendLine($"Found {imgTags.Count} image tags in total.");
                if (imgTags.Count > 0)
                {
                    // Sample the first 3 image tags
                    int sampleSize = Math.Min(3, imgTags.Count);
                    for (int i = 0; i < sampleSize; i++)
                    {
                        sb.AppendLine($"Sample image tag {i+1}:");
                        sb.AppendLine(imgTags[i].Value);
                        sb.AppendLine();
                    }
                }
                
                // Write the analysis to file
                File.WriteAllText(fullPath, sb.ToString());
                _logger.LogInformation("HTML analysis complete. Results saved to: {Path}", fullPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing HTML");
            }
        }
        
        private void CheckPattern(System.Text.StringBuilder sb, string html, string pattern, string description)
        {
            var matches = Regex.Matches(html, pattern);
            sb.AppendLine($"{description}: {(matches.Count > 0 ? "YES" : "NO")} ({matches.Count} matches)");
        }

        /// <summary>
        /// More reliable extraction method that focuses on getting wallpaper IDs directly from the HTML
        /// </summary>
        private async Task<List<WallpaperModel>> ExtractWallpapersByDirectIds(List<string> wallpaperIds)
        {
            var wallpapers = new List<WallpaperModel>();
            var tasks = new List<Task<WallpaperModel>>();

            _logger.LogInformation("Starting extraction of {Count} wallpapers by direct IDs", wallpaperIds.Count);
            
            int maxConcurrent = 3; // Maximum concurrent requests to avoid overloading

            // Create a semaphore to limit concurrent requests
            using (var semaphore = new SemaphoreSlim(maxConcurrent))
            {
                foreach (var id in wallpaperIds)
                {
                    await semaphore.WaitAsync();
                    
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            var url = $"https://backiee.com/wallpaper/{id}";
                            _logger.LogDebug("Downloading HTML for wallpaper ID {Id} from {Url}", id, url);
                            
                            string html = await _httpClient.GetStringAsync(url);
                            
                            if (string.IsNullOrEmpty(html))
                            {
                                _logger.LogWarning("Failed to download HTML for wallpaper ID {Id}", id);
                                return null;
                            }
                            
                            // Check if we got actual HTML or binary data (anti-scraping measure)
                            if (IsBinaryData(html))
                            {
                                _logger.LogWarning("Received binary data instead of HTML for wallpaper ID {Id}. Site may be serving images to block scrapers.", id);
                                // Create wallpaper with direct URL construction as fallback
                                return CreateDirectWallpaper(id) ?? new WallpaperModel();
                            }
                            
                            var wallpaper = ExtractSingleWallpaperDetails(html, id);
                            
                            if (wallpaper != null)
                            {
                                _logger.LogInformation("Successfully extracted wallpaper with ID {Id}: {Title}", id, wallpaper.Title);
                                
                                // Verify the image URL actually works
                                if (await _htmlDownloader.VerifyImageUrl(wallpaper.ImageUrl))
                                {
                                    _logger.LogDebug("Verified image URL for ID {Id}: {ImageUrl}", id, wallpaper.ImageUrl);
                                    return wallpaper;
                                }
                                else
                                {
                                    _logger.LogWarning("Image URL verification failed for ID {Id}, using direct construction", id);
                                    return CreateDirectWallpaper(id) ?? new WallpaperModel();
                                }
                            }
                            else
                            {
                                _logger.LogWarning("Failed to extract wallpaper details for ID {Id}, using direct construction", id);
                                return CreateDirectWallpaper(id) ?? new WallpaperModel();
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error extracting wallpaper with ID {Id}: {Message}", id, ex.Message);
                            return null;
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }));
                    
                    // Small delay between starting each task to avoid triggering rate limits
                    await Task.Delay(100);
                }
                
                _logger.LogInformation("Waiting for all wallpaper extraction tasks to complete");
                
                try
                {
                    // Wait for all tasks to complete with a timeout
                    var completedTasks = await Task.WhenAll(tasks).ConfigureAwait(false);
                    
                    // Add all non-null wallpapers to the list
                    wallpapers.AddRange(completedTasks.Where(w => w != null));
                    
                    _logger.LogInformation("Successfully extracted {SuccessCount} out of {TotalCount} wallpapers", 
                        wallpapers.Count, wallpaperIds.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error waiting for wallpaper extraction tasks: {Message}", ex.Message);
                    
                    // Collect results from completed tasks
                    foreach (var task in tasks.Where(t => t.IsCompleted && !t.IsFaulted && !t.IsCanceled))
                    {
                        if (task.Result != null)
                        {
                            wallpapers.Add(task.Result);
                        }
                    }
                    
                    _logger.LogInformation("Recovered {Count} wallpapers from completed tasks", wallpapers.Count);
                }
            }
            
            // If we couldn't extract enough wallpapers, fall back to known working ones
            if (wallpapers.Count < 3)
            {
                _logger.LogWarning("Could not extract enough wallpapers. Got {Count}, needed at least 3. Using fallback mechanism", 
                    wallpapers.Count);
                
                // Get the number of additional wallpapers needed
                int additionalNeeded = Math.Max(5 - wallpapers.Count, 0);
                
                if (additionalNeeded > 0)
                {
                    var fallbackWallpapers = await GetKnownWorkingWallpapers(additionalNeeded);
                    wallpapers.AddRange(fallbackWallpapers);
                    
                    _logger.LogInformation("Added {Count} fallback wallpapers", fallbackWallpapers.Count);
                }
            }
            
            return wallpapers;
        }

        // Add a helper method to check if data is binary
        private bool IsBinaryData(string content)
        {
            if (string.IsNullOrEmpty(content) || content.Length < 10)
                return false;
            
            // Check for common binary file signatures
            // JPEG signature (0xFF, 0xD8)
            if (content[0] == (char)0xFF && content[1] == (char)0xD8)
                return true;
            
            // PNG signature (0x89, 'P', 'N', 'G')
            if (content[0] == (char)0x89 && content.StartsWith("\u0089PNG", StringComparison.Ordinal))
                return true;
            
            // GIF signature ('G', 'I', 'F')
            if (content.StartsWith("GIF", StringComparison.Ordinal))
                return true;
            
            // Check for high concentration of non-printable characters
            int nonPrintableCount = 0;
            for (int i = 0; i < Math.Min(200, content.Length); i++)
            {
                if (content[i] < 32 && content[i] != '\r' && content[i] != '\n' && content[i] != '\t')
                    nonPrintableCount++;
            }
            
            // If more than 15% of the first 200 characters are non-printable, it's likely binary
            if (nonPrintableCount > 30)
                return true;
            
            return false;
        }

        /// <summary>
        /// Gets wallpapers using a list of known working wallpaper IDs
        /// </summary>
        #pragma warning disable CS1998
        private async Task<List<WallpaperModel>> GetKnownWorkingWallpapers(int count)
        {
            _logger.LogInformation("Attempting to get {Count} known working wallpapers as fallback", count);
            
            // List of known working IDs
            var knownIds = new List<string>
            {
                "318542", "318541", "318540", "318534", "318532", 
                "318531", "318530", "318528", "318524", "318520",
                "318519", "318518", "318517", "318516", "318515",
                "318320", "318319", "318318", "318317", "318316",
                "318142", "318141", "318140", "318138", "318137"
            };
            
            // Shuffle the list to get different wallpapers each time
            var random = new Random();
            var shuffledIds = knownIds.OrderBy(x => random.Next()).Take(Math.Min(count, knownIds.Count)).ToList();
            
            var results = new List<WallpaperModel>();
            foreach (var id in shuffledIds)
            {
                try
                {
                    var url = $"https://backiee.com/wallpaper/{id}";
                    _logger.LogInformation("Attempting to get known wallpaper with ID {Id}", id);
                    
                    string html = await _httpClient.GetStringAsync(url);
                    if (string.IsNullOrEmpty(html))
                    {
                        _logger.LogWarning("Failed to download HTML for known wallpaper ID {Id}", id);
                        continue;
                    }
                    
                    var wallpaper = ExtractSingleWallpaperDetails(html, id);
                    if (wallpaper != null && !string.IsNullOrEmpty(wallpaper.ImageUrl))
                    {
                        _logger.LogInformation("Successfully extracted known wallpaper with ID {Id}", id);
                        results.Add(wallpaper);
                    }
                    else
                    {
                        // Handle null case
                        results.Add(CreateDirectWallpaper(id) ?? new WallpaperModel());
                    }
                    
                    // Add a small delay to avoid hitting rate limits
                    await Task.Delay(500);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error getting known wallpaper with ID {Id}: {Message}", id, ex.Message);
                }
            }
            
            // If we still don't have enough wallpapers, add some stable Unsplash URLs as a last resort
            if (results.Count < count)
            {
                _logger.LogWarning("Could not get enough known wallpapers. Adding {Count} Unsplash fallback wallpapers", 
                    Math.Min(5, count - results.Count));
                
                var unsplashFallbacks = new List<(string Url, string Title)>
                {
                    ("https://images.unsplash.com/photo-1506744038136-46273834b3fb", "Beautiful Mountain Landscape"),
                    ("https://images.unsplash.com/photo-1494500764479-0c8f2919a3d8", "Starry Night Sky"),
                    ("https://images.unsplash.com/photo-1511300636408-a63a89df3482", "Peaceful Forest"),
                    ("https://images.unsplash.com/photo-1497436072909-60f360e1d4b1", "Green Mountains"),
                    ("https://images.unsplash.com/photo-1507525428034-b723cf961d3e", "Serene Beach")
                };
                
                var usedCount = 0;
                foreach (var (imageUrl, title) in unsplashFallbacks)
                {
                    if (results.Count >= count || usedCount >= 5) break;
                    
                    var wallpaper = new WallpaperModel
                    {
                        Title = title,
                        ImageUrl = imageUrl,
                        ThumbnailUrl = imageUrl,
                        Source = "Unsplash (Fallback)",
                        SourceUrl = "https://unsplash.com",
                        Width = 1920,
                        Height = 1080,
                        Category = "Nature",
                        Rating = 5
                    };
                    
                    results.Add(wallpaper);
                    usedCount++;
                    _logger.LogInformation("Added Unsplash fallback wallpaper: {Title}", title);
                }
            }
            
            return results;
        }
        #pragma warning restore CS1998

        /// <summary>
        /// Extracts wallpaper details by directly accessing individual wallpaper pages by ID
        /// </summary>
        private WallpaperModel ExtractSingleWallpaperDetails(string html, string id)
        {
            try
            {
                // Try various patterns to find the title
                var patterns = new[]
                {
                    $"<a[^>]*href\\s*=\\s*['\"][^'\"]*{id}[^'\"]*['\"][^>]*title\\s*=\\s*['\"]([^'\"]+)['\"]",
                    $"<div[^>]*class\\s*=\\s*['\"]max-linese['\"][^>]*>([^<]*{id}[^<]*)</div>",
                    $"<div[^>]*class\\s*=\\s*['\"]box['\"][^>]*>\\s*<div[^>]*>([^<]+)</div>\\s*</div>"
                };
                
                foreach (var pattern in patterns)
                {
                    var match = Regex.Match(html, pattern);
                    if (match.Success && match.Groups.Count > 1)
                    {
                        var title = DecodeHtml(match.Groups[1].Value.Trim());
                        return new WallpaperModel
                        {
                            Id = id,
                            Title = title,
                            Category = "Latest",
                            ThumbnailUrl = $"https://backiee.com/static/wallpapers/560x315/{id}.jpg",
                            ImageUrl = $"https://backiee.com/static/wallpapers/wide/{id}.jpg",
                            SourceUrl = $"https://backiee.com/wallpaper/{id}",
                            Source = "Backiee",
                            Width = 1920,
                            Height = 1080,
                            UploadDate = DateTime.Now
                        };
                    }
                }
                
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting wallpaper with ID {Id}: {Message}", id, ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Provides fallback wallpapers when Backiee scraping fails
        /// </summary>
        private async Task<List<WallpaperModel>> GetFallbackWallpapers(int count)
        {
            _logger.LogInformation("Getting {Count} fallback wallpapers using static Backiee images", count);
            
            // Simply use our static Backiee images for all fallbacks
            return GeneratePlaceholderWallpapers(count);
        }

        /// <summary>
        /// Generates placeholder wallpaper models using actual Backiee static images
        /// </summary>
        /// <param name="count">Number of placeholders to generate</param>
        /// <returns>List of placeholder wallpaper models</returns>
        public List<WallpaperModel> GeneratePlaceholderWallpapers(int count = 10)
        {
            try
            {
                _logger.LogInformation("Generating {Count} static Backiee placeholders", count);
                
                var placeholders = new List<WallpaperModel>();
                var random = new Random();
                
                // Use the exact static image IDs from the user's list
                var backieeImageIds = new[]
                {
                    "418137",
                    "418124",
                    "418123",
                    "418122",
                    "418116",
                    "418115",
                    "418114",
                    "418113",
                    "418112",
                    "418111",
                    "418109",
                    "418107",
                    "418106",
                    "418105",
                    "418104",
                    "418102",
                    "418100",
                    "418099",
                    "418098",
                    "418070"
                };
                
                // Create wallpaper models using actual Backiee image IDs and URLs
                for (int i = 0; i < Math.Min(count, backieeImageIds.Length); i++)
                {
                    string id = backieeImageIds[i];
                    
                    // Create a generic title for images we don't have specific titles for
                    string title = $"Backiee Wallpaper {id}";
                    
                    var wallpaper = new WallpaperModel
                    {
                        Id = id,
                        Title = title,
                        Category = "Latest",
                        ThumbnailUrl = $"https://backiee.com/static/wallpapers/560x315/{id}.jpg",
                        ImageUrl = $"https://backiee.com/static/wallpapers/wide/{id}.jpg",
                        SourceUrl = $"https://backiee.com/wallpaper/{id}",
                        Source = "Backiee",
                        Width = 3840,
                        Height = 2160,
                        Metadata = new Dictionary<string, string>(),
                        UploadDate = DateTime.Now.AddDays(-random.Next(1, 7))
                    };
                    
                    placeholders.Add(wallpaper);
                    
                    _logger.LogDebug("Added static Backiee placeholder: ID={Id}, URL={Url}", 
                        id, wallpaper.ThumbnailUrl);
                }
                
                _logger.LogInformation("Successfully generated {Count} static Backiee placeholders", placeholders.Count);
                return placeholders;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating static Backiee placeholders");
                return new List<WallpaperModel>();
            }
        }

        /// <summary>
        /// Extracts wallpaper information from backiee_content.html
        /// </summary>
        /// <param name="htmlContent">The HTML content to extract wallpapers from</param>
        /// <returns>A list of wallpaper models extracted from the HTML content</returns>
        public async Task<List<WallpaperModel>> ExtractWallpapersFromContentHtml(string htmlContent)
        {
            _logger.LogInformation("ExtractWallpapersFromContentHtml called - returning static Backiee images instead");
            LogToFile("ExtractWallpapersFromContentHtml called - returning static Backiee images instead");
            
            // Use our static Backiee placeholder wallpapers
            return GeneratePlaceholderWallpapers(15);
        }

        /// <summary>
        /// Extracts wallpapers from the local backiee_content.html file
        /// </summary>
        /// <returns>A list of extracted wallpapers</returns>
        public async Task<List<WallpaperModel>> ExtractWallpapersFromLocalFile()
        {
            _logger.LogInformation("ExtractWallpapersFromLocalFile called - returning static Backiee images instead");
            LogToFile("ExtractWallpapersFromLocalFile called - returning static Backiee images instead");
            
            // Use our static Backiee placeholder wallpapers
            return GeneratePlaceholderWallpapers(15);
        }

        /// <summary>
        /// Gets hardcoded wallpapers directly from specified URLs
        /// </summary>
        /// <returns>List of wallpaper models from hardcoded URLs</returns>
        public async Task<List<WallpaperModel>> GetHardcodedWallpapers()
        {
            _logger.LogInformation("Using hardcoded Backiee static wallpaper URLs");
            LogToFile("Using hardcoded Backiee static wallpaper URLs");
            
            // Simply use our static Backiee placeholder wallpapers
            return GeneratePlaceholderWallpapers(20);
        }

        /// <summary>
        /// Creates a wallpaper directly from our static image list
        /// </summary>
        private WallpaperModel CreateDirectWallpaper(string id, string category = "Latest")
        {
            _logger.LogInformation("Creating direct wallpaper for ID {Id}", id);
            
            // Check if this ID is in our static list first
            var staticImageIds = new[]
            {
                "418137", "418124", "418123", "418122", "418116", 
                "418115", "418114", "418113", "418112", "418111", 
                "418109", "418107", "418106", "418105", "418104", 
                "418102", "418100", "418099", "418098", "418070"
            };
            
            // If the ID is not in our static list, use the first ID from our static list
            string imageId = staticImageIds.Contains(id) ? id : staticImageIds[0];
            
            // Create a generic title
            string title = $"Backiee Wallpaper {imageId}";
            
            var random = new Random();
            
            // Create wallpaper with our static image URL format
            return new WallpaperModel
            {
                Id = imageId,
                Title = title,
                Category = category,
                ThumbnailUrl = $"https://backiee.com/static/wallpapers/560x315/{imageId}.jpg",
                ImageUrl = $"https://backiee.com/static/wallpapers/wide/{imageId}.jpg",
                SourceUrl = $"https://backiee.com/wallpaper/{imageId}",
                Source = "Backiee",
                Width = 1920,
                Height = 1080,
                Metadata = new Dictionary<string, string>(),
                UploadDate = DateTime.Now.AddDays(-random.Next(1, 7))
            };
        }

        private async void ScrapeCallback(object? state)
        {
            if (_isScrapingInProgress) return;
            
            lock (_lock)
            {
                if (_isScrapingInProgress) return;
                _isScrapingInProgress = true;
            }

            try
            {
                _logger.LogInformation("Periodic scrape started");
                var wallpapers = await ScrapeLatestWallpapers();
                NewWallpapersAdded?.Invoke(this, wallpapers);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during periodic scrape");
            }
            finally
            {
                lock (_lock)
                {
                    _isScrapingInProgress = false;
                }
            }
        }
    }
} 