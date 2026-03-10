using System.Collections.Generic;
using System.Threading.Tasks;
using WallYouNeed.Core.Models;

namespace WallYouNeed.Core.Repositories
{
    public interface ICollectionRepository
    {
        Task<List<Collection>> GetAllCollectionsAsync();
        Task<Collection> GetCollectionByIdAsync(string id);
        Task<Collection> GetCollectionByNameAsync(string name);
        Task<List<WallpaperModel>> GetWallpapersInCollectionAsync(string collectionId);
        Task AddCollectionAsync(Collection collection);
        Task UpdateCollectionAsync(Collection collection);
        Task DeleteCollectionAsync(string id);
        Task AddWallpaperToCollectionAsync(string collectionId, string wallpaperId);
        Task RemoveWallpaperFromCollectionAsync(string collectionId, string wallpaperId);
    }
} 