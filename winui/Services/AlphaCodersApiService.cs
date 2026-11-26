using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Aura.Models;

namespace Aura.Services
{
    // Official AlphaCoders API 4.0 client (api.alphacoders.com/4.0).
    //
    // Why this exists: alphacoders.com + wall.alphacoders.com have sat behind a
    // Cloudflare managed JS challenge since ~2026-11-20, so every scrape of the
    // browse pages 403s (see the 2026-11-24 section in AGENTS.md). The API host
    // is NOT challenged - it answers plain HttpClient calls (verified live
    // 2026-11-25: a no-key contents call returns 200 + real JSON). The API is
    // free for personal use (personal tier: 30 calls/min, 500/day, max page 200,
    // 30 items per page, all keys on one account share the cap) - the key lives
    // in Settings > API Keys (ApiKeySettingsService) or ALPHACODERS_API_KEY.
    //
    // Endpoints used (all GET, key as the `auth` query param):
    // - /contents?content_type=wallpaper&sort=newest&width=3840&height=2160
    //   &operator=min&page=N : the "latest" feed, 4K-or-bigger only - the same
    //   feed the old scraper read from /resolution/4k-wallpapers.
    // - /search?term=<slug>&content_type=wallpaper&page=N : one category. When
    //   the term matches an approved tag the response carries that tag's
    //   contents; otherwise the term is alt-text searched (this is how the
    //   tag-less categories - dark, technology, religious, humor - still
    //   resolve: their search results ARE the category's wallpapers).
    // - /contents/{id} : one item, for the detail page's uncached-id path.
    //
    // Rate-limit care: one API call per grid page / category thumbnail, and the
    // service-level static cache + the Categories page's session bitmap cache
    // mean re-opens cost nothing. The category INDEX still comes from the
    // scrape/archive path in AlphaCodersService - the API has no "categories"
    // endpoint (its /tags list is thousands of tags, not the site's 24).
    //
    // Failures are LOUD (rule 8): success=false throws with the API's own
    // error string (invalid_auth, rate_limit, invalid_page...), 429 throws
    // naming the Retry-After, network failures throw as-is. Nothing here
    // returns an empty list to hide a failure - callers decide what an
    // error means for their UI.
    public sealed class AlphaCodersApiService
    {
        public const string ApiBaseUrl = "https://api.alphacoders.com/4.0";
        public const int PageSize = 30;

        private static readonly HttpClient SharedClient = new HttpClient();

        public static bool HasApiKey =>
            !string.IsNullOrWhiteSpace(ApiKeySettingsService.GetAlphaCodersApiKey());

        // The 4K latest feed (what the grid page opens on, and what the
        // slideshow's Latest basis fetches): newest first, 4K-or-bigger only.
        public async Task<List<WallpaperItem>> GetLatestAsync(int page)
        {
            var json = await GetJsonAsync(
                $"contents?content_type=wallpaper&sort=newest&width=3840&height=2160&operator=min&page={page}");
            return ParseContents(json);
        }

        // One category page. "4k" is the resolution-page exception (not a tag),
        // so it reads the latest feed like the grid's quick button always did.
        public async Task<List<WallpaperItem>> GetCategoryAsync(string slug, int page)
        {
            if (string.Equals(slug, "4k", StringComparison.OrdinalIgnoreCase))
            {
                return await GetLatestAsync(page);
            }

            var json = await GetJsonAsync(
                $"search?term={Uri.EscapeDataString(slug)}&content_type=wallpaper&page={page}");
            return ParseContents(json);
        }

        // One item by its content_id (the detail page's uncached-id path).
        public async Task<WallpaperItem?> GetContentAsync(string contentId)
        {
            var json = await GetJsonAsync($"contents/{Uri.EscapeDataString(contentId)}");
            if (!json.RootElement.TryGetProperty("content", out var content))
            {
                return null;
            }

            return MapContent(content);
        }

        private async Task<JsonDocument> GetJsonAsync(string relativeUrl)
        {
            var key = ApiKeySettingsService.GetAlphaCodersApiKey();
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException(
                    "alphacoders API key is missing - add it in Settings > API Keys.");
            }

            var separator = relativeUrl.Contains("?") ? "&" : "?";
            var url = $"{ApiBaseUrl}/{relativeUrl}{separator}auth={Uri.EscapeDataString(key.Trim())}";

            HttpResponseMessage response;
            try
            {
                response = await SharedClient.GetAsync(url);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"alphacoders API request failed: {ex.Message}", ex);
            }

            if ((int)response.StatusCode == 429)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta?.TotalSeconds;
                throw new InvalidOperationException(
                    "alphacoders API rate limit reached (30 calls/min, 500/day)" +
                    (retryAfter.HasValue ? $" - retry after {retryAfter.Value} seconds." : "."));
            }

            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"alphacoders API returned {(int)response.StatusCode} ({response.StatusCode}).");
            }

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(body);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"alphacoders API returned unparseable JSON: {ex.Message}", ex);
            }

            if (doc.RootElement.TryGetProperty("success", out var success) && !success.GetBoolean())
            {
                var error = GetString(doc.RootElement, "error", "unknown error");
                throw new InvalidOperationException($"alphacoders API: {error}.");
            }

            return doc;
        }

        private static List<WallpaperItem> ParseContents(JsonDocument doc)
        {
            var wallpapers = new List<WallpaperItem>();
            if (!doc.RootElement.TryGetProperty("contents", out var contents))
            {
                return wallpapers;
            }

            foreach (var content in contents.EnumerateArray())
            {
                wallpapers.Add(MapContent(content));
            }

            return wallpapers;
        }

        // content_id -> Id (the same id the scraper used to derive from thumb
        // filenames, so history rows + the initiate-download derivation keep
        // working); thumb_350 / thumb_1920 are the grid + detail images, exactly
        // the sizes the scraper's thumbbig / thumb-1920 URLs used to be;
        // url_page is the big.php detail page (the old "View on Web" target).
        // Likes/Downloads are REAL now (favorite_count / view_count) - the
        // scraper faked both with Random. The API has no download count, so
        // Downloads shows view_count, the closest honest number.
        private static WallpaperItem MapContent(JsonElement content)
        {
            var width = GetInt(content, "width", 0);
            var height = GetInt(content, "height", 0);

            return new WallpaperItem
            {
                Id = GetString(content, "content_id", string.Empty),
                Title = GetString(content, "name", string.Empty),
                ImageUrl = GetString(content, "thumb_350", GetString(content, "thumb", string.Empty)),
                FullPhotoUrl = GetString(content, "thumb_1920", GetString(content, "src", string.Empty)),
                SourceUrl = GetString(content, "url_page", string.Empty),
                Resolution = width > 0 && height > 0 ? $"{width}x{height}" : string.Empty,
                QualityTag = AlphaCodersService.DetermineQualityTag(width, height),
                Likes = GetInt(content, "favorite_count", 0).ToString(),
                Downloads = GetInt(content, "view_count", 0).ToString(),
                Platform = "AlphaCoders",
                IsAI = false,
            };
        }

        private static string GetString(JsonElement element, string name, string fallback)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                return fallback;
            }

            if (!element.TryGetProperty(name, out var value))
            {
                return fallback;
            }

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? fallback,
                JsonValueKind.Number => value.GetRawText(),
                _ => fallback,
            };
        }

        private static int GetInt(JsonElement element, string name, int fallback)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                return fallback;
            }

            if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number)
            {
                return fallback;
            }

            return value.TryGetInt32(out var result) ? result : fallback;
        }
    }
}
