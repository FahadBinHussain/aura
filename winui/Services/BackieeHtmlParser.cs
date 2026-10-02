using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Aura.Models;
using HtmlAgilityPack;

namespace Aura.Services
{
    internal static class BackieeHtmlParser
    {
        private static readonly Regex AccentRegex = new Regex(
            @"--category-accent:\s*(#[0-9a-fA-F]{3,8})",
            RegexOptions.Compiled);

        public static List<BackieeCategory> ParseCategories(string html)
        {
            var document = new HtmlDocument();
            document.LoadHtml(html);

            var categories = new List<BackieeCategory>();
            var nodes = document.DocumentNode.SelectNodes("//a[contains(@class,'category-card')]");
            if (nodes == null)
            {
                return categories;
            }

            foreach (var node in nodes)
            {
                var href = node.GetAttributeValue("href", string.Empty);
                var slug = GetSlug(href, "/categories/");
                if (string.IsNullOrWhiteSpace(slug))
                {
                    continue;
                }

                var name = HtmlEntity.DeEntitize(node.SelectSingleNode(".//h3")?.InnerText?.Trim() ?? string.Empty);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var imageUrl = node.SelectSingleNode(".//img")?.GetAttributeValue("src", string.Empty) ?? string.Empty;
                var accentMatch = AccentRegex.Match(node.InnerHtml);
                var accentHex = accentMatch.Success ? accentMatch.Groups[1].Value : string.Empty;

                categories.Add(new BackieeCategory
                {
                    Slug = slug,
                    Name = name,
                    ImageUrl = imageUrl,
                    AccentHex = accentHex
                });
            }

            return categories;
        }

        public static List<string> ParseSearchChips(string html)
        {
            var document = new HtmlDocument();
            document.LoadHtml(html);

            var terms = new List<string>();
            var nodes = document.DocumentNode.SelectNodes("//a[contains(@class,'detail-chip')]");
            if (nodes == null)
            {
                return terms;
            }

            foreach (var node in nodes)
            {
                var href = node.GetAttributeValue("href", string.Empty);
                var term = GetSlug(href, "/search/");
                if (string.IsNullOrWhiteSpace(term))
                {
                    continue;
                }

                var decoded = Uri.UnescapeDataString(term);
                if (!terms.Any(existing => string.Equals(existing, decoded, StringComparison.OrdinalIgnoreCase)))
                {
                    terms.Add(decoded);
                }
            }

            return terms;
        }

        public static List<WallpaperItem> ParseWallpaperCards(string html)
        {
            var document = new HtmlDocument();
            document.LoadHtml(html);

            var wallpapers = new List<WallpaperItem>();
            var nodes = document.DocumentNode.SelectNodes("//a[contains(@class,'wall-link')]");
            if (nodes == null)
            {
                return wallpapers;
            }

            foreach (var node in nodes)
            {
                var href = node.GetAttributeValue("href", string.Empty);
                var id = GetLastSegment(href, "/wallpaper/");
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                var imageNode = node.SelectSingleNode(".//picture//img");
                var imageUrl = imageNode?.GetAttributeValue("src", string.Empty) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(imageUrl))
                {
                    continue;
                }

                var title = HtmlEntity.DeEntitize(
                    node.SelectSingleNode(".//div[contains(@class,'wall-body')]//h3")?.InnerText?.Trim()
                    ?? string.Empty);

                if (string.IsNullOrWhiteSpace(title))
                {
                    var alt = HtmlEntity.DeEntitize(imageNode?.GetAttributeValue("alt", string.Empty) ?? string.Empty);
                    title = alt.EndsWith(" wallpaper", StringComparison.OrdinalIgnoreCase)
                        ? alt[..^" wallpaper".Length].Trim()
                        : alt;
                }

                var qualityTag = HtmlEntity.DeEntitize(
                    node.SelectSingleNode(".//span[contains(@class,'flag-pill')]")?.InnerText?.Trim()
                    ?? string.Empty);

                var statPills = node.SelectNodes(".//span[contains(@class,'stat-pill') and not(contains(@class,'flag-pill'))]");
                var pillTexts = new List<string>();
                if (statPills != null)
                {
                    foreach (var pill in statPills)
                    {
                        pillTexts.Add(HtmlEntity.DeEntitize(pill.InnerText.Trim()));
                    }
                }

                var likes = pillTexts.Count > 0 ? pillTexts[0] : "0";
                var downloads = pillTexts.Count > 1 ? pillTexts[1] : "0";

                wallpapers.Add(new WallpaperItem
                {
                    Id = id,
                    Title = string.IsNullOrWhiteSpace(title) ? "Untitled wallpaper" : title,
                    Description = title,
                    ImageUrl = imageUrl,
                    FullPhotoUrl = string.Empty,
                    SourceUrl = href,
                    Platform = "Backiee",
                    QualityTag = qualityTag,
                    Likes = string.IsNullOrWhiteSpace(likes) ? "0" : likes,
                    Downloads = string.IsNullOrWhiteSpace(downloads) ? "0" : downloads
                });
            }

            return wallpapers;
        }

        private static string GetSlug(string href, string pathPrefix)
        {
            if (string.IsNullOrWhiteSpace(href) ||
                !Uri.TryCreate(href, UriKind.Absolute, out var uri))
            {
                return string.Empty;
            }

            var path = uri.AbsolutePath.TrimEnd('/');
            var index = path.LastIndexOf(pathPrefix, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return string.Empty;
            }

            var slug = path[(index + pathPrefix.Length)..];
            if (slug.Contains('/'))
            {
                return string.Empty;
            }

            return slug;
        }

        private static string GetLastSegment(string href, string pathPrefix)
        {
            if (string.IsNullOrWhiteSpace(href) ||
                !Uri.TryCreate(href, UriKind.Absolute, out var uri))
            {
                return string.Empty;
            }

            var path = uri.AbsolutePath.TrimEnd('/');
            if (!path.Contains(pathPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            var lastSlash = path.LastIndexOf('/');
            return lastSlash < 0 ? string.Empty : path[(lastSlash + 1)..];
        }
    }
}
