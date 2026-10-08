using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Aura.Models;
using Aura.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage;
using Windows.System.UserProfile;

namespace Aura.Views.PublicSources
{
    public sealed partial class PublicWallpaperDetailPage : Page
    {
        private readonly PublicWallpaperService _wallpaperService = new();
        private string _platformName = "Wallpaper";
        private WallpaperItem? _wallpaper;

        public PublicWallpaperDetailPage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is PublicWallpaperNavigationParameter parameter && parameter.Wallpaper != null)
            {
                _platformName = parameter.PlatformName;
                _wallpaper = parameter.Wallpaper;
                RenderWallpaper();
                return;
            }

            ShowStatus("Invalid wallpaper data.", InfoBarSeverity.Error);
        }

        private void RenderWallpaper()
        {
            if (_wallpaper == null)
            {
                return;
            }

            PlatformTextBlock.Text = _platformName;
            TitleTextBlock.Text = _wallpaper.Title;
            DescriptionTextBlock.Text = _wallpaper.Description;
            ResolutionTextBlock.Text = string.IsNullOrWhiteSpace(_wallpaper.Resolution) ? "-" : _wallpaper.Resolution;
            LikesTextBlock.Text = string.IsNullOrWhiteSpace(_wallpaper.Likes) ? "-" : _wallpaper.Likes;
            DownloadsTextBlock.Text = string.IsNullOrWhiteSpace(_wallpaper.Downloads) ? "-" : _wallpaper.Downloads;

            var imageUrl = GetBestImageUrl();
            if (!string.IsNullOrWhiteSpace(imageUrl))
            {
                WallpaperImage.Source = new BitmapImage(new Uri(imageUrl));
            }

            // artgram: list pages only carry the presigned 512x512 cover - upgrade to the
            // original (the artworks/ url exists only on the art's own detail page). the
            // mutation below feeds GetBestImageUrl, so render, set, and download all flip
            // to full res together (automata-private/www.artgram.co/AGENTS.md)
            if (_wallpaper != null &&
                !string.IsNullOrEmpty(_wallpaper.SourceUrl) &&
                _wallpaper.SourceUrl.Contains("artgram.co/a/", StringComparison.OrdinalIgnoreCase))
            {
                _ = UpgradeArtgramImageAsync();
            }
        }

        // the artworks/ url is presigned with a 1h expiry and minted by THIS fetch, so it is
        // always fresh on success; a failure keeps the cover and says so loudly in the InfoBar
        private async Task UpgradeArtgramImageAsync()
        {
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Aura/1.0");
                var html = await client.GetStringAsync(_wallpaper!.SourceUrl);
                var match = Regex.Match(
                    html,
                    "https://fsn1\\.your-objectstorage\\.com/artgram-media/artworks/[^\"'\\s]+",
                    RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var fullUrl = WebUtility.HtmlDecode(match.Value);
                    _wallpaper.FullPhotoUrl = fullUrl;
                    WallpaperImage.Source = new BitmapImage(new Uri(fullUrl));
                    ShowStatus("Artgram original image loaded.", InfoBarSeverity.Success);
                }
                else
                {
                    ShowStatus("Artgram: original not found on the detail page - showing the 512px cover.", InfoBarSeverity.Warning);
                }
            }
            catch (Exception ex)
            {
                ShowStatus($"Artgram: full-res fetch failed ({ex.Message}) - showing the 512px cover.", InfoBarSeverity.Warning);
            }
        }

        private async void SetAsDesktopWallpaperButton_Click(object sender, RoutedEventArgs e)
        {
            await SetWallpaperAsync(isLockScreen: false);
        }

        private async void SetAsLockScreenButton_Click(object sender, RoutedEventArgs e)
        {
            await SetWallpaperAsync(isLockScreen: true);
        }

        private async Task SetWallpaperAsync(bool isLockScreen)
        {
            if (_wallpaper == null)
            {
                ShowStatus("No wallpaper is loaded.", InfoBarSeverity.Error);
                return;
            }

            try
            {
                SetBusy(true);
                var imageUrl = GetBestImageUrl();
                if (string.IsNullOrWhiteSpace(imageUrl))
                {
                    ShowStatus("No downloadable image URL is available.", InfoBarSeverity.Error);
                    return;
                }

                var cacheFolder = await LocalFileStorageService.GetWallpaperCacheFolderAsync();
                var file = await SaveImageToFolderAsync(cacheFolder, imageUrl);
                var settings = UserProfilePersonalizationSettings.Current;
                var success = isLockScreen
                    ? await settings.TrySetLockScreenImageAsync(file)
                    : await settings.TrySetWallpaperImageAsync(file);

                if (!success && !isLockScreen)
                {
                    success = Aura.Views.AlphaCoders.WallpaperHelper.SetWallpaper(file.Path);
                }
                else if (!success && isLockScreen)
                {
                    success = Aura.Views.AlphaCoders.WallpaperHelper.SetLockScreen(file.Path);
                }

                if (success)
                {
                    WallpaperHistoryService.Instance.AddEntry(
                        _wallpaper.Title,
                        file.Path,
                        isLockScreen ? "Lock Screen" : "Desktop",
                        _platformName);

                    ShowStatus($"{(isLockScreen ? "Lock screen" : "Desktop wallpaper")} set successfully.", InfoBarSeverity.Success);
                }
                else
                {
                    ShowStatus("Windows did not accept this image as a wallpaper.", InfoBarSeverity.Error);
                }
            }
            catch (Exception ex)
            {
                ShowStatus($"Failed to set wallpaper: {ex.Message}", InfoBarSeverity.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (_wallpaper == null)
            {
                ShowStatus("No wallpaper is loaded.", InfoBarSeverity.Error);
                return;
            }

            try
            {
                SetBusy(true);
                var imageUrl = GetBestImageUrl();
                if (string.IsNullOrWhiteSpace(imageUrl))
                {
                    ShowStatus("No downloadable image URL is available.", InfoBarSeverity.Error);
                    return;
                }

                var downloadsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Downloads");
                var downloadsFolder = await StorageFolder.GetFolderFromPathAsync(downloadsPath);
                var appFolder = await downloadsFolder.CreateFolderAsync("Aura", CreationCollisionOption.OpenIfExists);
                var file = await SaveImageToFolderAsync(appFolder, imageUrl, CreationCollisionOption.GenerateUniqueName);

                ShowStatus($"Downloaded to {file.Path}", InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                ShowStatus($"Download failed: {ex.Message}", InfoBarSeverity.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void ViewOnWebButton_Click(object sender, RoutedEventArgs e)
        {
            if (_wallpaper == null || string.IsNullOrWhiteSpace(_wallpaper.SourceUrl))
            {
                ShowStatus("This wallpaper does not have a source URL.", InfoBarSeverity.Error);
                return;
            }

            await Windows.System.Launcher.LaunchUriAsync(new Uri(_wallpaper.SourceUrl));
        }

        private async Task<StorageFile> SaveImageToFolderAsync(
            StorageFolder folder,
            string imageUrl,
            CreationCollisionOption collisionOption = CreationCollisionOption.ReplaceExisting)
        {
            if (_wallpaper == null)
            {
                throw new InvalidOperationException("No wallpaper is loaded.");
            }

            var extension = GetImageExtension(imageUrl);
            var fileName = $"{SanitizeFileName(_wallpaper.Title)}_{SanitizeFileName(_wallpaper.Id)}.{extension}";
            var file = await folder.CreateFileAsync(fileName, collisionOption);
            var bytes = await _wallpaperService.GetImageBytesAsync(imageUrl);
            await FileIO.WriteBytesAsync(file, bytes);
            return file;
        }

        private string GetBestImageUrl()
        {
            if (_wallpaper == null)
            {
                return string.Empty;
            }

            return !string.IsNullOrWhiteSpace(_wallpaper.FullPhotoUrl)
                ? _wallpaper.FullPhotoUrl
                : _wallpaper.ImageUrl;
        }

        private static string GetImageExtension(string imageUrl)
        {
            if (Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri))
            {
                var extension = Path.GetExtension(uri.AbsolutePath).TrimStart('.').ToLowerInvariant();
                if (!string.IsNullOrWhiteSpace(extension))
                {
                    return extension;
                }
            }

            return "jpg";
        }

        private static string SanitizeFileName(string value)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var safeName = new string((value ?? string.Empty).Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray()).Trim();
            return string.IsNullOrWhiteSpace(safeName) ? "wallpaper" : safeName;
        }

        private void SetBusy(bool isBusy)
        {
            LoadingProgressBar.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
            SetWallpaperButton.IsEnabled = !isBusy;
            SetLockScreenButton.IsEnabled = !isBusy;
            DownloadButton.IsEnabled = !isBusy;
            ViewOnWebButton.IsEnabled = !isBusy;
        }

        private void ShowStatus(string message, InfoBarSeverity severity)
        {
            StatusInfoBar.Message = message;
            StatusInfoBar.Severity = severity;
            StatusInfoBar.IsOpen = true;
        }
    }
}
