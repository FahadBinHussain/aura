using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Aura.Models;

namespace Aura.Services
{
    public sealed class PublicWallpaperService
    {
        private const string Wallhaven = "Wallhaven";
        private const string Bing = "Bing Wallpaper Archive";
        private const string SimpleDesktops = "Simple Desktops";
        private const string WallpaperHub = "WallpaperHub";
        private const string Pexels = "Pexels";
        private const string Pixabay = "Pixabay";

        private static readonly HashSet<string> SupportedPlatforms = new(StringComparer.OrdinalIgnoreCase)
        {
            Wallhaven,
            Bing,
            SimpleDesktops,
            WallpaperHub,
            Pexels,
            Pixabay
        };

        private readonly HttpClient _httpClient;

        public PublicWallpaperService()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Aura/1.0");
            _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json,text/html,image/*,*/*");
            _httpClient.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        }

        public static bool IsSupportedPlatform(string platformName)
        {
            return SupportedPlatforms.Contains(platformName ?? string.Empty);
        }

        public static IReadOnlyList<string> GetSupportedPlatformNames()
        {
            return SupportedPlatforms.OrderBy(platform => platform).ToList();
        }

        public static IReadOnlyList<string> GetModes(string platformName)
        {
            return platformName switch
            {
                Wallhaven => new[] { "Toplist", "Latest", "Random" },
                Pexels => new[] { "Curated", "Nature", "Space" },
                Pixabay => new[] { "Backgrounds", "Nature", "Places" },
                _ => Array.Empty<string>()
            };
        }

        public static string GetDefaultMode(string platformName)
        {
            var modes = GetModes(platformName);
            return modes.Count > 0 ? modes[0] : string.Empty;
        }

        public static string GetPlatformDescription(string platformName)
        {
            return platformName switch
            {
                Wallhaven => "High-resolution wallpapers from Wallhaven's public JSON search endpoint.",
                Bing => "Recent daily Bing homepage wallpapers from Microsoft's public archive endpoint.",
                SimpleDesktops => "Minimal, distraction-free wallpapers from Simple Desktops.",
                WallpaperHub => "Microsoft, Surface, Windows, and Bing wallpapers from WallpaperHub.",
                Pexels => "Free stock photos via the official Pexels API. Requires PEXELS_API_KEY.",
                Pixabay => "Royalty-free images via the official Pixabay API. Requires PIXABAY_API_KEY.",
                _ => "Browse wallpapers from this source."
            };
        }

        public async Task<List<WallpaperItem>> GetWallpapersAsync(
            string platformName,
            int page,
            string mode,
            CancellationToken cancellationToken = default)
        {
            page = Math.Max(page, 1);

            return platformName switch
            {
                Wallhaven => await GetWallhavenWallpapersAsync(page, mode, cancellationToken),
                Bing => await GetBingWallpapersAsync(page, cancellationToken),
                SimpleDesktops => await GetSimpleDesktopWallpapersAsync(page, cancellationToken),
                WallpaperHub => await GetWallpaperHubWallpapersAsync(page, cancellationToken),
                Pexels => await GetPexelsWallpapersAsync(page, mode, cancellationToken),
                Pixabay => await GetPixabayWallpapersAsync(page, mode, cancellationToken),
                _ => throw new NotSupportedException($"{platformName} is not implemented yet.")
            };
        }

        public async Task<byte[]> GetImageBytesAsync(string imageUrl, CancellationToken cancellationToken = default)
        {
            return await _httpClient.GetByteArrayAsync(imageUrl, cancellationToken);
        }

        private async Task<List<WallpaperItem>> GetWallhavenWallpapersAsync(
            int page,
            string mode,
            CancellationToken cancellationToken)
        {
            var sorting = mode switch
            {
                "Latest" => "date_added",
                "Random" => "random",
                _ => "toplist"
            };

            var url = $"https://wallhaven.cc/api/v1/search?categories=111&purity=100&sorting={sorting}&order=desc&page={page}";
            var json = await _httpClient.GetStringAsync(url, cancellationToken);
            var wallpapers = new List<WallpaperItem>();

            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("data", out var dataElement) || dataElement.ValueKind != JsonValueKind.Array)
            {
                return wallpapers;
            }

            foreach (var item in dataElement.EnumerateArray())
            {
                var id = GetString(item, "id");
                var fullUrl = GetString(item, "path");
                var sourceUrl = GetString(item, "url");
                var resolution = GetString(item, "resolution");
                var category = GetString(item, "category", "wallpaper");
                var thumbnail = GetNestedString(item, "thumbs", "large");

                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(fullUrl))
                {
                    continue;
                }

