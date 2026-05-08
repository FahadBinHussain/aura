namespace Aura.Models
{
    public sealed class PublicWallpaperNavigationParameter
    {
        public string PlatformName { get; set; } = string.Empty;
        public WallpaperItem? Wallpaper { get; set; }

        public PublicWallpaperNavigationParameter()
        {
        }

        public PublicWallpaperNavigationParameter(string platformName, WallpaperItem wallpaper)
        {
            PlatformName = platformName;
            Wallpaper = wallpaper;
        }
    }
}
