using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Aura.Services
{
    internal static class BackieeNetworkClient
    {
        private static readonly HttpClient HttpClient = CreateHttpClient();

        public static async Task<string> GetStringAsync(string url, CancellationToken cancellationToken = default)
        {
            try
            {
                using var response = await HttpClient.GetAsync(url, cancellationToken);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync(cancellationToken);
            }
            catch when (IsBackieeUrl(url))
            {
                var bytes = await CurlClient.GetByteArrayAsync(url, cancellationToken);
                return Encoding.UTF8.GetString(bytes);
            }
        }

        public static async Task<byte[]> GetByteArrayAsync(string url, CancellationToken cancellationToken = default)
        {
            try
            {
                return await HttpClient.GetByteArrayAsync(url, cancellationToken);
            }
            catch when (IsBackieeUrl(url))
            {
                return await CurlClient.GetByteArrayAsync(url, cancellationToken);
            }
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Aura/1.0");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json,text/plain,image/*,*/*");
            client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
            return client;
        }

        private static bool IsBackieeUrl(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                   uri.Host.EndsWith("backiee.com", StringComparison.OrdinalIgnoreCase);
        }
    }
}
