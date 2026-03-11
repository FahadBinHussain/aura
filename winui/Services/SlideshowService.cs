using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Aura.Models;

namespace Aura.Services
{
    public class SlideshowService
    {
        private static SlideshowService? _instance;
        public static SlideshowService Instance => _instance ??= new SlideshowService();

        private readonly AlphaCodersService _alphaCodersService;
        private readonly AlphaCodersScraperService _alphaCodersScraperService;
        private readonly WallpaperService _wallpaperService;

        private DispatcherQueueTimer? _desktopTimer;
        private DispatcherQueueTimer? _lockScreenTimer;
        private List<WallpaperItem> _desktopWallpapers = new();
        private List<WallpaperItem> _lockScreenWallpapers = new();
        private int _desktopCurrentIndex = 0;
        private int _lockScreenCurrentIndex = 0;
        private readonly HttpClient _httpClient = new();
        
        // Batch tracking for continuous slideshow
        private int _desktopCurrentBatch = 1;
        private int _lockScreenCurrentBatch = 1;
        private string _desktopCategory = "";
        private string _lockScreenCategory = "";
        private DispatcherQueue? _desktopDispatcherQueue;
        private DispatcherQueue? _lockScreenDispatcherQueue;
        private TimeSpan _desktopInterval = TimeSpan.FromHours(12);
        private TimeSpan _lockScreenInterval = TimeSpan.FromHours(12);
        
        // Track next wallpaper change times
        private DateTime _desktopNextChangeTime = DateTime.MinValue;
        private DateTime _lockScreenNextChangeTime = DateTime.MinValue;
        
        // Guard flags to prevent overlapping timer ticks (race condition)
        private bool _isChangingDesktop = false;
        private bool _isChangingLockScreen = false;
        
        // Track current platform for desktop and lockscreen
        private string _desktopPlatform = "";
        private string _lockScreenPlatform = "";
        
        // Current wallpaper URLs for display
        private string _currentDesktopWallpaperUrl = "";
        private string _currentLockScreenWallpaperUrl = "";
        
        // Events for wallpaper changes
        public event EventHandler<string>? DesktopWallpaperChanged;
        public event EventHandler<string>? LockScreenWallpaperChanged;
        
        // Properties to get next change times
        public DateTime DesktopNextChangeTime => _desktopNextChangeTime;
        public DateTime LockScreenNextChangeTime => _lockScreenNextChangeTime;
        public TimeSpan DesktopInterval => _desktopInterval;
        public TimeSpan LockScreenInterval => _lockScreenInterval;

        // Windows API for setting desktop wallpaper
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);

        private const int SPI_SETDESKWALLPAPER = 20;
        private const int SPIF_UPDATEINIFILE = 0x01;
        private const int SPIF_SENDCHANGE = 0x02;

        private SlideshowService()
        {
            _alphaCodersService = new AlphaCodersService();
            _alphaCodersScraperService = new AlphaCodersScraperService();
            _wallpaperService = new WallpaperService();
        }

        private void LogInfo(string message)
        {
            try
            {
                ((App)Application.Current).LogInfo($"[SlideshowService] {message}");
            }
            catch
            {
            }
        }

        public async Task StartDesktopSlideshow(string platform, string category, TimeSpan interval, DispatcherQueue dispatcherQueue)
        {
            try
            {

                // Store parameters for batch loading
                _desktopCategory = category;
                _desktopDispatcherQueue = dispatcherQueue;
                _desktopInterval = interval;

                // Stop existing timer if any
                StopDesktopSlideshow();

                // Load progress if exists
                LoadProgress();

                // Fetch wallpapers
                await LoadWallpapersForDesktop(platform, category);

                if (_desktopWallpapers.Count == 0)
                {
                    return;
                }


                // Set first wallpaper immediately (or current index if resuming)
                await SetDesktopWallpaper(_desktopWallpapers[_desktopCurrentIndex]);
                SaveProgress();
                
                
                // Set next change time AFTER wallpaper is set
                _desktopNextChangeTime = DateTime.Now.Add(interval);
                LogInfo($"[NEW CODE] Desktop next change time set to: {_desktopNextChangeTime}");

                // Use one-shot timer so the countdown restarts AFTER work completes
                // This ensures: set wallpaper → countdown → download → set → countdown → ...
                _desktopTimer = dispatcherQueue.CreateTimer();
                _desktopTimer.Interval = interval;
                _desktopTimer.IsRepeating = false;
                _desktopTimer.Tick += async (sender, e) =>
                {
                    if (_isChangingDesktop) return;
                    _isChangingDesktop = true;
                    try
                    {
                        await NextDesktopWallpaper();
                        // Countdown starts only after wallpaper is successfully set
                        _desktopNextChangeTime = DateTime.Now.Add(interval);
                        LogInfo($"[NEW CODE] Desktop next change time set to: {_desktopNextChangeTime}");
                        // Restart timer for next cycle
                        if (_desktopTimer != null)
                        {
                            _desktopTimer.Start();
                        }
                    }
                    catch (Exception ex)
                    {
                    }
                    finally
                    {
                        _isChangingDesktop = false;
                    }
                };
                _desktopTimer.Start();

            }
            catch (Exception ex)
            {
            }
        }

