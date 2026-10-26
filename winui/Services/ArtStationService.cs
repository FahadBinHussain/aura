using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Aura.Models;

namespace Aura.Services
{
    public sealed class ArtStationService
    {
        private const string BaseUrl = "https://www.artstation.com";

        // curl-only transport for /projects/<hash>.json - Cloudflare fingerprints the TLS
        // client there: the endpoint 403s SocketsHttpHandler (with or without full browser
        // headers) while curl passes (side-by-side proof 2026-10-09), and the Sec-Fetch
        // pair is REQUIRED (bare curl = a CF challenge page that looks like JSON); fail is
        // loud = curl exit code + stderr thrown. shared by the detail refetch and the
        // history-pill category lookup.
        private static readonly string[] ProjectDetailHeaders =
        {
            "Accept: application/json,text/plain,image/*,*/*",
            "Sec-Fetch-Mode: cors",
            "Sec-Fetch-Site: same-origin"
        };
        private readonly HttpClient _httpClient;

        public ArtStationService()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Aura/1.0");
            _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json,text/plain,image/*,*/*");
            _httpClient.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
            _httpClient.DefaultRequestHeaders.Referrer = new Uri(BaseUrl);
        }

        public async Task<List<WallpaperItem>> GetProjectsAsync(
            string sorting,
            int page,
            CancellationToken cancellationToken = default)
        {
            var safeSorting = string.IsNullOrWhiteSpace(sorting) ? "trending" : Uri.EscapeDataString(sorting);
            var url = $"{BaseUrl}/projects.json?sorting={safeSorting}&page={page}";
            var json = await _httpClient.GetStringAsync(url, cancellationToken);
            var wallpapers = new List<WallpaperItem>();

            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("data", out var dataElement) ||
                dataElement.ValueKind != JsonValueKind.Array)
            {
                return wallpapers;
            }

            foreach (var projectElement in dataElement.EnumerateArray())
            {
                var wallpaper = CreateWallpaperItem(projectElement);
                if (!string.IsNullOrWhiteSpace(wallpaper.Id) &&
                    !string.IsNullOrWhiteSpace(wallpaper.ImageUrl))
                {
                    wallpapers.Add(wallpaper);
                }
            }

