using System;
using System.Collections.Generic;

namespace WallYouNeed.Core.Models
{
    public class Collection
    {
        public string Id { get; set; }
        
        public string Name { get; set; } = string.Empty;
        
        public string Description { get; set; } = string.Empty;
        
        public string CoverImagePath { get; set; } = string.Empty;
        
        public DateTime CreatedDate { get; set; }
        
        public DateTime ModifiedDate { get; set; }
        
        public List<string> WallpaperIds { get; set; } = new List<string>();
        
        public Collection()
        {
            Id = Guid.NewGuid().ToString();
            CreatedDate = DateTime.Now;
            ModifiedDate = DateTime.Now;
        }
    }
} 