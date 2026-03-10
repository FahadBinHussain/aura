using System.Collections.Generic;
using System.Threading.Tasks;
using WallYouNeed.Core.Models;

namespace WallYouNeed.Core.Repositories
{
    public interface IWallpaperRepository
    {
        Task<List<WallpaperModel>> GetAllWallpapersAsync();
        Task<WallpaperModel?> GetWallpaperByIdAsync(string id);
        Task<List<WallpaperModel>> GetWallpapersByCategoryAsync(string category);
        Task<List<WallpaperModel>> GetWallpapersByResolutionCategoryAsync(string resolutionCategory);
        Task<WallpaperModel?> GetWallpapersBySourceUrlAsync(string sourceUrl);
        Task AddWallpaperAsync(WallpaperModel wallpaper);
        Task UpdateWallpaperAsync(WallpaperModel wallpaper);
        Task DeleteWallpaperAsync(string id);
    }
} 