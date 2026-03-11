using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml;
using Aura.Models;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Shapes; // for Ellipse and Path shape
using Microsoft.UI.Xaml.Media; // for ImageBrush and Stretch
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.Storage.AccessCache; // For KnownFolders and KnownFolderId
using System.IO;
using WinRT.Interop;
using Microsoft.UI.Windowing;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Linq;
using System.Reflection; // For reflection functionality
using Windows.System.UserProfile; // For wallpaper functionality
using System.Runtime.InteropServices; // For P/Invoke
using Windows.ApplicationModel; // For package detection
using Aura.Services;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Aura.Views.AlphaCoders
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public static class WallpaperHelper
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);

        private const int SPI_SETDESKWALLPAPER = 0x0014;
        private const int SPIF_UPDATEINIFILE = 0x01;
        private const int SPIF_SENDCHANGE = 0x02;
        private const int SPIF_SENDWININICHANGE = 0x02;

        // Registry keys for wallpaper settings
        private const string WALLPAPER_REG_KEY = @"Control Panel\Desktop";
        private const string WALLPAPER_REG_VALUE = "Wallpaper";
        private const string WALLPAPER_STYLE_REG_VALUE = "WallpaperStyle";
        private const string WALLPAPER_TILE_REG_VALUE = "TileWallpaper";

        // Registry keys for lock screen settings
        private const string PERSONALIZE_REG_KEY = @"SOFTWARE\Microsoft\Windows\CurrentVersion\PersonalizationCSP";
        private const string LOCKSCREEN_PATH_REG_VALUE = "LockScreenImagePath";
        private const string LOCKSCREEN_STATUS_REG_VALUE = "LockScreenImageStatus";
        private const string LOCKSCREEN_URL_REG_VALUE = "LockScreenImageUrl";

        public static bool SetWallpaper(string path)
        {
            try
            {
                // Ensure the file exists and is accessible
                if (!File.Exists(path))
                {
                    return false;
                }

                // Make sure the file has proper permissions
                try
                {
                    // Test read access to the file
                    using (var fileStream = File.OpenRead(path))
                    {
                        // Just testing access, no need to read anything
                    }
                }
                catch (Exception ex)
                {
                    return false;
                }

                // Method 1: Using SystemParametersInfo
                bool success = false;
                try
                {

                    // Make sure the file is accessible to the system
                    System.IO.File.SetAttributes(path, System.IO.FileAttributes.Normal);

                    // Use all available flags to ensure the setting persists
                    int result = SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, path, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE | SPIF_SENDWININICHANGE);
                    success = result != 0;

                    if (!success)
                    {
                        int error = Marshal.GetLastWin32Error();
                    }

                }
                catch (Exception ex)
                {
                }

                // Method 2: Using Registry if Method 1 failed
                if (!success)
                {
                    try
                    {
                        // First, ensure the file is accessible with proper permissions
                        System.IO.File.SetAttributes(path, System.IO.FileAttributes.Normal);

                        // Copy the file to Windows directory for better persistence
                        string windowsPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                        string persistentWallpaperPath = System.IO.Path.Combine(windowsPath, "Web", "Wallpaper", "Windows", System.IO.Path.GetFileName(path));

                        try {
                            // Create directory if it doesn't exist
                            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(persistentWallpaperPath));
                            // Copy the file
                            System.IO.File.Copy(path, persistentWallpaperPath, true);
                            // Use the Windows directory path for registry
                            path = persistentWallpaperPath;
                        }
                        catch (Exception ex) {
                            // Continue with original path if copy fails
                        }

                        using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(WALLPAPER_REG_KEY, true))
                        {
                            if (key != null)
                            {
                                // Set the wallpaper path
                                key.SetValue(WALLPAPER_REG_VALUE, path);
                                // Set the wallpaper style (2 = stretched)
                                key.SetValue(WALLPAPER_STYLE_REG_VALUE, "2");
                                // Set the wallpaper tile (0 = no tile)
                                key.SetValue(WALLPAPER_TILE_REG_VALUE, "0");
                                success = true;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                    }
                }

                return success;
            }
            catch (Exception ex)
            {
                return false;
            }
        }

        public static bool SetLockScreen(string path)
        {
            try
            {
                bool success = false;

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
                                    newKey.SetValue(LOCKSCREEN_PATH_REG_VALUE, path);
                                    newKey.SetValue(LOCKSCREEN_STATUS_REG_VALUE, 1);
                                    newKey.SetValue(LOCKSCREEN_URL_REG_VALUE, path);
                                    success = true;
                                }
                            }
                        }
                        else
                        {
                            // Key exists, set the values
                            key.SetValue(LOCKSCREEN_PATH_REG_VALUE, path);
                            key.SetValue(LOCKSCREEN_STATUS_REG_VALUE, 1);
                            key.SetValue(LOCKSCREEN_URL_REG_VALUE, path);
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
                                        newKey.SetValue(LOCKSCREEN_PATH_REG_VALUE, path);
                                        newKey.SetValue(LOCKSCREEN_STATUS_REG_VALUE, 1);
                                        newKey.SetValue(LOCKSCREEN_URL_REG_VALUE, path);
                                        success = true;
                                    }
                                }
                            }
                            else
                            {
                                // Key exists, set the values
                                key.SetValue(LOCKSCREEN_PATH_REG_VALUE, path);
                                key.SetValue(LOCKSCREEN_STATUS_REG_VALUE, 1);
                                key.SetValue(LOCKSCREEN_URL_REG_VALUE, path);
                                success = true;
                            }
                        }
                    }
                    catch (Exception innerEx)
                    {
                    }
                }

                return success;
            }
            catch (Exception ex)
            {
                return false;
            }
        }
    }
    public sealed partial class WallpaperDetailPage : Page
    {
        private readonly HttpClient _httpClient = new HttpClient();
        private const string ApiBaseUrl = "https://atozmashprima.com/api/search-wall-papers?id=";
        private const string DetailApiBaseUrl = "https://backiee.com/api/wallpaper/list.php?action=detail_page_v2&wallpaper_id=";
        private WallpaperItem _currentWallpaper;

        private async Task LoadAlphaCodersImageAsync(WallpaperItem wallpaper)
        {
            try
            {

                // Only fetch the big thumb URL for display
                var scraperService = new Aura.Services.AlphaCodersScraperService();
                var bigThumbUrl = await scraperService.GetBigImageUrlForWallpaperAsync(wallpaper.Id, wallpaper.ImageUrl);

                // Update debug TextBlock with big thumb URL
                if (!string.IsNullOrEmpty(bigThumbUrl))
                {
                    DebugBigThumbTextBlock.Text = $"Big Thumb URL: {bigThumbUrl}";

                    // Store big thumb for display, original URL will be fetched on-demand during set/download

                    // Load the big thumb directly (no WebP conversion needed)
                    WallpaperImage.Source = new BitmapImage(new Uri(bigThumbUrl));
                }
                else
                {
                    DebugBigThumbTextBlock.Text = "Big Thumb URL: Not available";
                    await ShowErrorDialogAsync("Image preview is not available for this wallpaper.");
                }
            }
            catch (Exception ex)
            {
            }
        }




        public WallpaperDetailPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);


            if (e.Parameter is WallpaperItem wallpaper)
            {
                _currentWallpaper = wallpaper;
                // Always override SourceUrl for Alpha Coders wallpapers, set default for others
                if (!string.IsNullOrEmpty(_currentWallpaper.Id))
                {
                    // Check if this is an Alpha Coders wallpaper by looking at the ImageUrl
                    if (!string.IsNullOrEmpty(_currentWallpaper.ImageUrl) && _currentWallpaper.ImageUrl.Contains("alphacoders.com"))
                    {
                        _currentWallpaper.SourceUrl = $"https://wall.alphacoders.com/big.php?i={_currentWallpaper.Id}";
                    }
                    else if (string.IsNullOrEmpty(_currentWallpaper.SourceUrl))
                    {
                        _currentWallpaper.SourceUrl = $"https://backiee.com/wallpaper/{_currentWallpaper.Id}";
                    }
                }



                // Set title at the top of the page
                TitleTextBlock.Text = wallpaper.Title ?? "Error Loading Wallpaper";

                // Check if this is an Alpha Coders wallpaper that needs on-demand big thumb fetching
                if (!string.IsNullOrEmpty(_currentWallpaper.ImageUrl) && _currentWallpaper.ImageUrl.Contains("alphacoders.com"))
                {
                    // Load the big thumb asynchronously
                    _ = LoadAlphaCodersImageAsync(wallpaper);
                }
                else if (!string.IsNullOrEmpty(wallpaper.FullPhotoUrl))
                {
                    // For non-Alpha Coders wallpapers with FullPhotoUrl, load directly
                    try
                    {
                        WallpaperImage.Source = new BitmapImage(new Uri(wallpaper.FullPhotoUrl));
                    }
                    catch (Exception ex)
                    {
                    }
                }
                else
                {
                }

                // Handle AI tag exactly as in LatestWallpapersPage.SetItemMetadata
                AITagBorder.Visibility = wallpaper.IsAI ? Visibility.Visible : Visibility.Collapsed;
                if (wallpaper.IsAI)
                {
                    AIImage.Source = new BitmapImage(new Uri("ms-appx:///Assets/aigenerated-icon.png"));
                }

                // Handle quality tag exactly as in LatestWallpapersPage.SetItemMetadata
                if (!string.IsNullOrEmpty(wallpaper.QualityTag))
                {
                    QualityTagBorder.Visibility = Visibility.Visible;
                    // Set the quality image source
                    string qualityImagePath = wallpaper.QualityLogoPath;

                    if (!string.IsNullOrEmpty(qualityImagePath))
                    {
                        try
                        {
                            QualityImage.Source = new BitmapImage(new Uri(qualityImagePath));
                        }
                        catch (Exception ex)
                        {
                            // Log error and keep the quality tag hidden
                            QualityTagBorder.Visibility = Visibility.Collapsed;
                        }
                    }
                    else
                    {
                        // No quality logo path available
                        QualityTagBorder.Visibility = Visibility.Collapsed;
                    }
                }
                else
                {
                    // No quality tag available
                    QualityTagBorder.Visibility = Visibility.Collapsed;
                }

                // Fetch and display the publisher details
                if (int.TryParse(wallpaper.Id, out int wallpaperId))
                {
                    LoadPublisherDetailsAsync(wallpaperId).ConfigureAwait(false);
                }
                else
                {
                }
            }
            else
            {
                TitleTextBlock.Text = "Error: Invalid wallpaper data";
                WallpaperImage.Source = new BitmapImage(new Uri("ms-appx:///Assets/placeholder-wallpaper-1000.png"));
                // Ensure all overlays are hidden/reset in error case
                AITagBorder.Visibility = Visibility.Collapsed;
                QualityTagBorder.Visibility = Visibility.Collapsed;
            }
        }

        private async Task LoadPublisherDetailsAsync(int wallpaperId)
        {
            try
            {
                string apiUrl = $"{DetailApiBaseUrl}{wallpaperId}";

                HttpResponseMessage response = await _httpClient.GetAsync(apiUrl);

                if (response.IsSuccessStatusCode)
                {
                    string jsonResponse = await response.Content.ReadAsStringAsync();

                    using JsonDocument document = JsonDocument.Parse(jsonResponse);

                    // Extract publisher data and wallpaper source URL
                    if (document.RootElement.TryGetProperty("WallpaperPublisher", out JsonElement publisherElement))
                    {
                        // Check if there's a source URL in the response
                        if (document.RootElement.TryGetProperty("SourceUrl", out JsonElement sourceUrlElement) &&
                            !string.IsNullOrEmpty(sourceUrlElement.GetString()))
                        {
                            string sourceUrl = sourceUrlElement.GetString();
                            // Update the SourceUrl property of the current wallpaper
                            _currentWallpaper.SourceUrl = sourceUrl;
                        }
                        else
                        {
                            // If no source URL in API, create one based on wallpaper ID
                            _currentWallpaper.SourceUrl = $"https://backiee.com/wallpaper/{wallpaperId}";
                        }

                        // Update UI with publisher information on the UI thread
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            UpdatePublisherUI(publisherElement);
                        });
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

        private void UpdatePublisherUI(JsonElement publisherElement)
        {
            try
            {
                // Extract publisher details
                string publisherName = publisherElement.TryGetProperty("Name", out JsonElement nameElement)
                    ? nameElement.GetString() : "Unknown Publisher";

                string gender = publisherElement.TryGetProperty("Gender", out JsonElement genderElement)
                    ? genderElement.GetString() : "Private";

                string age = publisherElement.TryGetProperty("Age", out JsonElement ageElement)
                    ? ageElement.GetString() : "Private";

                string country = publisherElement.TryGetProperty("Country", out JsonElement countryElement)
                    ? countryElement.GetString() : "Private";

                // Publisher profile picture
                string profileImageUrl = publisherElement.TryGetProperty("ProfileImage", out JsonElement profileImageElement)
                    ? profileImageElement.GetString() : "";

                // Statistics
                string uploads = publisherElement.TryGetProperty("Uploads", out JsonElement uploadsElement)
                    ? uploadsElement.GetString() : "0";

                string likes = publisherElement.TryGetProperty("Likes", out JsonElement likesElement)
                    ? likesElement.GetString() : "0";

                string followers = publisherElement.TryGetProperty("Followers", out JsonElement followersElement)
                    ? followersElement.GetString() : "0";

                // Update UI elements with publisher information
                // Name
                var publisherNameElement = FindName("PublisherNameTextBlock") as TextBlock;
                if (publisherNameElement != null)
                {
                    publisherNameElement.Text = publisherName;
                }

                // Publisher info
                var publisherInfoElement = FindName("PublisherInfoTextBlock") as TextBlock;
                if (publisherInfoElement != null)
                {
                    publisherInfoElement.Text = $"Gender: {gender} · Age: {age} · Country: {country}";
                }

                // Statistics
                var uploadsTextBlock = FindName("UploadsTextBlock") as TextBlock;
                if (uploadsTextBlock != null)
                {
                    uploadsTextBlock.Text = uploads;
                }

                var likesTextBlock = FindName("LikesTextBlock") as TextBlock;
                if (likesTextBlock != null)
                {
                    likesTextBlock.Text = likes;
                }

                var followersTextBlock = FindName("FollowersTextBlock") as TextBlock;
                if (followersTextBlock != null)
                {
                    followersTextBlock.Text = followers;
                }

                // Profile image
                if (!string.IsNullOrEmpty(profileImageUrl))
                {
                    try
                    {
                        // Try to find the background of the ellipse
                        var profileEllipse = FindName("PublisherProfileEllipse") as Ellipse;
                        if (profileEllipse != null)
                        {
                            var imageBrush = new ImageBrush
                            {
                                ImageSource = new BitmapImage(new Uri(profileImageUrl)),
                                Stretch = Stretch.UniformToFill
                            };
                            profileEllipse.Fill = imageBrush;
                        }

                        // Hide the wolf silhouette if we have a profile image
                        var wolfPath = FindName("PublisherProfileIcon") as Microsoft.UI.Xaml.Shapes.Path;
                        if (wolfPath != null)
                        {
                            wolfPath.Visibility = Visibility.Collapsed;
                        }
                    }
                    catch (Exception ex)
                    {
                        // Keep the default gradient and silhouette
                    }
                }
            }
            catch (Exception ex)
            {
            }
        }

        private async void SetAsDesktopWallpaperItem_Click(object sender, RoutedEventArgs e)
        {
            await SetWallpaperAsync(WallpaperType.Desktop);
        }

        private async void SetAsLockScreenItem_Click(object sender, RoutedEventArgs e)
        {
            await SetWallpaperAsync(WallpaperType.LockScreen);
        }

        private enum WallpaperType
        {
            Desktop,
            LockScreen
        }

        private async Task SetWallpaperAsync(WallpaperType wallpaperType)
        {
            if (_currentWallpaper == null)
            {
                await ShowErrorDialogAsync($"Failed to set {(wallpaperType == WallpaperType.Desktop ? "desktop wallpaper" : "lock screen")}. No wallpaper available.");
                return;
            }

            try
            {
                // Check if app has permission to access Pictures library
                try
                {
                    var picturesFolder = KnownFolders.PicturesLibrary;
                    // Just accessing this will throw an exception if we don't have permission
                }
                catch (UnauthorizedAccessException)
                {
                    await ShowErrorDialogAsync($"This app doesn't have permission to access your Pictures library. Please grant this permission in Settings.");
                    return;
                }

                // Show loading dialog
                var loadingDialog = new ContentDialog
                {
                    Title = $"Setting {(wallpaperType == WallpaperType.Desktop ? "Desktop Wallpaper" : "Lock Screen")}",
                    Content = $"Please wait while we set your {(wallpaperType == WallpaperType.Desktop ? "desktop wallpaper" : "lock screen")}...",
                    CloseButtonText = "Cancel",
                    XamlRoot = this.XamlRoot
                };

                // Show the dialog
                var dialogTask = loadingDialog.ShowAsync();

                // Start the background task to set wallpaper
                bool success = false;
                string errorMessage = string.Empty;
                string savedFilePath = string.Empty;
                try
                {
                    // Get Pictures folder
                    var picturesFolder = KnownFolders.PicturesLibrary;
                    var wallpapersFolder = await picturesFolder.CreateFolderAsync("Aura", CreationCollisionOption.OpenIfExists);

                    // Get the big thumb URL to extract extension
                    var scraperService = new Aura.Services.AlphaCodersScraperService();
                    var bigThumbUrl = await scraperService.GetBigImageUrlForWallpaperAsync(_currentWallpaper.Id, _currentWallpaper.ImageUrl);

                    if (string.IsNullOrEmpty(bigThumbUrl))
                    {
                        throw new Exception("Could not get image information for this wallpaper.");
                    }

                    // Extract extension from big thumb URL
                    var bigThumbUri = new Uri(bigThumbUrl);
                    var bigThumbPath = bigThumbUri.AbsolutePath;
                    var extension = System.IO.Path.GetExtension(bigThumbPath).TrimStart('.');


                    // Build original URL with same extension
                    var imageId = _currentWallpaper.Id;
                    var uri = new Uri(_currentWallpaper.ImageUrl);
                    var domainParts = uri.Host.Split('.');
                    var domainShort = domainParts[0];

                    var originalUrl = $"https://initiate.alphacoders.com/download/{domainShort}/{imageId}/{extension}";


                    byte[] imageBytes;
                    // Use stable filename based on wallpaper ID (no timestamp to avoid duplicates)
                    string fileExtension = !string.IsNullOrEmpty(extension) ? extension : "jpg";
                    string stableFileName = $"wallpaper-{_currentWallpaper.Id}.{fileExtension}";
                    string stableFilePath2 = System.IO.Path.Combine(wallpapersFolder.Path, stableFileName);

                    StorageFile wallpaperFile;
                    if (System.IO.File.Exists(stableFilePath2))
                    {
                        // Reuse existing file
                        wallpaperFile = await wallpapersFolder.GetFileAsync(stableFileName);
                    }
                    else
                    {
                        try
                        {
                            imageBytes = await _httpClient.GetByteArrayAsync(originalUrl);
                        }
                        catch (Exception ex)
                        {
                            throw new Exception($"Failed to download original image. Error: {ex.Message}");
                        }

                        wallpaperFile = await wallpapersFolder.CreateFileAsync(
                            stableFileName,
                            CreationCollisionOption.ReplaceExisting);

                        using (var stream = await wallpaperFile.OpenStreamForWriteAsync())
                        {
                            await stream.WriteAsync(imageBytes, 0, imageBytes.Length);
                            await stream.FlushAsync();
                        }
                    }

                    savedFilePath = wallpaperFile.Path;

                    // Verify the file exists and has content
                    var fileProperties = await wallpaperFile.GetBasicPropertiesAsync();
                    if (fileProperties.Size == 0)
                    {
                        throw new Exception("Failed to save wallpaper image properly.");
                    }


                    // Try to set the wallpaper or lock screen using WinRT API
                    var userProfilePersonalizationSettings = UserProfilePersonalizationSettings.Current;

                    if (wallpaperType == WallpaperType.Desktop)
                    {
                        // Try multiple methods to set desktop wallpaper
                        try
                        {
                            // Method 1: WinRT API
                            success = await userProfilePersonalizationSettings.TrySetWallpaperImageAsync(wallpaperFile);
                        }
                        catch (Exception ex)
                        {
                            errorMessage = ex.Message;
                        }

                        // If WinRT API fails, try our enhanced WallpaperHelper
                        if (!success)
                        {
                            try
                            {
                                success = WallpaperHelper.SetWallpaper(wallpaperFile.Path);
                            }
                            catch (Exception innerEx)
                            {
                                errorMessage += $" Fallback method also failed: {innerEx.Message}";
                            }
                        }
                    }
                    else // Lock Screen
                    {
                        // Try multiple methods to set lock screen
                        try
                        {
                            // Method 1: WinRT API
                            success = await userProfilePersonalizationSettings.TrySetLockScreenImageAsync(wallpaperFile);
                        }
                        catch (Exception ex)
                        {
                            errorMessage = ex.Message;
                        }

                        // If WinRT API fails, try our enhanced WallpaperHelper
                        if (!success)
                        {
                            try
                            {
                                success = WallpaperHelper.SetLockScreen(wallpaperFile.Path);
                            }
                            catch (Exception innerEx)
                            {
                                errorMessage += $" Fallback method also failed: {innerEx.Message}";
                            }
                        }

                    }
                }
                catch (Exception ex)
                {
                    errorMessage = ex.Message;
                }

                // Hide the loading dialog
                loadingDialog.Hide();

                // Show result to user
                if (success)
                {
                    var wallpaperTypeLabel = wallpaperType == WallpaperType.Desktop ? "Desktop" : "Lock Screen";
                    WallpaperHistoryService.Instance.AddEntry(
                        _currentWallpaper.Title ?? "Unknown",
                        !string.IsNullOrEmpty(savedFilePath) ? savedFilePath : (_currentWallpaper.ImageUrl ?? ""),
                        wallpaperTypeLabel,
                        "Manual");
                    await ShowSuccessDialogAsync($"{(wallpaperType == WallpaperType.Desktop ? "Desktop wallpaper" : "Lock screen")} set successfully!");
                }
                else
                {
                    string message = $"Failed to set {(wallpaperType == WallpaperType.Desktop ? "desktop wallpaper" : "lock screen")}. ";
                    if (!string.IsNullOrEmpty(errorMessage))
                    {
                        message += $"Error: {errorMessage}";
                    }
                    else
                    {
                        message += "This may be due to system restrictions or permissions. Please try again later.";
                    }
                    await ShowErrorDialogAsync(message);
                }
            }
            catch (Exception ex)
            {
                await ShowErrorDialogAsync($"An error occurred: {ex.Message}");
            }
        }

        private async void ViewOnWebButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentWallpaper == null || string.IsNullOrEmpty(_currentWallpaper.SourceUrl))
            {
                // Show error message
                await ShowErrorDialogAsync("Cannot view on web: Source URL is missing");
                return;
            }

            try
            {
                // Launch the default browser with the wallpaper's source URL
                Uri sourceUri = new Uri(_currentWallpaper.SourceUrl);
                bool success = await Windows.System.Launcher.LaunchUriAsync(sourceUri);

                if (!success)
                {
                    await ShowErrorDialogAsync("Failed to open browser");
                }
            }
            catch (Exception ex)
            {
                await ShowErrorDialogAsync("Error opening source URL");
            }
        }

        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentWallpaper == null)
            {
                await ShowErrorDialogAsync("No wallpaper available to download.");
                return;
            }

            try
            {
                // Show progress dialog
                var progressDialog = new ContentDialog
                {
                    Title = "Downloading Wallpaper",
                    Content = "Please wait while your wallpaper downloads...",
                    CloseButtonText = "Cancel",
                    XamlRoot = this.XamlRoot
                };

                // Show the dialog
                var dialogTask = progressDialog.ShowAsync();

                try
                {
                    // Get Downloads folder using StorageFolder.GetFolderFromPathAsync
                    string downloadsPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads";
                    var downloadsFolder = await StorageFolder.GetFolderFromPathAsync(downloadsPath);

                    // Create a subfolder for our app if it doesn't exist
                    var appFolder = await downloadsFolder.CreateFolderAsync("Aura", CreationCollisionOption.OpenIfExists);

                    // Get the correct extension from the big thumb URL (will be set below)
                    var scraperService = new Aura.Services.AlphaCodersScraperService();
                    var bigThumbUrl = await scraperService.GetBigImageUrlForWallpaperAsync(_currentWallpaper.Id, _currentWallpaper.ImageUrl);

                    if (string.IsNullOrEmpty(bigThumbUrl))
                    {
                        throw new Exception("Could not get image information for this wallpaper.");
                    }

                    // Extract extension from big thumb URL for filename
                    var bigThumbUri = new Uri(bigThumbUrl);
                    var bigThumbPath = bigThumbUri.AbsolutePath;
                    var correctExtension = System.IO.Path.GetExtension(bigThumbPath).TrimStart('.');

                    // Create a unique filename based on wallpaper title and ID with correct extension
                    string safeFileName = _currentWallpaper.Title.Replace(" ", "_");
                    safeFileName = string.Join("_", safeFileName.Split(System.IO.Path.GetInvalidFileNameChars()));
                    var fileName = $"{safeFileName}_{_currentWallpaper.Id}.{correctExtension}";


                    // Create the file in the downloads folder
                    var downloadFile = await appFolder.CreateFileAsync(fileName, CreationCollisionOption.GenerateUniqueName);

                    // Build original URL with same extension as determined above
                    var imageId = _currentWallpaper.Id;
                    var uri = new Uri(_currentWallpaper.ImageUrl);
                    var domainParts = uri.Host.Split('.');
                    var domainShort = domainParts[0];

                    var originalUrl = $"https://initiate.alphacoders.com/download/{domainShort}/{imageId}/{correctExtension}";


                    byte[] imageBytes;
                    try
                    {
                        imageBytes = await _httpClient.GetByteArrayAsync(originalUrl);
                    }
                    catch (Exception ex)
                    {
                        throw new Exception($"Failed to download original image. Error: {ex.Message}");
                    }
                    using (var stream = await downloadFile.OpenStreamForWriteAsync())
                    {
                        await stream.WriteAsync(imageBytes, 0, imageBytes.Length);
                    }

                    // Hide progress dialog
                    progressDialog.Hide();

                    // Show success message with the path
                    await ShowSuccessDialogAsync($"Wallpaper downloaded to:\n{downloadFile.Path}");
                }
                catch (Exception ex)
                {
                    // Hide progress dialog
                    progressDialog.Hide();

                    await ShowErrorDialogAsync($"Error downloading wallpaper: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                await ShowErrorDialogAsync($"An error occurred: {ex.Message}");
            }
        }

        // Helper to try to get current window via reflection
        private Window GetCurrentWindowViaReflection()
        {
            try
            {
                // Try to get the current window
                var app = Application.Current;
                if (app != null)
                {
                    // Try via m_window field (private in App class)
                    var windowField = app.GetType().GetField("m_window",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic);

                    if (windowField != null)
                    {
                        return windowField.GetValue(app) as Window;
                    }

                    // Alternatively try MainWindow property
                    var mainWindowProperty = app.GetType().GetProperty("MainWindow");
                    if (mainWindowProperty != null)
                    {
                        return mainWindowProperty.GetValue(app) as Window;
                    }
                }
            }
            catch (Exception ex)
            {
            }

            return null;
        }

        private async Task ShowSuccessDialogAsync(string message)
        {
            var dialog = new ContentDialog
            {
                Title = "Success",
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };

            await dialog.ShowAsync();
        }

        private async Task ShowErrorDialogAsync(string message)
        {
            var dialog = new ContentDialog
            {
                Title = "Error",
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };

            await dialog.ShowAsync();
        }

        private bool IsPackaged()
        {
            try
            {
                return Package.Current != null;
            }
            catch
            {
                return false;
            }
        }
    }
}
