# Additional Platform Findings

Implemented now (keyless):
- Wallhaven: public JSON search works at `https://wallhaven.cc/api/v1/search` with paging, sorting, thumbnail, full image path, views, favorites, and resolution fields. Live endpoint returned `application/json` on 2026-05-08.
- Bing Wallpaper Archive: public `HPImageArchive.aspx` endpoint returns JSON with recent daily wallpaper metadata. Live endpoint returned `images[]` with image URL paths and copyright fields on 2026-05-08.
- Simple Desktops: site confirms it has a site/API history and exposes a FeedBurner RSS link. The browse pages also contain predictable static thumbnail/full-image URLs, so Aura uses the browse page HTML as a lightweight source.
- WallpaperHub: wallpapers page contains `__NEXT_DATA__` with `initWallpapers`, thumbnail URLs, source URLs, and resolution variants; Aura parses that static page data.

Implemented with user API keys:
- Pexels: official REST API requires an `Authorization` header containing an API key. Aura reads `PEXELS_API_KEY` from the environment.
- Pixabay: official API requires a `key` query parameter. Aura reads `PIXABAY_API_KEY` from the environment.

Skipped for now:
- Unsplash: official guidelines say API keys must remain confidential and disallow wallpaper-app-like replication, so it is not a good fit for a local wallpaper browser without a proper proxy/approval path.
- DeviantArt: current gallery/browse APIs require OAuth access tokens and browse scopes.
- Behance: public API/new client access appears unavailable or legacy-only.
- Newgrounds: official RSS/API surfaces are not wallpaper/image-wallpaper oriented.
- Peakpx, Wallpaper Cave, DesktopNexus, HDwallpapers and similar sites: no stable public API found; scrape-only support would be brittle and higher maintenance.

Sources:
- https://wallhaven.cc/help/api
- https://www.bing.com/HPImageArchive.aspx?format=js&idx=0&n=1&mkt=en-US
- https://simpledesktops.com/about/
- https://www.wallpaperhub.app/wallpapers
- https://www.pexels.com/api/documentation/
- https://pixabay.com/api/docs/
- https://help.unsplash.com/en/articles/2511245-unsplash-api-guidelines
- https://www.deviantart.com/developers/console/browse/browse_home/71bd4127e0511f31ed5ff6606b115c2a
