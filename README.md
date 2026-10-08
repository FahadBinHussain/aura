# Aura
<img src="https://wakapi-qt1b.onrender.com/api/badge/fahad/interval:any/project:Aura" 
     alt="Wakapi Time Tracking" 
     title="Time spent on this project">

A modern wallpaper management and personalization application available in **three implementations**:
- **WinUI 3** - Modern Windows native app with Fluent Design
- **WPF** - Traditional Windows desktop application  
- **Python** - Cross-platform GUI with Tkinter

Browse, organize, and apply beautiful wallpapers from **17 supported sites** -
see [Supported sites](#supported-sites) below, more get added over time.

## Supported sites

Wallpaper sources wired into the WinUI app. New sites land in this table as
they get ported (17 as of 2026-10-08).

| Site | What Aura browses |
| --- | --- |
| [Backiee](https://backiee.com) | 19 categories, tag search, latest feed |
| [Alpha Coders](https://wall.alphacoders.com) | 24 categories, live from the site's category index |
| [Pixabay](https://pixabay.com) | 63 curated collections (live) |
| [WallpaperHub](https://www.wallpaperhub.app) | 17 curated collections (live) |
| [ArtStation](https://www.artstation.com) | wallpaper / landscape / nature / space / abstract queries |
| [Pexels](https://www.pexels.com) | curated / nature / space queries (API key in Settings) |
| [Wallhaven](https://wallhaven.cc) | General / Anime / People |
| [Bing Wallpaper Archive](https://www.bing.com) | daily homepage wallpapers |
| [Simple Desktops](https://simpledesktops.com) | minimal desktop sets |
| [DesktopNexus](https://desktopnexus.com) | All + 15 category galleries |
| [Digital Blasphemy](https://digitalblasphemy.com) | wallpapers + free set (640x480 preview cap) |
| [HDwallpapers](https://www.hdwallpapers.net) | latest feed + category listings |
| [Pixiv](https://www.pixiv.net) | safe-search illustration tags |
| [Cara](https://cara.app) | explore feed |
| [Wallpaper Cave](https://wallpapercave.com) | curated albums |
| [Wallpaper Engine workshop](https://steamcommunity.com/workshop/browse/?appid=431960) | trending / tagged browse (preview resolution) |
| [Artgram](https://www.artgram.co) | trending / latest / oldest (full resolution on detail open) |

### Category links

The URLs behind each site's category/mode entries (2026-10-08; source of truth
is the code - mode tables + fetchers).

- **backiee** - https://backiee.com/categories (19 slugs parse from there) ·
  drill `https://backiee.com/api/wallpaper/list.php?action=paging_list&category=<slug>`
- **alphacoders** - https://alphacoders.com/tag/is-category
- **pixabay** - https://pixabay.com/collections/ (63 collections, paginated
  `?pagi=N`; fetched through the OS `curl.exe` - Cloudflare 403s .NET's TLS
  fingerprint on the HTML pages)
- **wallpaperhub** - https://www.wallpaperhub.app/collections (17 collections,
  live index, one page - `?page=N` ignored; fetched through .NET, no curl
  needed)
- **artstation** - https://www.artstation.com/api/v2/search/projects.json?query=wallpaper&page=1&per_page=50&sorting=relevance ·
  https://www.artstation.com/api/v2/search/projects.json?query=landscape&page=1&per_page=50&sorting=relevance ·
  https://www.artstation.com/api/v2/search/projects.json?query=nature&page=1&per_page=50&sorting=relevance ·
  https://www.artstation.com/api/v2/search/projects.json?query=space&page=1&per_page=50&sorting=relevance ·
  https://www.artstation.com/api/v2/search/projects.json?query=abstract&page=1&per_page=50&sorting=relevance
- **pexels** - `https://api.pexels.com/v1/curated?page=1&per_page=30` ·
  `https://api.pexels.com/v1/search?query=nature&orientation=landscape&page=1&per_page=30` ·
  `https://api.pexels.com/v1/search?query=space&orientation=landscape&page=1&per_page=30`
- **wallhaven** - https://wallhaven.cc/api/v1/search?categories=100&purity=100&sorting=toplist&order=desc&page=1 ·
  https://wallhaven.cc/api/v1/search?categories=010&purity=100&sorting=toplist&order=desc&page=1 ·
  https://wallhaven.cc/api/v1/search?categories=001&purity=100&sorting=toplist&order=desc&page=1
- **bing** - https://www.bing.com/HPImageArchive.aspx?format=js&idx=0&n=8&mkt=en-US
- **simple desktops** - https://simpledesktops.com/browse/1/
- **desktopnexus** - https://www.desktopnexus.com/all/ · https://abstract.desktopnexus.com/all/ ·
  https://aircraft.desktopnexus.com/all/ · https://animals.desktopnexus.com/all/ ·
  https://anime.desktopnexus.com/all/ · https://architecture.desktopnexus.com/all/ ·
  https://boats.desktopnexus.com/all/ · https://cars.desktopnexus.com/all/ ·
  https://entertainment.desktopnexus.com/all/ · https://motorcycles.desktopnexus.com/all/ ·
  https://nature.desktopnexus.com/all/ · https://people.desktopnexus.com/all/ ·
  https://space.desktopnexus.com/all/ · https://sports.desktopnexus.com/all/ ·
  https://technology.desktopnexus.com/all/ · https://videogames.desktopnexus.com/all/
- **digital blasphemy** - https://digitalblasphemy.com/product-category/wallpapers/ ·
  https://digitalblasphemy.com/product-tag/free/
- **hdwallpapers** - https://www.hdwallpapers.net/latest-wallpapers · https://www.hdwallpapers.net/3d ·
  https://www.hdwallpapers.net/abstract · https://www.hdwallpapers.net/animals ·
  https://www.hdwallpapers.net/anime · https://www.hdwallpapers.net/bikes ·
  https://www.hdwallpapers.net/brands · https://www.hdwallpapers.net/cars ·
  https://www.hdwallpapers.net/celebrations · https://www.hdwallpapers.net/celebrities ·
  https://www.hdwallpapers.net/city-and-architecture · https://www.hdwallpapers.net/digital-art ·
  https://www.hdwallpapers.net/flowers · https://www.hdwallpapers.net/funny ·
  https://www.hdwallpapers.net/games · https://www.hdwallpapers.net/love ·
  https://www.hdwallpapers.net/nature · https://www.hdwallpapers.net/people ·
  https://www.hdwallpapers.net/quotes · https://www.hdwallpapers.net/space ·
  https://www.hdwallpapers.net/sports · https://www.hdwallpapers.net/technology ·
  https://www.hdwallpapers.net/tv-and-movies · https://www.hdwallpapers.net/typography ·
  https://www.hdwallpapers.net/world
- **pixiv** - https://www.pixiv.net/ajax/search/artworks/Wallpaper?word=Wallpaper&restrict=safe&p=1&order=date_d ·
  https://www.pixiv.net/ajax/search/artworks/Landscape?word=Landscape&restrict=safe&p=1&order=date_d ·
  https://www.pixiv.net/ajax/search/artworks/Nature?word=Nature&restrict=safe&p=1&order=date_d
- **cara** - https://cara.app/explore
- **wallpaper cave** - https://wallpapercave.com/wallpapers · https://wallpapercave.com/cloud-wallpapers ·
  https://wallpapercave.com/desert-wallpapers · https://wallpapercave.com/fire-wallpapers ·
  https://wallpapercave.com/ice-wallpapers · https://wallpapercave.com/lake-wallpapers ·
  https://wallpapercave.com/ocean-wallpapers · https://wallpapercave.com/sunshine-wallpapers ·
  https://wallpapercave.com/soulslike-wallpapers
- **wallpaper engine** - https://steamcommunity.com/workshop/browse/?appid=431960&browsesort=trend&days=90&p=1 ·
  https://steamcommunity.com/workshop/browse/?appid=431960&browsesort=trend&days=90&p=1&requiredtags%5B0%5D=Scene ·
  https://steamcommunity.com/workshop/browse/?appid=431960&browsesort=trend&days=90&p=1&requiredtags%5B0%5D=Anime ·
  https://steamcommunity.com/workshop/browse/?appid=431960&browsesort=trend&days=90&p=1&requiredtags%5B0%5D=3D ·
  https://steamcommunity.com/workshop/browse/?appid=431960&browsesort=trend&days=90&p=1&requiredtags%5B0%5D=Video ·
  https://steamcommunity.com/workshop/browse/?appid=431960&browsesort=trend&days=90&p=1&requiredtags%5B0%5D=Interactive ·
  https://steamcommunity.com/workshop/browse/?appid=431960&browsesort=trend&days=90&p=1&requiredtags%5B0%5D=Audio%20Responsive
- **artgram** - https://www.artgram.co/ · https://www.artgram.co/?sortBy=latest ·
  https://www.artgram.co/?sortBy=oldest

## 🌟 Features

### All Implementations
- **Multiple Wallpaper Sources**: 17 sites and counting - see [Supported sites](#supported-sites)
- **Intelligent Categorization**: Browse wallpapers by collections, AI-generated content, and more
- **Adaptive Layout**: Responsive grid that adapts to any screen size
- **Visual Tagging System**: Identify wallpaper qualities (4K, 5K, 8K) and AI-generated content
- **Slideshow Functionality**: Set multiple wallpapers to rotate automatically
- **Performance Optimized**: Fast loading and smooth scrolling

### WinUI 3 & WPF Specific
- **Modern UI**: Clean interface following Fluent Design principles (WinUI) or Modern WPF UI
- **Infinite Scrolling**: Smooth browsing experience with dynamic content loading
- **Personalization Options**: Create and manage custom collections
- **Interactive Slideshow**: Dynamic, interactive wallpaper experiences

### Python/Tkinter Specific
- **Lightweight**: Minimal resource usage
- **Startup Integration**: Run on system startup
- **Lock Screen Support**: Set lock screen wallpapers
- **DepotDownloader Integration**: Support for Wallpaper Engine workshop content

## Comparison

`✅` means the tool is built around that capability. `partial` means it has a related feature, but
not the same scope or depth. `-` means it is not the point of that tool.

| Capability | Aura | [Wallpaper Engine](https://store.steampowered.com/app/431960/Wallpaper_Engine/) | [Lively Wallpaper](https://livelywallpaper.io/) | [backiee](https://backiee.com/application) | [John's Background Switcher](https://johnsad.ventures/software/backgroundswitcher/) | [Variety](https://peterlevi.com/variety/) |
| --- | --- | --- | --- | --- | --- | --- |
| Static wallpaper browsing and applying | ✅ | partial | partial | ✅ | ✅ | ✅ |
| Multiple online sources | ✅ | partial | partial | ✅ | ✅ | ✅ |
| Native Windows desktop app | ✅ | ✅ | ✅ | ✅ | ✅ | - |
| Multiple implementations in one repo | ✅ | - | - | - | - | - |
| Slideshow / wallpaper rotation | ✅ | ✅ | partial | ✅ | ✅ | ✅ |
| Lock screen support | ✅ | - | - | partial | - | - |
| Live or interactive wallpaper engine | partial | ✅ | ✅ | partial | - | - |
| Wallpaper Engine Workshop path | partial | ✅ | - | - | - | - |
| Local collections and personalization | ✅ | ✅ | ✅ | ✅ | partial | partial |
| Mobile/cloud account ecosystem | - | partial | - | ✅ | - | - |
| Open-source core | ✅ | - | ✅ | - | - | ✅ |

Aura overlaps most with backiee for wallpaper browsing and with Lively/Wallpaper Engine for
desktop personalization. The difference is that Aura is a playground of implementations:
WinUI 3, WPF, and Python paths live together while sharing the same wallpaper-management idea.

The biggest gaps are a real live-wallpaper engine, cloud/mobile sync, a community gallery, and
polished packaging across all three implementations. Lively and Wallpaper Engine are ahead on
live/interactive wallpapers; backiee is ahead on consumer-ready wallpaper discovery.

## 🚀 Getting Started

### Choose Your Implementation

Navigate to the respective directory:
- **WinUI 3**: `cd winui`
- **WPF**: `cd wpf`
- **Python**: `cd python`

---

## 🔨 Building Each Implementation

### 🎨 WinUI 3 (Modern Windows App)

#### Prerequisites
- Windows 10 version 17763 or higher
- .NET 8.0 SDK
- Windows App SDK 1.7+

#### Run Directly
```bash
cd winui
dotnet run
```

#### 🔥 Hot Reload / Dev Mode (Recommended)

Since `dotnet watch` doesn't fully reload `.cs` file changes in WinUI 3, use the included `dev.bat` script instead. It acts like `pnpm dev` — automatically rebuilds and relaunches the app whenever it exits.

```bash
cd winui
.\dev.bat
```

Once the app is running, click the **↺ (restart) button** next to the "Next Gen Edition" badge in the bottom-right corner of the Platform Selection page. The app will exit and `dev.bat` will automatically rebuild and relaunch it with your latest changes.

> **Tip:** Always use `dev.bat` during development instead of plain `dotnet run`.

#### Build Portable Versions

**Self-Contained Portable Folder:**

For **x64** (64-bit Intel/AMD):
```bash
dotnet publish -c Release -p:Platform=x64 -r win-x64 --self-contained true
```
Output: `bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\publish\`

For **x86** (32-bit):
```bash
dotnet publish -c Release -p:Platform=x86 -r win-x86 --self-contained true
```
Output: `bin\x86\Release\net8.0-windows10.0.19041.0\win-x86\publish\`

For **ARM64**:
```bash
dotnet publish -c Release -p:Platform=ARM64 -r win-arm64 --self-contained true
```
Output: `bin\ARM64\Release\net8.0-windows10.0.19041.0\win-arm64\publish\`

**Single EXE File (Self-Contained):**

For **x64**:
```bash
dotnet publish -c Release -p:Platform=x64 -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:WindowsAppSDKSelfContained=true -p:UseWinUI=true
```
Output: `bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\publish\Aura.exe`

For **x86**:
```bash
dotnet publish -c Release -p:Platform=x86 -r win-x86 --self-contained true -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:WindowsAppSDKSelfContained=true -p:UseWinUI=true
```
Output: `bin\x86\Release\net8.0-windows10.0.19041.0\win-x86\publish\Aura.exe`

For **ARM64**:
```bash
dotnet publish -c Release -p:Platform=ARM64 -r win-arm64 --self-contained true -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:WindowsAppSDKSelfContained=true -p:UseWinUI=true
```
Output: `bin\ARM64\Release\net8.0-windows10.0.19041.0\win-arm64\publish\Aura.exe`

---

### 🖼️ WPF (Windows Presentation Foundation)

#### Prerequisites
- Windows 7 SP1 or higher
- .NET 8.0 SDK

#### Run Directly
```bash
cd wpf
dotnet run --project WallYouNeed.App
```

#### Build Portable Versions

**Self-Contained (Any CPU):**
```bash
dotnet publish WallYouNeed.App -c Release -r win-x64 --self-contained true
```
Output: `WallYouNeed.App\bin\Release\net8.0-windows\win-x64\publish\`

**Single EXE:**
```bash
dotnet publish WallYouNeed.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```
Output: `WallYouNeed.App\bin\Release\net8.0-windows\win-x64\publish\WallYouNeed.App.exe`

**For 32-bit:**
Replace `win-x64` with `win-x86` in the commands above.

---

### 🐍 Python (Cross-Platform)

#### Prerequisites
- Python 3.8 or higher
- pip

#### Setup
```bash
cd python
pip install -r requirements.txt
```

#### Run Directly
```bash
python gui.py
```

#### Build Portable EXE (Using PyInstaller)

**Windows:**
```bash
pip install pyinstaller
pyinstaller --clean --noconfirm Wall-You-Need.spec
```
Output: `dist\Wall-You-Need.exe`

**Manual Build (Single EXE):**
```bash
pyinstaller --onefile --windowed --name "Wall-You-Need" gui.py
```

**Custom Build with Dependencies:**
```bash
pyinstaller --onefile --windowed ^
  --add-data "utils.py;." ^
  --add-data "pexels.py;." ^
  --add-data "unsplash.py;." ^
  --add-data "wallpaper_engine.py;." ^
  --add-data "DepotDownloaderMod;DepotDownloaderMod" ^
  --name "Wall-You-Need" gui.py
```

---

## 📁 Project Structure

```
aura/
├── winui/          # WinUI 3 implementation (Modern Windows app)
│   ├── Views/
│   │   ├── AlphaCoders/
│   │   └── Backiee/
│   ├── Services/
│   ├── Models/
│   └── Aura.csproj
│
├── wpf/            # WPF implementation (Traditional Windows desktop)
│   ├── WallYouNeed.App/
│   ├── WallYouNeed.Core/
│   ├── BackieeScraper/
│   └── WallYouNeed.sln
│
└── python/         # Python/Tkinter implementation (Cross-platform)
    ├── gui.py
    ├── utils.py
    ├── pexels.py
    ├── unsplash.py
    ├── wallpaper_engine.py
    ├── DepotDownloaderMod/
    └── requirements.txt
```

## 🧩 Technologies

### WinUI 3
- **WinUI 3**: Modern native UI framework with Fluent Design
- **Windows App SDK**: Unified APIs for Windows apps
- **C# & .NET 8**: Modern language features
- **XAML**: Declarative UI
- **wpf-ui**: UI component library
- **HtmlAgilityPack**: Web scraping
- **Newtonsoft.Json**: JSON handling
- **ImageSharp**: Image processing

### WPF
- **WPF**: Windows Presentation Foundation
- **.NET 8**: Latest framework
- **MVVM Pattern**: CommunityToolkit.Mvvm
- **Modern WPF UI**: Modern styling
- **MahApps.Metro**: UI framework
- **LiteDB**: Local database
- **Serilog**: Logging framework

### Python
- **Tkinter**: Cross-platform GUI
- **PIL/Pillow**: Image processing
- **requests**: HTTP client
- **PyInstaller**: Executable builder
- **threading**: Async operations
- **Windows Registry**: Native integration

## � Roadmap

- [ ] **Unified API**: Shared backend service for all implementations
- [ ] **Widget Support**: Desktop widgets for wallpaper previews
- [ ] **User Accounts**: Cloud sync of collections and preferences
- [ ] **AI Generation**: Generate custom wallpapers using AI
- [ ] **Enhanced Search**: Advanced filtering and search
- [ ] **Mobile App**: Android/iOS companion apps
- [ ] **macOS/Linux Support**: Expand Python implementation
- [ ] **Performance**: Further optimizations across all platforms

## 🤝 Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

1. Fork the project
2. Create your feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add some amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🔗 Links

- **Repository**: [GitHub](https://github.com/FahadBinHussain/aura)
- **Issues**: [Report a bug](https://github.com/FahadBinHussain/aura/issues)
- **Discussions**: [Community discussions](https://github.com/FahadBinHussain/aura/discussions)

## 🙏 Acknowledgments

- Wallpaper API provided by [Backiee](https://backiee.com/)
- Icons from [Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons)
- WinUI 3 and Windows App SDK teams at Microsoft

---

Built with ❤️ using WinUI 3 and Windows App SDK

## Contributors

<a href="https://github.com/FahadBinHussain/aura/graphs/contributors">
  <img src="https://contrib.rocks/image?repo=FahadBinHussain/aura" alt="Contributors" />
</a>
