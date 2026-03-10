using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Newtonsoft.Json;
using System.Threading;
using System.Linq;
using Aura.Models;

namespace Aura.Services
{
    public class AlphaCodersScraperService
    {
        private readonly HttpClient _httpClient;
        private readonly string _smallFolder = "small_thumbs";
        private readonly string _bigFolder = "big_thumbs";
        private readonly string _originalFolder = "originals";
        private readonly string _metadataFolder = "metadata";
        private readonly string _smallJsonFile = "small_urls.json";
        private readonly string _bigJsonFile = "big_urls.json";
        private readonly string _originalJsonFile = "original_urls.json";
        private readonly string _baseUrl = "https://alphacoders.com/resolution/4k-wallpapers?page={0}";

        private readonly List<string> _allSmallUrls = new List<string>();
        private readonly List<string> _allBigUrls = new List<string>();
        private readonly List<string> _allOriginalUrls = new List<string>();

        // Static debug logger that can be set by the UI
        public static Action<string> DebugLogger { get; set; }

        private static void LogDebug(string message)
        {
            System.Diagnostics.Debug.WriteLine(message);
            DebugLogger?.Invoke(message);
        }

        public AlphaCodersScraperService()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");

            // Create directories
            Directory.CreateDirectory(_smallFolder);
            Directory.CreateDirectory(_bigFolder);
            Directory.CreateDirectory(_originalFolder);
            Directory.CreateDirectory(_metadataFolder);
        }

        public async Task<List<WallpaperItem>> ScrapeWallpapersAsync(int startPage = 1, int endPage = 3)
        {
            var wallpapers = new List<WallpaperItem>();

            try
            {
                // Clear previous URLs for fresh scraping
                var currentPageUrls = new List<string>();

                LogDebug($"Starting to scrape pages {startPage} to {endPage}...");
                for (int page = startPage; page <= endPage; page++)
                {
                    LogDebug($"Scraping page {page}...");
                    var smallUrls = await GetSmallImageUrlsAsync(page);
                    LogDebug($"Found {smallUrls.Count} small images on page {page}");

                    // Log first few URLs for debugging
                    for (int i = 0; i < Math.Min(3, smallUrls.Count); i++)
                    {
                        LogDebug($"  Small URL {i + 1}: {smallUrls[i]}");
                    }

                    currentPageUrls.AddRange(smallUrls);
                }
                LogDebug($"Total small URLs collected: {currentPageUrls.Count}");

                // Create WallpaperItem objects directly from current scrape
                wallpapers = CreateWallpaperItemsFromUrls(currentPageUrls);

                return wallpapers;
            }
            catch (Exception ex)
            {
                LogDebug($"Error in ScrapeWallpapersAsync: {ex.Message}");
                return wallpapers;
            }
        }

        public async Task<List<WallpaperItem>> ScrapeWallpapersByCategoryAsync(string category, int startPage = 1, int endPage = 3)
        {
            var wallpapers = new List<WallpaperItem>();

            try
            {
                var currentPageUrls = new List<string>();
                string categoryUrl = GetCategoryUrl(category);

                LogDebug($"Starting to scrape category '{category}' pages {startPage} to {endPage}...");
                for (int page = startPage; page <= endPage; page++)
                {
                    LogDebug($"Scraping page {page}...");
                    var smallUrls = await GetSmallImageUrlsByCategoryAsync(categoryUrl, page);
                    LogDebug($"Found {smallUrls.Count} small images on page {page}");

                    // Log first few URLs for debugging
                    for (int i = 0; i < Math.Min(3, smallUrls.Count); i++)
                    {
                        LogDebug($"  Small URL {i + 1}: {smallUrls[i]}");
                    }

                    currentPageUrls.AddRange(smallUrls);
                }
                LogDebug($"Total small URLs collected: {currentPageUrls.Count}");

                // Create WallpaperItem objects directly from current scrape
                wallpapers = CreateWallpaperItemsFromUrls(currentPageUrls);

                return wallpapers;
            }
            catch (Exception ex)
            {
                LogDebug($"Error in ScrapeWallpapersByCategoryAsync: {ex.Message}");
                return wallpapers;
            }
        }

        private string GetCategoryUrl(string category)
        {
            return category.ToLower() switch
            {
                "4k" => "https://alphacoders.com/resolution/4k-wallpapers?page={0}",
                "harvest" => "https://alphacoders.com/search?search=harvest&page={0}",
                "rain" => "https://alphacoders.com/search?search=rain&page={0}",
                _ => "https://alphacoders.com/resolution/4k-wallpapers?page={0}"
            };
        }