        public async Task StartLockScreenSlideshow(string platform, string category, TimeSpan interval, DispatcherQueue dispatcherQueue)
        {

            // Store parameters for batch loading
            _lockScreenCategory = category;
            _lockScreenDispatcherQueue = dispatcherQueue;
            _lockScreenInterval = interval;

            // Stop existing timer if any
            StopLockScreenSlideshow();

            // Load progress if exists
            LoadProgress();

            // Fetch wallpapers
            await LoadWallpapersForLockScreen(platform, category);

            if (_lockScreenWallpapers.Count == 0)
            {
                return;
            }

            // Set first wallpaper immediately (or current index if resuming)
            await SetLockScreenWallpaper(_lockScreenWallpapers[_lockScreenCurrentIndex]);
            SaveProgress();
            
            // Set next change time AFTER wallpaper is set
            _lockScreenNextChangeTime = DateTime.Now.Add(interval);
            LogInfo($"Lock screen next change time set to: {_lockScreenNextChangeTime}");

            // Use one-shot timer so the countdown restarts AFTER work completes
            _lockScreenTimer = dispatcherQueue.CreateTimer();
            _lockScreenTimer.Interval = interval;
            _lockScreenTimer.IsRepeating = false;
            _lockScreenTimer.Tick += async (sender, e) =>
            {
                if (_isChangingLockScreen) return;
                _isChangingLockScreen = true;
                try
                {
                    await NextLockScreenWallpaper();
                    // Countdown starts only after wallpaper is successfully set
                    _lockScreenNextChangeTime = DateTime.Now.Add(interval);
                    LogInfo($"Lock screen next change time set to: {_lockScreenNextChangeTime}");
                    // Restart timer for next cycle
                    if (_lockScreenTimer != null)
                    {
                        _lockScreenTimer.Start();
                    }
                }
                catch (Exception ex)
                {
                }
                finally
                {
                    _isChangingLockScreen = false;
                }
            };
            _lockScreenTimer.Start();

        }

        public void StopDesktopSlideshow()
        {
            if (_desktopTimer != null)
            {
                _desktopTimer.Stop();
                _desktopTimer = null;
            }
            _isChangingDesktop = false;
        }

        public void StopLockScreenSlideshow()
        {
            if (_lockScreenTimer != null)
            {
                _lockScreenTimer.Stop();
                _lockScreenTimer = null;
            }
            _isChangingLockScreen = false;
        }

        public async Task NextDesktopWallpaper()
        {
            try
            {
                
                if (_desktopWallpapers.Count > 0)
                {
                    _desktopCurrentIndex++;
                    
                    // Check if we've reached the end of current batch
                    if (_desktopCurrentIndex >= _desktopWallpapers.Count)
                    {
                        // Load next batch
                        _desktopCurrentBatch++;
                        _desktopCurrentIndex = 0;
                        await LoadWallpapersForDesktop(_desktopPlatform, _desktopCategory);
                        SaveProgress(); // Save progress after loading new batch
                    }
                    
                    if (_desktopWallpapers.Count > 0 && _desktopCurrentIndex < _desktopWallpapers.Count)
                    {
                        await SetDesktopWallpaper(_desktopWallpapers[_desktopCurrentIndex]);
                        SaveProgress(); // Save progress after each wallpaper change
                    }
                    else
                    {
                    }
                }
                else
                {
                }
            }
            catch (Exception ex)
            {
            }
        }

