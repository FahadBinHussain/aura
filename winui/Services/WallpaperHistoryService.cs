using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;

namespace Aura.Services
{
    public class HistoryEntry
    {
        public string Title { get; set; }
        public string ImageUrl { get; set; }   // Local file path or URL for thumbnail
        public DateTime Timestamp { get; set; }
        public string WallpaperType { get; set; }  // "Desktop" or "Lock Screen"
        public string Source { get; set; }          // "Manual" or "Slideshow"
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

        public void AddEntry(string title, string imageUrl, string wallpaperType, string source)
        {
            var entry = new HistoryEntry
            {
                Title = title,
                ImageUrl = imageUrl,
                Timestamp = DateTime.Now,
                WallpaperType = wallpaperType,
                Source = source
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
    }
}
