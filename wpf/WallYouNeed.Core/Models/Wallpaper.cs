namespace WallYouNeed.Core.Models;

/// <summary>
/// Represents a wallpaper with its metadata
/// </summary>
public class Wallpaper
{
    /// <summary>
    /// Unique identifier for the wallpaper
    /// </summary>
    public string Id { get; set; } = string.Empty;
    
    /// <summary>
    /// Title or name of the wallpaper
    /// </summary>
    public string Title { get; set; } = string.Empty;
    
    /// <summary>
    /// Name of the wallpaper (for backwards compatibility)
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Description of the wallpaper
    /// </summary>
    public string Description { get; set; } = string.Empty;
    
    /// <summary>
    /// Local file path to the wallpaper
    /// </summary>
    public string FilePath { get; set; } = string.Empty;
    
    /// <summary>
    /// URL to the online source of the wallpaper
    /// </summary>
    public string SourceUrl { get; set; } = string.Empty;
    
    /// <summary>
    /// URL to the thumbnail of the wallpaper
    /// </summary>
    public string ThumbnailUrl { get; set; } = string.Empty;
    
    /// <summary>
    /// Author/creator of the wallpaper
    /// </summary>
    public string Author { get; set; } = string.Empty;
    
    /// <summary>
    /// Categories/tags for the wallpaper
    /// </summary>
    public List<string> Tags { get; set; } = new();
    
    /// <summary>
    /// Width of the wallpaper in pixels
    /// </summary>
    public int Width { get; set; }
    
    /// <summary>
    /// Height of the wallpaper in pixels
    /// </summary>
    public int Height { get; set; }
    
    /// <summary>
    /// Whether this is a live/animated wallpaper
    /// </summary>
    public bool IsLive { get; set; }
    
    /// <summary>
    /// Source type of the wallpaper (Unsplash, Pexels, Wallpaper Engine, etc.)
    /// </summary>
    public WallpaperSource Source { get; set; }
    
    /// <summary>
    /// When the wallpaper was downloaded or created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    
    /// <summary>
    /// When the wallpaper was last used
    /// </summary>
    public DateTime LastUsedAt { get; set; } = DateTime.Now;
    
    /// <summary>
    /// Is this wallpaper marked as a favorite by the user
    /// </summary>
    public bool IsFavorite { get; set; }
    
    /// <summary>
    /// Collection IDs this wallpaper belongs to
    /// </summary>
    public List<string> CollectionIds { get; set; } = new();
    
    /// <summary>
    /// Additional metadata for the wallpaper
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new();
    
    /// <summary>
    /// Number of likes for the wallpaper
    /// </summary>
    public int Likes { get; set; }
    
    /// <summary>
    /// Number of downloads for the wallpaper
    /// </summary>
    public int Downloads { get; set; }
}

/// <summary>
/// Defines the source of a wallpaper
/// </summary>
public enum WallpaperSource
{
    Unsplash,
    Pexels,
    WallpaperEngine,
    Local,
    Custom,
    AI
} 