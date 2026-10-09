using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Aura.Services
{
    // curl-only HTTP transport for hosts that block .NET's TLS fingerprint: cara.app and
    // images.cara.app return 403 to SocketsHttpHandler (SChannel ClientHello, http/1.1 and
    // http/2 both) while curl.exe from the same machine passes - verified live 2026-10-08.
    // pixabay.com's HTML pages (collections index + collection pages) join that set - same
    // day, same proof shape: identical headers pass through curl and 403 through .NET (its
    // CDN and /api/ pass .NET, so only the HTML pages route here; pixabay additionally
    // requires the two Sec-Fetch-* headers passed as extraHeaders - plain curl = 403).
    // artstation.com's /projects/<hash>.json (the detail-page refetch) joins that set -
    // 2026-10-09: 403 to .NET with full browser headers, curl + Sec-Fetch-Mode: cors +
    // Sec-Fetch-Site: same-origin = 200 (its channels/projects.json endpoints still pass
    // .NET, so only the per-project fetch routes here).
    // this is NOT a fallback: these hosts only ever go through curl here, and a failure is
    // loud - curl exit != 0 (incl. --fail exit 22 on any 4xx/5xx) throws with stderr attached,
    // which surfaces as the page's StatusInfoBar / the category-thumbnail bar line.
    internal static class CurlClient
    {
        public static async Task<string> GetStringAsync(string url, CancellationToken cancellationToken = default, IReadOnlyList<string> extraHeaders = null)
        {
            var bytes = await GetByteArrayAsync(url, cancellationToken, extraHeaders);
            return Encoding.UTF8.GetString(bytes);
        }

        public static async Task<byte[]> GetByteArrayAsync(string url, CancellationToken cancellationToken = default, IReadOnlyList<string> extraHeaders = null)
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
            if (extraHeaders != null)
            {
                foreach (var header in extraHeaders)
                {
                    process.StartInfo.ArgumentList.Add("--header");
                    process.StartInfo.ArgumentList.Add(header);
                }
            }
            process.StartInfo.ArgumentList.Add(url);

            if (!process.Start())
            {
                throw new HttpRequestException("Failed to start curl.exe.");
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
                throw new HttpRequestException($"curl request failed with exit code {process.ExitCode}: {error}");
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
