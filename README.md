# Aura / Wall-You-Need
<img src="https://wakapi-qt1b.onrender.com/api/badge/fahad/interval:any/project:Aura" 
     alt="Wakapi Time Tracking" 
     title="Time spent on this project">

A modern wallpaper management and personalization application available in **three implementations**:
- **WinUI 3** - Modern Windows native app with Fluent Design
- **WPF** - Traditional Windows desktop application  
- **Python** - Cross-platform GUI with Tkinter

Browse, organize, and apply beautiful wallpapers from multiple sources including Backiee, AlphaCoders, Unsplash, Pexels, and Wallpaper Engine.

## 🌟 Features

### All Implementations
- **Multiple Wallpaper Sources**: Backiee, AlphaCoders, Unsplash, Pexels, and Wallpaper Engine
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
