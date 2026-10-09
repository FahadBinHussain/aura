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
            var json = await _httpClient.GetStringAsync(url, cancellationToken);

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            wallpaper.Title = GetString(root, "title", wallpaper.Title);
            wallpaper.Description = GetString(root, "description", wallpaper.Description);
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

            return wallpaper;
        }

        public async Task<byte[]> GetImageBytesAsync(string imageUrl, CancellationToken cancellationToken = default)
        {
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
                Title = title,
                Description = string.IsNullOrWhiteSpace(artistName) ? GetString(projectElement, "description", title) : $"by {artistName}",
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
                Title = title,
                Description = string.IsNullOrWhiteSpace(artistName) ? title : $"by {artistName}",
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
