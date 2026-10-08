using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Aura.Models;

namespace Aura.Services
{
    public sealed class PublicWallpaperService
    {
        private const string Wallhaven = "Wallhaven";
        private const string Bing = "Bing Wallpaper Archive";
        private const string SimpleDesktops = "Simple Desktops";
        private const string WallpaperHub = "WallpaperHub";
        private const string Pexels = "Pexels";
        private const string Pixabay = "Pixabay";
        private const string DesktopNexus = "DesktopNexus";
        private const string DigitalBlasphemy = "Digital Blasphemy";
        private const string Hdwallpapers = "HDwallpapers";
        private const string Pixiv = "Pixiv";
        private const string Cara = "Cara";
        private const string WallpaperCave = "Wallpaper Cave";
        private const string WallpaperEngine = "Wallpaper Engine";
        private const string Artgram = "Artgram";

        // pixabay categories = the site's curated Collections index
        // (https://pixabay.com/collections/ - 2 pages: pagi=1 = 40 + pagi=2 = 23 = 63
        // collections; TRAP: ?pagi>=3 silently WRAPS to page 1, so the index loader
        // stops at the first page with no NEW slugs). live-loaded by the Categories
        // page (GetPixabayCollectionsIndexAsync + SetPixabayCollections), same loud
        // contract as backiee/alphacoders - NO static list, a failed fetch = a loud
        // error line, never a silent empty list. cards and tiles both come from the
        // collection's own page, fetched through CurlClient (like cara - Cloudflare 403s
        // .NET's TLS fingerprint) with Sec-Fetch-Mode: navigate + Sec-Fetch-Site: none
        // REQUIRED on every request (bare UA = 403 even through curl); a challenge
        // page ("Just a moment" - rapid-fire requests trip it) is a thrown error ->
        // the loud bar, never an empty grid. tile = cdn __340.jpg, full = _1280.jpg
        // (single underscore - __1280/__640 = 403). the old API category list is gone:
        // the API cannot filter by collection. (automata-private/pixabay.com/AGENTS.md)
        private static readonly object PixabayCollectionsLock = new object();
        private static readonly List<(string Name, string Slug)> PixabayCollectionList =
            new List<(string Name, string Slug)>();

        public static IReadOnlyList<(string Name, string Slug)> PixabayCollections
        {
            get
            {
                lock (PixabayCollectionsLock)
                {
                    return PixabayCollectionList.ToArray();
                }
            }
        }

        public static void SetPixabayCollections(IReadOnlyList<(string Name, string Slug)> collections)
        {
            lock (PixabayCollectionsLock)
            {
                PixabayCollectionList.Clear();
                PixabayCollectionList.AddRange(collections);
            }
        }

        // walk the collections index page by page: page 1 = the bare url, then ?pagi=N.
        // stops at the first page that yields no NEW slugs (the index wraps ?pagi>=3
        // back to page 1 - a pure "stop when empty" loop would spin there forever).
        public async Task<List<(string Name, string Slug)>> GetPixabayCollectionsIndexAsync(
            CancellationToken cancellationToken = default)
        {
            var result = new List<(string Name, string Slug)>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int page = 1; page <= 10; page++)
            {
                var url = page == 1
                    ? "https://pixabay.com/collections/"
                    : $"https://pixabay.com/collections/?pagi={page}";
                var html = await GetPixabayHtmlAsync(url, cancellationToken);
                var rows = ParsePixabayCollectionRows(html);
                int added = 0;
                foreach (var row in rows)
                {
                    if (seen.Add(row.Slug))
                    {
                        result.Add(row);
                        added++;
                    }
                }

                if (added == 0)
                {
                    return result;
                }
            }

            throw new InvalidOperationException(
                "pixabay collections index did not end within 10 pages - refusing to ship a truncated category list.");
        }

        private static List<(string Name, string Slug)> ParsePixabayCollectionRows(string html)
        {
            var result = new List<(string Name, string Slug)>();
            var matches = Regex.Matches(
                html,
                @"<a href=""/collections/(?<slug>[a-z0-9-]+-\d+)/"">(.*?)<span>(?<name>[^<]+)</span>",
                RegexOptions.Singleline);

            foreach (Match m in matches)
            {
                var name = WebUtility.HtmlDecode(m.Groups["name"].Value).Trim();
                if (name.Length == 0)
                {
                    continue;
                }

                result.Add((name, m.Groups["slug"].Value));
            }

            return result;
        }

        // the two Sec-Fetch-* headers pixabay's Cloudflare edge REQUIRES on top of any
        // browser-ish UA - a plain curl GET = 403 (verified live 2026-10-08).
        private static readonly string[] PixabaySecFetchHeaders =
        {
            "Sec-Fetch-Mode: navigate",
            "Sec-Fetch-Site: none"
        };

