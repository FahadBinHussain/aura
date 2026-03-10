using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Aura.Models;
using System.Linq;
using System.IO;

namespace Aura.Services
{
    public class AlphaCodersService
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl = "https://alphacoders.com";
        private readonly string _wallBaseUrl = "https://wall.alphacoders.com";
        private readonly AlphaCodersScraperService _scraperService;
        private static List<WallpaperItem> _cachedWallpapers = new List<WallpaperItem>();
        private static int _lastScrapedPage = 0;
        private static string _currentCategory = "4k"; // Track current category for cache management

        // Static debug logger that can be set by the UI
        public static Action<string> DebugLogger { get; set; }

        private static void LogDebug(string message)
        {
            System.Diagnostics.Debug.WriteLine(message);
            DebugLogger?.Invoke(message);
        }

        public AlphaCodersService()
        {
            _httpClient = new HttpClient();
            _scraperService = new AlphaCodersScraperService();

            // Set debug logger for scraper
            AlphaCodersScraperService.DebugLogger = LogDebug;

            // Set default headers for all requests
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
            _httpClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
            _httpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.5");
            _httpClient.DefaultRequestHeaders.Add("Referer", "https://wall.alphacoders.com/");
        }

        public async Task<List<WallpaperItem>> GetLatestWallpapersAsync(int page = 1, int count = 15)
        {
            try
            {
                LogDebug($"Requesting page {page} with {count} wallpapers");

                // Load the requested page if we haven't loaded it yet
                if (page > _lastScrapedPage)
                {
                    // Load only the pages we need, not all from 1 to current
                    for (int p = _lastScrapedPage + 1; p <= page; p++)
                    {
                        LogDebug($"Loading page {p} from scraper...");
                        var newWallpapers = await _scraperService.ScrapeWallpapersAsync(p, p);
                        if (newWallpapers.Count > 0)
                        {
                            _cachedWallpapers.AddRange(newWallpapers);
                            _lastScrapedPage = p;
                            LogDebug($"Loaded {newWallpapers.Count} wallpapers from page {p}");
                        }
                        else
                        {
                            LogDebug($"No wallpapers found on page {p}");
                            break; // Stop loading if we hit an empty page
                        }
                    }
                }

                // Calculate pagination based on cached wallpapers
                int startIndex = (page - 1) * count;
                int endIndex = Math.Min(startIndex + count, _cachedWallpapers.Count);

                if (startIndex >= _cachedWallpapers.Count)
                {
                    LogDebug($"No wallpapers available for page {page}");
                    return new List<WallpaperItem>();
                }

                var pageWallpapers = _cachedWallpapers.GetRange(startIndex, Math.Min(count, _cachedWallpapers.Count - startIndex));
                LogDebug($"Returning {pageWallpapers.Count} wallpapers for page {page}");

                return pageWallpapers;
            }
            catch (Exception ex)
            {
                LogDebug($"Error fetching wallpapers: {ex.Message}");
                return new List<WallpaperItem>();
            }
        }

        public async Task<List<WallpaperItem>> GetWallpapersByCategoryAsync(string category, int page = 1, int count = 15)
        {
            try
            {
                LogDebug($"Requesting category '{category}' page {page} with {count} wallpapers");

                // Clear cache if category changed
                if (category != _currentCategory)
                {
                    LogDebug($"Category changed from '{_currentCategory}' to '{category}', clearing cache");
                    _cachedWallpapers.Clear();
                    _lastScrapedPage = 0;
                    _currentCategory = category;
                }

                // Determine the search term based on category
                string searchTerm = category switch
                {
                    "4k" => "4k",
                    "harvest" => "harvest",
                    "rain" => "rain",
                    _ => "4k"
                };

                // Load the requested page with the specific search term
                if (page > _lastScrapedPage)
                {
                    for (int p = _lastScrapedPage + 1; p <= page; p++)
                    {
                        LogDebug($"Loading page {p} from scraper with search term '{searchTerm}'...");
                        var newWallpapers = await _scraperService.ScrapeWallpapersByCategoryAsync(searchTerm, p, p);
                        if (newWallpapers.Count > 0)
                        {
                            _cachedWallpapers.AddRange(newWallpapers);
                            _lastScrapedPage = p;
                            LogDebug($"Loaded {newWallpapers.Count} wallpapers from page {p}");
                        }
                        else
                        {
                            LogDebug($"No wallpapers found on page {p}");
                            break;
                        }
                    }
                }

                // Calculate pagination based on cached wallpapers
                int startIndex = (page - 1) * count;
                int endIndex = Math.Min(startIndex + count, _cachedWallpapers.Count);

                if (startIndex >= _cachedWallpapers.Count)
                {
                    LogDebug($"No wallpapers available for page {page}");
                    return new List<WallpaperItem>();
                }

                var pageWallpapers = _cachedWallpapers.GetRange(startIndex, Math.Min(count, _cachedWallpapers.Count - startIndex));
                LogDebug($"Returning {pageWallpapers.Count} wallpapers for page {page}");

                return pageWallpapers;
            }
            catch (Exception ex)
            {
                LogDebug($"Error fetching wallpapers by category: {ex.Message}");
                return new List<WallpaperItem>();
            }
        }



