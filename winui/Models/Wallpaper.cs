using System;

namespace Aura.Models
{
    public class Wallpaper
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; } = string.Empty;
        public string ImagePath { get; set; } = string.Empty;
        public string Resolution { get; set; } = string.Empty;
        public bool IsAIGenerated { get; set; }
        public int Likes { get; set; }
        public int Downloads { get; set; }
        public string Category { get; set; } = string.Empty;
        public DateTime DateAdded { get; set; } = DateTime.Now;
    }
} 
