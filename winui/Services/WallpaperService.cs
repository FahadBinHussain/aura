using System;
using System.Collections.Generic;
using System.Linq;
using Aura.Models;

namespace Aura.Services
{
    public class WallpaperService
    {
        private List<Wallpaper> _wallpapers;

        public WallpaperService()
        {
            _wallpapers = GenerateSampleWallpapers();
        }

        public List<Wallpaper> GetLatestWallpapers(int count = 10)
        {
            return _wallpapers
                .OrderByDescending(w => w.DateAdded)
                .Take(count)
                .ToList();
        }

        public List<Wallpaper> GetAIGeneratedWallpapers(int count = 10)
        {
            return _wallpapers
                .Where(w => w.IsAIGenerated)
                .OrderByDescending(w => w.DateAdded)
                .Take(count)
                .ToList();
        }

        public List<Wallpaper> GetDailyPopularWallpapers(int count = 10)
        {
            return _wallpapers
                .OrderByDescending(w => w.Likes)
                .Take(count)
                .ToList();
        }

        private List<Wallpaper> GenerateSampleWallpapers()
        {
            // This is just sample data - in a real app, you'd fetch from an API or database
            return new List<Wallpaper>
            {
                new Wallpaper { 
                    Title = "Lion with Cherry Blossoms", 
                    ImagePath = "ms-appx:///Assets/placeholder-dark.png", 
                    Resolution = "4K", 
                    IsAIGenerated = true, 
                    Likes = 94, 
                    Downloads = 223, 
                    Category = "Animals" 
                },
                new Wallpaper { 
                    Title = "Heartbeat Pulse", 
                    ImagePath = "ms-appx:///Assets/placeholder-dark.png", 
                    Resolution = "8K UltraHD", 
                    IsAIGenerated = false, 
                    Likes = 76, 
                    Downloads = 189, 
                    Category = "Abstract" 
                },
                new Wallpaper { 
                    Title = "Mystical Forest", 
                    ImagePath = "ms-appx:///Assets/placeholder-dark.png", 
                    Resolution = "4K", 
                    IsAIGenerated = true, 
                    Likes = 83, 
                    Downloads = 156, 
                    Category = "Nature" 
                },
                new Wallpaper { 
                    Title = "Glowing Rhino", 
                    ImagePath = "ms-appx:///Assets/placeholder-dark.png", 
                    Resolution = "5K", 
                    IsAIGenerated = true, 
                    Likes = 23, 
                    Downloads = 94, 
                    Category = "AI Generated" 
                },
                new Wallpaper { 
                    Title = "Anime Character", 
                    ImagePath = "ms-appx:///Assets/placeholder-dark.png", 
                    Resolution = "4K", 
                    IsAIGenerated = true, 
                    Likes = 22, 
                    Downloads = 83, 
                    Category = "Anime" 
                },
                new Wallpaper { 
                    Title = "Portal View", 
                    ImagePath = "ms-appx:///Assets/placeholder-dark.png", 
                    Resolution = "4K", 
                    IsAIGenerated = true, 
                    Likes = 17, 
                    Downloads = 68, 
                    Category = "Sci-Fi" 
                },
                new Wallpaper { 
                    Title = "Night Sky", 
                    ImagePath = "ms-appx:///Assets/placeholder-dark.png", 
                    Resolution = "5K", 
                    IsAIGenerated = false, 
                    Likes = 45, 
                    Downloads = 112, 
                    Category = "Nature" 
                },
                new Wallpaper { 
                    Title = "Solar System", 
                    ImagePath = "ms-appx:///Assets/placeholder-dark.png", 
                    Resolution = "8K UltraHD", 
                    IsAIGenerated = false, 
                    Likes = 89, 
                    Downloads = 205, 
                    Category = "Space" 
                },
                new Wallpaper { 
                    Title = "Cyberpunk City", 
                    ImagePath = "ms-appx:///Assets/placeholder-dark.png", 
                    Resolution = "4K", 
                    IsAIGenerated = true, 
                    Likes = 103, 
                    Downloads = 237, 
                    Category = "Sci-Fi" 
                },
                new Wallpaper { 
                    Title = "Tropical Beach", 
                    ImagePath = "ms-appx:///Assets/placeholder-dark.png", 
                    Resolution = "5K", 
                    IsAIGenerated = false, 
                    Likes = 56, 
                    Downloads = 143, 
                    Category = "Nature" 
                },
                new Wallpaper { 
                    Title = "Mountain Range", 
                    ImagePath = "ms-appx:///Assets/placeholder-dark.png", 
                    Resolution = "4K", 
                    IsAIGenerated = false, 
                    Likes = 67, 
                    Downloads = 178, 
                    Category = "Nature" 
                },
                new Wallpaper { 
                    Title = "AI Abstract Art", 
                    ImagePath = "ms-appx:///Assets/placeholder-dark.png", 
                    Resolution = "4K", 
                    IsAIGenerated = true, 
                    Likes = 41, 
                    Downloads = 104, 
                    Category = "Abstract" 
                }
            };
        }
    }
} 
