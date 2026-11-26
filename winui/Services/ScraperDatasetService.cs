using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Aura.Models;

namespace Aura.Services
{
    /// <summary>
    /// Reads the dataset the python scraper writes (see `scraper/` in this repo).
    ///
    /// The app used to hotlink every source live, which is why the Categories
    /// page showed red lines: a Cloudflare JS challenge (alphacoders), a site
    /// redesign (desktopnexus) and a dead domain (wallpapercave). The scraper
    /// owns those problems OFF the UI thread and writes a dataset:
    ///
    ///   &lt;root&gt;/&lt;site&gt;/thumbs/&lt;category&gt;/&lt;id&gt;.jpg
    ///   &lt;root&gt;/&lt;site&gt;/full/&lt;category&gt;/&lt;id&gt;.jpg
    ///   &lt;root&gt;/&lt;site&gt;/index.json
    ///
    /// The image urls handed to the UI are LOCAL FILE PATHS - the app's
    /// WallpaperDownloader already reads a local path directly
    /// (File.Exists -&gt; ReadAllBytesAsync), so no new image plumbing is
    /// needed and every thumbnail is a real, already-decoded JPEG.
    ///
    /// The dataset root is resolved in this order:
    ///   1. the AURA_SCRAPER_DATA environment variable
    ///   2. &lt;repo&gt;/scraper/data when that folder exists (dev checkout)
    ///   3. %LOCALAPPDATA%\Aura\dataset (the shipped default)
    ///
    /// Nothing here fails silently: a missing site index returns null and the
    /// caller decides what that means (the categories page says so in its
    /// error bar, rule 8).
    /// </summary>
    public static class ScraperDatasetService
    {
        public const string SiteAlphaCoders = "alphacoders";
        public const string SiteDesktopNexus = "desktopnexus";

        /// <summary>The dataset site key for an app platform name ("") if none.</summary>
        public static string SiteFor(string platform)
        {
            return platform switch
            {
                "AlphaCoders" => SiteAlphaCoders,
                "DesktopNexus" => SiteDesktopNexus,
                _ => string.Empty,
            };
        }

        private static readonly Lazy<string> RootResolver = new(() => ResolveDatasetRoot());

        public static string DatasetRoot => RootResolver.Value;

        private static string ResolveDatasetRoot()
        {
            var fromEnv = Environment.GetEnvironmentVariable("AURA_SCRAPER_DATA");
            if (!string.IsNullOrWhiteSpace(fromEnv) && Directory.Exists(fromEnv))
            {
                return Path.GetFullPath(fromEnv);
            }

            // the dev checkout: &lt;repo&gt;/scraper/data (the venv lives next to it)
            var repoData = Path.Combine(AppContext.BaseDirectory, "scraper", "data");
            var walkUp = new DirectoryInfo(AppContext.BaseDirectory);
            for (var depth = 0; depth < 6 && walkUp != null; depth++)
            {
                var candidate = Path.Combine(walkUp.FullName, "scraper", "data");
                if (Directory.Exists(candidate))
                {
                    repoData = candidate;
                    break;
                }

                walkUp = walkUp.Parent;
            }

            if (Directory.Exists(repoData))
            {
                return Path.GetFullPath(repoData);
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Aura",
                "dataset");
        }

        /// <summary>true = the site has a scraped index on disk.</summary>
        public static bool IsAvailable(string site)
        {
            return File.Exists(IndexPath(site));
        }

        public static string IndexPath(string site)
        {
            return Path.Combine(DatasetRoot, site, "index.json");
        }

        private sealed class IndexFile
        {
            public string Site { get; set; } = string.Empty;
            public string GeneratedAt { get; set; } = string.Empty;
            public List<JsonElement> Categories { get; set; } = new();
            public int ItemCount { get; set; }
            public List<FailureEntry> Failures { get; set; } = new();
            public List<ItemEntry> Items { get; set; } = new();
        }

        /// <summary>
        /// A category entry is {"key": "...", "name": "..."}; a plain string
        /// (an older dataset) is treated as the key AND the name.
        /// </summary>
        private static (string Key, string Name) ReadCategory(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                var text = element.GetString() ?? string.Empty;
                return (text, text);
            }

            if (element.ValueKind != JsonValueKind.Object)
            {
                return (string.Empty, string.Empty);
            }

            var key = element.TryGetProperty("key", out var keyElement) && keyElement.ValueKind == JsonValueKind.String
                ? keyElement.GetString() ?? string.Empty
                : string.Empty;
            var name = element.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String
                ? nameElement.GetString() ?? string.Empty
                : string.Empty;

            return (key, string.IsNullOrWhiteSpace(name) ? key : name);
        }