        // the only sanctioned pixabay html fetch - through CurlClient, same as cara:
        // Cloudflare 403s .NET's TLS fingerprint on pixabay's HTML pages even with perfect
        // browser headers (identical request passes through curl.exe and 403s through
        // SocketsHttpHandler - verified side by side 2026-10-08), and the app cannot solve
        // a Turnstile challenge. curl's --fail makes a block/challenge status throw with
        // exit code + stderr (loud bar); a 200 challenge body is caught below.
        private async Task<string> GetPixabayHtmlAsync(string url, CancellationToken cancellationToken)
        {
            var html = await CurlClient.GetStringAsync(url, cancellationToken, PixabaySecFetchHeaders);

            if (html.Contains("Just a moment", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Cloudflare challenged {url} (rapid-fire requests trip it) - the next Categories open or grid scroll retries.");
            }

            return html;
        }

        // wallpaperhub's 17 collections (title is the browse key; id is the page path)
        private static readonly (string Title, string Id)[] WallpaperHubCollections =
        {
            ("Windows 11", "9280"),
            ("Surface Duo", "7716"),
            ("Build 2020", "7058"),
            ("October 2019 Event", "5472"),
            ("Surface Collection", "1274"),
            ("Windows Wallpapers", "6292"),
            ("Office + Fluent Design", "2863"),
            ("October 2018 Event", "1484"),
            ("Ninja Cat Originals", "1386"),
            ("Conference Collection", "1387"),
            ("Xbox E3 2018 Collection", "1238"),
            ("2019", "3692"),
            ("idek", "4045"),
            ("One World", "4636"),
            ("Chat Backgrounds", "6401"),
            ("Rainbows", "6638"),
            ("Festive Wallpapers", "8318"),
        };

        // desktopnexus: galleries page = the subdomain catalog; every subdomain browses /all/<page>
        // (automata-private/desktopnexus.com/AGENTS.md)
        private static readonly (string Title, string Subdomain)[] DesktopNexusCategories =
        {
            ("All", "www"),
            ("Abstract", "abstract"),
            ("Aircraft", "aircraft"),
            ("Animals", "animals"),
            ("Anime", "anime"),
            ("Architecture", "architecture"),
            ("Boats", "boats"),
            ("Cars", "cars"),
            ("Entertainment", "entertainment"),
            ("Motorcycles", "motorcycles"),
            ("Nature", "nature"),
            ("People", "people"),
            ("Space", "space"),
            ("Sports", "sports"),
            ("Technology", "technology"),
            ("Video Games", "videogames"),
        };

        // digital blasphemy: woo listing paths; the free tag ships everything on ONE page
        // (automata-private/digitalblasphemy.com/AGENTS.md)
        private static readonly (string Title, string Path)[] DigitalBlasphemyModes =
        {
            ("Wallpapers", "product-category/wallpapers"),
            ("Free", "product-tag/free"),
        };

        // hdwallpapers: nav slug catalog (verified 2026-10-08; "Latest" = plain feed)
        // (automata-private/www.hdwallpapers.net/AGENTS.md)
        private static readonly (string Title, string Slug)[] HdwallpapersModes =
        {
            ("Latest", "latest-wallpapers"),
            ("3D", "3d"),
            ("Abstract", "abstract"),
            ("Animals", "animals"),
            ("Anime", "anime"),
            ("Bikes", "bikes"),
            ("Brands", "brands"),
            ("Cars", "cars"),
            ("Celebrations", "celebrations"),
            ("Celebrities", "celebrities"),
            ("City & Architecture", "city-and-architecture"),
            ("Digital Art", "digital-art"),
            ("Flowers", "flowers"),
            ("Funny", "funny"),
            ("Games", "games"),
            ("Love", "love"),
            ("Nature", "nature"),
            ("People", "people"),
            ("Quotes", "quotes"),
            ("Space", "space"),
            ("Sports", "sports"),
            ("Technology", "technology"),
            ("TV & Movies", "tv-and-movies"),
            ("Typography", "typography"),
            ("World", "world"),
        };

        // pixiv: ajax search tags, lowercased by the fetcher; restrict=safe is PINNED there
        // (automata-private/www.pixiv.net/AGENTS.md)
        private static readonly string[] PixivTags = { "Wallpaper", "Landscape", "Nature" };

        // wallpaper cave: album page slugs; albums are single-shot (no server pagination anywhere)
        // (automata-private/wallpapercave.com/AGENTS.md)
        private static readonly (string Title, string Album)[] WallpaperCaveAlbums =
        {
            ("Wallpapers", "wallpapers"),
            ("Cloud", "cloud-wallpapers"),
            ("Desert", "desert-wallpapers"),
            ("Fire", "fire-wallpapers"),
            ("Ice", "ice-wallpapers"),
            ("Lake", "lake-wallpapers"),
            ("Ocean", "ocean-wallpapers"),
            ("Sunshine", "sunshine-wallpapers"),
            ("Soulslike", "soulslike-wallpapers"),
        };

        // wallpaper engine: verified requiredtags filters ("Wallpaper" returned the unfiltered
        // list - excluded; Trending = no tag) (automata-private/steamcommunity.com/AGENTS.md)
        private static readonly string[] WallpaperEngineModes =
        {
            "Trending", "Scene", "Anime", "3D", "Video", "Interactive", "Audio Responsive"
        };

        // artgram: the livewire gallery's own sortBy values via GET ?sortBy= (default = trending),
        // 50 cards/page (automata-private/www.artgram.co/AGENTS.md)
        private static readonly string[] ArtgramModes =
        {
            "Trending", "Latest", "Oldest"
        };

        private static readonly HashSet<string> SupportedPlatforms = new(StringComparer.OrdinalIgnoreCase)
        {
            Wallhaven,
            Bing,
            SimpleDesktops,
            WallpaperHub,
            Pexels,
            Pixabay,
            DesktopNexus,
            DigitalBlasphemy,
            Hdwallpapers,
            Pixiv,
            Cara,
            WallpaperCave,
            WallpaperEngine,
            Artgram
        };

        private readonly HttpClient _httpClient;

        public PublicWallpaperService()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Aura/1.0");
            _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json,text/html,image/*,*/*");
            _httpClient.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        }

        public static bool IsSupportedPlatform(string platformName)
        {
            return SupportedPlatforms.Contains(platformName ?? string.Empty);
        }

        public static IReadOnlyList<string> GetSupportedPlatformNames()
        {
            return SupportedPlatforms.OrderBy(platform => platform).ToList();
        }

        public static IReadOnlyList<string> GetModes(string platformName)
        {
            return platformName switch
            {
                // wallhaven categories = the API's 3-bit mask (general/anime/people), see automata wallhaven.cc/AGENTS.md
                Wallhaven => new[] { "General", "Anime", "People" },
                // pexels' API has no taxonomy - query modes are all it offers (automata www.pexels.com/AGENTS.md)
                Pexels => new[] { "Curated", "Nature", "Space" },
                Pixabay => PixabayCollections.Select(collection => collection.Name).ToArray(),
                WallpaperHub => WallpaperHubCollections.Select(collection => collection.Title).ToArray(),
                // bing/simpledesktops have no taxonomy at all - one honest entry each (their AGENTS.md)
                Bing => new[] { "Daily" },
                SimpleDesktops => new[] { "Minimal" },
                DesktopNexus => DesktopNexusCategories.Select(category => category.Title).ToArray(),
                DigitalBlasphemy => DigitalBlasphemyModes.Select(category => category.Title).ToArray(),
                Hdwallpapers => HdwallpapersModes.Select(category => category.Title).ToArray(),
                Pixiv => PixivTags,
                Cara => new[] { "Explore" },
                WallpaperCave => WallpaperCaveAlbums.Select(category => category.Title).ToArray(),
                WallpaperEngine => WallpaperEngineModes,
                Artgram => ArtgramModes,
                _ => Array.Empty<string>()
            };
        }

        public static string GetDefaultMode(string platformName)
        {
            var modes = GetModes(platformName);
            return modes.Count > 0 ? modes[0] : string.Empty;
        }

