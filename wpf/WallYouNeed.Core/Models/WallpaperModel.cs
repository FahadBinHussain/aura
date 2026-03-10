using System;
using System.Collections.Generic;

namespace WallYouNeed.Core.Models
{
    public class WallpaperModel
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Category { get; set; } = "General";
        public string ResolutionCategory { get; set; } = "General";
        public string ThumbnailUrl { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public string SourceUrl { get; set; } = string.Empty;
        public string Source { get; set; } = "Unknown"; // Website source (Backiee, Unsplash, etc.)
        public int Width { get; set; }
        public int Height { get; set; }
        public DateTime UploadDate { get; set; }
        public bool IsDownloaded { get; set; }
        public string LocalPath { get; set; } = string.Empty;
        public int Rating { get; set; } // User rating
        public long FileSizeBytes { get; set; } // Size of the wallpaper file in bytes
        public double FileSizeMB { get; set; } // Size of the wallpaper file in megabytes
        public Dictionary<string, string> Metadata { get; set; } // Additional metadata

        public WallpaperModel()
        {
            Id = Guid.NewGuid().ToString();
            UploadDate = DateTime.Now;
            Metadata = new Dictionary<string, string>();
        }

        public string GetResolution()
        {
            return $"{Width}x{Height}";
        }
    }
} 