            return wallpapers;
        }

        // ---- channels: the site's own category surface (live index) ----
        // CategoriesPage live-loads this on every open (same loud contract as
        // backiee/alpha/pixabay/wallpaperhub): a failed fetch = zero ArtStation
        // cards + a bar line, never a stale static list.
        private static readonly object ChannelsLock = new object();
        private static IReadOnlyList<(int Id, string Name)> _channels = Array.Empty<(int, string)>();

        public static IReadOnlyList<(int Id, string Name)> Channels
        {
            get
            {
                lock (ChannelsLock)
                {
                    return _channels;
                }
            }
        }

        public static void SetChannels(IReadOnlyList<(int Id, string Name)> channels)
        {
            lock (ChannelsLock)
            {
                _channels = channels;
            }
        }

        public async Task<List<(int Id, string Name)>> GetChannelsIndexAsync(
            CancellationToken cancellationToken = default)
        {
            var url = $"{BaseUrl}/api/v2/community/channels/channels.json";
            var json = await _httpClient.GetStringAsync(url, cancellationToken);
            var channels = new List<(int Id, string Name)>();

            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("data", out var dataElement) ||
                dataElement.ValueKind != JsonValueKind.Array)
            {
                return channels;
            }

            foreach (var channelElement in dataElement.EnumerateArray())
            {
                // only what the site itself publishes into its own channel picker
                if (!string.Equals(GetString(channelElement, "state"), "published", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!channelElement.TryGetProperty("id", out var idElement) ||
                    !idElement.TryGetInt32(out var id))
                {
                    continue;
                }

                var name = GetString(channelElement, "name").Trim();
                if (name.Length == 0)
                {
                    continue;
                }

                channels.Add((id, name));
            }

            return channels;
        }

        public async Task<List<WallpaperItem>> GetChannelProjectsAsync(
            int channelId,
            int page,
            CancellationToken cancellationToken = default)
        {
            // sorting=trending + per_page=45 = the site's own channel-page call
            // (sorting is restricted to trending|latest|popular - anything else = 400)
            var url = $"{BaseUrl}/api/v2/community/channels/projects.json?channel_id={channelId}&page={page}&sorting=trending&per_page=45";
            var json = await _httpClient.GetStringAsync(url, cancellationToken);
            var wallpapers = new List<WallpaperItem>();

            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("data", out var dataElement) ||
                dataElement.ValueKind != JsonValueKind.Array)
            {
                return wallpapers;
            }

            foreach (var projectElement in dataElement.EnumerateArray())
            {
                // channel cards carry only hide_as_adult (no is_adult_content field)
                if (GetBool(projectElement, "hide_as_adult"))
                {
                    continue;
                }

                var wallpaper = CreateSearchWallpaperItem(projectElement);
                if (!string.IsNullOrWhiteSpace(wallpaper.Id) &&
                    !string.IsNullOrWhiteSpace(wallpaper.ImageUrl))
                {
                    wallpapers.Add(wallpaper);
                }
            }

            return wallpapers;
        }

        public async Task<WallpaperItem> GetProjectDetailsAsync(
            WallpaperItem wallpaper,
            CancellationToken cancellationToken = default)
        {
            if (wallpaper == null || string.IsNullOrWhiteSpace(wallpaper.Id))
            {
                return wallpaper;
            }

            var url = $"{BaseUrl}/projects/{Uri.EscapeDataString(wallpaper.Id)}.json";
            // (see ProjectDetailHeaders above for the fingerprint proof)
            var json = await CurlClient.GetStringAsync(url, cancellationToken, ProjectDetailHeaders);

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            wallpaper.Title = TextUtilities.CleanTitle(GetString(root, "title", wallpaper.Title));
            wallpaper.Description = TextUtilities.CleanHtml(GetString(root, "description", wallpaper.Description));
            wallpaper.SourceUrl = GetString(root, "permalink", wallpaper.SourceUrl);
            wallpaper.Likes = GetMetric(root, "likes_count", wallpaper.Likes);
            wallpaper.Downloads = GetMetric(root, "views_count", wallpaper.Downloads);

            var fullImageUrl = GetFirstImageAssetUrl(root);
            if (string.IsNullOrWhiteSpace(fullImageUrl))
            {
                fullImageUrl = GetString(root, "cover_url", wallpaper.FullPhotoUrl);
            }

            if (!string.IsNullOrWhiteSpace(fullImageUrl))
            {
                wallpaper.FullPhotoUrl = fullImageUrl;
            }

            var resolution = GetFirstImageResolution(root);
            if (!string.IsNullOrWhiteSpace(resolution))
            {
                wallpaper.Resolution = resolution;
            }

            var category = ExtractCategory(root);
            if (!string.IsNullOrWhiteSpace(category))
            {
                wallpaper.Category = category;
            }

            return wallpaper;
        }

        public static string ExtractCategory(JsonElement root)
        {
            foreach (var key in new[] { "categories", "mediums" })
            {
                if (!root.TryGetProperty(key, out var entries) || entries.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var entry in entries.EnumerateArray())
                {
                    if (entry.ValueKind == JsonValueKind.Object &&
                        entry.TryGetProperty("name", out var name) &&
                        name.ValueKind == JsonValueKind.String)
                    {
                        var value = name.GetString()?.Trim();
                        if (!string.IsNullOrEmpty(value))
                        {
                            return value;
                        }
                    }
                }
            }

            return string.Empty;
        }

        // history-pill enrichment (registry ItemCategory): the project's OWN category
        // from its detail JSON - categories[].name first ("Concept Art", "Fashion &
        // Costume Design", ... on ~5/8 latest hits), mediums[].name as the honest
        // fallback when the project lists no category ("Digital 3D", ... - both proven
        // live 2026-10-12). itemUrl = the project's permalink; its LAST segment is the
        // hash_id this endpoint wants (artwork/KR3nPy -> /projects/KR3nPy.json, curl-
        // proven 2026-10-12). a published project carrying NEITHER is thrown (honest -
        // the caller logs it and the pill stays empty, never guessed).
        public async Task<string> GetItemCategoryAsync(string itemUrl, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(itemUrl))
            {
                throw new ArgumentException("artstation item url is empty - cannot look up its category", nameof(itemUrl));
            }

            var path = itemUrl.Split(new[] { '?' }, 2)[0].TrimEnd('/');
            var lastSlash = path.LastIndexOf('/');
            var hash = lastSlash >= 0 && lastSlash < path.Length - 1 ? path.Substring(lastSlash + 1) : path;
            if (string.IsNullOrWhiteSpace(hash) || hash.Equals("artwork", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"no project hash in {itemUrl} - artstation permalink markup may have changed");
            }

            var url = $"{BaseUrl}/projects/{Uri.EscapeDataString(hash)}.json";
            var json = await CurlClient.GetStringAsync(url, cancellationToken, ProjectDetailHeaders);

            using var document = JsonDocument.Parse(json);
            var category = ExtractCategory(document.RootElement);
            if (!string.IsNullOrWhiteSpace(category))
            {
                return category;
            }

            throw new InvalidOperationException(
                $"no categories or mediums in the project JSON of {itemUrl} - artstation detail markup may have changed");
        }

        public async Task<byte[]> GetImageBytesAsync(string imageUrl, CancellationToken cancellationToken = default)
        {
            // history rows can hand us the applied LOCAL file (the slideshow set
            // the wallpaper from exactly this file) - read it directly instead
            // of http-fetching what is really a path
            if (System.IO.File.Exists(imageUrl))
            {
                return await System.IO.File.ReadAllBytesAsync(imageUrl, cancellationToken);
            }

            return await _httpClient.GetByteArrayAsync(imageUrl, cancellationToken);
        }

        private static WallpaperItem CreateWallpaperItem(JsonElement projectElement)
        {
            var hashId = GetString(projectElement, "hash_id");
            var numericId = GetString(projectElement, "id");
            var title = GetString(projectElement, "title", "ArtStation artwork");
            var thumbnailUrl = GetCoverUrl(projectElement);
            var artistName = GetNestedString(projectElement, "user", "full_name");

            return new WallpaperItem
            {
                Id = string.IsNullOrWhiteSpace(hashId) ? numericId : hashId,
                Title = TextUtilities.CleanTitle(title),
                Description = string.IsNullOrWhiteSpace(artistName) ? TextUtilities.CleanHtml(GetString(projectElement, "description", title)) : $"by {artistName}",
                ImageUrl = thumbnailUrl,
                FullPhotoUrl = thumbnailUrl,
                SourceUrl = GetString(projectElement, "permalink"),
                Likes = GetMetric(projectElement, "likes_count", "0"),
                Downloads = GetMetric(projectElement, "views_count", "0"),
                Resolution = "ArtStation",
                QualityTag = string.Empty,
                IsAI = false
            };
        }

        private static WallpaperItem CreateSearchWallpaperItem(JsonElement projectElement)
        {
            // search cards differ from projects.json cards: no cover object, no permalink,
            // no like/view counts - thumb + url + user only
            var hashId = GetString(projectElement, "hash_id");
            var numericId = GetString(projectElement, "id");
            var title = GetString(projectElement, "title", "ArtStation artwork");
            var thumbnailUrl = GetFirstString(
                projectElement,
                "smaller_square_cover_url",
                "smaller_cover_url",
                "thumb_url");
            var artistName = GetNestedString(projectElement, "user", "full_name");

            return new WallpaperItem
            {
                Id = string.IsNullOrWhiteSpace(hashId) ? numericId : hashId,
                Title = TextUtilities.CleanTitle(title),
                Description = string.IsNullOrWhiteSpace(artistName) ? TextUtilities.CleanTitle(title) : $"by {artistName}",
                ImageUrl = thumbnailUrl,
                FullPhotoUrl = thumbnailUrl,
                SourceUrl = GetString(projectElement, "url", $"{BaseUrl}/search"),
                Likes = "0",
                Downloads = "0",
                Resolution = "ArtStation",
                QualityTag = string.Empty,
                IsAI = false
            };
        }

        private static string GetCoverUrl(JsonElement projectElement)
        {
            if (!projectElement.TryGetProperty("cover", out var coverElement) ||
                coverElement.ValueKind != JsonValueKind.Object)
            {
                return string.Empty;
            }

            return GetFirstString(
                coverElement,
                "thumb_url",
                "small_square_url",
                "micro_square_image_url");
        }

        private static string GetFirstImageAssetUrl(JsonElement projectElement)
        {
            if (!projectElement.TryGetProperty("assets", out var assetsElement) ||
                assetsElement.ValueKind != JsonValueKind.Array)
            {
                return string.Empty;
            }

            foreach (var assetElement in assetsElement.EnumerateArray())
            {
                var hasImage = GetBool(assetElement, "has_image");
                var assetType = GetString(assetElement, "asset_type");
                var imageUrl = GetString(assetElement, "image_url");

                if (hasImage &&
                    string.Equals(assetType, "image", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(imageUrl))
                {
                    return imageUrl;
                }
            }

            return string.Empty;
        }

        private static string GetFirstImageResolution(JsonElement projectElement)
        {
            if (!projectElement.TryGetProperty("assets", out var assetsElement) ||
                assetsElement.ValueKind != JsonValueKind.Array)
            {
                return string.Empty;
            }

            foreach (var assetElement in assetsElement.EnumerateArray())
            {
                if (assetElement.TryGetProperty("width", out var widthElement) &&
                    assetElement.TryGetProperty("height", out var heightElement) &&
                    widthElement.TryGetInt32(out var width) &&
                    heightElement.TryGetInt32(out var height) &&
                    width > 0 &&
                    height > 0)
                {
                    return $"{width}x{height}";
                }
            }

            return string.Empty;
        }

        private static string GetFirstString(JsonElement element, params string[] propertyNames)
        {
            foreach (var propertyName in propertyNames)
            {
                var value = GetString(element, propertyName);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }

        private static string GetNestedString(JsonElement element, string parentPropertyName, string childPropertyName)
        {
            if (element.TryGetProperty(parentPropertyName, out var parentElement) &&
                parentElement.ValueKind == JsonValueKind.Object)
            {
                return GetString(parentElement, childPropertyName);
            }

            return string.Empty;
        }

        private static string GetMetric(JsonElement element, string propertyName, string fallback)
        {
            var value = GetString(element, propertyName, fallback);
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
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

        private static bool GetBool(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var value))
            {
                return false;
            }

            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.Number => value.TryGetInt32(out var number) && number != 0,
                JsonValueKind.String => bool.TryParse(value.GetString(), out var parsed) && parsed,
                _ => false
            };
        }
    }
}
