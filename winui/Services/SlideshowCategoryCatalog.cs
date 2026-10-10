using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aura.Models;

namespace Aura.Services
{
    // one merged category universe for the Set-slideshow dialog's Category basis,
    // plus the name -> per-platform fetch key resolution the slideshow loader needs.
    //
    // sources = the dialog's category-capable platforms only (bing/simple desktops are
    // categoryless - PublicWallpaperService.CategoryExcludedPlatforms rejects them, and
    // ResolveKey never resolves them either). every list is the SAME live index the
    // Categories page loads; a failed or empty source throws LOUD for the whole load
    // (the dialog shows the reason and a Category basis cannot be saved without a
    // list - never a silently partial checklist, no fallback). loaded once per process
    // and cached: the indexes are stable for a session, and the startup restore path
    // needs the same resolution the dialog has.
    //
    // fetch-key contract: backiee = the category slug, alphacoders = the category key
    // (the legacy trio 4K/Harvest/Rain maps to its real keys too), artstation =
    // channel:<id> (same shape the Categories page drills with), public platforms =
    // the canonical PublicWallpaperService.GetModes entry (collection titles,
    // discover terms, wallhaven's 3 bits).
    public sealed class SlideshowCategoryCatalog
    {
        // the "All categories" master tick: the dialog's DEFAULT Category state,
        // saved as this single sentinel instead of ~270 names, and resolved by the
        // slideshow loader as "no filter" (one latest-feed fetch per platform)
        public const string AllCategories = "All categories";

        public static SlideshowCategoryCatalog Instance { get; } = new();

        private readonly SemaphoreSlim _loadGate = new(1, 1);
        private List<string>? _universe;
        private Dictionary<string, List<string>> _platformNames = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, string> _backieeNameToSlug = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, string> _alphaNameToKey = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, string> _artStationNameToKey = new(StringComparer.OrdinalIgnoreCase);

        // alphacoders' three legacy slideshow entries stay tickable even though the
        // site's real category index has no such rows (the fetcher still resolves
        // these keys - 2026-10-09)
        private static readonly (string Name, string Key)[] AlphaLegacyEntries =
        {
            ("4K Wallpapers", "4k"),
            ("Harvest Wallpapers", "harvest"),
            ("Rain Wallpapers", "rain"),
        };

        public bool IsLoaded => _universe != null;

        public async Task<IReadOnlyList<string>> GetUniverseAsync()
        {
            if (_universe != null)
            {
                return _universe;
            }

            await _loadGate.WaitAsync();
            try
            {
                if (_universe == null)
                {
                    await LoadAsync();
                }

                return _universe!;
            }
            finally
            {
                _loadGate.Release();
            }
        }

        // this platform's own display names (empty = unknown or categoryless
        // platform). the loader compares a platform's names against the tick set:
        // every one of them ticked (or the AllCategories sentinel) = no filter at
        // all - one latest-feed fetch instead of N category fetches
        public List<string> GetPlatformNames(string platform)
        {
            if (_universe == null)
            {
                throw new InvalidOperationException("SlideshowCategoryCatalog is not loaded - await GetUniverseAsync() first");
            }

            return _platformNames.TryGetValue(platform, out var names)
                ? new List<string>(names)
                : new List<string>();
        }

