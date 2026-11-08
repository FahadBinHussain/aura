using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Aura.Services
{
    // Unified image and content downloader for all wallpaper hosts.
    // Handles host-specific transport and header rules:
    // - cara.app / *.cara.app: Cloudflare bot management blocks .NET TLS fingerprint (403), curl passes
    // - backiee.com / *.backiee.com: Cloudflare blocks .NET TLS and Aura/1.0 UA (403), curl with browser UA passes
    // - i.pximg.net (Pixiv CDN): strictly requires Referer: https://www.pixiv.net/ (403 without it)
    // - standard browser User-Agent across all requests
    internal static class WallpaperDownloader
    {
        public const string BrowserUserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";

        private static readonly HttpClient HttpClient = CreateHttpClient();

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            return client;
        }

        public static bool IsBackieeUrl(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                   uri.Host.EndsWith("backiee.com", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsCaraUrl(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                   (uri.Host.Equals("cara.app", StringComparison.OrdinalIgnoreCase) ||
                    uri.Host.EndsWith(".cara.app", StringComparison.OrdinalIgnoreCase));
        }

        public static string GetRefererFor(string url)
        {
            if (url.Contains("i.pximg.net", StringComparison.OrdinalIgnoreCase))
            {
                return "https://www.pixiv.net/";
            }
            if (url.Contains("alphacoders.com", StringComparison.OrdinalIgnoreCase))
            {
                return "https://wall.alphacoders.com/";
            }
            if (IsBackieeUrl(url))
            {
                return "https://backiee.com/";
            }
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return $"{uri.Scheme}://{uri.Host}/";
            }
            return "https://wall.alphacoders.com/";
        }

        public static async Task<byte[]> GetImageBytesAsync(string urlOrPath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(urlOrPath))
            {
                throw new ArgumentException("url or path is empty", nameof(urlOrPath));
            }

            // Local file (e.g. from history or local cache)
            if (File.Exists(urlOrPath))
            {
                return await File.ReadAllBytesAsync(urlOrPath, cancellationToken);
            }

            // App packaged asset
            if (urlOrPath.StartsWith("ms-appx://", StringComparison.OrdinalIgnoreCase))
            {
                string localPath = urlOrPath.Replace("ms-appx:///", "").Replace("/", "\\");
                string fullLocalPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, localPath);
                if (File.Exists(fullLocalPath))
                {
                    return await File.ReadAllBytesAsync(fullLocalPath, cancellationToken);
                }
                throw new FileNotFoundException($"local asset missing: {fullLocalPath}");
            }

            // Cara: Cloudflare bot protection blocks .NET TLS fingerprint (403), curl passes
            if (IsCaraUrl(urlOrPath))
            {
                return await CurlClient.GetByteArrayAsync(urlOrPath, cancellationToken);
            }

            // Backiee: Cloudflare bot protection blocks .NET TLS fingerprint (403), curl passes
            if (IsBackieeUrl(urlOrPath))
            {
                return await CurlClient.GetByteArrayAsync(urlOrPath, cancellationToken);
            }

            // HTTP fetch with browser headers & host referer
            using var request = new HttpRequestMessage(HttpMethod.Get, urlOrPath);
            request.Headers.TryAddWithoutValidation("User-Agent", BrowserUserAgent);
            request.Headers.TryAddWithoutValidation("Accept", "image/webp,image/apng,image/*,*/*;q=0.8");
            request.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
            request.Headers.TryAddWithoutValidation("Referer", GetRefererFor(urlOrPath));

            using var response = await HttpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync(cancellationToken);
        }
    }
}