        private sealed class FailureEntry
        {
            public string Stage { get; set; } = string.Empty;
            public string Category { get; set; } = string.Empty;
            public string ItemId { get; set; } = string.Empty;
            public string Url { get; set; } = string.Empty;
            public string Error { get; set; } = string.Empty;
        }

        private sealed class ItemEntry
        {
            public string Site { get; set; } = string.Empty;
            public string Category { get; set; } = string.Empty;
            public string ItemId { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string Thumb { get; set; } = string.Empty;
            public string Full { get; set; } = string.Empty;
            public string SourceUrl { get; set; } = string.Empty;
            public int Width { get; set; }
            public int Height { get; set; }
            public long Bytes { get; set; }
        }

        private static IndexFile? ReadIndex(string site)
        {
            var path = IndexPath(site);
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                };
                return JsonSerializer.Deserialize<IndexFile>(File.ReadAllText(path), options);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// The scraped category list (key + display name) for a site, or an
        /// empty list when the site has no dataset.
        /// </summary>
        public static IReadOnlyList<(string Key, string Name)> GetCategories(string site)
        {
            var index = ReadIndex(site);
            if (index == null)
            {
                return Array.Empty<(string, string)>();
            }

            return index.Categories
                .Select(ReadCategory)
                .Where(c => !string.IsNullOrWhiteSpace(c.Key))
                .ToList();
        }

        /// <summary>How many items the dataset holds for a site (0 = no dataset).</summary>
        public static int GetItemCount(string site)
        {
            return ReadIndex(site)?.ItemCount ?? 0;
        }

        /// <summary>
        /// One page of scraped items for a category. The pages are the
        /// dataset's own order (the scraper wrote them in listing order).
        /// </summary>
        public static IReadOnlyList<WallpaperItem> GetWallpapers(
            string site,
            string category,
            int page,
            int pageSize)
        {
            var index = ReadIndex(site);
            if (index == null)
            {
                return Array.Empty<WallpaperItem>();
            }

            var items = index.Items
                .Where(i => string.Equals(i.Category, category, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var start = Math.Max(0, (page - 1) * pageSize);
            if (start >= items.Count)
            {
                return Array.Empty<WallpaperItem>();
            }

            return items
                .Skip(start)
                .Take(pageSize)
                .Select(i => ToWallpaperItem(site, i))
                .ToList();
        }

        /// <summary>
        /// The scraped items of a category whose image file is actually on
        /// disk - the categories page uses this to pick a card thumbnail
        /// (it must be a real file, not a path the scraper never wrote).
        /// Several keys can be passed: the dataset stores the SOURCE's own
        /// category key (alphacoders' slug, desktopnexus' subdomain) while
        /// the app addresses the same category by its display name.
        /// </summary>
        public static IReadOnlyList<WallpaperItem> GetWallpapersWithFiles(
            string site,
            params string[] categoryKeys)
        {
            var index = ReadIndex(site);
            if (index == null || categoryKeys.Length == 0)
            {
                return Array.Empty<WallpaperItem>();
            }

            var wanted = categoryKeys
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Select(k => k.Trim())
                .ToList();

            var results = new List<WallpaperItem>();
            foreach (var entry in index.Items.Where(i =>
                wanted.Any(w => string.Equals(w, i.Category, StringComparison.OrdinalIgnoreCase))))
            {
                var item = ToWallpaperItem(site, entry);
                if (File.Exists(item.ImageUrl))
                {
                    results.Add(item);
                }
            }

            return results;
        }

        private static WallpaperItem ToWallpaperItem(string site, ItemEntry entry)
        {
            var siteRoot = Path.Combine(DatasetRoot, site);
            var thumbPath = Path.Combine(siteRoot, entry.Thumb.Replace('/', Path.DirectorySeparatorChar));
            var fullPath = Path.Combine(siteRoot, entry.Full.Replace('/', Path.DirectorySeparatorChar));

            return new WallpaperItem
            {
                Id = entry.ItemId,
                Title = entry.Title,
                Description = $"{site} wallpaper",
                // local file paths: WallpaperDownloader reads a local path directly
                ImageUrl = File.Exists(thumbPath) ? thumbPath : string.Empty,
                FullPhotoUrl = File.Exists(fullPath) ? fullPath : (File.Exists(thumbPath) ? thumbPath : string.Empty),
                SourceUrl = entry.SourceUrl,
                Resolution = entry.Width > 0 && entry.Height > 0 ? $"{entry.Width}x{entry.Height}" : string.Empty,
                Platform = site,
                Category = entry.Category,
                Likes = string.Empty,
                Downloads = string.Empty,
                IsAI = false,
            };
        }
    }
}