        public async Task<WallpaperItem> GetWallpaperDetailsAsync(string wallpaperId)
        {
            try
            {
                // Find the wallpaper in cached data first
                var cachedWallpaper = _cachedWallpapers.FirstOrDefault(w => w.Id == wallpaperId);
                if (cachedWallpaper != null)
                {
                    // Load full image if not already loaded
                    if (cachedWallpaper.ImageSource == null)
                    {
                        cachedWallpaper.ImageSource = await cachedWallpaper.LoadFullImageAsync();
                    }
                    return cachedWallpaper;
                }

                // Fallback to original HTML parsing method
                string requestUrl = $"{_wallBaseUrl}/big.php?i={wallpaperId}";

                HttpResponseMessage response = await _httpClient.GetAsync(requestUrl);

                if (response.IsSuccessStatusCode)
                {
                    string htmlContent = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"Successfully fetched wallpaper details, length: {htmlContent.Length}");
                    var wallpaper = ParseHtmlWallpaperDetails(htmlContent, wallpaperId);

                    if (wallpaper == null)
                    {
                        System.Diagnostics.Debug.WriteLine("Failed to parse wallpaper details, using placeholder");
                        return GeneratePlaceholderWallpaper(wallpaperId);
                    }

                    return wallpaper;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Error fetching wallpaper details: {response.StatusCode}");
                    return GeneratePlaceholderWallpaper(wallpaperId);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error fetching wallpaper details: {ex.Message}");
                return GeneratePlaceholderWallpaper(wallpaperId);
            }
        }

        public async Task<string> GetWallpaperDownloadUrlAsync(string wallpaperId, string fileType = "jpg")
        {
            try
            {
                // Try to find the original URL from scraped data first
                if (_cachedWallpapers.Count > 0)
                {
                    var cachedWallpaper = _cachedWallpapers.FirstOrDefault(w => w.Id == wallpaperId);
                    if (cachedWallpaper != null && !string.IsNullOrEmpty(cachedWallpaper.SourceUrl))
                    {
                        System.Diagnostics.Debug.WriteLine($"Using scraped original URL: {cachedWallpaper.SourceUrl}");
                        return cachedWallpaper.SourceUrl;
                    }
                }

                // Fallback to the old method
                return $"{_wallBaseUrl}/download/images5/{wallpaperId}/{fileType}";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting download URL: {ex.Message}");
                return null;
            }
        }

