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

        // the three LOCAL platforms (their own services + pages). they sit in the
        // same registry as the public ones below, so every platform list in the
        // app is built from ONE table
        private const string Backiee = "Backiee";
        private const string AlphaCoders = "AlphaCoders";
        private const string ArtStation = "ArtStation";

        // "Bing Editions" = bingwallpaper.anerg.com, a THIRD-PARTY Bing archive - not
        // Microsoft's own feed, which is the "Bing Wallpaper Archive" row above. the name
        // has to stay distinct from that one in the picker (names are all the picker
        // shows), so it uses the site's own wording for its country list ("Bing editions").
        // what it adds over the official feed: 11 country editions and a monthly archive
        // back to 2011-10, plus 3840x2160 originals on a public CDN (the official feed
        // tops out at 1920x1080). plain .NET passes everywhere - no curl, no key.
        // (automata-private/bingwallpaper.anerg.com/AGENTS.md)
        private const string BingEditions = "Bing Editions";
        private const string BingEditionsBaseUrl = "https://bingwallpaper.anerg.com";
        private const string BingEditionsFirstMonth = "201110";

        // label -> (edition page, archive code). the site's root page IS the United States
        // edition (/us 301s to /) but its detail and month paths still use the "us" code,
        // so both halves are carried. the site's own default edition stays first (it is
        // what the platform's grid opens on), the rest are alphabetical.
        private static readonly (string Label, string PagePath, string Code)[] BingEditionsList =
        {
            ("United States", "/", "us"),
            ("Australia", "/au", "au"),
            ("Canada", "/ca", "ca"),
            ("China", "/cn", "cn"),
            ("France", "/fr", "fr"),
            ("Germany", "/de", "de"),
            ("Italy", "/it", "it"),
            ("Japan", "/jp", "jp"),
            ("New Zealand", "/nz", "nz"),
            ("Spain", "/es", "es"),
            ("United Kingdom", "/uk", "uk")
        };

        // the newest month an edition carries is read off its own edition page (the first
        // <h2 id="month-YYYYMM"> section) and cached per edition, because page 1 always
        // lands there first and every later page walks the archive back from that month
        private static readonly object BingEditionsMonthLock = new object();
        private static readonly Dictionary<string, string> BingEditionsNewestMonth =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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

        // All-categories/Latest pill enrichment (2026-10-12): the pixabay latest
        // search's JSON-LD carries title+url only (NO per-item category - verified
        // 2026-10-12), but every item's OWN detail page ships the site's full tag
        // list in its ImageObject JSON-LD `keywords` field ("Sheep, Cartoon,
        // Meadow, ..." - verified live across 3 items). one fetch per APPLIED
        // wallpaper (1/min - far under Cloudflare's ~4-rapid limit) yields the
        // FIRST tag = the item's own category for the history pill. same sanctioned
        // transport as every other pixabay HTML fetch (curl + the Sec-Fetch pair);
        // a challenge or parse miss THROWS so the caller can log the reason -
        // never a silent pretend.
        public async Task<string> GetPixabayItemCategoryAsync(string itemUrl, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(itemUrl))
            {
                throw new ArgumentException("pixabay item url is empty - cannot look up its tags", nameof(itemUrl));
            }

            var html = await GetPixabayHtmlAsync(itemUrl, cancellationToken);

            // string form first, then the schema.org array form
            var keywords = Regex.Match(html, "\"keywords\":\\s*\"(?<v>[^\"]+)\"").Groups["v"].Value;
            if (string.IsNullOrEmpty(keywords))
            {
                keywords = Regex.Match(html, "\"keywords\":\\s*\\[\\s*\"(?<v>[^\"]+)\"").Groups["v"].Value;
            }

            if (string.IsNullOrEmpty(keywords))
            {
                throw new InvalidOperationException(
                    $"no keywords field in the JSON-LD of {itemUrl} - pixabay's detail markup may have changed");
            }

            var firstTag = keywords.Split(',')[0].Trim();
            if (string.IsNullOrEmpty(firstTag))
            {
                throw new InvalidOperationException($"empty first tag in the keywords field of {itemUrl}");
            }

            return firstTag;
        }

        // ---- history pill enrichment: per-item category lookups (registry ItemCategory) ----
        // one resolver per platform whose LATEST feed carries no usable category but whose
        // OWN item page does. the slideshow runs it once per APPLIED wallpaper (1 fetch per
        // tick, 60s apart - far under every host's rate limit) right before AddEntry.
        // contract: return the item's own first real value; "" = the page genuinely carries
        // none (honest no-pill, logged as `category: none`); throw = lookup failed (logged).
        // never a failed apply, never a guessed pill.

        // pexels: the API photo object has no category field, but the photo PAGE ships its
        // tags in <meta name="keywords"> ("Gradients, Green Gradient, ..." - verified live
        // across 4 photos 2026-10-12). transport = the same sanctioned full-browser-header
        // curl path every other www.pexels.com HTML fetch uses (GetPexelsHtmlAsync).
        public async Task<string> GetPexelsItemCategoryAsync(string itemUrl, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(itemUrl))
            {
                throw new ArgumentException("pexels item url is empty - cannot look up its keywords", nameof(itemUrl));
            }

            var html = await GetPexelsHtmlAsync(itemUrl, cancellationToken);

            // attribute order can flip - name-first, then content-first
            var keywords = Regex.Match(html, "<meta\\s+name=\"keywords\"\\s+content=\"(?<v>[^\"]+)\"",
                RegexOptions.IgnoreCase).Groups["v"].Value;
            if (string.IsNullOrEmpty(keywords))
            {
                keywords = Regex.Match(html, "<meta\\s+content=\"(?<v>[^\"]+)\"\\s+name=\"keywords\"",
                    RegexOptions.IgnoreCase).Groups["v"].Value;
            }

            if (string.IsNullOrEmpty(keywords))
            {
                throw new InvalidOperationException(
                    $"no keywords meta on {itemUrl} - pexels photo markup may have changed (or the page challenged)");
            }

            var firstTag = keywords.Split(',')[0].Trim();
            if (string.IsNullOrEmpty(firstTag))
            {
                throw new InvalidOperationException($"empty first keyword in the meta of {itemUrl}");
            }

            return firstTag;
        }

        // digital blasphemy: feeds carry no category, but every item page lists the site's
        // own product tags as rel="tag" anchors - the FIRST is always the year ("2026"),
        // so years are skipped and the first real tag is the theme ("Autumn" on
        // /sec/autumnwood2k262/ - verified live 2026-10-12). the nav dropdown also links
        // product-tag pages but carries no rel="tag", so it can never match. plain .NET
        // fetch, same transport as the feed.
        public async Task<string> GetDigitalBlasphemyItemCategoryAsync(string itemUrl, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(itemUrl))
            {
                throw new ArgumentException("digital blasphemy item url is empty - cannot look up its tags", nameof(itemUrl));
            }

            var html = await _httpClient.GetStringAsync(itemUrl, cancellationToken);
            var tags = Regex.Matches(
                html,
                "<a(?=[^>]*href=\"https://digitalblasphemy\\.com/product-tag/[^\"]+\")(?=[^>]*\\brel=\"tag\")[^>]*>(?<t>.*?)</a>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            foreach (Match tag in tags)
            {
                var value = WebUtility.HtmlDecode(Regex.Replace(tag.Groups["t"].Value, "<[^>]+>", string.Empty)).Trim();
                if (string.IsNullOrEmpty(value) || Regex.IsMatch(value, "^\\d{4}$"))
                {
                    continue;
                }

                return value;
            }

            throw new InvalidOperationException(
                $"no non-year product-tag anchors on {itemUrl} - digital blasphemy item markup may have changed");
        }

        // wallpaper cave: feeds carry no category, but each item page's breadcrumb root IS
        // the site's taxonomy ("Devices" / "Screen" / "Games" - the first
        // <span itemprop="name"> inside #breadcrumbs; Home uses a <meta> so it never
        // matches). the breadcrumb is the ONLY per-item category signal on the site
        // (metas/description/JSON-LD carry none - curl-diffed 2026-10-12), and the
        // site only assigns it to SOME uploads: 18 of 53 items on the current
        // /wallpapers feed ship a populated nav, 34 ship it PRESENT but EMPTY
        // (<nav id="breadcrumbs"> </nav>, uncategorized uploads) - that is honest
        // no-pill on those rows, never a guess; a missing nav = markup drift = thrown.
        public async Task<string> GetWallpaperCaveItemCategoryAsync(string itemUrl, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(itemUrl))
            {
                throw new ArgumentException("wallpaper cave item url is empty - cannot read its breadcrumbs", nameof(itemUrl));
            }

            var html = await _httpClient.GetStringAsync(itemUrl, cancellationToken);
            var nav = Regex.Match(html, "<nav id=\"breadcrumbs\"[^>]*>(?<bc>.*?)</nav>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!nav.Success)
            {
                throw new InvalidOperationException(
                    $"no breadcrumbs nav on {itemUrl} - wallpaper cave item markup may have changed");
            }

            var crumb = Regex.Match(nav.Groups["bc"].Value, "<span itemprop=\"name\">(?<n>[^<]+)</span>",
                RegexOptions.IgnoreCase);
            if (!crumb.Success)
            {
                return string.Empty;
            }

            return WebUtility.HtmlDecode(crumb.Groups["n"].Value).Trim();
        }

        // pexels categories = the site's own Wallpapers discover index
        // (https://www.pexels.com/discover/wallpapers/ - the __NEXT_DATA__
        // props.pageProps.topics[] array: 9 topics of search-term pills, each
        // term = a /search/<term>/ link. user audit 2026-10-09: use every
        // section EXCEPT "Phone & mobile" (slug "phone" skipped - a size class,
        // not a wallpaper style) => 78 terms over 8 topics. the term is BOTH the
        // card name (the pill's own lowercase text) and the API search query
        // (the site's link carries no orientation param, so the drill dropped
        // orientation=landscape to match it - "vertical wallpaper" resolves to
        // portrait). the pill's own 420x420 photo (imageUrl + ?h=420&w=420&
        // fit=crop&dpr=1) is the card thumb: fills every card with ZERO API
        // calls (the free tier caps 200 req/hour - 78 search fills per open
        // would eat 40% of the quota before any browsing). the API stays only
        // for the drills (per_page=30 search). transport: www.pexels.com 403s
        // .NET's TLS fingerprint AND bare/minimal-header curl - the FULL browser
        // header set through CurlClient passes (verified side by side
        // 2026-10-09), so html fetches route there like pixabay/cara. one pill
        // ("live wallpaper") links to /search/videos/ on the site, but its
        // photo surface serves 100s of results - the photo query is what this
        // image app ships. the old Curated/Nature/Space query entries are GONE
        // (Nature/Space live on as other platforms' cards; Curated had no other
        // owner and disappeared). (automata-private/www.pexels.com/AGENTS.md)
        private static readonly object PexelsDiscoverLock = new object();
        private static readonly List<(string Term, string ImageUrl)> PexelsDiscoverList =
            new List<(string Term, string ImageUrl)>();

        public static IReadOnlyList<(string Term, string ImageUrl)> PexelsDiscoverTerms
        {
            get
            {
                lock (PexelsDiscoverLock)
                {
                    return PexelsDiscoverList.ToArray();
                }
            }
        }

        public static void SetPexelsDiscoverTerms(IReadOnlyList<(string Term, string ImageUrl)> terms)
        {
            lock (PexelsDiscoverLock)
            {
                PexelsDiscoverList.Clear();
                PexelsDiscoverList.AddRange(terms);
            }
        }

        // the discover page's representative photo for a term - the card thumb
        // source. missing term = loud throw (index fetch failed), never a
        // placeholder that lies about the card having art.
        public static string GetPexelsTermImageUrl(string term)
        {
            foreach (var entry in PexelsDiscoverTerms)
            {
                if (entry.Term.Equals(term ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                {
                    return entry.ImageUrl + "?h=420&w=420&fit=crop&dpr=1";
                }
            }

            throw new InvalidOperationException(
                $"No pexels discover term named '{term}' is loaded - the discover/wallpapers index fetch failed, so this card cannot honestly pick a thumbnail.");
        }

        public async Task<List<(string Term, string ImageUrl)>> GetPexelsDiscoverIndexAsync(
            CancellationToken cancellationToken = default)
        {
            var html = await GetPexelsHtmlAsync("https://www.pexels.com/discover/wallpapers/", cancellationToken);

            // the taxonomy lives in Next.js' __NEXT_DATA__ payload; the script tag
            // carries id + type + crossorigin attrs, so match on the id alone. the
            // JSON escapes '<' itself, so a non-greedy </script> stop is safe.
            var match = Regex.Match(
                html,
                @"<script id=""__NEXT_DATA__""[^>]*>(?<json>[\s\S]*?)</script>");
            if (!match.Success)
            {
                throw new InvalidOperationException(
                    "pexels discover page had no __NEXT_DATA__ payload - the markup changed, refusing to guess a category list.");
            }

            var terms = new List<(string Term, string ImageUrl)>();
            using (var document = JsonDocument.Parse(match.Groups["json"].Value))
            {
                var topics = document.RootElement
                    .GetProperty("props")
                    .GetProperty("pageProps")
                    .GetProperty("topics");

                foreach (var topic in topics.EnumerateArray())
                {
                    var slug = topic.GetProperty("slug").GetString();
                    if (string.Equals(slug, "phone", StringComparison.OrdinalIgnoreCase))
                    {
                        continue; // "Phone & mobile" excluded by user decision 2026-10-09
                    }

                    foreach (var term in topic.GetProperty("terms").EnumerateArray())
                    {
                        var name = term.GetProperty("term").GetString();
                        var image = term.GetProperty("imageUrl").GetString();
                        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(image))
                        {
                            throw new InvalidOperationException(
                                "pexels discover term is missing its name or imageUrl - the payload shape changed, refusing to ship a broken card.");
                        }

                        terms.Add((name.Trim(), image));
                    }
                }
            }

            if (terms.Count == 0)
            {
                throw new InvalidOperationException(
                    "pexels discover payload had topics but no terms - the payload shape changed.");
            }

            return terms;
        }

        // pexels' Cloudflare edge needs the full browser header set: minimal curl
        // (UA alone, or the pixabay Sec-Fetch pair) and .NET's HttpClient both
        // 403 (verified side by side 2026-10-09) - only curl + Accept,
        // Accept-Language, the three Sec-Fetch-* headers and
        // Upgrade-Insecure-Requests pass, through CurlClient like pixabay/cara.
        // curl's --fail turns any 4xx/5xx into a thrown error -> the loud bar;
        // a 200 challenge body is caught by the __NEXT_DATA__ absence check above.
        private async Task<string> GetPexelsHtmlAsync(string url, CancellationToken cancellationToken)
        {
            return await CurlClient.GetStringAsync(url, cancellationToken, PexelsBrowserHeaders);
        }

        private static readonly string[] PexelsBrowserHeaders =
        {
            "Accept: text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8",
            "Accept-Language: en-US,en;q=0.9",
            "Sec-Fetch-Mode: navigate",
            "Sec-Fetch-Site: none",
            "Sec-Fetch-Dest: document",
            "Upgrade-Insecure-Requests: 1"
        };

        // wallpaperhub categories = the site's own Collections index
        // (https://www.wallpaperhub.app/collections - single page today, 17
        // collections; ?page=N is ignored and re-serves the same rows, so the
        // loader stops at the first page with no NEW ids and real future
        // pagination just works). live-loaded by the Categories page
        // (GetWallpaperHubCollectionsIndexAsync + SetWallpaperHubCollections),
        // same loud contract as backiee/alphacoders/pixabay - NO static list, a
        // failed fetch = a loud error line. title is the browse key, id is the
        // /collections/<id> page path (pageProps.collectionWallpapers - the drill
        // fetcher is unchanged). the site passes .NET's TLS fingerprint (200 via
        // SocketsHttpHandler) - unlike pixabay/cara no curl is needed here.
        // (automata-private/wallpaperhub.app/AGENTS.md)
        private static readonly object WallpaperHubCollectionsLock = new object();
        private static readonly List<(string Title, string Id)> WallpaperHubCollectionList =
            new List<(string Title, string Id)>();

        public static IReadOnlyList<(string Title, string Id)> WallpaperHubCollections
        {
            get
            {
                lock (WallpaperHubCollectionsLock)
                {
                    return WallpaperHubCollectionList.ToArray();
                }
            }
        }

        public static void SetWallpaperHubCollections(IReadOnlyList<(string Title, string Id)> collections)
        {
            lock (WallpaperHubCollectionsLock)
            {
                WallpaperHubCollectionList.Clear();
                WallpaperHubCollectionList.AddRange(collections);
            }
        }

        // walk the collections index page by page: page 1 = the bare url, then
        // ?page=N; stop at the first page that yields no NEW ids (today the site
        // ignores ?page=N and page 2 comes back identical - one wasted fetch).
        public async Task<List<(string Title, string Id)>> GetWallpaperHubCollectionsIndexAsync(
            CancellationToken cancellationToken = default)
        {
            var result = new List<(string Title, string Id)>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int page = 1; page <= 10; page++)
            {
                var url = page == 1
                    ? "https://www.wallpaperhub.app/collections"
                    : $"https://www.wallpaperhub.app/collections?page={page}";
                var html = await _httpClient.GetStringAsync(url, cancellationToken);
                var rows = ParseWallpaperHubCollectionRows(html);
                int added = 0;
                foreach (var row in rows)
                {
                    if (seen.Add(row.Id))
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
                "wallpaperhub collections index did not end within 10 pages - refusing to ship a truncated category list.");
        }

        private static List<(string Title, string Id)> ParseWallpaperHubCollectionRows(string html)
        {
            var result = new List<(string Title, string Id)>();
            var matches = Regex.Matches(
                html,
                "<h3>(?<name>[^<]+)</h3>(?s:.*?)href=\"/collections/(?<id>\\d+)\"",
                RegexOptions.Singleline);

            foreach (Match m in matches)
            {
                var name = WebUtility.HtmlDecode(m.Groups["name"].Value).Trim();
                if (name.Length == 0)
                {
                    continue;
                }

                result.Add((name, m.Groups["id"].Value));
            }

            return result;
        }

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

        // digital blasphemy: woo listing paths; galleries live on
        // /wallpaper-galleries/ (14 product-tag pages, ported 2026-10-12).
        // single-page tags 404 past /page/1/ (curl-verified) -> honest
        // end-of-feed empty list instead of a 404 fetch
        // (automata-private/digitalblasphemy.com/AGENTS.md)
        private static readonly (string Title, string Path, bool SinglePage)[] DigitalBlasphemyModes =
        {
            ("Wallpapers", "product-category/wallpapers", false),
            ("Abstracts", "product-tag/abstracts", false),
            ("Autumn", "product-tag/autumn", false),
            ("Christmas", "product-tag/christmas", true),
            ("Day Scenes", "product-tag/day-scenes", false),
            ("Free", "product-tag/free", true),
            ("Halloween", "product-tag/halloween", true),
            ("Interiors", "product-tag/interiors", false),
            ("Night Scenes", "product-tag/night-scenes", false),
            ("Other Holidays", "product-tag/other-holidays", true),
            ("Pickle Jar", "product-tag/pickle-jar", false),
            ("Planetscapes", "product-tag/planetscapes", false),
            ("Spring", "product-tag/spring", false),
            ("Underwater", "product-tag/underwater", true),
            ("Winter", "product-tag/winter", false),
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

        // ===== platform registry =============================================================
        // THE single list: one row per site the app knows about. adding a platform is ONE row
        // here - every platform surface below derives from it, so no other file needs a list,
        // a switch arm, or a name literal:
        //   picker grid             -> GetPickerPlatformNames()
        //   not-implemented dialog  -> IsImplementedPlatform() + GetImplementedPlatformNames()
        //   Set-slideshow boxes     -> GetImplementedPlatformNames()
        //   Categories scope menu   -> GetCategoryPlatformNames()
        //   category rejection      -> IsCategoryExcluded() (an implemented row with
        //                              CategoryCapable = false is the whole change)
        //   merged cards + ticks    -> GetSupportedPlatformNames() + IsCategoryExcluded()
        //   grid chips / fetch keys -> GetModes()
        //   grid description        -> GetPlatformDescription()
        //   fetch routing           -> GetWallpapersAsync() (the row's Fetch delegate)
        //   live index error lines  -> HasLiveIndex()
        //   thumbnail gate          -> NeedsSoloThumbGate() (solo = 1-wide fill, else 4-wide)
        //   history pill lookup     -> GetItemCategoryResolver() (the row's ItemCategory
        //                              delegate; null = no per-item category source)
        //
        // kinds: Planned = listed in the picker but not built yet (7 WAF-walled + 2 needing
        // credentials, see the picker's not-implemented dialog); Local = owns its own service
        // and page, so its Fetch stays NULL - routing it through this service must fail LOUD
        // with NotSupportedException; Public = fetched by the private fetcher below.
        private enum PlatformKind { Planned, Local, Public }

        private sealed class PlatformSpec
        {
            public string Name = string.Empty;
            public PlatformKind Kind;

            // false = rejected from EVERY category surface (the per-site audit verdict, dated
            // in each row): no scope menu entry, no merged cards, no checklist ticks, and a
            // LOUD categoryless skip under the Category basis. its OWN grid chips stay
            // factual - GetModes is never filtered by this flag.
            public bool CategoryCapable;

            // true = the Categories page fetches this platform's category INDEX live, so it
            // feeds BuildMerged as an index source and its load errors show for its scope.
            public bool LiveIndex;

            // true = its thumbnail fills run alone through a 1-wide gate (a static, non
            // thread-safe scrape cache or a rate-limited host) instead of the shared one.
            public bool SoloThumbGate;

            public string? Description;

            // null = no modes here (Local/Planned platforms list categories in their own service)
            public Func<IReadOnlyList<string>>? Modes;

            // null = not fetchable here (Local/Planned) - GetWallpapersAsync throws for it
            public Func<PublicWallpaperService, int, string, CancellationToken, Task<List<WallpaperItem>>>? Fetch;

            // null = no per-item category source for this platform (its rows pill from a
            // parse-time stamp or its own service's source tag). non-null = the slideshow's
            // pre-AddEntry enrichment fetches the applied item's OWN category through it:
            // return the first real value, "" = the page genuinely carries none, throw =
            // the lookup failed - both are honest no-pills, logged, never a failed apply.
            public Func<PublicWallpaperService, string, Task<string>>? ItemCategory;
        }

        private static readonly PlatformSpec[] PlatformRegistry =
        {
            // ---- public sources: fetched through this service ------------------------------
            new PlatformSpec
            {
                Name = Wallhaven,
                Kind = PlatformKind.Public,
                // rejected 2026-10-10: general/anime/people is a content-type filter on
                // search, not a category taxonomy
                CategoryCapable = false,
                Description = "Wallhaven's public JSON search, split into General / Anime / People categories.",
                // the API's 3-bit mask (general/anime/people), automata wallhaven.cc/AGENTS.md
                Modes = () => new[] { "General", "Anime", "People" },
                Fetch = (svc, page, mode, ct) => svc.GetWallhavenWallpapersAsync(page, mode, ct)
            },
            new PlatformSpec
            {
                Name = Bing,
                Kind = PlatformKind.Public,
                // rejected 2026-10-09: proven zero taxonomy
                CategoryCapable = false,
                Description = "Recent daily Bing homepage wallpapers from Microsoft's public archive endpoint.",
                // one honest entry so the platform's own grid has a default mode - it never
                // becomes a category card (the rejection flag above)
                Modes = () => new[] { "Daily" },
                Fetch = (svc, page, mode, ct) => svc.GetBingWallpapersAsync(page, ct)
            },
            new PlatformSpec
            {
                Name = BingEditions,
                Kind = PlatformKind.Public,
                // rejected 2026-10-16: country edition + month are the site's only axes -
                // a region and a time range, no content taxonomy (user verdict)
                CategoryCapable = false,
                Description = "11 country editions of Bing's daily wallpaper with a monthly archive back to 2011, originals in 3840x2160 (bingwallpaper.anerg.com).",
                Modes = () => BingEditionsList.Select(edition => edition.Label).ToArray(),
                Fetch = (svc, page, mode, ct) => svc.GetBingEditionsWallpapersAsync(page, mode, ct)
            },
            new PlatformSpec
            {
                Name = SimpleDesktops,
                Kind = PlatformKind.Public,
                // rejected 2026-10-09: proven zero taxonomy
                CategoryCapable = false,
                Description = "Minimal, distraction-free wallpapers from Simple Desktops.",
                Modes = () => new[] { "Minimal" },
                Fetch = (svc, page, mode, ct) => svc.GetSimpleDesktopWallpapersAsync(page, ct)
            },
            new PlatformSpec
            {
                Name = WallpaperHub,
                Kind = PlatformKind.Public,
                CategoryCapable = true,
                LiveIndex = true,
                Description = "Windows, Surface, Office, Xbox, and event collections from WallpaperHub.",
                // live snapshot from the site's own Collections index (see the field above)
                Modes = () => WallpaperHubCollections.Select(collection => collection.Title).ToArray(),
                Fetch = (svc, page, mode, ct) => svc.GetWallpaperHubWallpapersAsync(page, mode, ct)
            },
            new PlatformSpec
            {
                Name = Pexels,
                Kind = PlatformKind.Public,
                CategoryCapable = true,
                LiveIndex = true,
                Description = "The site's own discover/wallpapers search terms, live-loaded. Add a Pexels API key in Settings.",
                Modes = () => PexelsDiscoverTerms.Select(term => term.Term).ToArray(),
                Fetch = (svc, page, mode, ct) => svc.GetPexelsWallpapersAsync(page, mode, ct),
                // API photos carry no category field - the photo page's keywords meta does
                ItemCategory = (svc, url) => svc.GetPexelsItemCategoryAsync(url)
            },
            new PlatformSpec
            {
                Name = Pixabay,
                Kind = PlatformKind.Public,
                CategoryCapable = true,
                LiveIndex = true,
                // Cloudflare rate-limits rapid collection fetches (live 2026-10-08)
                SoloThumbGate = true,
                Description = "Pixabay's curated collections, live from pixabay.com/collections (no key needed).",
                Modes = () => PixabayCollections.Select(collection => collection.Name).ToArray(),
                Fetch = (svc, page, mode, ct) => svc.GetPixabayWallpapersAsync(page, mode, ct),
                // search JSON-LD is title+url only - the detail page's ImageObject keywords tag
                ItemCategory = (svc, url) => svc.GetPixabayItemCategoryAsync(url)
            },
            new PlatformSpec
            {
                Name = DesktopNexus,
                Kind = PlatformKind.Public,
                CategoryCapable = true,
                Description = "15 category galleries plus All from Desktop Nexus's public browse pages.",
                Modes = () => DesktopNexusCategories.Select(category => category.Title).ToArray(),
                Fetch = (svc, page, mode, ct) => svc.GetDesktopNexusWallpapersAsync(page, mode, ct)
            },
            new PlatformSpec
            {
                Name = DigitalBlasphemy,
                Kind = PlatformKind.Public,
                CategoryCapable = true,
                Description = "Brian's wallpapers plus a free set on Digital Blasphemy (640x480 preview cap - originals are membership-only).",
                Modes = () => DigitalBlasphemyModes.Select(category => category.Title).ToArray(),
                Fetch = (svc, page, mode, ct) => svc.GetDigitalBlasphemyWallpapersAsync(page, mode, ct),
                // feeds carry no category - the item page's product-tag links do (year skipped)
                ItemCategory = (svc, url) => svc.GetDigitalBlasphemyItemCategoryAsync(url)
            },
            new PlatformSpec
            {
                Name = Hdwallpapers,
                Kind = PlatformKind.Public,
                CategoryCapable = true,
                Description = "Latest feed plus 25 category listings from HDwallpapers.net.",
                Modes = () => HdwallpapersModes.Select(category => category.Title).ToArray(),
                Fetch = (svc, page, mode, ct) => svc.GetHdwallpapersWallpapersAsync(page, mode, ct)
            },
            new PlatformSpec
            {
                Name = Pixiv,
                Kind = PlatformKind.Public,
                // rejected 2026-10-11: 3 hand-picked search queries over unbounded UGC
                CategoryCapable = false,
                Description = "Safe-for-work illustrations from Pixiv's public search (restrict=safe, no key).",
                Modes = () => PixivTags,
                Fetch = (svc, page, mode, ct) => svc.GetPixivWallpapersAsync(page, mode, ct)
            },
            new PlatformSpec
            {
                Name = Cara,
                Kind = PlatformKind.Public,
                // rejected 2026-10-11: feed-only surface, every /collections|/tags route 404s
                CategoryCapable = false,
                Description = "Explore feed from the Cara art community's server-rendered page.",
                Modes = () => new[] { "Explore" },
                Fetch = (svc, page, mode, ct) => svc.GetCaraWallpapersAsync(page, mode, ct)
            },
            new PlatformSpec
            {
                Name = WallpaperCave,
                Kind = PlatformKind.Public,
                CategoryCapable = true,
                Description = "Curated albums from Wallpaper Cave (single-shot album pages).",
                Modes = () => WallpaperCaveAlbums.Select(category => category.Title).ToArray(),
                Fetch = (svc, page, mode, ct) => svc.GetWallpaperCaveWallpapersAsync(page, mode, ct),
                // feeds carry no category - the item page's breadcrumb root does
                ItemCategory = (svc, url) => svc.GetWallpaperCaveItemCategoryAsync(url)
            },
            new PlatformSpec
            {
                Name = WallpaperEngine,
                Kind = PlatformKind.Public,
                // rejected 2026-10-11: steam workshop's requiredtags axis is a filter over
                // one flat upload pool, not a browsable taxonomy
                CategoryCapable = false,
                Description = "Wallpaper Engine's Steam Workshop browse pages with public preview images.",
                Modes = () => WallpaperEngineModes,
                Fetch = (svc, page, mode, ct) => svc.GetWallpaperEngineWallpapersAsync(page, mode, ct)
            },
            new PlatformSpec
            {
                Name = Artgram,
                Kind = PlatformKind.Public,
                // rejected 2026-10-12: one flat gallery feed with only a sortBy switch
                CategoryCapable = false,
                Description = "Trending, latest, and oldest gallery feeds from the Artgram art community (50 cards per page).",
                Modes = () => ArtgramModes,
                Fetch = (svc, page, mode, ct) => svc.GetArtgramWallpapersAsync(page, mode, ct)
            },

            // ---- local sources: their own service + their own page --------------------------
            // their category lists live in their own live indexes (BackieeHtmlParser /
            // AlphaCodersService.Categories / ArtStationService.Channels), so Modes stays
            // null here - SlideshowCategoryCatalog resolves them directly.
            new PlatformSpec
            {
                Name = Backiee,
                Kind = PlatformKind.Local,
                CategoryCapable = true,
                LiveIndex = true,
                Description = "Backiee's own wallpaper catalog, loaded from its categories page."
            },
            new PlatformSpec
            {
                Name = AlphaCoders,
                Kind = PlatformKind.Local,
                CategoryCapable = true,
                LiveIndex = true,
                // static, non thread-safe scrape cache in AlphaCodersService
                SoloThumbGate = true,
                Description = "AlphaCoders' own catalog, live from its real category index."
            },
            new PlatformSpec
            {
                Name = ArtStation,
                Kind = PlatformKind.Local,
                CategoryCapable = true,
                LiveIndex = true,
                Description = "ArtStation's own channels directory, loaded live from its community index.",
                // projects.json hits carry no category - the project's own detail JSON does
                // (curl-only); Local only means Fetch is null, the pill lookup still lives here
                ItemCategory = (svc, url) => new ArtStationService().GetItemCategoryAsync(url)
            },

            // ---- planned: named in the picker, not built yet --------------------------------
            // 7 are WAF/anti-bot walled (ArtFol, Behance, CGSociety,
            // CharacterDesignReferences, Dribbble, NewGrounds, Peakpx) and 2 need credentials
            // (Unsplash = API key with a hosting-terms caveat, DeviantArt = OAuth). a future
            // port flips its row to Local/Public and fills it in - nothing else to touch.
            new PlatformSpec { Name = "ArtFol", Kind = PlatformKind.Planned },
            new PlatformSpec { Name = "Behance", Kind = PlatformKind.Planned },
            new PlatformSpec { Name = "CGSociety", Kind = PlatformKind.Planned },
            new PlatformSpec { Name = "CharacterDesignReferences", Kind = PlatformKind.Planned },
            new PlatformSpec { Name = "DeviantArt", Kind = PlatformKind.Planned },
            new PlatformSpec { Name = "Dribbble", Kind = PlatformKind.Planned },
            new PlatformSpec { Name = "NewGrounds", Kind = PlatformKind.Planned },
            new PlatformSpec { Name = "Peakpx", Kind = PlatformKind.Planned },
            new PlatformSpec { Name = "Unsplash", Kind = PlatformKind.Planned }
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

        // ---- registry queries: the one lookup + the lists everything else reads -------------
        private static PlatformSpec? FindPlatform(string? platformName)
        {
            if (string.IsNullOrEmpty(platformName))
            {
                return null;
            }

            foreach (var spec in PlatformRegistry)
            {
                if (spec.Name.Equals(platformName, StringComparison.OrdinalIgnoreCase))
                {
                    return spec;
                }
            }

            return null;
        }

        // ordinal-ignore-case so the derived lists keep ONE deterministic order (it reproduces
        // the hand-maintained picker order exactly, and a name's case can never split a list)
        private static IReadOnlyList<string> Sorted(IEnumerable<string> names)
        {
            return names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // a public source - the only kind that routes through GetWallpapersAsync
        public static bool IsSupportedPlatform(string platformName)
        {
            return FindPlatform(platformName)?.Kind == PlatformKind.Public;
        }

        // built (own service or public fetcher) - what the picker needs to tell apart a real
        // destination from a not-implemented-yet row
        public static bool IsImplementedPlatform(string? platformName)
        {
            var spec = FindPlatform(platformName);
            return spec != null && spec.Kind != PlatformKind.Planned;
        }

        // the picker's grid: every row, planned ones included
        public static IReadOnlyList<string> GetPickerPlatformNames()
        {
            return Sorted(PlatformRegistry.Select(spec => spec.Name));
        }

        // the Set-slideshow platform boxes, the scope-menu base and the not-implemented
        // dialog's "Available now" line: everything with a working destination
        public static IReadOnlyList<string> GetImplementedPlatformNames()
        {
            return Sorted(PlatformRegistry.Where(spec => spec.Kind != PlatformKind.Planned).Select(spec => spec.Name));
        }

        public static IReadOnlyList<string> GetSupportedPlatformNames()
        {
            return Sorted(PlatformRegistry.Where(spec => spec.Kind == PlatformKind.Public).Select(spec => spec.Name));
        }

        // the Categories scope menu: implemented AND category-capable (a rejected scope would
        // always render an empty grid)
        public static IReadOnlyList<string> GetCategoryPlatformNames()
        {
            return Sorted(PlatformRegistry
                .Where(spec => spec.Kind != PlatformKind.Planned && spec.CategoryCapable)
                .Select(spec => spec.Name));
        }

        // the platforms whose category INDEX is fetched live: they feed BuildMerged as an index
        // source, and their index-load errors are shown while their scope is on screen
        public static IReadOnlyList<string> GetLiveIndexPlatformNames()
        {
            return Sorted(PlatformRegistry
                .Where(spec => spec.Kind != PlatformKind.Planned && spec.LiveIndex)
                .Select(spec => spec.Name));
        }

        public static bool HasLiveIndex(string? platformName)
        {
            return FindPlatform(platformName)?.LiveIndex == true;
        }

        // solo = this platform's thumbnail fills need the 1-wide gate (see the row's flag)
        public static bool NeedsSoloThumbGate(string? platformName)
        {
            return FindPlatform(platformName)?.SoloThumbGate == true;
        }

        // ---- category rejection ------------------------------------------------------------
        // rejected sites stay FULLY supported in the picker + the Set dialog, but they are
        // REJECTED from every category surface - a category list built from them would be
        // fake entries ("Daily"/"Minimal" are fetcher modes, not taxonomy). the verdict is a
        // PER-ROW flag in the registry (an implemented row with CategoryCapable = false, the
        // date + reason sitting on that row): flipping one flag is the whole change, this
        // array is only its public shape, and GetModes is deliberately NOT filtered so each
        // platform's own grid chips stay factual (the bing/SD Daily/Minimal precedent).
        // consumers: the Categories scope menu, BuildMerged, the Set dialog's basis collapse,
        // the checklist universe and the slideshow's LOUD categoryless skip. verdict dates
        // live on the rows above (bing + simple desktops 2026-10-09, wallhaven + pixiv
        // 2026-10-10, cara + wallpaper engine 2026-10-11, artgram 2026-10-12); per-site
        // evidence: automata-private/<site>/AGENTS.md.
        public static readonly string[] CategoryExcludedPlatforms = PlatformRegistry
            .Where(spec => spec.Kind != PlatformKind.Planned && !spec.CategoryCapable)
            .Select(spec => spec.Name)
            .ToArray();

        public static bool IsCategoryExcluded(string? platformName)
        {
            if (platformName == null)
            {
                return false;
            }

            foreach (var name in CategoryExcludedPlatforms)
            {
                if (name.Equals(platformName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static IReadOnlyList<string> GetModes(string platformName)
        {
            // straight from the platform's registry row: a null Modes (Local/Planned, which
            // list their categories in their own service) and an unknown name both give the
            // caller an empty list, exactly as the old switch's default arm did
            var modes = FindPlatform(platformName)?.Modes;
            return modes == null ? Array.Empty<string>() : modes();
        }

        public static string GetDefaultMode(string platformName)
        {
            var modes = GetModes(platformName);
            return modes.Count > 0 ? modes[0] : string.Empty;
        }

        public static string GetPlatformDescription(string platformName)
        {
            var description = FindPlatform(platformName)?.Description;
            return string.IsNullOrEmpty(description) ? "Browse wallpapers from this source." : description;
        }

        // the slideshow's pre-AddEntry pill enrichment reads the row's ItemCategory
        // delegate through this: null = this platform has no per-item lookup (nothing
        // to fetch - its rows keep whatever the feed/parse-time stamp recorded), a
        // resolver = the fetch + 3-shape log contract in
        // SlideshowService.EnrichItemCategoryAsync (value / none / failed).
        public static Func<PublicWallpaperService, string, Task<string>>? GetItemCategoryResolver(string? platformName)
        {
            return FindPlatform(platformName)?.ItemCategory;
        }

        public async Task<List<WallpaperItem>> GetWallpapersAsync(
            string platformName,
            int page,
            string mode,
            CancellationToken cancellationToken = default)
        {
            page = Math.Max(page, 1);

            // the row's own fetcher - a Planned or Local row has none (Local routes through
            // its own service + page), so routing it through this service fails LOUD here
            // instead of silently fetching the wrong site
            var fetch = FindPlatform(platformName)?.Fetch;
            if (fetch == null)
            {
                throw new NotSupportedException($"{platformName} is not implemented yet.");
            }

            var wallpapers = await fetch(this, page, mode, cancellationToken);

            // the mode IS the category the caller asked for (term, collection,
            // mode) - tag items so history rows can show a category sticker
            var categoryLabel = string.IsNullOrWhiteSpace(mode) ? "latest" : mode;
            foreach (var wallpaper in wallpapers)
            {
                if (string.IsNullOrEmpty(wallpaper.Category)) wallpaper.Category = categoryLabel;
            }
            return wallpapers;
        }

        public async Task<byte[]> GetImageBytesAsync(string imageUrl, CancellationToken cancellationToken = default)
        {
            // history rows can hand us the applied LOCAL file (the slideshow set
            // the wallpaper from exactly this file) - read it directly instead
            // of http-fetching what is really a path
            if (System.IO.File.Exists(imageUrl))
            {
                return await System.IO.File.ReadAllBytesAsync(imageUrl, cancellationToken);
            }

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
                    // no Category on purpose: wallhaven's general/anime/people is a
                    // content-type FILTER, not a category taxonomy (user verdict
                    // 2026-10-10, the platform is category-rejected) - its history
                    // pill stays empty, like every source without real categories
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

        // ---- Bing Editions (bingwallpaper.anerg.com) -------------------------------------
        // paging: page 1 = the edition page itself, which carries the two newest months
        // (~46 cards); page N >= 2 = the month N steps back from the newest, so the pair
        // page 1 already showed is skipped and infinite scroll never repeats an item.
        // an empty list (a month before the archive starts, or a month the edition does
        // not carry) is the honest end of the feed.
        private async Task<List<WallpaperItem>> GetBingEditionsWallpapersAsync(
            int page,
            string mode,
            CancellationToken cancellationToken)
        {
            // "latest" (or empty) = the slideshow's Latest basis and the All-categories
            // fast path. this site has no cross-edition feed, so those mean the site's
            // own default edition: the ROOT page's United States listing (the first
            // table row, also the platform grid's default chip).
            if (string.IsNullOrWhiteSpace(mode) ||
                string.Equals(mode, "latest", StringComparison.OrdinalIgnoreCase))
            {
                mode = BingEditionsList[0].Label;
            }

            var edition = BingEditionsList.FirstOrDefault(item =>
                string.Equals(item.Label, mode, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(edition.Label))
            {
                // a mode this platform does not have = LOUD, never a silently empty grid
                throw new InvalidOperationException($"Bing Editions has no edition named '{mode}'.");
            }

            if (page <= 1)
            {
                var editionHtml = await _httpClient.GetStringAsync(
                    BingEditionsBaseUrl + edition.PagePath, cancellationToken);
                RememberBingEditionsNewestMonth(edition.Code, editionHtml);
                return ParseBingEditionsCards(editionHtml);
            }

            var newest = await GetBingEditionsNewestMonthAsync(edition, cancellationToken);
            var month = ShiftBingEditionsMonth(newest, -page);
            if (string.CompareOrdinal(month, BingEditionsFirstMonth) < 0)
            {
                return new List<WallpaperItem>();
            }

            using var response = await _httpClient.GetAsync(
                $"{BingEditionsBaseUrl}/archive/{edition.Code}/{month}", cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new List<WallpaperItem>();
            }

            response.EnsureSuccessStatusCode();
            return ParseBingEditionsCards(await response.Content.ReadAsStringAsync(cancellationToken));
        }

        private static List<string> ParseBingEditionsMonths(string html)
        {
            var months = new List<string>();
            foreach (Match match in Regex.Matches(html, "<h2 id=\"month-(?<month>\\d{6})\"", RegexOptions.IgnoreCase))
            {
                var month = match.Groups["month"].Value;
                if (!months.Contains(month))
                {
                    months.Add(month);
                }
            }
            return months;
        }

        private static void RememberBingEditionsNewestMonth(string code, string html)
        {
            var months = ParseBingEditionsMonths(html);
            if (months.Count == 0)
            {
                // card parsing and month parsing read the same page - a page without month
                // sections means paging is broken, and that has to be loud rather than
                // quietly stopping at page 1
                throw new InvalidOperationException("Bing Editions returned no month sections - the page shape changed.");
            }

            lock (BingEditionsMonthLock)
            {
                BingEditionsNewestMonth[code] = months[0];
            }
        }

        private async Task<string> GetBingEditionsNewestMonthAsync(
            (string Label, string PagePath, string Code) edition,
            CancellationToken cancellationToken)
        {
            lock (BingEditionsMonthLock)
            {
                if (BingEditionsNewestMonth.TryGetValue(edition.Code, out var cached))
                {
                    return cached;
                }
            }

            // page 1 was skipped (a deep-linked month, or a fresh service instance) - read
            // the edition page once to learn which month is newest
            var html = await _httpClient.GetStringAsync(BingEditionsBaseUrl + edition.PagePath, cancellationToken);
            RememberBingEditionsNewestMonth(edition.Code, html);

            lock (BingEditionsMonthLock)
            {
                return BingEditionsNewestMonth[edition.Code];
            }
        }

        /// <summary>
        /// YYYYMM shifted by <paramref name="months"/> (negative walks back). the archive is
        /// contiguous month by month, so a page index maps straight onto a month.
        /// </summary>
        private static string ShiftBingEditionsMonth(string month, int months)
        {
            var year = int.Parse(month.Substring(0, 4));
            var index = int.Parse(month.Substring(4, 2)) - 1 + months;
            year += index / 12;
            index %= 12;
            if (index < 0)
            {
                index += 12;
                year--;
            }
            return year.ToString("D4") + (index + 1).ToString("D2");
        }

        private List<WallpaperItem> ParseBingEditionsCards(string html)
        {
            var wallpapers = new List<WallpaperItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // card = <a class="wallpaper-card" href="/detail/<code>/<slug>"> with the
            // imgproxy thumbnail and the caption (the site's only per-item text)
            var matches = Regex.Matches(
                html,
                "<a class=\"wallpaper-card\" href=\"(?<href>[^\"]+)\"[^>]*>\\s*<div class=\"wallpaper-media\">\\s*" +
                "<img\\s+src=\"(?<thumb>[^\"]+)\"(?<img>.*?)</div>\\s*<span class=\"wallpaper-caption\"[^>]*>(?<caption>.*?)</span>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            foreach (Match match in matches)
            {
                var href = WebUtility.HtmlDecode(match.Groups["href"].Value);
                var thumbnail = WebUtility.HtmlDecode(match.Groups["thumb"].Value);
                var caption = WebUtility.HtmlDecode(
                    Regex.Replace(match.Groups["caption"].Value, "<[^>]+>", string.Empty)).Trim();
                var slug = href.TrimEnd('/').Split('/').Last();

                if (string.IsNullOrWhiteSpace(thumbnail) || string.IsNullOrWhiteSpace(slug) || !seen.Add(slug))
                {
                    continue;
                }

                wallpapers.Add(new WallpaperItem
                {
                    Id = slug,
                    Title = string.IsNullOrWhiteSpace(caption) ? slug : caption,
                    Description = caption,
                    ImageUrl = thumbnail,
                    FullPhotoUrl = DecodeBingEditionsOrigin(thumbnail),
                    SourceUrl = MakeAbsoluteUrl(BingEditionsBaseUrl, href),
                    Likes = string.Empty,
                    Downloads = string.Empty,
                    // the site's own biggest download is w:3840 and the originals measure
                    // 3840x2160 - the feed itself carries no size, so this is the honest cap
                    Resolution = "3840x2160",
                    QualityTag = "4K",
                    IsAI = false
                });
            }

            return wallpapers;
        }

        /// <summary>
        /// The thumbnail is an imgproxy url whose base64 tail IS the original's public url
        /// (3840x2160, no signature needed): /w:600/&lt;base64 of the original&gt;.jpg. That
        /// makes the full-size image free - no detail-page request, no per-width signature.
        /// </summary>
        private static string DecodeBingEditionsOrigin(string thumbnailUrl)
        {
            var match = Regex.Match(
                thumbnailUrl, "/w:\\d+/(?<chunks>.+?)\\.(?:jpg|jpeg|png|webp)$", RegexOptions.IgnoreCase);

            var base64 = match.Success
                ? match.Groups["chunks"].Value.Replace("/", string.Empty).Replace('-', '+').Replace('_', '/')
                : string.Empty;
            if (base64.Length % 4 == 2)
            {
                base64 += "==";
            }
            else if (base64.Length % 4 == 3)
            {
                base64 += "=";
            }

            try
            {
                var origin = base64.Length == 0
                    ? string.Empty
                    : System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64));
                if (origin.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    return origin;
                }
            }
            catch (FormatException)
            {
                // falls through to the throw below: a shape change must be loud, never a
                // silently downgraded item that quietly serves its thumbnail as the original
            }

            throw new InvalidOperationException(
                $"Bing Editions thumbnail is not a decodable imgproxy url: {thumbnailUrl}");
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

                // the LATEST feed's entities all carry the site's own label ("Bing
                // Wallpaper" on 20/20 sampled 2026-10-12) - stamp it at parse time, zero
                // fetch. collection items stay unstamped so the wrapper's tick label wins.
                var category = string.IsNullOrWhiteSpace(collectionId) ? GetString(item, "label") : string.Empty;

                wallpapers.Add(new WallpaperItem
                {
                    Id = id,
                    Title = title,
                    Description = description,
                    ImageUrl = thumbnail,
                    FullPhotoUrl = string.IsNullOrWhiteSpace(full) ? thumbnail : full,
                    SourceUrl = sourceUrl,
                    Category = category,
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

            // every mode is a discover term now (the Curated endpoint entry is gone with the
            // old 3-mode table), and the site's pill links carry NO orientation param - the
            // search mirrors the site exactly, so "vertical wallpaper" resolves portrait.
            // "latest" is the SLIDESHOW Latest basis (Q1 audit 2026-10-09: a literal
            // `query=latest` searched the WORD): it queries `wallpaper` with
            // `order_by=latest` instead - the newest photos, not results about "latest".
            bool latestOrder = string.IsNullOrWhiteSpace(mode) || mode.Equals("latest", StringComparison.OrdinalIgnoreCase);
            var requestUrl = latestOrder
                ? $"https://api.pexels.com/v1/search?query=wallpaper&order_by=latest&page={page}&per_page=30"
                : $"https://api.pexels.com/v1/search?query={Uri.EscapeDataString(mode)}&page={page}&per_page=30";

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
            // "latest" (the slideshow's Latest basis + the All-categories fast path -
            // NormalizePublicMode maps the legacy names onto it) is NOT a collection
            // name: it routes to the site's own newest wallpaper results. without
            // this arm every pixabay slideshow start threw "No pixabay collection
            // named 'latest'" and skipped the platform (found live 2026-10-10).
            if (string.IsNullOrWhiteSpace(mode) || mode.Equals("latest", StringComparison.OrdinalIgnoreCase))
            {
                return await GetPixabayLatestAsync(page, cancellationToken);
            }

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

            // one tile = <div id="item-N" data-pk="N" class="item[ video-item]"> <a href=poster-page>
            // then its poster img. Shapes proven across photo/illustration/vector/video pages:
            // only the first ~15 tiles render eagerly (src=), the rest are
            // src="/static/img/blank.gif" + data-lazy=; posters come as __340.jpg, __340.png
            // (full = _1280 + SAME extension - _1280.jpg on a png asset = 403) or video
            // _tiny.jpg (1280x720 ladder: small/medium/large all 200, large = 3840x2160,
            // "big" = 404); tile links span /photos /illustrations /vectors /videos and are
            // kept generic so a future media type degrades to skipping that tile, not a
            // broken SourceUrl. matching only eager jpg photo tiles served 15/49 per page
            // and 0 items on the single video collection ("Nature videos" = permanent
            // error-bar line until this pass existed).
            var tilePatterns = new[]
            {
                "<div id=\"item-\\d+\" data-pk=\"(?<id>\\d+)\" class=\"item[^\\\"]*\">\\s*<a href=\"(?<href>/[^\\\"]+)\">[\\s\\S]{0,600}?<img[^>]+src=\"(?<img>https://cdn\\.pixabay\\.com/(?:photo|video)/[^\\\"]+?(?:__340|_tiny)\\.(?:jpg|png))\"[^>]+alt=\"(?<alt>[^\\\"]*)\"",
                "<div id=\"item-\\d+\" data-pk=\"(?<id>\\d+)\" class=\"item[^\\\"]*\">\\s*<a href=\"(?<href>/[^\\\"]+)\">[\\s\\S]{0,600}?<img[^>]+data-lazy=\"(?<img>https://cdn\\.pixabay\\.com/(?:photo|video)/[^\\\"]+?(?:__340|_tiny)\\.(?:jpg|png))\"[^>]+alt=\"(?<alt>[^\\\"]*)\"",
            };

            var tiles = new List<Match>();
            foreach (var tilePattern in tilePatterns)
            {
                foreach (Match tile in Regex.Matches(html, tilePattern, RegexOptions.Singleline))
                {
                    tiles.Add(tile);
                }
            }

            // a page carries both shapes - restore document order across the passes
            tiles.Sort((left, right) => left.Index.CompareTo(right.Index));

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in tiles)
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
                    // photo: __340 preview -> _1280 original (single underscore; __1280 = 403,
                    // extension preserved - _1280.jpg on a png asset = 403).
                    // video: tiny poster -> large 4K still
                    FullPhotoUrl = preview.Contains("/video/")
                        ? preview.Replace("_tiny.", "_large.")
                        : preview.Replace("__340", "_1280"),
                    SourceUrl = $"https://pixabay.com{WebUtility.HtmlDecode(match.Groups["href"].Value)}",
                    Likes = string.Empty,
                    Downloads = string.Empty,
                    IsAI = false
                });
            }

            return wallpapers;
        }

        // pixabay's newest wallpaper results = `/images/search/wallpaper/
        // ?order=latest&pagi=N` (curl-transport like every pixabay html fetch).
        // reversed live 2026-10-10: the page serves ~100 tiles per page, real deep
        // pagination (?pagi=99 returns older DISTINCT ids - no wrap like the
        // collections index), and every tile ships a schema.org ImageObject
        // JSON-LD block with the FULL `_1280` contentUrl, the item href, and the
        // title - the visible <img> is eager on only ~19 tiles (the rest render
        // src=/static/img/blank.gif with the url living ONLY in the JSON-LD), so
        // the tile-img parse that works on collection pages matches 19/100 here.
        private async Task<List<WallpaperItem>> GetPixabayLatestAsync(int page, CancellationToken cancellationToken)
        {
            var url = $"https://pixabay.com/images/search/wallpaper/?order=latest&pagi={page}";
            var html = await GetPixabayHtmlAsync(url, cancellationToken);
            var wallpapers = new List<WallpaperItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Match block in Regex.Matches(html, "<script type=\"application/ld\\+json\">(?<json>\\{.*?\\})</script>", RegexOptions.Singleline))
            {
                var json = block.Groups["json"].Value;
                if (!json.Contains("\"ImageObject\""))
                {
                    continue;
                }

                // independent lookups per block - key order is the serializer's, not a contract
                var contentUrl = Regex.Match(json, "\"contentUrl\":\"(?<url>https://cdn\\.pixabay\\.com/(?:photo|video)/[^\"]+)\"");
                var href = Regex.Match(json, "\"acquireLicensePage\":\"(?<href>/[^\"]+)\"");
                var name = Regex.Match(json, "\"name\":\"(?<name>[^\"]*)\"");
                if (!contentUrl.Success || !href.Success)
                {
                    continue;
                }

                var id = Regex.Match(href.Groups["href"].Value, "(\\d+)/?$").Groups[1].Value;
                if (string.IsNullOrEmpty(id) || !seen.Add(id))
                {
                    continue;
                }

                var preview = contentUrl.Groups["url"].Value;
                wallpapers.Add(new WallpaperItem
                {
                    Id = id,
                    Title = WebUtility.HtmlDecode(name.Groups["name"].Value).Trim(),
                    Description = "Pixabay latest wallpaper",
                    ImageUrl = preview,
                    // contentUrl already carries the _1280 rung - the same CDN
                    // pass-through the collection parser upgrades __340/_640 into
                    FullPhotoUrl = preview,
                    SourceUrl = $"https://pixabay.com{WebUtility.HtmlDecode(href.Groups["href"].Value)}",
                    Likes = string.Empty,
                    Downloads = string.Empty,
                    IsAI = false
                });
            }

            if (wallpapers.Count == 0)
            {
                throw new InvalidOperationException("pixabay latest search returned no items - its /images/search markup may have changed");
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
            var entry = DigitalBlasphemyModes
                .FirstOrDefault(e => e.Title.Equals(mode ?? string.Empty, StringComparison.OrdinalIgnoreCase));
            var path = entry.Path ?? "product-category/wallpapers";

            if (page > 1 && entry.SinglePage)
            {
                // single-page gallery tags (free/christmas/halloween/other-holidays/
                // underwater) - there is no /page/N/ (curl: 404)
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
                    // the slug is the stable per-item id - the digit run inside
                    // slugs like "opalinehollow2"/"autumnwood2k262" is a version
                    // and collides across items (the old first-digit id made
                    // distinct wallpapers share id "2"; caught live 2026-10-16
                    // when a history row opened the wrong item)
                    Id = href.TrimEnd('/').Split('/').Last(),
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
                var category = string.Empty;
                if (detail.Success)
                {
                    full = $"https://www.hdwallpapers.net/previews/{detail.Groups["slug"].Value}-{detail.Groups["id"].Value}.jpg";

                    // the href's first path segment IS the site's own category slug ("3d",
                    // "nature", "city-and-architecture" - live across the latest feed
                    // 2026-10-12) - stamp it at parse time, zero fetch. "latest-wallpapers"
                    // is OUR feed slug, not a category, so it stays empty (= no pill).
                    var catSlug = detail.Groups["cat"].Value;
                    if (!catSlug.Equals("latest-wallpapers", StringComparison.OrdinalIgnoreCase))
                    {
                        var catMode = HdwallpapersModes
                            .FirstOrDefault(entry => entry.Slug.Equals(catSlug, StringComparison.OrdinalIgnoreCase));
                        category = string.IsNullOrEmpty(catMode.Title) ? catSlug : catMode.Title;
                    }
                }

                wallpapers.Add(new WallpaperItem
                {
                    Id = detail.Success ? detail.Groups["id"].Value : href,
                    Title = title,
                    Description = "HDwallpapers.net wallpaper",
                    ImageUrl = thumb,
                    FullPhotoUrl = full,
                    SourceUrl = href,
                    Category = category,
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
