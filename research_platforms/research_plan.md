# Research Plan: Additional Wallpaper Platform Support

Main question: Which existing Aura platform entries can be implemented in WinUI with stable public APIs, JSON feeds, RSS feeds, or keyless endpoints?

Subtopics:
1. Keyless image/wallpaper APIs: Wallhaven, Bing Wallpaper Archive, Simple Desktops, WallpaperHub.
2. Official APIs requiring keys: Unsplash, Pexels, Pixabay, DeviantArt, Behance/Dribbble.
3. Scrape-only or unsupported platforms: Peakpx, Wallpaper Cave, DesktopNexus, CGSociety, Newgrounds, Pixiv, Cara, ArtFol, Artgram, Digital Blasphemy, Vladstudio, Wallpaper Engine.

Expected output:
- Identify which platforms can be shipped now without user credentials.
- Identify which can be added later with API-key settings.
- Avoid brittle scrape-only implementations unless the endpoint is simple and stable.

Synthesis:
- Implement the highest-confidence keyless platforms first in the WinUI app.
- Keep research notes with source URLs for final citations.