        private List<WallpaperItem> ParseHtmlWallpaperResponse(string htmlContent, int count)
        {
            var wallpapers = new List<WallpaperItem>();

            try
            {
                // Find all wallpaper items in the HTML
                // The wallpapers are typically in div elements with class "thumb-container-big"
                int startIndex = 0;
                int foundCount = 0;

                while (foundCount < count)
                {
                    // Find the next wallpaper container
                    string thumbContainerPattern = "<div class=\"thumb-container-big\"";
                    startIndex = htmlContent.IndexOf(thumbContainerPattern, startIndex);

                    if (startIndex == -1)
                        break; // No more wallpapers found

                    // Find the wallpaper ID
                    string idPattern = "data-id=\"";
                    int idStartIndex = htmlContent.IndexOf(idPattern, startIndex);

                    if (idStartIndex != -1)
                    {
                        idStartIndex += idPattern.Length;
                        int idEndIndex = htmlContent.IndexOf('"', idStartIndex);
                        string id = htmlContent.Substring(idStartIndex, idEndIndex - idStartIndex);

                        // Find the image URL
                        string imgPattern = "<img src=\"";
                        int imgStartIndex = htmlContent.IndexOf(imgPattern, startIndex);

                        if (imgStartIndex != -1)
                        {
                            imgStartIndex += imgPattern.Length;
                            int imgEndIndex = htmlContent.IndexOf('"', imgStartIndex);
                            string imageUrl = htmlContent.Substring(imgStartIndex, imgEndIndex - imgStartIndex);
                    System.Diagnostics.Debug.WriteLine($"Extracted detail image URL: {imageUrl}");

                    // Ensure the URL is absolute
                    if (imageUrl.StartsWith("//"))
                    {
                        imageUrl = "https:" + imageUrl;
                        System.Diagnostics.Debug.WriteLine($"Fixed protocol-relative detail URL to: {imageUrl}");
                    }
                    else if (imageUrl.StartsWith("/"))
                    {
                        imageUrl = _wallBaseUrl + imageUrl;
                        System.Diagnostics.Debug.WriteLine($"Fixed relative detail URL to: {imageUrl}");
                    }

                            // Ensure the URL is absolute
                            if (imageUrl.StartsWith("//"))
                            {
                                imageUrl = "https:" + imageUrl;
                                System.Diagnostics.Debug.WriteLine($"Fixed protocol-relative URL to: {imageUrl}");
                            }
                            else if (imageUrl.StartsWith("/"))
                            {
                                imageUrl = _wallBaseUrl + imageUrl;
                                System.Diagnostics.Debug.WriteLine($"Fixed relative URL to: {imageUrl}");
                            }

                            // Ensure the URL is absolute
                            if (imageUrl.StartsWith("//"))
                            {
                                imageUrl = "https:" + imageUrl;
                                System.Diagnostics.Debug.WriteLine($"Fixed protocol-relative URL to: {imageUrl}");
                            }
                            else if (imageUrl.StartsWith("/"))
                            {
                                imageUrl = _wallBaseUrl + imageUrl;
                                System.Diagnostics.Debug.WriteLine($"Fixed relative URL to: {imageUrl}");
                            }

                            // Find the title
                            string titlePattern = "alt=\"";
                            int titleStartIndex = htmlContent.IndexOf(titlePattern, imgStartIndex);

                            if (titleStartIndex != -1)
                            {
                                titleStartIndex += titlePattern.Length;
                                int titleEndIndex = htmlContent.IndexOf('"', titleStartIndex);
                                string title = htmlContent.Substring(titleStartIndex, titleEndIndex - titleStartIndex);

                                // Create the wallpaper item
                                var wallpaper = new WallpaperItem
                                {
                                    Id = id,
                                    Title = title,
                                    ImageUrl = imageUrl,
                                    FullPhotoUrl = $"{_wallBaseUrl}/big.php?i={id}",
                                    SourceUrl = $"{_wallBaseUrl}/wallpaper.php?i={id}",
                                    Resolution = "3840x2160", // Assuming 4K resolution
                                    QualityTag = "4K",
                                    Likes = new Random().Next(10, 1000).ToString(), // Placeholder
                                    Downloads = new Random().Next(100, 5000).ToString(), // Placeholder
                                    IsAI = false // We can't determine this from the HTML
                                };

                                // Load the image
                                try
                                {
                                    var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(imageUrl));
                                    wallpaper.ImageSource = bitmap;
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"Error loading image: {ex.Message}");
                                    // Use placeholder image if loading fails
                                    wallpaper.ImageSource = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/placeholder-wallpaper-1000.png"));
                                }

                                wallpapers.Add(wallpaper);
                                foundCount++;
                            }
                        }
                    }

                    // Move to the next wallpaper container
                    startIndex += thumbContainerPattern.Length;
                }

                // If we didn't find any wallpapers, return placeholders
                if (wallpapers.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("No wallpapers found in HTML, using placeholders");
                    return GeneratePlaceholderWallpapers(count);
                }