        // null = this platform honestly has no such category (a QUIET miss in the
        // loader: only an empty TOTAL batch is loud). categoryless platforms are
        // never resolvable. throws if the universe was never loaded (a programming
        // error must fail loudly, not resolve nothing).
        public string? ResolveKey(string platform, string tickedName)
        {
            if (_universe == null)
            {
                throw new InvalidOperationException("SlideshowCategoryCatalog is not loaded - await GetUniverseAsync() first");
            }

            if (PublicWallpaperService.IsCategoryExcluded(platform))
            {
                return null;
            }

            var name = (tickedName ?? string.Empty).Trim();
            switch (platform)
            {
                case "Backiee":
                    return _backieeNameToSlug.TryGetValue(name, out var slug) ? slug : null;
                case "AlphaCoders":
                    if (_alphaNameToKey.TryGetValue(name, out var alphaKey))
                    {
                        return alphaKey;
                    }

                    foreach (var legacy in AlphaLegacyEntries)
                    {
                        if (legacy.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                        {
                            return legacy.Key;
                        }
                    }

                    return null;
                case "ArtStation":
                    return _artStationNameToKey.TryGetValue(name, out var channelKey) ? channelKey : null;
                default:
                    foreach (var mode in PublicWallpaperService.GetModes(platform))
                    {
                        if (mode.Equals(name, StringComparison.OrdinalIgnoreCase))
                        {
                            return mode;
                        }
                    }

                    return null;
            }
        }

        private async Task LoadAsync()
        {
            var errors = new List<string>();

            // six independent round trips together; the local functions below capture
            // these lists directly (same pattern as the Categories page's index load)
            List<BackieeCategory> backieeCategories = new();
            List<(string Key, string Name)> alphaCategories = new();
            List<(int Id, string Name)> artChannels = new();
            List<(string Name, string Slug)> pixabayCollections = new();
            List<(string Title, string Id)> hubCollections = new();
            List<(string Term, string ImageUrl)> pexelsTerms = new();

            async Task<List<string>> LoadBackieeAsync()
            {
                var own = new List<string>();
                try
                {
                    var html = await BackieeNetworkClient.GetStringAsync("https://backiee.com/categories");
                    backieeCategories = BackieeHtmlParser.ParseCategories(html);
                    if (backieeCategories.Count == 0)
                    {
                        own.Add("backiee returned no categories - its /categories markup may have changed");
                    }
                }
                catch (Exception ex)
                {
                    own.Add($"couldn't load backiee categories: {ex.Message}");
                }

                return own;
            }

            async Task<List<string>> LoadAlphaIndexAsync()
            {
                var own = new List<string>();
                try
                {
                    alphaCategories = await new AlphaCodersService().GetCategoryIndexAsync();
                    if (alphaCategories.Count == 0)
                    {
                        own.Add("alphacoders returned no categories - its tag index markup may have changed");
                    }
                }
                catch (Exception ex)
                {
                    own.Add($"couldn't load alphacoders categories: {ex.Message}");
                }

                return own;
            }

            async Task<List<string>> LoadArtStationChannelsAsync()
            {
                var own = new List<string>();
                try
                {
                    artChannels = await new ArtStationService().GetChannelsIndexAsync();
                    if (artChannels.Count == 0)
                    {
                        own.Add("artstation returned no channels - its index markup may have changed");
                    }
                }
                catch (Exception ex)
                {
                    own.Add($"couldn't load artstation channels: {ex.Message}");
                }

                return own;
            }

            async Task<List<string>> LoadPixabayCollectionsAsync()
            {
                var own = new List<string>();
                try
                {
                    pixabayCollections = await new PublicWallpaperService().GetPixabayCollectionsIndexAsync();
                    if (pixabayCollections.Count == 0)
                    {
                        own.Add("pixabay returned no collections - its tag index markup may have changed");
                    }
                }
                catch (Exception ex)
                {
                    own.Add($"couldn't load pixabay collections: {ex.Message}");
                }

                return own;
            }

            async Task<List<string>> LoadWallpaperHubCollectionsAsync()
            {
                var own = new List<string>();
                try
                {
                    hubCollections = await new PublicWallpaperService().GetWallpaperHubCollectionsIndexAsync();
                    if (hubCollections.Count == 0)
                    {
                        own.Add("wallpaperhub returned no collections - its index markup may have changed");
                    }
                }
                catch (Exception ex)
                {
                    own.Add($"couldn't load wallpaperhub collections: {ex.Message}");
                }

                return own;
            }

            async Task<List<string>> LoadPexelsDiscoverAsync()
            {
                var own = new List<string>();
                try
                {
                    pexelsTerms = await new PublicWallpaperService().GetPexelsDiscoverIndexAsync();
                    if (pexelsTerms.Count == 0)
                    {
                        own.Add("pexels returned no discover terms - its /search markup may have changed");
                    }
                }
                catch (Exception ex)
                {
                    own.Add($"couldn't load pexels discover terms: {ex.Message}");
                }

                return own;
            }

            foreach (var loadErrors in await Task.WhenAll(
                LoadBackieeAsync(),
                LoadAlphaIndexAsync(),
                LoadArtStationChannelsAsync(),
                LoadPixabayCollectionsAsync(),
                LoadWallpaperHubCollectionsAsync(),
                LoadPexelsDiscoverAsync()))
            {
                errors.AddRange(loadErrors);
            }

            if (errors.Count > 0)
            {
                throw new InvalidOperationException(string.Join("; ", errors));
            }

            // maps (first entry wins on a duplicate name - never ToDictionary, which throws)
            foreach (var category in backieeCategories)
            {
                if (!string.IsNullOrWhiteSpace(category.Slug))
                {
                    AddIfMissing(_backieeNameToSlug, category.Name, category.Slug);
                }
            }

            foreach (var (key, name) in alphaCategories)
            {
                AddIfMissing(_alphaNameToKey, name, key);
            }

            foreach (var legacy in AlphaLegacyEntries)
            {
                AddIfMissing(_alphaNameToKey, legacy.Name, legacy.Key);
            }

            foreach (var (id, name) in artChannels)
            {
                AddIfMissing(_artStationNameToKey, name, $"channel:{id}");
            }

            // shared snapshots: public fetch keys resolve against GetModes(), which
            // reads THESE - and the Categories page/drills keep working off them too
            PublicWallpaperService.SetPixabayCollections(pixabayCollections);
            PublicWallpaperService.SetWallpaperHubCollections(hubCollections);
            PublicWallpaperService.SetPexelsDiscoverTerms(pexelsTerms);
            AlphaCodersService.SetCategories(alphaCategories);
            ArtStationService.SetChannels(artChannels);

            // per-platform display names first (the loader's "all of this platform's
            // categories are ticked" check needs them), then merge the universe
            _platformNames = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            void AddPlatform(string platform, IEnumerable<string> displayNames)
            {
                var list = new List<string>();
                foreach (var display in displayNames)
                {
                    var trimmed = display.Trim();
                    if (trimmed.Length > 0 && !list.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                    {
                        list.Add(trimmed);
                    }
                }

                _platformNames[platform] = list;
            }

            AddPlatform("Backiee", backieeCategories.Select(c => c.Name));
            AddPlatform("AlphaCoders", alphaCategories.Select(t => t.Name).Concat(AlphaLegacyEntries.Select(l => l.Name)));
            AddPlatform("ArtStation", artChannels.Select(t => t.Name));
            AddPlatform("Pixabay", pixabayCollections.Select(t => t.Name));
            AddPlatform("WallpaperHub", hubCollections.Select(t => t.Title));
            AddPlatform("Pexels", pexelsTerms.Select(t => t.Term));
            AddPlatform("Wallhaven", PublicWallpaperService.GetModes("Wallhaven"));

            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var list in _platformNames.Values)
            {
                foreach (var name in list)
                {
                    names.Add(name);
                }
            }

            _universe = names.ToList();
        }

        private static void AddIfMissing(Dictionary<string, string> map, string name, string value)
        {
            var key = (name ?? string.Empty).Trim();
            if (key.Length > 0 && !map.ContainsKey(key))
            {
                map[key] = value;
            }
        }
    }
}
