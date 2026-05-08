using System;
using Aura.Models;

namespace Aura.Views.Backiee
{
    public sealed class BackieeWallpaperSection
    {
        public const string LatestKey = "latest";
        public const string UltraHd8KKey = "8k";
        public const string AiGeneratedKey = "ai";
        public const string DailyPopularKey = "daily-popular";

        private BackieeWallpaperSection(string title)
        {
            Title = title;
        }

        public string Title { get; }
        public string ListType { get; private init; } = "latest";
        public string Category { get; private init; } = "all";
        public string AiFilter { get; private init; } = "all";
        public bool FourK { get; private init; }
        public bool FiveK { get; private init; }
        public bool EightK { get; private init; }
        public Func<WallpaperItem, bool>? ClientFilter { get; private init; }
        public Func<WallpaperItem, int>? SortScore { get; private init; }

        public static BackieeWallpaperSection Latest { get; } =
            new BackieeWallpaperSection("Latest wallpapers");

        public static BackieeWallpaperSection UltraHd8K { get; } =
            new BackieeWallpaperSection("8K UltraHD wallpapers")
            {
                EightK = true,
                ClientFilter = wallpaper => string.Equals(wallpaper.QualityTag, "8K", StringComparison.OrdinalIgnoreCase)
            };

        public static BackieeWallpaperSection AiGenerated { get; } =
            new BackieeWallpaperSection("AI generated wallpapers")
            {
                // Backiee currently ignores is_ai=1 on this endpoint, so keep the API broad
                // and filter by the returned AIGenerated flag.
                ClientFilter = wallpaper => wallpaper.IsAI
            };

        public static BackieeWallpaperSection DailyPopular { get; } =
            new BackieeWallpaperSection("Daily popular wallpapers")
            {
                SortScore = wallpaper => ParseMetric(wallpaper.Likes) + ParseMetric(wallpaper.Downloads)
            };

        public static BackieeWallpaperSection FromNavigationParameter(object? parameter)
        {
            if (parameter is BackieeWallpaperSection section)
            {
                return section;
            }

            return parameter?.ToString()?.Trim().ToLowerInvariant() switch
            {
                UltraHd8KKey => UltraHd8K,
                AiGeneratedKey => AiGenerated,
                DailyPopularKey => DailyPopular,
                _ => Latest
            };
        }

        public string BuildApiUrl(string apiBaseUrl, int page, int pageSize)
        {
            return $"{apiBaseUrl}?action=paging_list&list_type={ListType}&page={page}&page_size={pageSize}" +
                   $"&category={Category}&is_ai={AiFilter}&sort_by=popularity" +
                   $"&4k={FormatBool(FourK)}&5k={FormatBool(FiveK)}&8k={FormatBool(EightK)}" +
                   "&status=active&args=";
        }

        public bool Matches(WallpaperItem wallpaper)
        {
            return ClientFilter == null || ClientFilter(wallpaper);
        }

        private static string FormatBool(bool value)
        {
            return value ? "true" : "false";
        }

        private static int ParseMetric(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return 0;
            }

            var normalized = value.Trim().Replace(",", string.Empty);
            double multiplier = 1;

            if (normalized.EndsWith("K", StringComparison.OrdinalIgnoreCase))
            {
                multiplier = 1_000;
                normalized = normalized[..^1];
            }
            else if (normalized.EndsWith("M", StringComparison.OrdinalIgnoreCase))
            {
                multiplier = 1_000_000;
                normalized = normalized[..^1];
            }

            return double.TryParse(normalized, out var number)
                ? (int)Math.Round(number * multiplier)
                : 0;
        }
    }
}