                return wallpapers;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error parsing HTML: {ex.Message}");
                return GeneratePlaceholderWallpapers(count);
            }
        }

        private WallpaperItem ParseHtmlWallpaperDetails(string htmlContent, string wallpaperId)
        {
            try
            {
                // Find the image URL
                string imgPattern = "<img id=\"wallpaper\" src=\"";
                int imgStartIndex = htmlContent.IndexOf(imgPattern);

                if (imgStartIndex != -1)
                {
                    imgStartIndex += imgPattern.Length;
                    int imgEndIndex = htmlContent.IndexOf('"', imgStartIndex);
                    string imageUrl = htmlContent.Substring(imgStartIndex, imgEndIndex - imgStartIndex);

                    // Find the title
                    string titlePattern = "<title>";
                    int titleStartIndex = htmlContent.IndexOf(titlePattern);

                    if (titleStartIndex != -1)
                    {
                        titleStartIndex += titlePattern.Length;
                        int titleEndIndex = htmlContent.IndexOf(" HD", titleStartIndex);
                        if (titleEndIndex == -1) titleEndIndex = htmlContent.IndexOf(" - Alpha", titleStartIndex);
                        if (titleEndIndex == -1) titleEndIndex = htmlContent.IndexOf("</title>", titleStartIndex);

                        string title = "Unknown Wallpaper";
                        if (titleEndIndex > titleStartIndex)
                        {
                            title = htmlContent.Substring(titleStartIndex, titleEndIndex - titleStartIndex).Trim();
                        }

                        // Find the resolution
                        string resPattern = "<div class=\"center\">"; // This is a common container before resolution
                        int resStartIndex = htmlContent.IndexOf(resPattern);
                        string resolution = "3840x2160"; // Default to 4K

                        if (resStartIndex != -1)
                        {
                            string resDigitPattern = "\\d+x\\d+";
                            // Use a simple approach to find resolution-like text
                            int searchEndIndex = resStartIndex + 500; // Search a reasonable range
                            for (int i = resStartIndex; i < searchEndIndex && i < htmlContent.Length - 10; i++)
                            {
                                if (char.IsDigit(htmlContent[i]) &&
                                    htmlContent[i+1] >= '0' && htmlContent[i+1] <= '9' &&
                                    htmlContent[i+2] >= '0' && htmlContent[i+2] <= '9' &&
                                    htmlContent[i+3] >= '0' && htmlContent[i+3] <= '9' &&
                                    htmlContent[i+4] == 'x')
                                {
                                    int resEndIndex = htmlContent.IndexOf('<', i);
                                    if (resEndIndex > i)
                                    {
                                        resolution = htmlContent.Substring(i, resEndIndex - i).Trim();
                                        break;
                                    }
                                }
                            }
                        }

                        // Create the wallpaper item
                        var wallpaper = new WallpaperItem
                        {
                            Id = wallpaperId,
                            Title = title,
                            ImageUrl = imageUrl,
                            FullPhotoUrl = imageUrl,
                            SourceUrl = $"{_wallBaseUrl}/wallpaper.php?i={wallpaperId}",
                            Resolution = resolution,
                            QualityTag = DetermineQualityTag(resolution),
                            Likes = new Random().Next(10, 1000).ToString(), // Placeholder
                            Downloads = new Random().Next(100, 5000).ToString(), // Placeholder
                            IsAI = false // We can't determine this from the HTML
                        };

                        // Load the image
                        try
                        {
                            var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(imageUrl));
                            wallpaper.ImageSource = bitmap;
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error loading detail image: {ex.Message}");
                            // Use placeholder image if loading fails
                            wallpaper.ImageSource = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/placeholder-wallpaper-1000.png"));
                        }

                        return wallpaper;
                    }
                }

                // If parsing fails, return null
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error parsing HTML details: {ex.Message}");
                return null;
            }
        }

        private List<WallpaperItem> ParseWallpaperResponse(string jsonContent)
        {
            var wallpapers = new List<WallpaperItem>();

            try
            {
                // Parse the JSON response
                using (JsonDocument doc = JsonDocument.Parse(jsonContent))
                {
                    JsonElement root = doc.RootElement;

                    // Check if the API returned success
                    if (root.TryGetProperty("success", out JsonElement success) && success.GetBoolean())
                    {
                        // Get the wallpapers array
                        if (root.TryGetProperty("wallpapers", out JsonElement wallpapersArray))
                        {
                            foreach (JsonElement wallpaperElement in wallpapersArray.EnumerateArray())
                            {
                                var wallpaper = new WallpaperItem
                                {
                                    Id = wallpaperElement.GetProperty("id").GetString(),
                                    Title = wallpaperElement.GetProperty("name").GetString(),
                                    ImageUrl = wallpaperElement.GetProperty("url_thumb").GetString(),
                                    FullPhotoUrl = wallpaperElement.GetProperty("url_image").GetString(),
                                    SourceUrl = wallpaperElement.GetProperty("url_page").GetString(),
                                    Resolution = $"{wallpaperElement.GetProperty("width").GetInt32()}x{wallpaperElement.GetProperty("height").GetInt32()}",
                                    QualityTag = DetermineQualityTag(wallpaperElement.GetProperty("width").GetInt32(), wallpaperElement.GetProperty("height").GetInt32()),
                                    Likes = wallpaperElement.GetProperty("favorites").GetString(),
                                    Downloads = wallpaperElement.GetProperty("views").GetString(),
                                    IsAI = false // Alpha Coders doesn't specify AI-generated content in the API
                                };

                                wallpapers.Add(wallpaper);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error parsing wallpaper response: {ex.Message}");
            }

            return wallpapers;
        }

        private string DetermineQualityTag(int width, int height)
        {
            int maxDimension = Math.Max(width, height);

            if (maxDimension >= 7680) return "8K";
            if (maxDimension >= 5120) return "5K";
            if (maxDimension >= 3840) return "4K";
            if (maxDimension >= 2560) return "2K";

            return string.Empty;
        }

        private string DetermineQualityTag(string resolution)
        {
            if (string.IsNullOrEmpty(resolution))
                return string.Empty;

            try
            {
                // Parse resolution in format "WidthxHeight"
                string[] dimensions = resolution.Split('x');
                if (dimensions.Length == 2 && int.TryParse(dimensions[0], out int width) && int.TryParse(dimensions[1], out int height))
                {
                    return DetermineQualityTag(width, height);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error parsing resolution: {ex.Message}");
            }

            return string.Empty;
        }

        private List<WallpaperItem> GeneratePlaceholderWallpapers(int count)
        {
            var wallpapers = new List<WallpaperItem>();

            for (int i = 1; i <= count; i++)
            {
                // Create a new BitmapImage directly in the constructor
                var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/placeholder-wallpaper-1000.png"));

                var wallpaper = new WallpaperItem
                {
                    Id = i.ToString(),
                    Title = $"Alpha Coders Wallpaper {i}",
                    ImageUrl = "ms-appx:///Assets/placeholder-wallpaper-1000.png",
                    FullPhotoUrl = "ms-appx:///Assets/placeholder-wallpaper-1000.png",
                    SourceUrl = $"https://wall.alphacoders.com/wallpaper.php?i={i}",
                    Resolution = "3840x2160",
                    QualityTag = "4K",
                    Likes = new Random().Next(10, 1000).ToString(),
                    Downloads = new Random().Next(100, 5000).ToString(),
                    IsAI = i % 3 == 0, // Every third wallpaper is AI-generated for demo purposes
                    ImageSource = bitmap // Set the ImageSource property directly
                };

                wallpapers.Add(wallpaper);
            }

            return wallpapers;
        }

        private WallpaperItem GeneratePlaceholderWallpaper(string id)
        {
            int idNumber = int.TryParse(id, out int result) ? result : 1;

            // Create a new BitmapImage directly in the constructor
            var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/placeholder-wallpaper-1000.png"));

            return new WallpaperItem
            {
                Id = id,
                Title = $"Alpha Coders Wallpaper {id}",
                ImageUrl = "ms-appx:///Assets/placeholder-wallpaper-1000.png",
                FullPhotoUrl = "ms-appx:///Assets/placeholder-wallpaper-1000.png",
                SourceUrl = $"https://wall.alphacoders.com/wallpaper.php?i={id}",
                Resolution = "3840x2160",
                QualityTag = "4K",
                Likes = new Random().Next(10, 1000).ToString(),
                Downloads = new Random().Next(100, 5000).ToString(),
                IsAI = idNumber % 3 == 0, // Every third wallpaper is AI-generated for demo purposes
                ImageSource = bitmap // Set the ImageSource property directly
            };
        }
    }
}