        public async Task NextLockScreenWallpaper()
        {
            try
            {
                
                if (_lockScreenWallpapers.Count > 0)
                {
                    _lockScreenCurrentIndex++;
                    
                    // Check if we've reached the end of current batch
                    if (_lockScreenCurrentIndex >= _lockScreenWallpapers.Count)
                    {
                        // Load next batch
                        _lockScreenCurrentBatch++;
                        _lockScreenCurrentIndex = 0;
                        await LoadWallpapersForLockScreen(_lockScreenPlatform, _lockScreenCategory);
                        SaveProgress(); // Save progress after loading new batch
                    }
                    
                    if (_lockScreenWallpapers.Count > 0 && _lockScreenCurrentIndex < _lockScreenWallpapers.Count)
                    {
                        await SetLockScreenWallpaper(_lockScreenWallpapers[_lockScreenCurrentIndex]);
                        SaveProgress(); // Save progress after each wallpaper change
                    }
                    else
                    {
                    }
                }
                else
                {
                }
            }
            catch (Exception ex)
            {
            }
        }

        private async Task LoadWallpapersForDesktop(string platform, string category)
        {
            try
            {
                _desktopWallpapers.Clear();
                _desktopPlatform = platform; // Store the platform


                if (platform == "AlphaCoders")
                {
                    // Get wallpapers from AlphaCoders service
                    string categoryKey = category switch
                    {
                        "4K Wallpapers" => "4k",
                        "Harvest Wallpapers" => "harvest",
                        "Rain Wallpapers" => "rain",
                        _ => "4k"
                    };

                    // Use scraper directly to avoid cache issues
                    var wallpapers = await _alphaCodersScraperService.ScrapeWallpapersByCategoryAsync(categoryKey, _desktopCurrentBatch, _desktopCurrentBatch);
                    _desktopWallpapers.AddRange(wallpapers);
                }
                else // Backiee
                {
                    
                    // Use batch number as page number (0-indexed so subtract 1)
                    int pageNumber = _desktopCurrentBatch - 1;
                    string apiUrl = $"https://backiee.com/api/wallpaper/list.php?action=paging_list&list_type=latest&page={pageNumber}&page_size=50&category=all&is_ai=all&sort_by=popularity&4k=false&5k=false&8k=false&status=active&args=";
                    
                    HttpResponseMessage response = await _httpClient.GetAsync(apiUrl);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        string jsonContent = await response.Content.ReadAsStringAsync();
                        using (JsonDocument doc = JsonDocument.Parse(jsonContent))
                        {
                            foreach (JsonElement wallpaperElement in doc.RootElement.EnumerateArray())
                            {
                                string id = wallpaperElement.GetProperty("ID").GetString();
                                string title = wallpaperElement.GetProperty("Title").GetString();
                                string fullPhotoUrl = wallpaperElement.GetProperty("FullPhotoUrl").GetString();
                                
                                _desktopWallpapers.Add(new WallpaperItem
                                {
                                    Id = id,
                                    Title = title,
                                    FullPhotoUrl = fullPhotoUrl
                                });
                            }
                        }
                    }
                    else
                    {
                    }
                }

            }
            catch (Exception ex)
            {
            }
        }

        private async Task LoadWallpapersForLockScreen(string platform, string category)
        {
            _lockScreenWallpapers.Clear();
            _lockScreenPlatform = platform; // Store the platform


            if (platform == "AlphaCoders")
            {
                // Get wallpapers from AlphaCoders service
                string categoryKey = category switch
                {
                    "4K Wallpapers" => "4k",
                    "Harvest Wallpapers" => "harvest",
                    "Rain Wallpapers" => "rain",
                    _ => "4k"
                };

                // Use scraper directly to avoid cache issues
                var wallpapers = await _alphaCodersScraperService.ScrapeWallpapersByCategoryAsync(categoryKey, _lockScreenCurrentBatch, _lockScreenCurrentBatch);
                _lockScreenWallpapers.AddRange(wallpapers);
            }
            else // Backiee
            {
                
                // Use batch number as page number (0-indexed so subtract 1)
                int pageNumber = _lockScreenCurrentBatch - 1;
                string apiUrl = $"https://backiee.com/api/wallpaper/list.php?action=paging_list&list_type=latest&page={pageNumber}&page_size=50&category=all&is_ai=all&sort_by=popularity&4k=false&5k=false&8k=false&status=active&args=";
                
                HttpResponseMessage response = await _httpClient.GetAsync(apiUrl);
                
                if (response.IsSuccessStatusCode)
                {
                    string jsonContent = await response.Content.ReadAsStringAsync();
                    using (JsonDocument doc = JsonDocument.Parse(jsonContent))
                    {
                        foreach (JsonElement wallpaperElement in doc.RootElement.EnumerateArray())
                        {
                            string id = wallpaperElement.GetProperty("ID").GetString();
                            string title = wallpaperElement.GetProperty("Title").GetString();
                            string fullPhotoUrl = wallpaperElement.GetProperty("FullPhotoUrl").GetString();
                            
                            _lockScreenWallpapers.Add(new WallpaperItem
                            {
                                Id = id,
                                Title = title,
                                FullPhotoUrl = fullPhotoUrl
                            });
                        }
                    }
                }
                else
                {
                }
            }
        }

        private async Task SetDesktopWallpaper(WallpaperItem wallpaper)
        {
            try
            {
                // Use platform-specific logic
                if (_desktopPlatform == "AlphaCoders")
                {
                    await SetDesktopWallpaper_AlphaCoders(wallpaper);
                }
                else // Backiee
                {
                    await SetDesktopWallpaper_Backiee(wallpaper);
                }
            }
            catch (Exception ex)
            {
            }
        }

        private async Task SetDesktopWallpaper_Backiee(WallpaperItem wallpaper)
        {
            try
            {
                string imageUrl = wallpaper.FullPhotoUrl;
                if (string.IsNullOrEmpty(imageUrl))
                {
                    return;
                }

                LogInfo($"Downloading desktop wallpaper: {wallpaper.Title}");

                // Get Pictures folder
                var picturesFolder = Windows.Storage.KnownFolders.PicturesLibrary;
                var wallpapersFolder = await picturesFolder.CreateFolderAsync("Aura", Windows.Storage.CreationCollisionOption.OpenIfExists);

                // Download the image
                byte[] imageBytes;
                if (imageUrl.StartsWith("ms-appx://", StringComparison.OrdinalIgnoreCase))
                {
                    // Handle local app resources
                    string localPath = imageUrl.Replace("ms-appx:///", "").Replace("/", "\\");
                    string appPath = AppDomain.CurrentDomain.BaseDirectory;
                    string fullLocalPath = Path.Combine(appPath, localPath);
                    
                    
                    if (File.Exists(fullLocalPath))
                    {
                        imageBytes = await File.ReadAllBytesAsync(fullLocalPath);
                    }
                    else
                    {
                        return;
                    }
                }
                else
                {
                    // Download from HTTP URL
                    imageBytes = await _httpClient.GetByteArrayAsync(imageUrl);
                }

                // Create a file in Pictures folder
                var wallpaperFile = await wallpapersFolder.CreateFileAsync(
                    $"wallpaper-{wallpaper.Id}.jpg",
                    Windows.Storage.CreationCollisionOption.ReplaceExisting);

                // Write the image to the file
                using (var stream = await wallpaperFile.OpenStreamForWriteAsync())
                {
                    await stream.WriteAsync(imageBytes, 0, imageBytes.Length);
                }

                LogInfo($"Wallpaper saved to: {wallpaperFile.Path}");

                // Try to set the wallpaper using WinRT API
                bool success = false;
                var userProfilePersonalizationSettings = Windows.System.UserProfile.UserProfilePersonalizationSettings.Current;

                try
                {
                    success = await userProfilePersonalizationSettings.TrySetWallpaperImageAsync(wallpaperFile);
                }
                catch (Exception ex)
                {
                }

                // If WinRT API fails, try WallpaperHelper as fallback
                if (!success)
                {
                    try
                    {
                        SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, wallpaperFile.Path, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
                        success = true;
                    }
                    catch (Exception ex)
                    {
                    }
                }

                if (success)
                {
                    LogInfo($"Desktop wallpaper set to: {wallpaper.Title}");
                    
                    // Store the current wallpaper URL and raise event
                    _currentDesktopWallpaperUrl = imageUrl;
                    DesktopWallpaperChanged?.Invoke(this, imageUrl);
                }
                else
                {
                }
            }
            catch (Exception ex)
            {
            }
        }

        private async Task SetDesktopWallpaper_AlphaCoders(WallpaperItem wallpaper)
        {
            try
            {
                if (string.IsNullOrEmpty(wallpaper.ImageUrl))
                {
                    return;
                }

                LogInfo($"Downloading desktop wallpaper: {wallpaper.Title}");

                // Get Pictures folder
                var picturesFolder = Windows.Storage.KnownFolders.PicturesLibrary;
                var wallpapersFolder = await picturesFolder.CreateFolderAsync("Aura", Windows.Storage.CreationCollisionOption.OpenIfExists);

                // Get the big thumb URL to extract extension
                var scraperService = new AlphaCodersScraperService();
                var bigThumbUrl = await scraperService.GetBigImageUrlForWallpaperAsync(wallpaper.Id, wallpaper.ImageUrl);

                if (string.IsNullOrEmpty(bigThumbUrl))
                {
                    return;
                }

                // Extract extension from big thumb URL
                var bigThumbUri = new Uri(bigThumbUrl);
                var bigThumbPath = bigThumbUri.AbsolutePath;
                var extension = System.IO.Path.GetExtension(bigThumbPath).TrimStart('.');

                // Build original URL with same extension
                var imageId = wallpaper.Id;
                var uri = new Uri(wallpaper.ImageUrl);
                var domainParts = uri.Host.Split('.');
                var domainShort = domainParts[0];

                var originalUrl = $"https://initiate.alphacoders.com/download/{domainShort}/{imageId}/{extension}";


                byte[] imageBytes;
                try
                {
                    imageBytes = await _httpClient.GetByteArrayAsync(originalUrl);
                    LogInfo($"Successfully downloaded AlphaCoders wallpaper");
                }
                catch (Exception ex)
                {
                    return;
                }

                // Create a file in Pictures folder with a unique name based on timestamp
                string timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
                string fileExtension = !string.IsNullOrEmpty(extension) ? extension : "jpg";

                var wallpaperFile = await wallpapersFolder.CreateFileAsync(
                    $"wallpaper-{wallpaper.Id}-{timestamp}.{fileExtension}",
                    Windows.Storage.CreationCollisionOption.ReplaceExisting);

                // Write the image to the file
                using (var stream = await wallpaperFile.OpenStreamForWriteAsync())
                {
                    await stream.WriteAsync(imageBytes, 0, imageBytes.Length);
                }

                LogInfo($"AlphaCoders wallpaper saved to: {wallpaperFile.Path}");

                // Try to set the wallpaper using WinRT API
                bool success = false;
                var userProfilePersonalizationSettings = Windows.System.UserProfile.UserProfilePersonalizationSettings.Current;

                try
                {
                    success = await userProfilePersonalizationSettings.TrySetWallpaperImageAsync(wallpaperFile);
                }
                catch (Exception ex)
                {
                }

                // If WinRT API fails, try SystemParametersInfo as fallback
                if (!success)
                {
                    try
                    {
                        SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, wallpaperFile.Path, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
                        success = true;
                    }
                    catch (Exception ex)
                    {
                    }
                }

                if (success)
                {
                    LogInfo($"AlphaCoders desktop wallpaper set to: {wallpaper.Title}");
                    
                    // Store the current wallpaper URL and raise event
                    _currentDesktopWallpaperUrl = originalUrl;
                    DesktopWallpaperChanged?.Invoke(this, originalUrl);
                }
                else
                {
                }
            }
            catch (Exception ex)
            {
            }
        }

        private async Task SetLockScreenWallpaper(WallpaperItem wallpaper)
        {
            try
            {
                // Use platform-specific logic
                if (_lockScreenPlatform == "AlphaCoders")
                {
                    await SetLockScreenWallpaper_AlphaCoders(wallpaper);
                }
                else // Backiee
                {
                    await SetLockScreenWallpaper_Backiee(wallpaper);
                }
            }
            catch (Exception ex)
            {
            }
        }

        private async Task SetLockScreenWallpaper_Backiee(WallpaperItem wallpaper)
        {
            try
            {
                string imageUrl = wallpaper.FullPhotoUrl;
                if (string.IsNullOrEmpty(imageUrl))
                {
                    return;
                }

                LogInfo($"Downloading lock screen wallpaper: {wallpaper.Title}");

                // Get Pictures folder
                var picturesFolder = Windows.Storage.KnownFolders.PicturesLibrary;
                var wallpapersFolder = await picturesFolder.CreateFolderAsync("Aura", Windows.Storage.CreationCollisionOption.OpenIfExists);

                // Download the image
                byte[] imageBytes;
                if (imageUrl.StartsWith("ms-appx://", StringComparison.OrdinalIgnoreCase))
                {
                    // Handle local app resources
                    string localPath = imageUrl.Replace("ms-appx:///", "").Replace("/", "\\");
                    string appPath = AppDomain.CurrentDomain.BaseDirectory;
                    string fullLocalPath = Path.Combine(appPath, localPath);
                    
                    
                    if (File.Exists(fullLocalPath))
                    {
                        imageBytes = await File.ReadAllBytesAsync(fullLocalPath);
                    }
                    else
                    {
                        return;
                    }
                }
                else
                {
                    // Download from HTTP URL
                    imageBytes = await _httpClient.GetByteArrayAsync(imageUrl);
                }

                // Create a file in Pictures folder
                var wallpaperFile = await wallpapersFolder.CreateFileAsync(
                    $"lockscreen-{wallpaper.Id}.jpg",
                    Windows.Storage.CreationCollisionOption.ReplaceExisting);

                // Write the image to the file
                using (var stream = await wallpaperFile.OpenStreamForWriteAsync())
                {
                    await stream.WriteAsync(imageBytes, 0, imageBytes.Length);
                }

                LogInfo($"Lock screen image saved to: {wallpaperFile.Path}");

                // Use registry method (same as WallpaperDetailPage)
                bool success = false;
                const string PERSONALIZE_REG_KEY = @"Software\Microsoft\Windows\CurrentVersion\PersonalizationCSP";
                const string LOCKSCREEN_PATH_REG_VALUE = "LockScreenImagePath";
                const string LOCKSCREEN_STATUS_REG_VALUE = "LockScreenImageStatus";
                const string LOCKSCREEN_URL_REG_VALUE = "LockScreenImageUrl";

                // Method: Using Registry for lock screen with LocalMachine
                try
                {
                    using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(PERSONALIZE_REG_KEY, true))
                    {
                        if (key == null)
                        {
                            // Try to create the key if it doesn't exist
                            using (var newKey = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(PERSONALIZE_REG_KEY, true))
                            {
                                if (newKey != null)
                                {
                                    newKey.SetValue(LOCKSCREEN_PATH_REG_VALUE, wallpaperFile.Path);
                                    newKey.SetValue(LOCKSCREEN_STATUS_REG_VALUE, 1);
                                    newKey.SetValue(LOCKSCREEN_URL_REG_VALUE, wallpaperFile.Path);
                                    success = true;
                                }
                            }
                        }
                        else
                        {
                            // Key exists, set the values
                            key.SetValue(LOCKSCREEN_PATH_REG_VALUE, wallpaperFile.Path);
                            key.SetValue(LOCKSCREEN_STATUS_REG_VALUE, 1);
                            key.SetValue(LOCKSCREEN_URL_REG_VALUE, wallpaperFile.Path);
                            success = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                }

                // If LocalMachine failed, try with CurrentUser as fallback
                if (!success)
                {
                    try
                    {
                        using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(PERSONALIZE_REG_KEY, true))
                        {
                            if (key == null)
                            {
                                // Try to create the key if it doesn't exist
                                using (var newKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(PERSONALIZE_REG_KEY, true))
                                {
                                    if (newKey != null)
                                    {
                                        newKey.SetValue(LOCKSCREEN_PATH_REG_VALUE, wallpaperFile.Path);
                                        newKey.SetValue(LOCKSCREEN_STATUS_REG_VALUE, 1);
                                        newKey.SetValue(LOCKSCREEN_URL_REG_VALUE, wallpaperFile.Path);
                                        success = true;
                                    }
                                }
                            }
                            else
                            {
                                // Key exists, set the values
                                key.SetValue(LOCKSCREEN_PATH_REG_VALUE, wallpaperFile.Path);
                                key.SetValue(LOCKSCREEN_STATUS_REG_VALUE, 1);
                                key.SetValue(LOCKSCREEN_URL_REG_VALUE, wallpaperFile.Path);
                                success = true;
                            }
                        }
                    }
                    catch (Exception innerEx)
                    {
                    }
                }

                if (success)
                {
                    LogInfo($"Lock screen set to: {wallpaper.Title}");
                    
                    // Store the current wallpaper URL and raise event
                    _currentLockScreenWallpaperUrl = imageUrl;
                    LockScreenWallpaperChanged?.Invoke(this, imageUrl);
                }
                else
                {
                }
            }
            catch (Exception ex)
            {
            }
        }

        private async Task SetLockScreenWallpaper_AlphaCoders(WallpaperItem wallpaper)
        {
            try
            {
                if (string.IsNullOrEmpty(wallpaper.ImageUrl))
                {
                    return;
                }

                LogInfo($"Downloading lock screen wallpaper: {wallpaper.Title}");

                // Get Pictures folder
                var picturesFolder = Windows.Storage.KnownFolders.PicturesLibrary;
                var wallpapersFolder = await picturesFolder.CreateFolderAsync("Aura", Windows.Storage.CreationCollisionOption.OpenIfExists);

                // Get the big thumb URL to extract extension
                var scraperService = new AlphaCodersScraperService();
                var bigThumbUrl = await scraperService.GetBigImageUrlForWallpaperAsync(wallpaper.Id, wallpaper.ImageUrl);

                if (string.IsNullOrEmpty(bigThumbUrl))
                {
                    return;
                }

                // Extract extension from big thumb URL
                var bigThumbUri = new Uri(bigThumbUrl);
                var bigThumbPath = bigThumbUri.AbsolutePath;
                var extension = System.IO.Path.GetExtension(bigThumbPath).TrimStart('.');

                // Build original URL with same extension
                var imageId = wallpaper.Id;
                var uri = new Uri(wallpaper.ImageUrl);
                var domainParts = uri.Host.Split('.');
                var domainShort = domainParts[0];

                var originalUrl = $"https://initiate.alphacoders.com/download/{domainShort}/{imageId}/{extension}";


                byte[] imageBytes;
                try
                {
                    imageBytes = await _httpClient.GetByteArrayAsync(originalUrl);
                    LogInfo($"Successfully downloaded AlphaCoders wallpaper for lock screen");
                }
                catch (Exception ex)
                {
                    return;
                }

                // Create a file in Pictures folder with a unique name based on timestamp
                string timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
                string fileExtension = !string.IsNullOrEmpty(extension) ? extension : "jpg";

                var wallpaperFile = await wallpapersFolder.CreateFileAsync(
                    $"lockscreen-{wallpaper.Id}-{timestamp}.{fileExtension}",
                    Windows.Storage.CreationCollisionOption.ReplaceExisting);

                // Write the image to the file
                using (var stream = await wallpaperFile.OpenStreamForWriteAsync())
                {
                    await stream.WriteAsync(imageBytes, 0, imageBytes.Length);
                }

                LogInfo($"AlphaCoders lock screen wallpaper saved to: {wallpaperFile.Path}");

                // Try to set the lock screen using WinRT API
                bool success = false;
                var userProfilePersonalizationSettings = Windows.System.UserProfile.UserProfilePersonalizationSettings.Current;

                try
                {
                    success = await userProfilePersonalizationSettings.TrySetLockScreenImageAsync(wallpaperFile);
                }
                catch (Exception ex)
                {
                }

                if (success)
                {
                    LogInfo($"AlphaCoders lock screen set to: {wallpaper.Title}");
                    
                    // Store the current wallpaper URL and raise event
                    _currentLockScreenWallpaperUrl = originalUrl;
                    LockScreenWallpaperChanged?.Invoke(this, originalUrl);
                }
                else
                {
                }
            }
            catch (Exception ex)
            {
            }
        }

        public static TimeSpan ParseInterval(string interval)
        {
            if (string.IsNullOrWhiteSpace(interval))
                return TimeSpan.FromHours(12); // Default

            var parts = interval.Trim().Split(' ');
            if (parts.Length < 2)
                return TimeSpan.FromHours(12); // Default

            if (!double.TryParse(parts[0], out double value))
                return TimeSpan.FromHours(12); // Default

            string unit = parts[1].Trim().ToLower();

            // Handle both singular and plural forms
            if (unit == "second" || unit == "seconds")
                return TimeSpan.FromSeconds(value);
            else if (unit == "minute" || unit == "minutes")
                return TimeSpan.FromMinutes(value);
            else if (unit == "hour" || unit == "hours")
                return TimeSpan.FromHours(value);
            else if (unit == "day" || unit == "days")
                return TimeSpan.FromDays(value);
            else
                return TimeSpan.FromHours(12); // Default
        }
        
        public string GetCurrentDesktopWallpaperUrl()
        {
            return _currentDesktopWallpaperUrl;
        }
        
        public string GetCurrentLockScreenWallpaperUrl()
        {
            return _currentLockScreenWallpaperUrl;
        }
        
        public WallpaperItem? GetCurrentDesktopWallpaperItem()
        {
            if (_desktopWallpapers.Count > 0 && _desktopCurrentIndex < _desktopWallpapers.Count)
            {
                return _desktopWallpapers[_desktopCurrentIndex];
            }
            return null;
        }
        
        public WallpaperItem? GetCurrentLockScreenWallpaperItem()
        {
            if (_lockScreenWallpapers.Count > 0 && _lockScreenCurrentIndex < _lockScreenWallpapers.Count)
            {
                return _lockScreenWallpapers[_lockScreenCurrentIndex];
            }
            return null;
        }

        private void SaveProgress()
        {
            try
            {
                var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Aura", "slideshow_settings.json");
                
                if (!File.Exists(settingsPath))
                {
                    return;
                }

                // Read existing settings
                var json = File.ReadAllText(settingsPath);
                var settings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                
                if (settings == null)
                {
                    settings = new Dictionary<string, JsonElement>();
                }

                // Convert to object dictionary for easier manipulation
                var settingsObj = new Dictionary<string, object>();
                foreach (var kvp in settings)
                {
                    if (kvp.Value.ValueKind == JsonValueKind.True || kvp.Value.ValueKind == JsonValueKind.False)
                    {
                        settingsObj[kvp.Key] = kvp.Value.GetBoolean();
                    }
                    else if (kvp.Value.ValueKind == JsonValueKind.Number)
                    {
                        settingsObj[kvp.Key] = kvp.Value.GetInt32();
                    }
                    else
                    {
                        settingsObj[kvp.Key] = kvp.Value.GetString() ?? "";
                    }
                }

                // Update progress fields
                settingsObj["DesktopCurrentBatch"] = _desktopCurrentBatch;
                settingsObj["DesktopCurrentIndex"] = _desktopCurrentIndex;
                settingsObj["LockScreenCurrentBatch"] = _lockScreenCurrentBatch;
                settingsObj["LockScreenCurrentIndex"] = _lockScreenCurrentIndex;

                // Save back to file
                var updatedJson = JsonSerializer.Serialize(settingsObj, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(settingsPath, updatedJson);
                
            }
            catch (Exception ex)
            {
            }
        }

        private void LoadProgress()
        {
            try
            {
                var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Aura", "slideshow_settings.json");
                
                if (!File.Exists(settingsPath))
                {
                    return;
                }

                var json = File.ReadAllText(settingsPath);
                var settings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                
                if (settings == null)
                {
                    return;
                }

                // Load desktop progress
                if (settings.ContainsKey("DesktopCurrentBatch"))
                {
                    _desktopCurrentBatch = settings["DesktopCurrentBatch"].GetInt32();
                }
                if (settings.ContainsKey("DesktopCurrentIndex"))
                {
                    _desktopCurrentIndex = settings["DesktopCurrentIndex"].GetInt32();
                }
                
                // Load lock screen progress
                if (settings.ContainsKey("LockScreenCurrentBatch"))
                {
                    _lockScreenCurrentBatch = settings["LockScreenCurrentBatch"].GetInt32();
                }
                if (settings.ContainsKey("LockScreenCurrentIndex"))
                {
                    _lockScreenCurrentIndex = settings["LockScreenCurrentIndex"].GetInt32();
                }
                
            }
            catch (Exception ex)
            {
            }
        }
    }
}
