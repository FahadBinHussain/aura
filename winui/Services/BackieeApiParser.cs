using System;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aura.Models;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Aura.Services
{
    internal static class BackieeApiParser
    {
        public static WallpaperItem CreateWallpaperItem(
            JsonElement wallpaperElement,
            BitmapImage? placeholderImage = null,
            string preferredImageProperty = "MiniPhotoUrl")
        {
            var id = GetString(wallpaperElement, "ID");
            var title = GetString(wallpaperElement, "Title", "Untitled wallpaper");
            var imageUrl = GetFirstString(
                wallpaperElement,
                preferredImageProperty,
                "MediumPhotoUrl",
                "MiniPhotoUrl",
                "SmallPhotoUrl",
                "FullPhotoUrl");
            var fullPhotoUrl = GetFirstString(
                wallpaperElement,
                "FullPhotoUrl",
                preferredImageProperty,
                "MediumPhotoUrl",
                "MiniPhotoUrl");

            var wallpaper = new WallpaperItem
            {
                Id = id,
                Title = title,
                Description = GetString(wallpaperElement, "Description", title),
                ImageUrl = imageUrl,
                FullPhotoUrl = fullPhotoUrl,
                SourceUrl = GetString(wallpaperElement, "WallpaperUrl", $"https://backiee.com/wallpaper/{id}"),
                Resolution = GetString(wallpaperElement, "Resolution"),
                QualityTag = GetString(wallpaperElement, "UltraHDType"),
                IsAI = GetBoolFlag(wallpaperElement, "AIGenerated"),
                Likes = GetFirstStringOrDefault(wallpaperElement, "0", "RatingsThousandFormat", "Rating"),
                Downloads = GetFirstStringOrDefault(wallpaperElement, "0", "DownloadsThousandFormat", "Downloads")
            };

            if (placeholderImage != null)
            {
                wallpaper.ImageSource = placeholderImage;
            }

            return wallpaper;
        }

        public static string GetString(JsonElement element, string propertyName, string fallback = "")
        {
            if (!element.TryGetProperty(propertyName, out var value))
            {
                return fallback;
            }

            return GetValueString(value, fallback);
        }

        public static string GetValueString(JsonElement value, string fallback = "")
        {
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? fallback,
                JsonValueKind.Number => value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => fallback
            };
        }

        public static string GetFirstString(JsonElement element, params string[] propertyNames)
        {
            foreach (var propertyName in propertyNames)
            {
                var value = GetString(element, propertyName);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }

        public static string GetFirstStringOrDefault(JsonElement element, string fallback, params string[] propertyNames)
        {
            var value = GetFirstString(element, propertyNames);
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        public static bool GetBoolFlag(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var value))
            {
                return false;
            }

            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.Number => value.TryGetInt32(out var number) && number != 0,
                JsonValueKind.String => IsTruthyString(value.GetString()),
                _ => false
            };
        }

        public static JsonDocument ParsePossiblyInvalidJson(string json)
        {
            return JsonDocument.Parse(NormalizeMissingValues(json));
        }

        private static bool IsTruthyString(string? value)
        {
            return value != null &&
                   (value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                    value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                    value.Equals("yes", StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeMissingValues(string json)
        {
            return Regex.Replace(json, "(\"[^\"]+\"\\s*:\\s*)(?=[,}])", "$1null");
        }
    }
}
