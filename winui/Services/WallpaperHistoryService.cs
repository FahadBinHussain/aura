using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using Aura.Models;

namespace Aura.Services
{
    // serializable snapshot of the WallpaperItem a wallpaper was set from -
    // lets a history row navigate back to its detail page. ImageSource /
    // DownloadCommand are runtime-only and deliberately not stored.
    public class HistoryWallpaper
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public string FullPhotoUrl { get; set; } = string.Empty;
        public string SourceUrl { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty;
        public string Resolution { get; set; } = string.Empty;
        public string QualityTag { get; set; } = string.Empty;
        public bool IsAI { get; set; }
        public string Likes { get; set; } = "0";
        public string Downloads { get; set; } = "0";

        public WallpaperItem ToWallpaperItem() => new WallpaperItem
        {
            Id = Id,
            Title = Title,
            Description = Description,
            ImageUrl = ImageUrl,
            FullPhotoUrl = FullPhotoUrl,
            SourceUrl = SourceUrl,
            Platform = Platform,
            Resolution = Resolution,
            QualityTag = QualityTag,
            IsAI = IsAI,
            Likes = Likes,
            Downloads = Downloads
        };

        public static HistoryWallpaper FromWallpaperItem(WallpaperItem wallpaper) => new HistoryWallpaper
        {
            Id = wallpaper.Id,
            Title = wallpaper.Title,
            Description = wallpaper.Description,
            ImageUrl = wallpaper.ImageUrl,
            FullPhotoUrl = wallpaper.FullPhotoUrl,
            SourceUrl = wallpaper.SourceUrl,
            Platform = wallpaper.Platform,
            Resolution = wallpaper.Resolution,
            QualityTag = wallpaper.QualityTag,
            IsAI = wallpaper.IsAI,
            Likes = wallpaper.Likes,
            Downloads = wallpaper.Downloads
        };
    }

    // where a history row click navigates: Page picks the detail page type,
    // Platform feeds PublicWallpaperNavigationParameter for public sources.
    // null on an entry = no detail page stored (slideshow / pre-click-through).
    public class HistoryNavigation
    {
        public string Page { get; set; } = string.Empty;      // "Public" | "Backiee" | "AlphaCoders" | "ArtStation"
        public string Platform { get; set; } = string.Empty;  // public-source platform name
        public HistoryWallpaper? Wallpaper { get; set; }
    }

    public class HistoryEntry
    {
        public string Title { get; set; }
        public string ImageUrl { get; set; }   // Local file path or URL for thumbnail
        public DateTime Timestamp { get; set; }
        public string WallpaperType { get; set; }  // "Desktop" or "Lock Screen"
        public string Source { get; set; }          // "Manual", "Slideshow", or the site name (public-source rows)
        public string? Platform { get; set; }       // site sticker: Backiee / Wallhaven / ... (null on old rows)
        public string? Category { get; set; }       // category sticker: Latest Wallpapers / term / collection (null when unknown)
        public HistoryNavigation? Navigation { get; set; }  // click-through target; null when none stored
    }

    public class WallpaperHistoryService
    {
        private static WallpaperHistoryService? _instance;
        public static WallpaperHistoryService Instance => _instance ??= new WallpaperHistoryService();

        private static readonly string HistoryFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Aura", "wallpaper_history.json");

        private WallpaperHistoryService()
        {
            LoadFromDisk();
        }

        public ObservableCollection<HistoryEntry> Entries { get; } = new();

        public event EventHandler? HistoryChanged;

        public void AddEntry(string title, string imageUrl, string wallpaperType, string source,
            WallpaperItem? wallpaper = null, string page = "", string platform = "", string category = "")
        {
            var effectivePlatform = !string.IsNullOrEmpty(platform) ? platform : wallpaper?.Platform ?? "";
            var entry = new HistoryEntry
            {
                Title = title,
                ImageUrl = imageUrl,
                Timestamp = DateTime.Now,
                WallpaperType = wallpaperType,
                Source = source,
                // stickers: explicit argument wins, then the item's own tagging
                // (services tag the category they fetched with)
                Platform = effectivePlatform,
                // the history-pill contract, owned HERE (the single normalizer
                // for every writer): a row shows the item's OWN category, never
                // the slideshow's basis/mode label. "latest" / "Latest Wallpapers"
                // / the old "All categories" blanket stamp are fetch context ->
                // empty = no pill; a real source value (backiee slug, alpha key,
                // wallhaven facet, a tick name) canonicalizes to the catalog's
                // display name, kept verbatim when the universe was never loaded
                // (fresh Latest-basis install - no list exists, and the source
                // value is still the honest answer).
                Category = NormalizeCategory(effectivePlatform, !string.IsNullOrEmpty(category) ? category : wallpaper?.Category ?? ""),
                Navigation = wallpaper == null || string.IsNullOrEmpty(page)
                    ? null
                    : new HistoryNavigation
                    {
                        Page = page,
                        Platform = platform ?? "",
                        Wallpaper = HistoryWallpaper.FromWallpaperItem(wallpaper)
                    }
            };

            // Insert at top (newest first)
            Entries.Insert(0, entry);

            // Cap history at 200 entries
            while (Entries.Count > 200)
                Entries.RemoveAt(Entries.Count - 1);

            SaveToDisk();
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }

