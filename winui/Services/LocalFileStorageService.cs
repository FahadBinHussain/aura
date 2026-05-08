using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;

namespace Aura.Services
{
    public static class LocalFileStorageService
    {
        public static string AppDataFolderPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aura");

        public static string ApiKeySettingsFilePath =>
            Path.Combine(AppDataFolderPath, "api-keys.json");

        public static string WallpaperCacheFolderPath =>
            Path.Combine(AppDataFolderPath, "WallpaperCache");

        public static async Task<StorageFolder> GetWallpaperCacheFolderAsync()
        {
            Directory.CreateDirectory(WallpaperCacheFolderPath);
            return await StorageFolder.GetFolderFromPathAsync(WallpaperCacheFolderPath);
        }
    }
}
