using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;

namespace Aura.Services
{
    public static class TextUtilities
    {
        /// <summary>
        /// Cleans HTML markup from rich text (e.g. ArtStation artwork descriptions),
        /// preserving paragraphs/breaks, stripping all HTML tags, decoding entities,
        /// and normalizing whitespace.
        /// </summary>
        public static string CleanHtml(string? html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return string.Empty;
            }

            // 1. Strip HTML comments
            string text = Regex.Replace(html, @"<!--.*?-->", string.Empty, RegexOptions.Singleline);

            // 2. Convert line break tags to newlines
            text = Regex.Replace(text, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);

            // 3. Convert closing block tags to paragraph breaks
            text = Regex.Replace(text, @"</(?:p|div|h[1-6]|li|tr|blockquote)>", "\n\n", RegexOptions.IgnoreCase);

            // 4. Convert opening block tags to newlines
            text = Regex.Replace(text, @"<(?:p|div|h[1-6]|li|tr|blockquote)[^>]*>", "\n", RegexOptions.IgnoreCase);

            // 5. Strip all remaining HTML tags
            text = Regex.Replace(text, @"<[^>]+>", string.Empty);

            // 6. Decode HTML entities (&amp;, &quot;, &#39;, &lt;, &gt;, &nbsp;, numeric entities)
            text = WebUtility.HtmlDecode(text);

            // 7. Normalize line endings and non-breaking spaces
            text = text.Replace("\r\n", "\n").Replace('\r', '\n').Replace('\u00A0', ' ');

            // 8. Normalize whitespace per line and collapse excess empty lines
            var rawLines = text.Split('\n');
            var cleanedLines = new List<string>();
            bool lastWasEmpty = true; // avoid leading empty lines

            foreach (var rawLine in rawLines)
            {
                var line = Regex.Replace(rawLine.Trim(), @"[ \t]+", " ");
                if (line.Length == 0)
                {
                    if (!lastWasEmpty)
                    {
                        cleanedLines.Add(string.Empty);
                        lastWasEmpty = true;
                    }
                }
                else
                {
                    cleanedLines.Add(line);
                    lastWasEmpty = false;
                }
            }

            return string.Join("\n", cleanedLines).Trim();
        }

        /// <summary>
        /// Cleans wallpaper titles by stripping HTML tags, decoding entities,
        /// and collapsing whitespace and newlines onto a single line.
        /// </summary>
        public static string CleanTitle(string? title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return string.Empty;
            }

            var clean = CleanHtml(title);
            // Titles should be single line: replace any newlines with space
            clean = Regex.Replace(clean, @"[\r\n]+", " ");
            return Regex.Replace(clean.Trim(), @"[ \t]+", " ");
        }
    }
}