        private async Task<List<string>> GetSmallImageUrlsByCategoryAsync(string categoryUrlTemplate, int pageNumber)
        {
            var imageUrls = new List<string>();

            try
            {
                var url = string.Format(categoryUrlTemplate, pageNumber);
                LogDebug($"Fetching URL: {url}");
                var response = await _httpClient.GetAsync(url);
                var htmlContent = await response.Content.ReadAsStringAsync();
                LogDebug($"Got HTML response, length: {htmlContent.Length} characters");

                var doc = new HtmlDocument();
                doc.LoadHtml(htmlContent);

                var imgNodes = doc.DocumentNode.SelectNodes("//img[@src]");
                LogDebug($"Found {imgNodes?.Count ?? 0} img nodes");

                if (imgNodes != null)
                {
                    int thumbbigCount = 0;
                    foreach (var imgNode in imgNodes)
                    {
                        var src = imgNode.GetAttributeValue("src", "");
                        if (!string.IsNullOrEmpty(src))
                        {
                            if (src.Contains("thumbbig"))
                            {
                                thumbbigCount++;
                                LogDebug($"Found thumbbig image: {src}");
                            }

                            if (src.Contains("thumbbig") && src.StartsWith("https://images"))
                            {
                                imageUrls.Add(src);
                            }
                        }
                    }
                    LogDebug($"Total thumbbig images found: {thumbbigCount}");
                    LogDebug($"Valid thumbbig images (starting with https://images): {imageUrls.Count}");
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Error getting small image URLs from page {pageNumber}: {ex.Message}");
            }

            return imageUrls;
        }

        private async Task<List<string>> GetSmallImageUrlsAsync(int pageNumber)
        {
            var imageUrls = new List<string>();

            try
            {
                var url = string.Format(_baseUrl, pageNumber);
                LogDebug($"Fetching URL: {url}");
                var response = await _httpClient.GetAsync(url);
                var htmlContent = await response.Content.ReadAsStringAsync();
                LogDebug($"Got HTML response, length: {htmlContent.Length} characters");

                var doc = new HtmlDocument();
                doc.LoadHtml(htmlContent);

                var imgNodes = doc.DocumentNode.SelectNodes("//img[@src]");
                LogDebug($"Found {imgNodes?.Count ?? 0} img nodes");

                if (imgNodes != null)
                {
                    int thumbbigCount = 0;
                    foreach (var imgNode in imgNodes)
                    {
                        var src = imgNode.GetAttributeValue("src", "");
                        if (!string.IsNullOrEmpty(src))
                        {
                            if (src.Contains("thumbbig"))
                            {
                                thumbbigCount++;
                                LogDebug($"Found thumbbig image: {src}");
                            }

                            if (src.Contains("thumbbig") && src.StartsWith("https://images"))
                            {
                                imageUrls.Add(src);
                            }
                        }
                    }
                    LogDebug($"Total thumbbig images found: {thumbbigCount}");
                    LogDebug($"Valid thumbbig images (starting with https://images): {imageUrls.Count}");
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Error getting small image URLs from page {pageNumber}: {ex.Message}");
            }

            return imageUrls;
        }



        private string GetImageIdFromUrl(string url)
        {
            try
            {
                var fileName = Path.GetFileNameWithoutExtension(new Uri(url).LocalPath);
                var parts = fileName.Split('-');
                return parts.Last();
            }
            catch
            {
                return "";
            }
        }

        // Method to fetch big thumb URL on-demand when a wallpaper is clicked
        public async Task<string> GetBigImageUrlForWallpaperAsync(string imageId, string smallUrl)
        {
            try
            {
                var uri = new Uri(smallUrl);
                var domain = uri.Host;
                var folderNumber = uri.Segments[1].TrimEnd('/');

                var baseBigUrl = $"https://{domain}/{folderNumber}/thumb-1920-{imageId}";

                // Try different extensions like Python scraper
                string[] extensions = { "jpeg", "jpg", "png" };

                using (var httpClient = new HttpClient())
                {
                    httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");

                    foreach (var ext in extensions)
                    {
                        var bigUrl = $"{baseBigUrl}.{ext}";
                        try
                        {
                            var response = await httpClient.GetAsync(bigUrl);
                            if (response.IsSuccessStatusCode)
                            {
                                Console.WriteLine($"Found big image with extension {ext}: {bigUrl}");
                                return bigUrl;
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed {bigUrl}: {ex.Message}");
                        }
                    }
                }

                Console.WriteLine($"Big image not found for {smallUrl}");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting big image URL for {smallUrl}: {ex.Message}");
                return null;
            }
        }

        private async Task<string> GetBigImageUrlAsync(string smallUrl)
        {
            try
            {
                var imageId = GetImageIdFromUrl(smallUrl);
                var uri = new Uri(smallUrl);
                var domain = uri.Host;
                var folderNumber = uri.Segments[1].TrimEnd('/');

                var baseBigUrl = $"https://{domain}/{folderNumber}/thumb-1920-{imageId}";

                // Try different extensions like Python scraper
                string[] extensions = { "jpeg", "jpg", "png" };

                using (var httpClient = new HttpClient())
                {
                    httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");

                    foreach (var ext in extensions)
                    {
                        var bigUrl = $"{baseBigUrl}.{ext}";
                        try
                        {
                            var response = await httpClient.GetAsync(bigUrl);
                            if (response.IsSuccessStatusCode)
                            {
                                Console.WriteLine($"Found big image with extension {ext}: {bigUrl}");
                                return bigUrl;
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed {bigUrl}: {ex.Message}");
                        }
                    }
                }

                Console.WriteLine($"Big image not found for {smallUrl}");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting big image URL for {smallUrl}: {ex.Message}");
                return null;
            }
        }

        // Public method to get original image URL for wallpapers (async version)
        public async Task<string> GetOriginalImageUrlAsync(string imageId, string smallUrl)
        {
            try
            {
                var imageIdFromUrl = GetImageIdFromUrl(smallUrl);
                var uri = new Uri(smallUrl);
                var domainParts = uri.Host.Split('.');
                var domainShort = domainParts[0]; // e.g., images3

                string[] extensions = { "jpeg", "jpg", "png" };

                Console.WriteLine($"Getting original URL for imageId: {imageIdFromUrl}, domainShort: {domainShort}");

                using (var httpClient = new HttpClient())
                {
                    httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");
                    httpClient.DefaultRequestHeaders.Add("Referer", "https://wall.alphacoders.com/");

                    // Try each extension and validate with HEAD request
                    foreach (var ext in extensions)
                    {
                        var originalUrl = $"https://initiate.alphacoders.com/download/{domainShort}/{imageIdFromUrl}/{ext}";
                        Console.WriteLine($"Trying original URL: {originalUrl}");

                        try
                        {
                            var response = await httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Head, originalUrl));
                            if (response.IsSuccessStatusCode)
                            {
                                Console.WriteLine($"Valid original URL found: {originalUrl}");
                                return originalUrl;
                            }
                            else
                            {
                                Console.WriteLine($"URL returned {response.StatusCode}: {originalUrl}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to validate {originalUrl}: {ex.Message}");
                        }
                    }
                }

                Console.WriteLine($"No valid original URL found for {smallUrl}");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting original image URL for {smallUrl}: {ex.Message}");
                return null;
            }
        }

        private string GetOriginalImageUrl(string smallUrl)
        {
            try
            {
                var imageId = GetImageIdFromUrl(smallUrl);
                var uri = new Uri(smallUrl);
                var domainParts = uri.Host.Split('.');
                var domainShort = domainParts[0]; // e.g., images3

                // Return base URL pattern - extension will be determined dynamically
                var baseOriginalUrl = $"https://initiate.alphacoders.com/download/{domainShort}/{imageId}";
                Console.WriteLine($"Converting to original URL base:");
                Console.WriteLine($"  Small URL: {smallUrl}");
                Console.WriteLine($"  Image ID: {imageId}");
                Console.WriteLine($"  Domain short: {domainShort}");
                Console.WriteLine($"  Base Original URL: {baseOriginalUrl}");
                return baseOriginalUrl;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting original image URL for {smallUrl}: {ex.Message}");
                return null;
            }
        }



        private List<WallpaperItem> CreateWallpaperItemsFromUrls(List<string> urls)
        {
            var wallpapers = new List<WallpaperItem>();

            try
            {
                for (int i = 0; i < urls.Count; i++)
                {
                    var smallUrl = urls[i];
                    var imageId = GetImageIdFromUrl(smallUrl);

                    var wallpaper = new WallpaperItem
                    {
                        Id = imageId,
                        Title = $"Alpha Coders Wallpaper {imageId}",
                        ImageUrl = smallUrl, // Small thumb for grid
                        FullPhotoUrl = "", // Will be fetched on-demand when clicked
                        SourceUrl = "", // Will be set when clicked
                        Resolution = "3840x2160", // Default 4K
                        QualityTag = "4K",
                        Likes = new Random().Next(10, 1000).ToString(),
                        Downloads = new Random().Next(100, 5000).ToString(),
                        IsAI = false
                    };

                    wallpapers.Add(wallpaper);
                }

                LogDebug($"Created {wallpapers.Count} wallpaper items from {urls.Count} URLs");
            }
            catch (Exception ex)
            {
                LogDebug($"Error creating wallpaper items: {ex.Message}");
            }

            return wallpapers;
        }

        private List<WallpaperItem> CreateWallpaperItemsFromSmallUrls()
        {
            return CreateWallpaperItemsFromUrls(_allSmallUrls);
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
