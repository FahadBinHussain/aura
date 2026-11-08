using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Aura.Services
{
    // Backiee network client. Cloudflare on backiee.com enforces TLS fingerprinting
    // and blocks .NET SocketsHttpHandler with 403; curl with modern browser User-Agent passes.
    // Backiee URLs route directly through CurlClient; non-backiee URLs route through WallpaperDownloader.
    internal static class BackieeNetworkClient
    {
        public static async Task<string> GetStringAsync(string url, CancellationToken cancellationToken = default)
        {
            if (IsBackieeUrl(url))
            {
                var bytes = await CurlClient.GetByteArrayAsync(url, cancellationToken);
                return Encoding.UTF8.GetString(bytes);
            }

            var nonBackieeBytes = await WallpaperDownloader.GetImageBytesAsync(url, cancellationToken);
            return Encoding.UTF8.GetString(nonBackieeBytes);
        }

        public static async Task<byte[]> GetByteArrayAsync(string url, CancellationToken cancellationToken = default)
        {
            if (IsBackieeUrl(url))
            {
                return await CurlClient.GetByteArrayAsync(url, cancellationToken);
            }

            return await WallpaperDownloader.GetImageBytesAsync(url, cancellationToken);
        }

        private static bool IsBackieeUrl(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                   uri.Host.EndsWith("backiee.com", StringComparison.OrdinalIgnoreCase);
        }
    }
}
