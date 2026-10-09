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
            var entry = new HistoryEntry
            {
                Title = title,
                ImageUrl = imageUrl,
                Timestamp = DateTime.Now,
                WallpaperType = wallpaperType,
                Source = source,
                // stickers: explicit argument wins, then the item's own tagging
                // (services tag the category they fetched with)
                Platform = !string.IsNullOrEmpty(platform) ? platform : wallpaper?.Platform ?? "",
                Category = !string.IsNullOrEmpty(category) ? category : wallpaper?.Category ?? "",
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

                if (string.IsNullOrEmpty(entry.Category))
                {
                    var category = isLock ? lockScreenCategory : desktopCategory;
                    if (!string.IsNullOrEmpty(category))
                    {
                        entry.Category = category;
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