                wallpapers.Add(new WallpaperItem
                {
                    Id = id,
                    Title = $"Wallhaven #{id}",
                    Description = $"{ToTitleCase(category)} wallpaper from Wallhaven.",
                    ImageUrl = string.IsNullOrWhiteSpace(thumbnail) ? fullUrl : thumbnail,
                    FullPhotoUrl = fullUrl,
                    SourceUrl = sourceUrl,
                    Likes = GetString(item, "favorites", "0"),
                    Downloads = GetString(item, "views", "0"),
                    Resolution = resolution,
                    QualityTag = GetQualityTag(resolution),
                    IsAI = false
                });
            }

            return wallpapers;
        }

        private async Task<List<WallpaperItem>> GetBingWallpapersAsync(int page, CancellationToken cancellationToken)
        {
            var idx = (page - 1) * 8;
            var url = $"https://www.bing.com/HPImageArchive.aspx?format=js&idx={idx}&n=8&mkt=en-US";
            var json = await _httpClient.GetStringAsync(url, cancellationToken);
            var wallpapers = new List<WallpaperItem>();

            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("images", out var imagesElement) || imagesElement.ValueKind != JsonValueKind.Array)
            {
                return wallpapers;
            }

            foreach (var item in imagesElement.EnumerateArray())
            {
                var startDate = GetString(item, "startdate");
                var title = GetString(item, "title", "Bing wallpaper");
                var description = GetString(item, "copyright", title);
                var relativeImageUrl = GetString(item, "url");
                var sourceUrl = GetString(item, "copyrightlink");

                if (string.IsNullOrWhiteSpace(relativeImageUrl))
                {
                    continue;
                }

                var imageUrl = MakeAbsoluteUrl("https://www.bing.com", relativeImageUrl);
                wallpapers.Add(new WallpaperItem
                {
                    Id = string.IsNullOrWhiteSpace(startDate) ? Guid.NewGuid().ToString("N") : startDate,
                    Title = title,
                    Description = description,
                    ImageUrl = imageUrl,
                    FullPhotoUrl = imageUrl,
                    SourceUrl = sourceUrl,
                    Likes = string.Empty,
                    Downloads = string.Empty,
                    Resolution = "1920x1080",
                    QualityTag = "1080p",
                    IsAI = false
                });
            }

            return wallpapers;
        }

        private async Task<List<WallpaperItem>> GetSimpleDesktopWallpapersAsync(int page, CancellationToken cancellationToken)
        {
            var url = $"https://simpledesktops.com/browse/{page}/";
            var html = await _httpClient.GetStringAsync(url, cancellationToken);
            var wallpapers = new List<WallpaperItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var matches = Regex.Matches(
                html,
                "<a\\s+href=\"(?<href>/browse/desktops/[^\"]+)\"[^>]*>\\s*<img\\s+src=\"(?<src>[^\"]+)\"[^>]*title=\"(?<title>[^\"]+)\"",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            foreach (Match match in matches)
            {
                var href = WebUtility.HtmlDecode(match.Groups["href"].Value);
                var thumbnail = NormalizeSimpleDesktopUrl(WebUtility.HtmlDecode(match.Groups["src"].Value));
                var title = WebUtility.HtmlDecode(match.Groups["title"].Value);

                if (string.IsNullOrWhiteSpace(href) || string.IsNullOrWhiteSpace(thumbnail) || !seen.Add(href))
                {
                    continue;
                }

                var fullUrl = Regex.Replace(thumbnail, @"\.(295x184|625x385)_q100\.png(\?.*)?$", string.Empty, RegexOptions.IgnoreCase);
                var sourceUrl = MakeAbsoluteUrl("https://simpledesktops.com", href);

                wallpapers.Add(new WallpaperItem
                {
                    Id = href.Trim('/').Replace('/', '-'),
                    Title = string.IsNullOrWhiteSpace(title) ? "Simple Desktop" : title,
                    Description = "Minimal wallpaper from Simple Desktops.",
                    ImageUrl = thumbnail,
                    FullPhotoUrl = fullUrl,
                    SourceUrl = sourceUrl,
                    Likes = string.Empty,
                    Downloads = string.Empty,
                    Resolution = "2560x1600",
                    QualityTag = string.Empty,
                    IsAI = false
                });
            }

            return wallpapers;
        }

        private async Task<List<WallpaperItem>> GetWallpaperHubWallpapersAsync(int page, CancellationToken cancellationToken)
        {
            if (page > 1)
            {
                return new List<WallpaperItem>();
            }

            var html = await _httpClient.GetStringAsync("https://www.wallpaperhub.app/wallpapers", cancellationToken);
            var match = Regex.Match(html, "<script id=\"__NEXT_DATA__\" type=\"application/json\">(?<json>.*?)</script>", RegexOptions.Singleline);
            if (!match.Success)
            {
                return new List<WallpaperItem>();
            }

            var json = WebUtility.HtmlDecode(match.Groups["json"].Value);
            var wallpapers = new List<WallpaperItem>();

            using var document = JsonDocument.Parse(json);
            if (!TryGetNestedProperty(document.RootElement, out var wallpapersElement, "props", "pageProps", "initWallpapers") ||
                wallpapersElement.ValueKind != JsonValueKind.Array)
            {
                return wallpapers;
            }

            foreach (var wrapper in wallpapersElement.EnumerateArray())
            {
                if (!wrapper.TryGetProperty("entity", out var item) || item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var id = GetString(item, "id");
                var title = GetString(item, "title", "WallpaperHub wallpaper");
                var description = GetString(item, "description", title);
                var thumbnail = GetString(item, "thumbnail");
                var full = GetWallpaperHubBestResolution(item, out var resolutionLabel, out var resolution);
                var sourceUrl = GetString(item, "source", $"https://www.wallpaperhub.app/wallpapers");

                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(thumbnail))
                {
                    continue;
                }

                wallpapers.Add(new WallpaperItem
                {
                    Id = id,
                    Title = title,
                    Description = description,
                    ImageUrl = thumbnail,
                    FullPhotoUrl = string.IsNullOrWhiteSpace(full) ? thumbnail : full,
                    SourceUrl = sourceUrl,
                    Likes = string.Empty,
                    Downloads = GetString(item, "downloads", string.Empty),
                    Resolution = string.IsNullOrWhiteSpace(resolution) ? resolutionLabel : resolution,
                    QualityTag = GetQualityTag(resolution),
                    IsAI = false
                });
            }

            return wallpapers;
        }

        private async Task<List<WallpaperItem>> GetPexelsWallpapersAsync(int page, string mode, CancellationToken cancellationToken)
        {
            var apiKey = Environment.GetEnvironmentVariable("PEXELS_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("Pexels support needs a PEXELS_API_KEY environment variable. Create a free Pexels API key, set it, then restart Aura.");
            }

            var requestUrl = string.Equals(mode, "Curated", StringComparison.OrdinalIgnoreCase)
                ? $"https://api.pexels.com/v1/curated?page={page}&per_page=30"
                : $"https://api.pexels.com/v1/search?query={Uri.EscapeDataString(string.IsNullOrWhiteSpace(mode) ? "wallpaper" : mode)}&orientation=landscape&page={page}&per_page=30";

            using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            request.Headers.TryAddWithoutValidation("Authorization", apiKey);
            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var wallpapers = new List<WallpaperItem>();

            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("photos", out var photosElement) || photosElement.ValueKind != JsonValueKind.Array)
            {
                return wallpapers;
            }

            foreach (var photo in photosElement.EnumerateArray())
            {
                var id = GetString(photo, "id");
                var photographer = GetString(photo, "photographer", "Pexels photographer");
                var sourceUrl = GetString(photo, "url");
                var width = GetString(photo, "width");
                var height = GetString(photo, "height");
                var thumbnail = GetNestedString(photo, "src", "medium");
                var full = GetNestedString(photo, "src", "original");
                var alt = GetString(photo, "alt", "Pexels photo");
                var resolution = !string.IsNullOrWhiteSpace(width) && !string.IsNullOrWhiteSpace(height) ? $"{width}x{height}" : string.Empty;

                wallpapers.Add(new WallpaperItem
                {
                    Id = id,
                    Title = alt,
                    Description = $"Photo by {photographer} on Pexels.",
                    ImageUrl = thumbnail,
                    FullPhotoUrl = full,
                    SourceUrl = sourceUrl,
                    Likes = string.Empty,
                    Downloads = string.Empty,
                    Resolution = resolution,
                    QualityTag = GetQualityTag(resolution),
                    IsAI = false
                });
            }

            return wallpapers;
        }

        private async Task<List<WallpaperItem>> GetPixabayWallpapersAsync(int page, string mode, CancellationToken cancellationToken)
        {
            var apiKey = Environment.GetEnvironmentVariable("PIXABAY_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("Pixabay support needs a PIXABAY_API_KEY environment variable. Create a free Pixabay API key, set it, then restart Aura.");
            }

            var category = mode switch
            {
                "Nature" => "nature",
                "Places" => "places",
                _ => "backgrounds"
            };

            var url = $"https://pixabay.com/api/?key={Uri.EscapeDataString(apiKey)}&image_type=photo&orientation=horizontal&safesearch=true&category={category}&page={page}&per_page=30";
            var json = await _httpClient.GetStringAsync(url, cancellationToken);
            var wallpapers = new List<WallpaperItem>();

            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("hits", out var hitsElement) || hitsElement.ValueKind != JsonValueKind.Array)
            {
                return wallpapers;
            }

            foreach (var hit in hitsElement.EnumerateArray())
            {
                var id = GetString(hit, "id");
                var tags = GetString(hit, "tags", "Pixabay photo");
                var user = GetString(hit, "user", "Pixabay contributor");
                var preview = GetString(hit, "webformatURL");
                var full = GetString(hit, "largeImageURL", preview);
                var sourceUrl = GetString(hit, "pageURL");
                var width = GetString(hit, "imageWidth");
                var height = GetString(hit, "imageHeight");
                var resolution = !string.IsNullOrWhiteSpace(width) && !string.IsNullOrWhiteSpace(height) ? $"{width}x{height}" : string.Empty;

                wallpapers.Add(new WallpaperItem
                {
                    Id = id,
                    Title = ToTitleCase(tags.Split(',').FirstOrDefault()?.Trim() ?? "Pixabay photo"),
                    Description = $"Photo by {user} on Pixabay. Tags: {tags}",
                    ImageUrl = preview,
                    FullPhotoUrl = full,
                    SourceUrl = sourceUrl,
                    Likes = GetString(hit, "likes", "0"),
                    Downloads = GetString(hit, "downloads", "0"),
                    Resolution = resolution,
                    QualityTag = GetQualityTag(resolution),
                    IsAI = false
                });
            }

            return wallpapers;
        }

        private static string GetWallpaperHubBestResolution(JsonElement item, out string resolutionLabel, out string resolution)
        {
            resolutionLabel = string.Empty;
            resolution = string.Empty;
            string bestUrl = string.Empty;
            long bestPixels = 0;

            if (!item.TryGetProperty("variations", out var variationsElement) || variationsElement.ValueKind != JsonValueKind.Array)
            {
                return bestUrl;
            }

            foreach (var variation in variationsElement.EnumerateArray())
            {
                if (!variation.TryGetProperty("resolutions", out var resolutionsElement) || resolutionsElement.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var res in resolutionsElement.EnumerateArray())
                {
                    var url = GetString(res, "url");
                    if (string.IsNullOrWhiteSpace(url))
                    {
                        continue;
                    }

                    var width = GetInt(res, "width");
                    var height = GetInt(res, "height");
                    if (height > width)
                    {
                        continue;
                    }

                    var pixels = (long)width * height;
                    if (pixels > bestPixels)
                    {
                        bestPixels = pixels;
                        bestUrl = url;
                        resolutionLabel = GetString(res, "resolutionLabel");
                        resolution = width > 0 && height > 0 ? $"{width}x{height}" : resolutionLabel;
                    }
                }
            }

            return bestUrl;
        }

        private static bool TryGetNestedProperty(JsonElement element, out JsonElement value, params string[] path)
        {
            value = element;
            foreach (var propertyName in path)
            {
                if (!value.TryGetProperty(propertyName, out value))
                {
                    return false;
                }
            }

            return true;
        }

        private static string GetNestedString(JsonElement element, string parentPropertyName, string childPropertyName)
        {
            if (element.TryGetProperty(parentPropertyName, out var parentElement) && parentElement.ValueKind == JsonValueKind.Object)
            {
                return GetString(parentElement, childPropertyName);
            }

            return string.Empty;
        }

        private static int GetInt(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var value))
            {
                return 0;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            {
                return number;
            }

            return int.TryParse(GetString(element, propertyName), out number) ? number : 0;
        }

        private static string GetString(JsonElement element, string propertyName, string fallback = "")
        {
            if (!element.TryGetProperty(propertyName, out var value))
            {
                return fallback;
            }

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? fallback,
                JsonValueKind.Number => value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => fallback
            };
        }

        private static string MakeAbsoluteUrl(string baseUrl, string value)
        {
            if (Uri.TryCreate(value, UriKind.Absolute, out _))
            {
                return value;
            }

            if (!value.StartsWith("/"))
            {
                value = "/" + value;
            }

            return baseUrl.TrimEnd('/') + value;
        }

        private static string NormalizeSimpleDesktopUrl(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            if (value.StartsWith("//"))
            {
                value = "https:" + value;
            }
            else if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                value = "https://" + value.Substring("http://".Length);
            }

            return value;
        }

        private static string GetQualityTag(string resolution)
        {
            if (string.IsNullOrWhiteSpace(resolution))
            {
                return string.Empty;
            }

            var parts = resolution.Split('x', 'X');
            if (parts.Length != 2 || !int.TryParse(parts[0], out var width) || !int.TryParse(parts[1], out var height))
            {
                return string.Empty;
            }

            var maxSide = Math.Max(width, height);
            return maxSide switch
            {
                >= 7680 => "8K",
                >= 5120 => "5K",
                >= 3840 => "4K",
                >= 1920 => "1080p",
                _ => string.Empty
            };
        }

        private static string ToTitleCase(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return string.Join(" ", value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(word =>
                word.Length == 1 ? word.ToUpperInvariant() : char.ToUpperInvariant(word[0]) + word.Substring(1)));
        }
    }
}
