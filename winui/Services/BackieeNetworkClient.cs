using System;
using System.Diagnostics;
using System.IO;
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
                var bytes = await GetBytesWithCurlAsync(url, cancellationToken);
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
                return await GetBytesWithCurlAsync(url, cancellationToken);
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

        private static async Task<byte[]> GetBytesWithCurlAsync(string url, CancellationToken cancellationToken)
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = FindCurlExecutable(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            process.StartInfo.ArgumentList.Add("--location");
            process.StartInfo.ArgumentList.Add("--fail");
            process.StartInfo.ArgumentList.Add("--silent");
            process.StartInfo.ArgumentList.Add("--show-error");
            process.StartInfo.ArgumentList.Add("--max-time");
            process.StartInfo.ArgumentList.Add("30");
            process.StartInfo.ArgumentList.Add("--user-agent");
            process.StartInfo.ArgumentList.Add("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Aura/1.0");
            process.StartInfo.ArgumentList.Add(url);

            if (!process.Start())
            {
                throw new HttpRequestException("Failed to start curl.exe for Backiee request.");
            }

            await using var output = new MemoryStream();
            var outputTask = process.StandardOutput.BaseStream.CopyToAsync(output, cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync();

            using var cancellationRegistration = cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                }
            });

            await process.WaitForExitAsync(cancellationToken);
            await outputTask;
            var error = await errorTask;

            if (process.ExitCode != 0)
            {
                throw new HttpRequestException($"Backiee curl fallback failed with exit code {process.ExitCode}: {error}");
            }

            return output.ToArray();
        }

        private static string FindCurlExecutable()
        {
            var path = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrWhiteSpace(path))
            {
                foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                {
                    var candidate = Path.Combine(directory.Trim('"'), "curl.exe");
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }

            return "curl.exe";
        }
    }
}
