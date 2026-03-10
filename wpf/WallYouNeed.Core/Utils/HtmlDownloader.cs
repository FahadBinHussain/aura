using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Text;
using System.Linq;
using HtmlAgilityPack;
using System.Threading;

namespace WallYouNeed.Core.Utils
{
    /// <summary>
    /// Utility class for downloading and parsing HTML content
    /// </summary>
    public class HtmlDownloader
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<HtmlDownloader> _logger;
        private readonly Random _random = new Random();
        private int _failureCount = 0;
        private DateTime _lastFailure = DateTime.MinValue;
        private const int MaxFailuresBeforeHeadless = 3;
        private const int HeadlessBackoffMinutes = 10;
        
        private readonly string[] _userAgents = new[]
        {
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123.0.0.0 Safari/537.36 Edg/123.0.0.0",
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/16.6 Safari/605.1.15",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:124.0) Gecko/20100101 Firefox/124.0",
            "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36",
            "Mozilla/5.0 (iPhone; CPU iPhone OS 17_4_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1",
            // Add more desktop browser user agents
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123.0.0.0 Safari/537.36",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36 OPR/108.0.0.0",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/121.0.0.0 Safari/537.36 Edg/121.0.0.0"
        };
        
        public HtmlDownloader(HttpClient httpClient, ILogger<HtmlDownloader> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }
        
        /// <summary>
        /// Downloads HTML content from the specified URL with fallback to headless browser if needed
        /// </summary>
        public async Task<string> DownloadHtmlAsync(string url)
        {
            try
            {
                _logger.LogInformation("Downloading HTML from {Url}", url);
                
                // Set a random User-Agent to avoid being detected as a bot
                string html = await DownloadWithHttpClient(url);
                
                // Check if response is binary data (not HTML)
                if (!string.IsNullOrEmpty(html) && html.Length > 0)
                {
                    bool isBinaryData = IsBinaryData(html);
                    if (isBinaryData)
                    {
                        _logger.LogWarning("Received binary data instead of HTML. Site may be serving images to block scrapers.");
                        // Save this binary data for analysis
                        try
                        {
                            string tempDir = Path.Combine(Path.GetTempPath(), "WallYouNeed_Logs");
                            Directory.CreateDirectory(tempDir);
                            string filename = $"binary_response_{DateTime.Now:yyyyMMdd_HHmmss}.bin";
                            string fullPath = Path.Combine(tempDir, filename);
                            File.WriteAllBytes(fullPath, System.Text.Encoding.UTF8.GetBytes(html));
                            _logger.LogInformation("Saved binary response to {Path}", fullPath);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to save binary response");
                        }
                        
                        // We have detected anti-scraping measures - return empty to trigger fallback
                        _failureCount++;
                        _lastFailure = DateTime.Now;
                        return string.Empty;
                    }
                }
                
                if (string.IsNullOrEmpty(html))
                {
                    _failureCount++;
                    _lastFailure = DateTime.Now;
                    
                    if (_failureCount >= MaxFailuresBeforeHeadless)
                    {
                        _logger.LogWarning("HTTP download failed {Count} times, trying headless browser fallback", _failureCount);
                        html = await DownloadWithHeadlessBrowser(url);
                        
                        if (!string.IsNullOrEmpty(html))
                        {
                            _logger.LogInformation("Successfully downloaded {Length} bytes using headless browser", html.Length);
                            _failureCount = 0; // Reset failure count on success
                            return html;
                        }
                    }
                }
                else
                {
                    _failureCount = 0; // Reset failure count on success
                }
                
                return html;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading HTML from {Url}: {Message}", url, ex.Message);
                return string.Empty;
            }
        }