        public static string GetPlatformDescription(string platformName)
        {
            return platformName switch
            {
                Wallhaven => "Wallhaven's public JSON search, split into General / Anime / People categories.",
                Bing => "Recent daily Bing homepage wallpapers from Microsoft's public archive endpoint.",
                SimpleDesktops => "Minimal, distraction-free wallpapers from Simple Desktops.",
                WallpaperHub => "Windows, Surface, Office, Xbox, and event collections from WallpaperHub.",
                Pexels => "Free stock photos via the official Pexels API. Add a Pexels API key in Settings.",
                Pixabay => "Pixabay's curated collections, live from pixabay.com/collections (no key needed).",
                DesktopNexus => "15 category galleries plus All from Desktop Nexus's public browse pages.",
                DigitalBlasphemy => "Brian's wallpapers plus a free set on Digital Blasphemy (640x480 preview cap - originals are membership-only).",
                Hdwallpapers => "Latest feed plus 25 category listings from HDwallpapers.net.",
                Pixiv => "Safe-for-work illustrations from Pixiv's public search (restrict=safe, no key).",
                Cara => "Explore feed from the Cara art community's server-rendered page.",
                WallpaperCave => "Curated albums from Wallpaper Cave (single-shot album pages).",
                WallpaperEngine => "Wallpaper Engine's Steam Workshop browse pages with public preview images.",
                Artgram => "Trending, latest, and oldest gallery feeds from the Artgram art community (50 cards per page).",
                _ => "Browse wallpapers from this source."
            };
        }

        public async Task<List<WallpaperItem>> GetWallpapersAsync(
            string platformName,
            int page,
            string mode,
            CancellationToken cancellationToken = default)
        {
            page = Math.Max(page, 1);

            return platformName switch
            {
                Wallhaven => await GetWallhavenWallpapersAsync(page, mode, cancellationToken),
                Bing => await GetBingWallpapersAsync(page, cancellationToken),
                SimpleDesktops => await GetSimpleDesktopWallpapersAsync(page, cancellationToken),
                WallpaperHub => await GetWallpaperHubWallpapersAsync(page, mode, cancellationToken),
                Pexels => await GetPexelsWallpapersAsync(page, mode, cancellationToken),
                Pixabay => await GetPixabayWallpapersAsync(page, mode, cancellationToken),
                DesktopNexus => await GetDesktopNexusWallpapersAsync(page, mode, cancellationToken),
                DigitalBlasphemy => await GetDigitalBlasphemyWallpapersAsync(page, mode, cancellationToken),
                Hdwallpapers => await GetHdwallpapersWallpapersAsync(page, mode, cancellationToken),
                Pixiv => await GetPixivWallpapersAsync(page, mode, cancellationToken),
                Cara => await GetCaraWallpapersAsync(page, mode, cancellationToken),
                WallpaperCave => await GetWallpaperCaveWallpapersAsync(page, mode, cancellationToken),
                WallpaperEngine => await GetWallpaperEngineWallpapersAsync(page, mode, cancellationToken),
                Artgram => await GetArtgramWallpapersAsync(page, mode, cancellationToken),
                _ => throw new NotSupportedException($"{platformName} is not implemented yet.")
            };
        }

        public async Task<byte[]> GetImageBytesAsync(string imageUrl, CancellationToken cancellationToken = default)
        {
            return await _httpClient.GetByteArrayAsync(imageUrl, cancellationToken);
        }

        private async Task<List<WallpaperItem>> GetWallhavenWallpapersAsync(
            int page,
            string mode,
            CancellationToken cancellationToken)
        {
            // category -> 3-bit mask (general, anime, people); legacy sort modes still resolve
            // (case-normalized: slideshow passes lowercase modes) - automata wallhaven.cc/AGENTS.md
            var (categories, sorting) = mode?.ToLowerInvariant() switch
            {
                "general" => ("100", "toplist"),
                "anime" => ("010", "toplist"),
                "people" => ("001", "toplist"),
                "latest" => ("111", "date_added"),
                "random" => ("111", "random"),
                "toplist" => ("111", "toplist"),
                _ => ("111", "toplist")
            };

            var url = $"https://wallhaven.cc/api/v1/search?categories={categories}&purity=100&sorting={sorting}&order=desc&page={page}";
            var json = await _httpClient.GetStringAsync(url, cancellationToken);
            var wallpapers = new List<WallpaperItem>();

            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("data", out var dataElement) || dataElement.ValueKind != JsonValueKind.Array)
            {
                return wallpapers;
            }

            foreach (var item in dataElement.EnumerateArray())
            {
                var id = GetString(item, "id");
                var fullUrl = GetString(item, "path");
                var sourceUrl = GetString(item, "url");
                var resolution = GetString(item, "resolution");
                var category = GetString(item, "category", "wallpaper");
                var thumbnail = GetNestedString(item, "thumbs", "large");

                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(fullUrl))
                {
                    continue;
                }

