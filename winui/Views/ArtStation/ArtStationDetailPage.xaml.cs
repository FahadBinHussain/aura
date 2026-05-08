using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Aura.Models;
using Aura.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage;
using Windows.System.UserProfile;

namespace Aura.Views.ArtStation
{
    public sealed partial class ArtStationDetailPage : Page
    {
        private readonly ArtStationService _artStationService = new();
        private WallpaperItem? _currentArtwork;

        public ArtStationDetailPage()
        {
            InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is not WallpaperItem artwork)
            {
                ShowStatus("Invalid ArtStation artwork data.", InfoBarSeverity.Error);
                return;
            }

            _currentArtwork = artwork;
            await LoadArtworkAsync();
        }

        private async Task LoadArtworkAsync()
        {
            if (_currentArtwork == null)
            {
                return;
            }

            try
            {
                if (string.IsNullOrWhiteSpace(_currentArtwork.FullPhotoUrl) ||
                    _currentArtwork.FullPhotoUrl == _currentArtwork.ImageUrl)
                {
                    _currentArtwork = await _artStationService.GetProjectDetailsAsync(_currentArtwork);
                }

                TitleTextBlock.Text = _currentArtwork.Title;
                DescriptionTextBlock.Text = _currentArtwork.Description;
                LikesTextBlock.Text = _currentArtwork.Likes;
                ViewsTextBlock.Text = _currentArtwork.Downloads;
                ResolutionTextBlock.Text = _currentArtwork.Resolution;

                var imageUrl = GetBestImageUrl();
                if (!string.IsNullOrWhiteSpace(imageUrl))
                {
                    ArtworkImage.Source = new BitmapImage(new Uri(imageUrl));
                }
            }
            catch (Exception ex)
            {
                ShowStatus($"Could not load ArtStation artwork: {ex.Message}", InfoBarSeverity.Error);
            }
        }

        private async void SetAsDesktopWallpaperItem_Click(object sender, RoutedEventArgs e)
        {
            await SetWallpaperAsync(isLockScreen: false);
        }

        private async void SetAsLockScreenItem_Click(object sender, RoutedEventArgs e)
        {
            await SetWallpaperAsync(isLockScreen: true);
        }

        private async Task SetWallpaperAsync(bool isLockScreen)
        {
            if (_currentArtwork == null)
            {
                ShowStatus("No ArtStation artwork is loaded.", InfoBarSeverity.Error);
                return;
            }

            try
            {
                var imageUrl = GetBestImageUrl();
                if (string.IsNullOrWhiteSpace(imageUrl))
                {
                    ShowStatus("No downloadable image URL is available for this artwork.", InfoBarSeverity.Error);
                    return;
                }

                var file = await SaveImageToFolderAsync(ApplicationData.Current.LocalFolder, imageUrl);
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
                        _currentArtwork.Title,
                        file.Path,
                        isLockScreen ? "Lock Screen" : "Desktop",
                        "ArtStation");

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
        }

        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentArtwork == null)
            {
                ShowStatus("No ArtStation artwork is loaded.", InfoBarSeverity.Error);
                return;
            }

            try
            {
                var imageUrl = GetBestImageUrl();
                if (string.IsNullOrWhiteSpace(imageUrl))
                {
                    ShowStatus("No downloadable image URL is available for this artwork.", InfoBarSeverity.Error);
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
        }

        private async void ViewOnWebButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentArtwork == null || string.IsNullOrWhiteSpace(_currentArtwork.SourceUrl))
            {
                ShowStatus("This artwork does not have a source URL.", InfoBarSeverity.Error);
                return;
            }

            await Windows.System.Launcher.LaunchUriAsync(new Uri(_currentArtwork.SourceUrl));
        }

        private async Task<StorageFile> SaveImageToFolderAsync(
            StorageFolder folder,
            string imageUrl,
            CreationCollisionOption collisionOption = CreationCollisionOption.ReplaceExisting)
        {
            if (_currentArtwork == null)
            {
                throw new InvalidOperationException("No artwork is loaded.");
            }

            var extension = GetImageExtension(imageUrl);
            var fileName = $"{SanitizeFileName(_currentArtwork.Title)}_{_currentArtwork.Id}.{extension}";
            var file = await folder.CreateFileAsync(fileName, collisionOption);
            var bytes = await _artStationService.GetImageBytesAsync(imageUrl);
            await FileIO.WriteBytesAsync(file, bytes);
            return file;
        }

        private string GetBestImageUrl()
        {
            if (_currentArtwork == null)
            {
                return string.Empty;
            }

            return !string.IsNullOrWhiteSpace(_currentArtwork.FullPhotoUrl)
                ? _currentArtwork.FullPhotoUrl
                : _currentArtwork.ImageUrl;
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
            var safeName = new string(value.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray()).Trim();
            return string.IsNullOrWhiteSpace(safeName) ? "artstation-artwork" : safeName;
        }

        private void ShowStatus(string message, InfoBarSeverity severity)
        {
            StatusInfoBar.Message = message;
            StatusInfoBar.Severity = severity;
            StatusInfoBar.IsOpen = true;
        }
    }
}