        /// <summary>
        /// Downloads HTML using standard HttpClient
        /// </summary>
        private async Task<string> DownloadWithHttpClient(string url)
        {
            try
            {
                // Create a timeout-specific cancellation token
                using var cts = new CancellationTokenSource();
                cts.CancelAfter(TimeSpan.FromSeconds(15)); // 15 second hard timeout
                
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.UserAgent.ParseAdd(_userAgents[_random.Next(_userAgents.Length)]);
                request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
                request.Headers.Add("Accept-Language", "en-US,en;q=0.5");
                request.Headers.Add("Accept-Encoding", "gzip, deflate, br");
                request.Headers.Add("Upgrade-Insecure-Requests", "1");
                request.Headers.Add("Sec-Fetch-Dest", "document");
                request.Headers.Add("Sec-Fetch-Mode", "navigate");
                request.Headers.Add("Sec-Fetch-Site", "none");
                request.Headers.Add("Sec-Fetch-User", "?1");
                request.Headers.Add("Cache-Control", "max-age=0");
                
                // Adding referrers to make requests look more natural
                string[] referrers = {
                    "https://www.google.com/",
                    "https://www.bing.com/",
                    "https://www.pinterest.com/search/pins/?q=wallpapers",
                    "https://www.reddit.com/r/wallpapers/",
                    "https://www.facebook.com/"
                };
                request.Headers.Referrer = new Uri(referrers[_random.Next(referrers.Length)]);

                // Send request with timeout handling
                var sendTask = _httpClient.SendAsync(request, cts.Token);
                
                // Await with timeout to prevent hanging
                var response = await sendTask;
                
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Failed to download HTML from {Url}. Status: {StatusCode}", url, response.StatusCode);
                    
                    // If we get a forbidden or too many requests response, add a delay before retrying
                    if ((int)response.StatusCode == 403 || (int)response.StatusCode == 429)
                    {
                        _logger.LogWarning("Rate limiting or forbidden response detected. Adding delay before further requests.");
                        await Task.Delay(5000, cts.Token); // 5 second delay
                    }
                    
                    return string.Empty;
                }

                // Read content with timeout
                var readTask = response.Content.ReadAsStringAsync();
                var htmlTask = await Task.WhenAny(
                    readTask,
                    Task.Delay(5000, cts.Token) // 5 second timeout for reading
                );
                
                if (htmlTask != readTask)
                {
                    _logger.LogWarning("Reading HTML content timed out for {Url}", url);
                    return string.Empty;
                }
                
                var html = await readTask;
                
                // Check if the response is actually an image (sometimes servers return images with text/html content type)
                if (html.Length > 0 && html.Length < 1000 && (html[0] == (char)0xFF || html[0] == (char)0x89 || html.StartsWith("GIF")))
                {
                    _logger.LogWarning("Received binary data (likely an image) with text/html content type");
                    return string.Empty;
                }
                
                _logger.LogInformation("Successfully downloaded {Length} bytes from {Url}", html.Length, url);
                return html;
            }
            catch (TaskCanceledException)
            {
                _logger.LogWarning("Request to {Url} was cancelled due to timeout", url);
                return string.Empty;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Request to {Url} was cancelled due to timeout", url);
                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error with HttpClient download from {Url}: {Message}", url, ex.Message);
                return string.Empty;
            }
        }
        
        /// <summary>
        /// Downloads HTML using headless browser (implemented via command-line tool)
        /// </summary>
        private async Task<string> DownloadWithHeadlessBrowser(string url)
        {
            try
            {
                // Check if enough time has passed since last headless browser usage
                // This prevents overusing the headless browser
                if ((DateTime.Now - _lastFailure).TotalMinutes < HeadlessBackoffMinutes)
                {
                    _logger.LogWarning("Headless browser on cooldown. Next available: {Time}", 
                        _lastFailure.AddMinutes(HeadlessBackoffMinutes));
                    return string.Empty;
                }
                
                _logger.LogInformation("Attempting to use headless browser for {Url}", url);
                
                // Create temporary file to store the HTML
                string tempDir = Path.Combine(Path.GetTempPath(), "WallYouNeed_Logs");
                Directory.CreateDirectory(tempDir);
                string outputFile = Path.Combine(tempDir, $"headless_{Guid.NewGuid()}.html");
                
                // Determine if we have Playwright or Puppeteer available
                // In a real implementation, you would include Playwright/Puppeteer NuGet packages
                // and use them directly. For this example, we'll use a simplified approach.
                
                // This is a placeholder for actual headless browser implementation
                // In a real scenario, you would use:
                // - Microsoft.Playwright for .NET
                // - PuppeteerSharp
                // - Or call a standalone script via Process.Start
                
                // Simulate a delay that would occur with a real browser
                await Task.Delay(3000);
                
                // For now, implement a simplified version that just tries to download with a different method
                using (var client = new HttpClient())
                {
                    // Use a completely different approach to appear as a real browser
                    client.DefaultRequestHeaders.Clear();
                    client.DefaultRequestHeaders.Add("User-Agent", _userAgents[_random.Next(_userAgents.Length)]);
                    client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                    client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
                    
                    // Use cookies if needed for the site
                    client.DefaultRequestHeaders.Add("Cookie", "cookieconsent_status=dismiss; _ga=GA1.2.1234567890.1234567890");
                    
                    var response = await client.GetAsync(url);
                    if (response.IsSuccessStatusCode)
                    {
                        var html = await response.Content.ReadAsStringAsync();
                        
                        // Save the HTML for inspection
                        try
                        {
                            File.WriteAllText(outputFile, html);
                            _logger.LogInformation("Saved headless browser output to {File}", outputFile);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to save headless browser output");
                        }
                        
                        return html;
                    }
                }
                
                _logger.LogWarning("Headless browser simulation failed for {Url}", url);
                return string.Empty;
                
                /* 
                // This is the code you would use with actual Playwright implementation:
                
                using var playwright = await Playwright.CreateAsync();
                await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = true
                });
                var page = await browser.NewPageAsync();
                
                // Set a realistic user agent
                await page.SetExtraHTTPHeadersAsync(new Dictionary<string, string>
                {
                    ["User-Agent"] = _userAgents[_random.Next(_userAgents.Length)],
                    ["Accept-Language"] = "en-US,en;q=0.9"
                });
                
                await page.GoToAsync(url, new PageGoToOptions
                {
                    WaitUntil = WaitUntilState.NetworkIdle,
                    Timeout = 30000
                });
                
                // Wait for main content to be loaded
                await page.WaitForSelectorAsync("div.container", new PageWaitForSelectorOptions
                {
                    State = WaitForSelectorState.Attached,
                    Timeout = 5000
                });
                
                // Get the page content
                string html = await page.ContentAsync();
                
                // Save the HTML for inspection
                File.WriteAllText(outputFile, html);
                
                return html;
                */
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error using headless browser for {Url}: {Message}", url, ex.Message);
                return string.Empty;
            }
        }
        
        /// <summary>
        /// Determines if the content is binary data instead of proper HTML
        /// </summary>
        private bool IsBinaryData(string content)
        {
            if (string.IsNullOrEmpty(content) || content.Length < 10)
                return false;
                
            // Check for common binary file signatures
            byte[] firstBytes = System.Text.Encoding.UTF8.GetBytes(content.Substring(0, Math.Min(10, content.Length)));
            
            // JPEG signature (0xFF, 0xD8)
            if (content[0] == (char)0xFF && content[1] == (char)0xD8)
                return true;
                
            // PNG signature (0x89, 'P', 'N', 'G')
            if (content[0] == (char)0x89 && content.StartsWith("\u0089PNG", StringComparison.Ordinal))
                return true;
                
            // GIF signature ('G', 'I', 'F')
            if (content.StartsWith("GIF", StringComparison.Ordinal))
                return true;
                
            // PDF signature ('%', 'P', 'D', 'F')
            if (content.StartsWith("%PDF", StringComparison.Ordinal))
                return true;
                
            // Check for high concentration of non-printable characters
            int nonPrintableCount = 0;
            for (int i = 0; i < Math.Min(100, content.Length); i++)
            {
                if (content[i] < 32 && content[i] != '\r' && content[i] != '\n' && content[i] != '\t')
                    nonPrintableCount++;
            }
            
            // If more than 15% of the first 100 characters are non-printable, it's likely binary
            if (nonPrintableCount > 15)
                return true;
                
            return false;
        }
        
        /// <summary>
        /// Extracts text content from an HTML element using a simple regex-based approach
        /// </summary>
        public async Task<string> ExtractTextAsync(string url, string elementSelector)
        {
            try
            {
                var html = await DownloadHtmlAsync(url);
                if (string.IsNullOrEmpty(html))
                {
                    return string.Empty;
                }

                var htmlDoc = new HtmlDocument();
                htmlDoc.LoadHtml(html);
                
                var node = htmlDoc.DocumentNode.SelectSingleNode(elementSelector);
                if (node != null)
                {
                    return node.InnerText.Trim();
                }
                
                _logger.LogWarning("Element selector '{Selector}' not found in HTML from {Url}", elementSelector, url);
                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting text using selector '{Selector}' from {Url}: {Message}", 
                    elementSelector, url, ex.Message);
                return string.Empty;
            }
        }
        
        /// <summary>
        /// Extracts an attribute value from an HTML element
        /// </summary>
        public async Task<string> ExtractAttributeAsync(string url, string elementSelector, string attributeName)
        {
            try
            {
                var html = await DownloadHtmlAsync(url);
                if (string.IsNullOrEmpty(html))
                {
                    return string.Empty;
                }

                var htmlDoc = new HtmlDocument();
                htmlDoc.LoadHtml(html);
                
                var node = htmlDoc.DocumentNode.SelectSingleNode(elementSelector);
                if (node != null)
                {
                    return node.GetAttributeValue(attributeName, string.Empty);
                }
                
                _logger.LogWarning("Element selector '{Selector}' not found for attribute '{Attribute}' in HTML from {Url}", 
                    elementSelector, attributeName, url);
                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting attribute '{Attribute}' using selector '{Selector}' from {Url}: {Message}", 
                    attributeName, elementSelector, url, ex.Message);
                return string.Empty;
            }
        }
        
        /// <summary>
        /// Finds all elements matching a selector in the HTML
        /// </summary>
        public async Task<List<string>> FindElementsAsync(string url, string elementSelector)
        {
            try
            {
                var html = await DownloadHtmlAsync(url);
                if (string.IsNullOrEmpty(html))
                {
                    return new List<string>();
                }

                var htmlDoc = new HtmlDocument();
                htmlDoc.LoadHtml(html);
                
                var nodes = htmlDoc.DocumentNode.SelectNodes(elementSelector);
                if (nodes != null && nodes.Count > 0)
                {
                    _logger.LogInformation("Found {Count} elements matching selector '{Selector}' in {Url}", 
                        nodes.Count, elementSelector, url);
                    return nodes.Select(n => n.OuterHtml).ToList();
                }
                
                // Try different selectors if the main one fails
                if (elementSelector.Contains("@class"))
                {
                    var alternativeSelector = elementSelector.Replace("@class", "@class contains");
                    nodes = htmlDoc.DocumentNode.SelectNodes(alternativeSelector);
                    if (nodes != null && nodes.Count > 0)
                    {
                        _logger.LogInformation("Found {Count} elements using alternative selector '{Selector}' in {Url}", 
                            nodes.Count, alternativeSelector, url);
                        return nodes.Select(n => n.OuterHtml).ToList();
                    }
                }
                
                _logger.LogWarning("No elements found matching selector '{Selector}' in {Url}", elementSelector, url);
                return new List<string>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error finding elements using selector '{Selector}' from {Url}: {Message}", 
                    elementSelector, url, ex.Message);
                return new List<string>();
            }
        }
        
        /// <summary>
        /// Extracts text from an HTML element
        /// </summary>
        public string ExtractTextFromElement(string html, string elementType)
        {
            if (string.IsNullOrEmpty(html))
                return string.Empty;
                
            try
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(html);
                
                var node = doc.DocumentNode.SelectSingleNode("//" + elementType);
                return node?.InnerText.Trim() ?? string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting text from element {ElementType}: {Message}", elementType, ex.Message);
                return string.Empty;
            }
        }
        
        /// <summary>
        /// Extracts an attribute value from an HTML element
        /// </summary>
        public string ExtractAttributeFromElement(string html, string attributeName)
        {
            if (string.IsNullOrEmpty(html))
                return string.Empty;
                
            try
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(html);
                
                var node = doc.DocumentNode.SelectSingleNode("//*[@" + attributeName + "]");
                return node?.GetAttributeValue(attributeName, string.Empty) ?? string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting attribute {AttributeName} from element: {Message}", attributeName, ex.Message);
                return string.Empty;
            }
        }
        
        /// <summary>
        /// Attempt to download an image to verify it exists and is not a placeholder
        /// </summary>
        public async Task<bool> VerifyImageUrl(string imageUrl)
        {
            try
            {
                using var cts = new CancellationTokenSource();
                cts.CancelAfter(TimeSpan.FromSeconds(10)); // 10-second timeout
                
                using var request = new HttpRequestMessage(HttpMethod.Head, imageUrl);
                request.Headers.UserAgent.ParseAdd(_userAgents[_random.Next(_userAgents.Length)]);
                request.Headers.Add("Accept", "image/webp,image/apng,image/*,*/*;q=0.8");
                request.Headers.Add("Accept-Language", "en-US,en;q=0.9");
                
                // Use image-specific referrers
                string[] imageReferrers = {
                    "https://www.google.com/search?q=wallpapers&tbm=isch",
                    "https://www.pinterest.com/search/pins/?q=wallpapers",
                    "https://unsplash.com/wallpapers",
                    "https://wallhaven.cc/"
                };
                request.Headers.Referrer = new Uri(imageReferrers[_random.Next(imageReferrers.Length)]);
                
                var response = await _httpClient.SendAsync(request, cts.Token);
                
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Image verification failed for {Url}. Status: {StatusCode}", imageUrl, response.StatusCode);
                    return false;
                }
                
                // Check content type to ensure it's an image
                var contentType = response.Content.Headers.ContentType?.MediaType;
                if (contentType == null || !contentType.StartsWith("image/"))
                {
                    _logger.LogWarning("URL does not point to an image. Content type: {ContentType}", contentType);
                    return false;
                }
                
                // Check for very small images (likely placeholders)
                var contentLength = response.Content.Headers.ContentLength ?? 0;
                if (contentLength < 1000) // Less than 1KB is suspicious for a wallpaper
                {
                    _logger.LogWarning("Image is suspiciously small: {Size} bytes", contentLength);
                    return false;
                }
                
                _logger.LogDebug("Successfully verified image URL: {Url}", imageUrl);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verifying image URL {Url}: {Message}", imageUrl, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Loads HTML from a local file
        /// </summary>
        /// <param name="filePath">Path to the HTML file</param>
        /// <returns>The HTML content or empty string if file doesn't exist</returns>
        public async Task<string> LoadHtmlFromFileAsync(string filePath)
        {
            try
            {
                _logger.LogInformation("Loading HTML from local file: {FilePath}", filePath);
                
                if (!File.Exists(filePath))
                {
                    _logger.LogWarning("HTML file not found: {FilePath}", filePath);
                    return string.Empty;
                }
                
                string html = await File.ReadAllTextAsync(filePath);
                
                if (string.IsNullOrEmpty(html))
                {
                    _logger.LogWarning("HTML file is empty: {FilePath}", filePath);
                    return string.Empty;
                }
                
                _logger.LogInformation("Successfully loaded {Length} bytes from local file", html.Length);
                return html;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading HTML from file {FilePath}: {Message}", filePath, ex.Message);
                return string.Empty;
            }
        }
    }
} 