                wallpapers.Add(new WallpaperItem
                {
                    Id = id,
                    Title = $"Wallhaven #{id}",
                    Description = $"{ToTitleCase(category)} wallpaper from Wallhaven.",
                    ImageUrl = string.IsNullOrWhiteSpace(thumbnail) ? fullUrl : thumbnail,
                    FullPhotoUrl = fullUrl,
                    SourceUrl = sourceUrl,
                    Likes = GetString(item, "favorites", "0"),
                    Downloads = GetString(item, "views", "0"),
                    Resolution = resolution,
                    QualityTag = GetQualityTag(resolution),
                    IsAI = false
                });
            }

            return wallpapers;
        }

        private async Task<List<WallpaperItem>> GetBingWallpapersAsync(int page, CancellationToken cancellationToken)
        {
            var idx = (page - 1) * 8;
            var url = $"https://www.bing.com/HPImageArchive.aspx?format=js&idx={idx}&n=8&mkt=en-US";
            var json = await _httpClient.GetStringAsync(url, cancellationToken);
            var wallpapers = new List<WallpaperItem>();

            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("images", out var imagesElement) || imagesElement.ValueKind != JsonValueKind.Array)
            {
                return wallpapers;
            }

            foreach (var item in imagesElement.EnumerateArray())
            {
                var startDate = GetString(item, "startdate");
                var title = GetString(item, "title", "Bing wallpaper");
                var description = GetString(item, "copyright", title);
                var relativeImageUrl = GetString(item, "url");
                var sourceUrl = GetString(item, "copyrightlink");

                if (string.IsNullOrWhiteSpace(relativeImageUrl))
                {
                    continue;
                }

                var imageUrl = MakeAbsoluteUrl("https://www.bing.com", relativeImageUrl);
                wallpapers.Add(new WallpaperItem
                {
                    Id = string.IsNullOrWhiteSpace(startDate) ? Guid.NewGuid().ToString("N") : startDate,
                    Title = title,
                    Description = description,
                    ImageUrl = imageUrl,
                    FullPhotoUrl = imageUrl,
                    SourceUrl = sourceUrl,
                    Likes = string.Empty,
                    Downloads = string.Empty,
                    Resolution = "1920x1080",
                    QualityTag = "1080p",
                    IsAI = false
                });
            }

            return wallpapers;
        }

        private async Task<List<WallpaperItem>> GetSimpleDesktopWallpapersAsync(int page, CancellationToken cancellationToken)
        {
            var url = $"https://simpledesktops.com/browse/{page}/";
            var html = await _httpClient.GetStringAsync(url, cancellationToken);
            var wallpapers = new List<WallpaperItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var matches = Regex.Matches(
                html,
                "<a\\s+href=\"(?<href>/browse/desktops/[^\"]+)\"[^>]*>\\s*<img\\s+src=\"(?<src>[^\"]+)\"[^>]*title=\"(?<title>[^\"]+)\"",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            foreach (Match match in matches)
            {
                var href = WebUtility.HtmlDecode(match.Groups["href"].Value);
                var thumbnail = NormalizeSimpleDesktopUrl(WebUtility.HtmlDecode(match.Groups["src"].Value));
                var title = WebUtility.HtmlDecode(match.Groups["title"].Value);

                if (string.IsNullOrWhiteSpace(href) || string.IsNullOrWhiteSpace(thumbnail) || !seen.Add(href))
                {
                    continue;
                }

                var fullUrl = Regex.Replace(thumbnail, @"\.(295x184|625x385)_q100\.png(\?.*)?$", string.Empty, RegexOptions.IgnoreCase);
                var sourceUrl = MakeAbsoluteUrl("https://simpledesktops.com", href);

                wallpapers.Add(new WallpaperItem
                {
                    Id = href.Trim('/').Replace('/', '-'),
                    Title = string.IsNullOrWhiteSpace(title) ? "Simple Desktop" : title,
                    Description = "Minimal wallpaper from Simple Desktops.",
                    ImageUrl = thumbnail,
                    FullPhotoUrl = fullUrl,
                    SourceUrl = sourceUrl,
                    Likes = string.Empty,
                    Downloads = string.Empty,
                    Resolution = "2560x1600",
                    QualityTag = string.Empty,
                    IsAI = false
                });
            }

            return wallpapers;
        }

        private async Task<List<WallpaperItem>> GetWallpaperHubWallpapersAsync(int page, string mode, CancellationToken cancellationToken)
        {
            if (page > 1)
            {
                // both SSR shapes serve the complete list at page 1 (collections SSR all items; /wallpapers has no paging)
                return new List<WallpaperItem>();
            }

            // mode = a collection title -> its page; anything else -> the plain /wallpapers feed
            var collectionId = WallpaperHubCollections
                .FirstOrDefault(collection => string.Equals(collection.Title, mode, StringComparison.OrdinalIgnoreCase)).Id;
            var requestPath = string.IsNullOrWhiteSpace(collectionId) ? "wallpapers" : $"collections/{collectionId}";
            var wallpapersArrayPath = string.IsNullOrWhiteSpace(collectionId)
                ? new[] { "props", "pageProps", "initWallpapers" }
                : new[] { "props", "pageProps", "collectionWallpapers" };

            var html = await _httpClient.GetStringAsync($"https://www.wallpaperhub.app/{requestPath}", cancellationToken);
            var match = Regex.Match(html, "<script id=\"__NEXT_DATA__\" type=\"application/json\">(?<json>.*?)</script>", RegexOptions.Singleline);
            if (!match.Success)
            {
                return new List<WallpaperItem>();
            }

            var json = WebUtility.HtmlDecode(match.Groups["json"].Value);
            var wallpapers = new List<WallpaperItem>();

            using var document = JsonDocument.Parse(json);
            if (!TryGetNestedProperty(document.RootElement, out var wallpapersElement, wallpapersArrayPath) ||
                wallpapersElement.ValueKind != JsonValueKind.Array)
            {
                return wallpapers;
            }

            foreach (var wrapper in wallpapersElement.EnumerateArray())
            {
                if (!wrapper.TryGetProperty("entity", out var item) || item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var id = GetString(item, "id");
                var title = GetString(item, "title", "WallpaperHub wallpaper");
                var description = GetString(item, "description", title);
                var thumbnail = GetString(item, "thumbnail");
                var full = GetWallpaperHubBestResolution(item, out var resolutionLabel, out var resolution);
                var sourceUrl = GetString(item, "source", $"https://www.wallpaperhub.app/wallpapers");

                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(thumbnail))
                {
                    continue;
                }

                wallpapers.Add(new WallpaperItem
                {
                    Id = id,
                    Title = title,
                    Description = description,
                    ImageUrl = thumbnail,
                    FullPhotoUrl = string.IsNullOrWhiteSpace(full) ? thumbnail : full,
                    SourceUrl = sourceUrl,
                    Likes = string.Empty,
                    Downloads = GetString(item, "downloads", string.Empty),
                    Resolution = string.IsNullOrWhiteSpace(resolution) ? resolutionLabel : resolution,
                    QualityTag = GetQualityTag(resolution),
                    IsAI = false
                });
            }

            return wallpapers;
        }

        private async Task<List<WallpaperItem>> GetPexelsWallpapersAsync(int page, string mode, CancellationToken cancellationToken)
        {
            var apiKey = ApiKeySettingsService.GetPexelsApiKey();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("Pexels support needs a Pexels API key. Add it in Settings > API Keys, then try again.");
            }

            var requestUrl = string.Equals(mode, "Curated", StringComparison.OrdinalIgnoreCase)
                ? $"https://api.pexels.com/v1/curated?page={page}&per_page=30"
                : $"https://api.pexels.com/v1/search?query={Uri.EscapeDataString(string.IsNullOrWhiteSpace(mode) ? "wallpaper" : mode)}&orientation=landscape&page={page}&per_page=30";

            using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            request.Headers.TryAddWithoutValidation("Authorization", apiKey);
            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var wallpapers = new List<WallpaperItem>();

            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("photos", out var photosElement) || photosElement.ValueKind != JsonValueKind.Array)
            {
                return wallpapers;
            }

            foreach (var photo in photosElement.EnumerateArray())
            {
                var id = GetString(photo, "id");
                var photographer = GetString(photo, "photographer", "Pexels photographer");
                var sourceUrl = GetString(photo, "url");
                var width = GetString(photo, "width");
                var height = GetString(photo, "height");
                var thumbnail = GetNestedString(photo, "src", "medium");
                var full = GetNestedString(photo, "src", "original");
                var alt = GetString(photo, "alt", "Pexels photo");
                var resolution = !string.IsNullOrWhiteSpace(width) && !string.IsNullOrWhiteSpace(height) ? $"{width}x{height}" : string.Empty;

                wallpapers.Add(new WallpaperItem
                {
                    Id = id,
                    Title = alt,
                    Description = $"Photo by {photographer} on Pexels.",
                    ImageUrl = thumbnail,
                    FullPhotoUrl = full,
                    SourceUrl = sourceUrl,
                    Likes = string.Empty,
                    Downloads = string.Empty,
                    Resolution = resolution,
                    QualityTag = GetQualityTag(resolution),
                    IsAI = false
                });
            }

            return wallpapers;
        }

        // tiles = the collection's own page (scraped - the API cannot filter by
        // collection, so it is gone from this path entirely). mode = the collection
        // NAME, resolved against the live-loaded list; a name with no slug = loud
        // throw (the index fetch failed - no silent empty grid, no stale default).
        private async Task<List<WallpaperItem>> GetPixabayWallpapersAsync(int page, string mode, CancellationToken cancellationToken)
        {
            var slug = PixabayCollections
                .FirstOrDefault(collection => collection.Name.Equals(mode ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                .Slug;
            if (string.IsNullOrEmpty(slug))
            {
                throw new InvalidOperationException($"No pixabay collection named '{mode}' is loaded - the collections index fetch failed, so this grid cannot honestly fetch anything.");
            }

            var url = $"https://pixabay.com/collections/{slug}/?pagi={page}";
            var html = await GetPixabayHtmlAsync(url, cancellationToken);
            var wallpapers = new List<WallpaperItem>();

            var matches = Regex.Matches(
                html,
                "<div id=\"item-\\d+\" data-pk=\"(?<id>\\d+)\" class=\"item\">\\s*<a href=\"(?<href>/(?:photos|illustrations)/[^\\\"]+)\">\\s*<img[^>]+src=\"(?<img>https://cdn\\.pixabay\\.com/photo/[^\\\"]+__340\\.jpg)\"[^>]+alt=\"(?<alt>[^\\\"]*)\"",
                RegexOptions.Singleline);

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in matches)
            {
                var id = match.Groups["id"].Value;
                if (!seen.Add(id))
                {
                    continue;
                }

                var preview = match.Groups["img"].Value;
                wallpapers.Add(new WallpaperItem
                {
                    Id = id,
                    Title = WebUtility.HtmlDecode(match.Groups["alt"].Value).Trim(),
                    Description = "Pixabay collection item",
                    ImageUrl = preview,
                    // __340 preview -> _1280 original (single underscore; __1280 = 403)
                    FullPhotoUrl = preview.Replace("__340", "_1280"),
                    SourceUrl = $"https://pixabay.com{WebUtility.HtmlDecode(match.Groups["href"].Value)}",
                    Likes = string.Empty,
                    Downloads = string.Empty,
                    IsAI = false
                });
            }

            return wallpapers;
        }

        // desktopnexus: subdomain browse pages - thumbnail (6.7KB) is too small for cards, so the
        // grid uses /preview (34KB) and the full view /original (285KB), both verified by GET
        // (automata-private/desktopnexus.com/AGENTS.md)
        private async Task<List<WallpaperItem>> GetDesktopNexusWallpapersAsync(int page, string mode, CancellationToken cancellationToken)
        {
            // unknown/empty mode -> the all-feeds www listing (documented default)
            var subdomain = DesktopNexusCategories
                .FirstOrDefault(category => category.Title.Equals(mode ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                .Subdomain ?? "www";

            var url = page <= 1
                ? $"https://{subdomain}.desktopnexus.com/all/"
                : $"https://{subdomain}.desktopnexus.com/all/{page}/";
            var html = await _httpClient.GetStringAsync(url, cancellationToken);
            var wallpapers = new List<WallpaperItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var matches = Regex.Matches(
                html,
                "<a href=\"(?<href>https://[a-z0-9]+\\.desktopnexus\\.com/wallpaper/\\d+)/\" alt=\"(?<title>[^\"]+)\"[^>]*>\\s*<img[^>]+src=\"(?<thumb>https://assets\\.desktopnexus\\.com/[^\"]+)\"",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            foreach (Match match in matches)
            {
                var href = WebUtility.HtmlDecode(match.Groups["href"].Value) + "/";
                var title = WebUtility.HtmlDecode(match.Groups["title"].Value);
                var thumb = match.Groups["thumb"].Value;

                if (string.IsNullOrWhiteSpace(title) || !seen.Add(href))
                {
                    continue;
                }

                var id = Regex.Match(href, "\\d+").Value;
                wallpapers.Add(new WallpaperItem
                {
                    Id = id,
                    Title = title,
                    Description = "DesktopNexus wallpaper",
                    ImageUrl = thumb.Replace("/thumbnail", "/preview"),
                    FullPhotoUrl = thumb.Replace("/thumbnail", "/original"),
                    SourceUrl = href,
                    Likes = string.Empty,
                    Downloads = string.Empty,
                    IsAI = false
                });
            }

            return wallpapers;
        }

        // digital blasphemy: woo product loop; full resolutions are membership-only, so both
        // urls point at the keyless 640x480 CDN preview (documented cap)
        // (automata-private/digitalblasphemy.com/AGENTS.md)
        private async Task<List<WallpaperItem>> GetDigitalBlasphemyWallpapersAsync(int page, string mode, CancellationToken cancellationToken)
        {
            var path = DigitalBlasphemyModes
                .FirstOrDefault(entry => entry.Title.Equals(mode ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                .Path ?? "product-category/wallpapers";

            if (page > 1 && path == "product-tag/free")
            {
                // the free tag ships all 21 products on one page - there is no /page/N/
                return new List<WallpaperItem>();
            }

            var url = page <= 1
                ? $"https://digitalblasphemy.com/{path}/"
                : $"https://digitalblasphemy.com/{path}/page/{page}/";
            var html = await _httpClient.GetStringAsync(url, cancellationToken);
            var wallpapers = new List<WallpaperItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var matches = Regex.Matches(
                html,
                "<a href=\"(?<href>https://digitalblasphemy\\.com/sec/[^/\"]+)/\"[^>]*>\\s*<img src=\"(?<thumb>https://cdn\\.digitalblasphemy\\.com/thumbnail/[^\"]+)\" alt=\"(?<alt>[^\"]*)\">\\s*<h2[^>]*>(?<title>.*?)</h2>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            foreach (Match match in matches)
            {
                var href = match.Groups["href"].Value + "/";
                var thumb = match.Groups["thumb"].Value;
                var title = WebUtility.HtmlDecode(Regex.Replace(match.Groups["title"].Value, "<[^>]+>", string.Empty)).Trim();
                if (string.IsNullOrWhiteSpace(title))
                {
                    title = WebUtility.HtmlDecode(match.Groups["alt"].Value).Trim();
                }

                if (string.IsNullOrWhiteSpace(title) || !seen.Add(href))
                {
                    continue;
                }

                var resolutionMatch = Regex.Match(thumb, "_(?<res>\\d+x\\d+)\\.jpg$");
                var resolution = resolutionMatch.Success ? resolutionMatch.Groups["res"].Value : string.Empty;

                wallpapers.Add(new WallpaperItem
                {
                    Id = Regex.Match(href, "\\d+").Success ? Regex.Match(href, "\\d+").Value : href,
                    Title = title,
                    Description = "Digital Blasphemy wallpaper",
                    ImageUrl = thumb,
                    FullPhotoUrl = thumb,
                    SourceUrl = href,
                    Likes = string.Empty,
                    Downloads = string.Empty,
                    Resolution = resolution,
                    QualityTag = GetQualityTag(resolution),
                    IsAI = false
                });
            }

            return wallpapers;
        }

        // hdwallpapers: item template serves latest + every category; full-size = detail og:image
        // (/previews/<slug>-<id>.jpg - dropping thumb_ from the cdn url is 404)
        // (automata-private/www.hdwallpapers.net/AGENTS.md)
        private async Task<List<WallpaperItem>> GetHdwallpapersWallpapersAsync(int page, string mode, CancellationToken cancellationToken)
        {
            var slug = HdwallpapersModes
                .FirstOrDefault(entry => entry.Title.Equals(mode ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                .Slug ?? "latest-wallpapers";

            var url = page <= 1
                ? $"https://www.hdwallpapers.net/{slug}"
                : $"https://www.hdwallpapers.net/{slug}/page-{page}";
            var html = await _httpClient.GetStringAsync(url, cancellationToken);
            var wallpapers = new List<WallpaperItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var matches = Regex.Matches(
                html,
                "href=\"(?<href>https://www\\.hdwallpapers\\.net/[^\"]+-wallpaper-\\d+\\.htm)\" title=\"(?<title>[^\"]+)\"[^>]*>\\s*<img[^>]+src=\"(?<thumb>https://static\\d\\.hdwallpapers\\.net/[^\"]+)\"",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            foreach (Match match in matches)
            {
                var href = match.Groups["href"].Value;
                var title = WebUtility.HtmlDecode(match.Groups["title"].Value);
                var thumb = match.Groups["thumb"].Value;

                if (string.IsNullOrWhiteSpace(title) || !seen.Add(href))
                {
                    continue;
                }

                var full = thumb;
                var detail = Regex.Match(href, "/(?<cat>[^/]+)/(?<slug>[a-z0-9-]+)-wallpaper-(?<id>\\d+)\\.htm$");
                if (detail.Success)
                {
                    full = $"https://www.hdwallpapers.net/previews/{detail.Groups["slug"].Value}-{detail.Groups["id"].Value}.jpg";
                }

                wallpapers.Add(new WallpaperItem
                {
                    Id = detail.Success ? detail.Groups["id"].Value : href,
                    Title = title,
                    Description = "HDwallpapers.net wallpaper",
                    ImageUrl = thumb,
                    FullPhotoUrl = full,
                    SourceUrl = href,
                    Likes = string.Empty,
                    Downloads = string.Empty,
                    IsAI = false
                });
            }

            return wallpapers;
        }

        // pixiv: internal ajax search, anonymous-safe, restrict=safe PINNED on every request and
        // xRestrict re-checked per item (adult content policy); i.pximg.net serves 403 unless the
        // image load sends Referer pixiv.net - WallpaperItem.GetRefererFor handles that
        // (automata-private/www.pixiv.net/AGENTS.md)
        private async Task<List<WallpaperItem>> GetPixivWallpapersAsync(int page, string mode, CancellationToken cancellationToken)
        {
            var tag = string.IsNullOrWhiteSpace(mode) ? "wallpaper" : mode.ToLowerInvariant();
            var url = $"https://www.pixiv.net/ajax/search/artworks/{Uri.EscapeDataString(tag)}?word={Uri.EscapeDataString(tag)}&restrict=safe&p={page}&order=date_d";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Referer", "https://www.pixiv.net/");
            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var wallpapers = new List<WallpaperItem>();

            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("body", out var body) ||
                !body.TryGetProperty("illustManga", out var search) ||
                !search.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Array)
            {
                return wallpapers;
            }

            foreach (var item in data.EnumerateArray())
            {
                if (GetInt(item, "xRestrict") != 0)
                {
                    continue;
                }

                var id = GetString(item, "id");
                var thumb = GetString(item, "url");
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(thumb))
                {
                    continue;
                }

                // full = strip the per-image /c/<w>x<h>_<q>_<flag> thumb prefix (the value varies
                // per image, so regex it out instead of hardcoding one prefix)
                var full = Regex.Replace(thumb, "/c/\\d+x\\d+_[^/]+(?=/img-master)", "");
                var width = GetInt(item, "width");
                var height = GetInt(item, "height");
                var resolution = width > 0 && height > 0 ? $"{width}x{height}" : string.Empty;

                wallpapers.Add(new WallpaperItem
                {
                    Id = id,
                    Title = GetString(item, "title", "Pixiv illustration"),
                    Description = $"Illustration by {GetString(item, "userName", "Pixiv artist")} on Pixiv.",
                    ImageUrl = thumb,
                    FullPhotoUrl = full,
                    SourceUrl = $"https://www.pixiv.net/artworks/{id}",
                    Likes = string.Empty,
                    Downloads = string.Empty,
                    Resolution = resolution,
                    QualityTag = GetQualityTag(resolution),
                    IsAI = false
                });
            }

            return wallpapers;
        }

        // cara: one SSR explore page (props.pageProps.imagePosts, no cursor key in the payload);
        // posts ship a single full-size image so thumb == full
        // (automata-private/cara.app/AGENTS.md)
        private async Task<List<WallpaperItem>> GetCaraWallpapersAsync(int page, string mode, CancellationToken cancellationToken)
        {
            if (page > 1)
            {
                return new List<WallpaperItem>();
            }

            // cara.app (HTML and images.cara.app) returns 403 to .NET's SocketsHttpHandler
            // TLS fingerprint while curl.exe passes - curl is cara's ONLY transport, not a
            // fallback (Services/CurlClient.cs, verified live 2026-10-08)
            var html = await CurlClient.GetStringAsync("https://cara.app/explore", cancellationToken);
            var match = Regex.Match(html, "<script id=\"__NEXT_DATA__\" type=\"application/json\">(?<json>.*?)</script>", RegexOptions.Singleline);
            var wallpapers = new List<WallpaperItem>();
            if (!match.Success)
            {
                return wallpapers;
            }

            using var document = JsonDocument.Parse(match.Groups["json"].Value);
            if (!TryGetNestedProperty(document.RootElement, out var posts, "props", "pageProps", "imagePosts") ||
                posts.ValueKind != JsonValueKind.Array)
            {
                return wallpapers;
            }

            foreach (var post in posts.EnumerateArray())
            {
                var id = GetString(post, "id");
                var cover = GetString(post, "coverImage");
                if (string.IsNullOrWhiteSpace(cover) &&
                    post.TryGetProperty("images", out var images) &&
                    images.ValueKind == JsonValueKind.Array)
                {
                    foreach (var image in images.EnumerateArray())
                    {
                        cover = GetString(image, "src");
                        if (!string.IsNullOrWhiteSpace(cover))
                        {
                            break;
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(cover))
                {
                    continue;
                }

                var imageUrls = MakeAbsoluteUrl("https://images.cara.app", cover);
                var author = GetString(post, "name", "Cara artist");

                wallpapers.Add(new WallpaperItem
                {
                    Id = id,
                    Title = GetString(post, "title", "Cara artwork"),
                    Description = $"Artwork by {author} on Cara.",
                    ImageUrl = imageUrls,
                    FullPhotoUrl = imageUrls,
                    SourceUrl = $"https://cara.app/post/{id}",
                    Likes = string.Empty,
                    Downloads = string.Empty,
                    IsAI = false
                });
            }

            return wallpapers;
        }

        // wallpaper cave: album pages are single-shot (no server pagination exists anywhere on the
        // site); per-item titles do not exist on list pages, so cards get `<h1> #<n>` positional
        // titles (automata-private/wallpapercave.com/AGENTS.md)
        private async Task<List<WallpaperItem>> GetWallpaperCaveWallpapersAsync(int page, string mode, CancellationToken cancellationToken)
        {
            if (page > 1)
            {
                return new List<WallpaperItem>();
            }

            var album = WallpaperCaveAlbums
                .FirstOrDefault(entry => entry.Title.Equals(mode ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                .Album ?? "wallpapers";
            var html = await _httpClient.GetStringAsync($"https://wallpapercave.com/{album}", cancellationToken);
            var wallpapers = new List<WallpaperItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var heading = Regex.Match(html, "<h1[^>]*>(?<h1>[^<]+)</h1>", RegexOptions.Singleline);
            var albumTitle = WebUtility.HtmlDecode(heading.Groups["h1"].Value).Trim();
            if (string.IsNullOrWhiteSpace(albumTitle))
            {
                albumTitle = "Wallpapers";
            }

            var matches = Regex.Matches(
                html,
                "<a href=\"/w/(?<id>wp\\d+)\"[^>]*>\\s*<picture>.*?<img src=\"(?<img>/wp/wp\\d+\\.jpg)\"[^>]*class=\"wimg\"",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            var index = 0;
            foreach (Match match in matches)
            {
                var id = match.Groups["id"].Value;
                var file = match.Groups["img"].Value; // /wp/wp2471696.jpg
                if (!seen.Add(id))
                {
                    continue;
                }

                index++;
                wallpapers.Add(new WallpaperItem
                {
                    Id = id,
                    Title = $"{albumTitle} #{index}",
                    Description = "Wallpaper Cave wallpaper",
                    ImageUrl = $"https://wallpapercave.com/dwp1x/{file.Substring(4)}", // desktop <source> thumb: JPEG (/mwp/ now transcodes to image/avif - WinUI has no AVIF decoder, tiles decode-fail)
                    FullPhotoUrl = $"https://wallpapercave.com{file}",               // 1980px original
                    SourceUrl = $"https://wallpapercave.com/w/{id}",
                    Likes = string.Empty,
                    Downloads = string.Empty,
                    IsAI = false
                });
            }

            return wallpapers;
        }

        // wallpaper engine: SSR React-Query payload with ESCAPED quotes - split per item on the
        // publishedfileid marker (id/preview_url counts match, 30/30 per page); WE's own maturity
        // tags ride in the same tags array and are dropped per item (adult content policy)
        // (automata-private/steamcommunity.com/AGENTS.md)
        private async Task<List<WallpaperItem>> GetWallpaperEngineWallpapersAsync(int page, string mode, CancellationToken cancellationToken)
        {
            // verified filters only; Trending/unknown = no tag (the "Wallpaper" tag returned the
            // unfiltered list, so it is deliberately not shipped)
            var tag = !string.IsNullOrWhiteSpace(mode) &&
                      !mode.Equals("Trending", StringComparison.OrdinalIgnoreCase) &&
                      WallpaperEngineModes.Contains(mode, StringComparer.OrdinalIgnoreCase)
                ? mode
                : string.Empty;

            var url = $"https://steamcommunity.com/workshop/browse/?appid=431960&browsesort=trend&days=90&p={page}" +
                      (tag.Length > 0 ? $"&requiredtags%5B0%5D={Uri.EscapeDataString(tag)}" : string.Empty);
            var html = await _httpClient.GetStringAsync(url, cancellationToken);
            var wallpapers = new List<WallpaperItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var parts = Regex.Split(html, @"publishedfileid\\\"":\\""");
            foreach (var part in parts.Skip(1))
            {
                var idMatch = Regex.Match(part, @"^\d+");
                var urlMatch = Regex.Match(part, @"preview_url\\\"":\\""(?<url>[^\\""]+)");
                if (!idMatch.Success || !urlMatch.Success || !seen.Add(idMatch.Value))
                {
                    continue;
                }

                if (part.Contains("tag\\\":\\\"Questionable") ||
                    part.Contains("tag\\\":\\\"Mature") ||
                    part.Contains("tag\\\":\\\"Adult") ||
                    part.Contains("tag\\\":\\\"NSFW") ||
                    part.Contains("tag\\\":\\\"18+"))
                {
                    continue;
                }

                var titleMatch = Regex.Match(part, @"title\\\"":\\""(?<title>[^\\""]+)");
                var resolutionMatch = Regex.Match(part, @"tag\\\"":\\""(?<res>\d{3,4} x \d{3,4})\\""" );
                var resolution = resolutionMatch.Success ? resolutionMatch.Groups["res"].Value.Replace(" ", string.Empty) : string.Empty;
                var preview = urlMatch.Groups["url"].Value;

                wallpapers.Add(new WallpaperItem
                {
                    Id = idMatch.Value,
                    Title = titleMatch.Success ? WebUtility.HtmlDecode(titleMatch.Groups["title"].Value) : "Wallpaper Engine wallpaper",
                    Description = "Wallpaper Engine workshop wallpaper",
                    ImageUrl = preview,
                    FullPhotoUrl = preview, // workshop file downloads need an authenticated call - previews are the public surface
                    SourceUrl = $"https://steamcommunity.com/sharedfiles/filedetails/?id={idMatch.Value}",
                    Likes = string.Empty,
                    Downloads = string.Empty,
                    Resolution = resolution,
                    QualityTag = GetQualityTag(resolution),
                    IsAI = false
                });
            }

            return wallpapers;
        }

        // artgram: livewire "gallery" at / with GET ?sortBy=trending|latest|oldest + ?page=N (50
        // cards). cards carry title + artist + a presigned 512x512 fsn1 cover inline - the
        // X-Amz-Expires=3600 urls are minted per page request, so they must never be cached
        // across sessions. originals only exist on the detail page: FullPhotoUrl keeps the cover
        // and WallpaperDetailPage upgrades it to the original on demand
        // (automata-private/www.artgram.co/AGENTS.md)
        private async Task<List<WallpaperItem>> GetArtgramWallpapersAsync(int page, string mode, CancellationToken cancellationToken)
        {
            var sort = mode?.Trim().ToLowerInvariant() switch
            {
                "latest" => "latest",
                "oldest" => "oldest",
                _ => "trending"
            };

            var url = $"https://www.artgram.co/?sortBy={sort}" + (page > 1 ? $"&page={page}" : string.Empty);
            var html = await _httpClient.GetStringAsync(url, cancellationToken);
            var wallpapers = new List<WallpaperItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var matches = Regex.Matches(
                html,
                "<a x-data=\"\" wire:key=\"\\d+\"[^>]*href=\"https://www\\.artgram\\.co/a/(?<slug>[A-Za-z0-9-]+)\"[^>]*>.*?<div class=\"text-sm font-semibold\">(?<title>[^<]*)</div>\\s*<div class=\"text-xs font-medium mt-1\">(?<artist>[^<]*)</div>.*?<img[^>]*src=\"(?<img>https://fsn1\\.your-objectstorage\\.com[^\"]+)\"",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            foreach (Match match in matches)
            {
                var slug = match.Groups["slug"].Value;
                if (!seen.Add(slug))
                {
                    continue;
                }

                var title = WebUtility.HtmlDecode(match.Groups["title"].Value).Trim();
                var artist = WebUtility.HtmlDecode(match.Groups["artist"].Value).Trim();
                var cover = WebUtility.HtmlDecode(match.Groups["img"].Value);
                if (title.Length == 0)
                {
                    title = slug;
                }

                wallpapers.Add(new WallpaperItem
                {
                    Id = slug,
                    Title = title,
                    Description = artist.Length > 0 ? $"Artgram \u00b7 {artist}" : "Artgram artwork",
                    ImageUrl = cover,
                    FullPhotoUrl = cover, // 512x512 cover; WallpaperDetailPage upgrades to the original
                    SourceUrl = $"https://www.artgram.co/a/{slug}",
                    Likes = string.Empty,
                    Downloads = string.Empty,
                    IsAI = false
                });
            }

            return wallpapers;
        }

        private static string GetWallpaperHubBestResolution(JsonElement item, out string resolutionLabel, out string resolution)
        {
            resolutionLabel = string.Empty;
            resolution = string.Empty;
            string bestUrl = string.Empty;
            long bestPixels = 0;

            if (!item.TryGetProperty("variations", out var variationsElement) || variationsElement.ValueKind != JsonValueKind.Array)
            {
                return bestUrl;
            }

            foreach (var variation in variationsElement.EnumerateArray())
            {
                if (!variation.TryGetProperty("resolutions", out var resolutionsElement) || resolutionsElement.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var res in resolutionsElement.EnumerateArray())
                {
                    var url = GetString(res, "url");
                    if (string.IsNullOrWhiteSpace(url))
                    {
                        continue;
                    }

                    var width = GetInt(res, "width");
                    var height = GetInt(res, "height");
                    if (height > width)
                    {
                        continue;
                    }

                    var pixels = (long)width * height;
                    if (pixels > bestPixels)
                    {
                        bestPixels = pixels;
                        bestUrl = url;
                        resolutionLabel = GetString(res, "resolutionLabel");
                        resolution = width > 0 && height > 0 ? $"{width}x{height}" : resolutionLabel;
                    }
                }
            }

            return bestUrl;
        }

        private static bool TryGetNestedProperty(JsonElement element, out JsonElement value, params string[] path)
        {
            value = element;
            foreach (var propertyName in path)
            {
                if (!value.TryGetProperty(propertyName, out value))
                {
                    return false;
                }
            }

            return true;
        }

        private static string GetNestedString(JsonElement element, string parentPropertyName, string childPropertyName)
        {
            if (element.TryGetProperty(parentPropertyName, out var parentElement) && parentElement.ValueKind == JsonValueKind.Object)
            {
                return GetString(parentElement, childPropertyName);
            }

            return string.Empty;
        }

        private static int GetInt(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var value))
            {
                return 0;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            {
                return number;
            }

            return int.TryParse(GetString(element, propertyName), out number) ? number : 0;
        }

        private static string GetString(JsonElement element, string propertyName, string fallback = "")
        {
            if (!element.TryGetProperty(propertyName, out var value))
            {
                return fallback;
            }

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? fallback,
                JsonValueKind.Number => value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => fallback
            };
        }

        private static string MakeAbsoluteUrl(string baseUrl, string value)
        {
            if (Uri.TryCreate(value, UriKind.Absolute, out _))
            {
                return value;
            }

            if (!value.StartsWith("/"))
            {
                value = "/" + value;
            }

            return baseUrl.TrimEnd('/') + value;
        }

        private static string NormalizeSimpleDesktopUrl(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            if (value.StartsWith("//"))
            {
                value = "https:" + value;
            }
            else if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                value = "https://" + value.Substring("http://".Length);
            }

            return value;
        }

        private static string GetQualityTag(string resolution)
        {
            if (string.IsNullOrWhiteSpace(resolution))
            {
                return string.Empty;
            }

            var parts = resolution.Split('x', 'X');
            if (parts.Length != 2 || !int.TryParse(parts[0], out var width) || !int.TryParse(parts[1], out var height))
            {
                return string.Empty;
            }

            var maxSide = Math.Max(width, height);
            return maxSide switch
            {
                >= 7680 => "8K",
                >= 5120 => "5K",
                >= 3840 => "4K",
                >= 1920 => "1080p",
                _ => string.Empty
            };
        }

        private static string ToTitleCase(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return string.Join(" ", value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(word =>
                word.Length == 1 ? word.ToUpperInvariant() : char.ToUpperInvariant(word[0]) + word.Substring(1)));
        }
    }
}