        // see the AddEntry pill contract above: mode labels are fetch context
        // (never pills), a real source value canonicalizes through the loaded
        // category universe and survives verbatim when it was never loaded.
        private static string NormalizeCategory(string platform, string value)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                value.Equals("latest", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("Latest Wallpapers", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, SlideshowCategoryCatalog.AllCategories, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return SlideshowCategoryCatalog.Instance.TryCanonicalName(platform, value) ?? value;
        }

        private void LoadFromDisk()
        {
            try
            {
                if (!File.Exists(HistoryFilePath))
                    return;

                var json = File.ReadAllText(HistoryFilePath);
                var list = JsonSerializer.Deserialize<List<HistoryEntry>>(json);
                if (list == null) return;

                foreach (var entry in list)
                    Entries.Add(entry);
            }
            catch
            {
                // Ignore read errors — start fresh
            }
        }

        private void SaveToDisk()
        {
            try
            {
                var dir = Path.GetDirectoryName(HistoryFilePath)!;
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var list = new List<HistoryEntry>(Entries);
                var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(HistoryFilePath, json);
            }
            catch
            {
                // Silently ignore write errors
            }
        }

        // wallpaper_history.json rows created before the sticker feature recorded
        // no site/category at set time - recover them ONCE from the CURRENT
        // slideshow batch (the local filename stores the exact wallpaper id) +
        // the configured slideshow categories. unmatched rows stay empty -
        // never guessed.
        public void RecoverSlideshowStickers(
            IReadOnlyList<WallpaperItem> desktopBatch,
            IReadOnlyList<WallpaperItem> lockScreenBatch,
            string desktopCategory,
            string lockScreenCategory)
        {
            bool changed = false;
            foreach (var entry in Entries)
            {
                if (entry.Source != "Slideshow") continue;
                bool isLock = entry.WallpaperType == "Lock Screen";

                // every non-null value goes through the pill normalizer. null =
                // pre-feature row (backfill from the configured category, itself
                // normalized - "Latest Wallpapers" is fetch context, never a
                // sticker); the old "All categories" blanket stamp recovers the
                // item's own category from the live batch (same id match as the
                // Platform backfill below); legacy mode stamps + raw source
                // slugs canonicalize ("latest" -> empty = no pill, backiee
                // "fantasy" -> "Fantasy"). only null gets a settings backfill,
                // or every startup would re-stamp the configured category onto
                // rows that honestly have none.
                if (entry.Category == null)
                {
                    var configured = NormalizeCategory(entry.Platform, isLock ? lockScreenCategory : desktopCategory);
                    if (!string.IsNullOrEmpty(configured))
                    {
                        entry.Category = configured;
                        changed = true;
                    }
                }
                else
                {
                    var raw = entry.Category;
                    if (string.Equals(raw, SlideshowCategoryCatalog.AllCategories, StringComparison.OrdinalIgnoreCase))
                    {
                        var batch = isLock ? lockScreenBatch : desktopBatch;
                        var id = ExtractStoredId(entry.ImageUrl);
                        var match = string.IsNullOrEmpty(id) ? null : batch.FirstOrDefault(w => w.Id == id);
                        raw = match?.Category ?? string.Empty;
                    }

                    var normalized = NormalizeCategory(entry.Platform, raw);
                    if (!string.Equals(normalized, entry.Category, StringComparison.Ordinal))
                    {
                        entry.Category = normalized;
                        changed = true;
                    }
                }

                if (string.IsNullOrEmpty(entry.Platform))
                {
                    var id = ExtractStoredId(entry.ImageUrl);
                    if (!string.IsNullOrEmpty(id))
                    {
                        var batch = isLock ? lockScreenBatch : desktopBatch;
                        var match = batch.FirstOrDefault(w => w.Id == id);
                        if (match != null && !string.IsNullOrEmpty(match.Platform))
                        {
                            entry.Platform = match.Platform;
                            changed = true;
                        }
                    }
                }
            }

            if (changed) SaveToDisk();
        }

        // local slideshow files are named wallpaper-<id>.<ext> /
        // lockscreen-<id>.<ext> - the id is whatever sits between prefix and dot
        private static string? ExtractStoredId(string imageUrl)
        {
            if (string.IsNullOrEmpty(imageUrl) || imageUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return null;
            var name = Path.GetFileNameWithoutExtension(imageUrl);
            var dash = name.IndexOf('-');
            if (dash <= 0 || dash == name.Length - 1) return null;
            var prefix = name[..dash];
            return prefix is "wallpaper" or "lockscreen" ? name[(dash + 1)..] : null;
        }
    }
}
