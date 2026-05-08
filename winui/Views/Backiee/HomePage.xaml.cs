using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Animation;
using Aura.Models;
using Aura.Services;

namespace Aura.Views.Backiee
{
    public sealed partial class HomePage : Page
    {
        private ObservableCollection<Wallpaper> _dailyWallpapers;

        // Fields for banner rotation
        private List<WallpaperItem> _latestBannerWallpapers = new List<WallpaperItem>();
        private int _currentBannerIndex = -1;
        private DispatcherTimer _bannerTimer;

        // API URL
        private const string BannerApiUrl = "https://backiee.com/api/wallpaper/list.php?action=paging_list&list_type=latest&page=0&page_size=30&category=all&is_ai=all&sort_by=popularity&4k=false&5k=false&8k=false&status=active&args=";
        private const string FeaturePoolApiUrl = "https://backiee.com/api/wallpaper/list.php?action=paging_list&list_type=latest&page=0&page_size=60&category=all&is_ai=all&sort_by=popularity&4k=false&5k=false&8k=false&status=active&args=";
        private const string UltraHdApiUrl = "https://backiee.com/api/wallpaper/list.php?action=paging_list&list_type=latest&page=0&page_size=12&category=all&is_ai=all&sort_by=popularity&4k=false&5k=false&8k=true&status=active&args=";
        private const string PlaceholderImageUri = "ms-appx:///Assets/placeholder-wallpaper-1000.png";

        public HomePage()
        {
            this.InitializeComponent();
            _dailyWallpapers = new ObservableCollection<Wallpaper>();

            Loaded += HomePage_Loaded;
            Unloaded += HomePage_Unloaded;
        }

        private async void HomePage_Loaded(object sender, RoutedEventArgs e)
        {
            DailyWallpapersGridView.ItemsSource = _dailyWallpapers;

            // Seed placeholders first, then replace every visible section with live Backiee data.
            LoadStaticBannerImages();

            await Task.WhenAll(
                LoadFeatureBannerImagesAsync(),
                LoadDailyPopularWallpapersAsync(),
                LoadLatestBannerWallpapersAsync());
        }

        private void HomePage_Unloaded(object sender, RoutedEventArgs e)
        {
            // Stop the timer when the page is unloaded
            _bannerTimer?.Stop();
        }

        private void LoadStaticBannerImages()
        {
            try
            {
                UltraHdImage.Source = new BitmapImage(new Uri(PlaceholderImageUri));
                AiGeneratedImage.Source = new BitmapImage(new Uri(PlaceholderImageUri));
            }
            catch
            {
                // Use placeholder if image loading fails
                UltraHdImage.Source = new BitmapImage(new Uri("ms-appx:///Assets/StoreLogo.png"));
                AiGeneratedImage.Source = new BitmapImage(new Uri("ms-appx:///Assets/StoreLogo.png"));
            }
        }

        private async Task LoadFeatureBannerImagesAsync()
        {
            try
            {
                var ultraHdTask = LoadFirstWallpaperAsync(UltraHdApiUrl);
                var aiTask = LoadFirstWallpaperAsync(FeaturePoolApiUrl, wallpaper => wallpaper.IsAI);

                var ultraHdWallpaper = await ultraHdTask;
                var aiWallpaper = await aiTask;

                if (!string.IsNullOrWhiteSpace(ultraHdWallpaper?.ImageUrl))
                {
                    UltraHdImage.Source = new BitmapImage(new Uri(ultraHdWallpaper.ImageUrl));
                }

                if (!string.IsNullOrWhiteSpace(aiWallpaper?.ImageUrl))
                {
                    AiGeneratedImage.Source = new BitmapImage(new Uri(aiWallpaper.ImageUrl));
                }
            }
            catch
            {
                // Keep the local placeholders visible if Backiee is temporarily unavailable.
            }
        }

        private async Task LoadLatestBannerWallpapersAsync()
        {
            try
            {
                string jsonContent = await BackieeNetworkClient.GetStringAsync(BannerApiUrl);
                if (!string.IsNullOrWhiteSpace(jsonContent))
                {
                    using (JsonDocument doc = JsonDocument.Parse(jsonContent))
                    {
                        List<WallpaperItem> fetchedWallpapers = new List<WallpaperItem>();
                        foreach (JsonElement wallpaperElement in doc.RootElement.EnumerateArray())
                        {
                            try
                            {
                                var wallpaper = BackieeApiParser.CreateWallpaperItem(
                                    wallpaperElement,
                                    preferredImageProperty: "MediumPhotoUrl");
                                if (!string.IsNullOrEmpty(wallpaper.ImageUrl))
                                {
                                    fetchedWallpapers.Add(wallpaper);
                                }
                            }
                            catch
                            {
                            }
                        }

                        if (fetchedWallpapers.Count > 0)
                        {
                            // Select 5 random wallpapers (or fewer if less than 5 available)
                            Random random = new Random();
                            _latestBannerWallpapers = fetchedWallpapers.OrderBy(x => random.Next()).Take(5).ToList();

                            // Start the timer if we have wallpapers
                            if (_latestBannerWallpapers.Count > 0)
                            {
                                SetupBannerTimer();
                                // Initial display
                                UpdateBannerContent();
                            }
                        }
                    }
                }
            }
            catch
            {
                // Optionally load a default banner image on failure
                LatestBannerImage.Source = new BitmapImage(new Uri(PlaceholderImageUri));
            }
        }

        private void SetupBannerTimer()
        {
            if (_bannerTimer == null)
            {
                _bannerTimer = new DispatcherTimer();
                _bannerTimer.Interval = TimeSpan.FromSeconds(5); // Change interval as needed
                _bannerTimer.Tick += BannerTimer_Tick;
            }
            _bannerTimer.Start();
        }

        private void BannerTimer_Tick(object sender, object e)
        {
            // Trigger fade-out animation before changing content
            FadeOutBanner.Begin();
        }

        private void FadeOutBanner_Completed(object sender, object e)
        {
             // Update content after fade-out completes
            UpdateBannerContent();
            // Trigger fade-in animation
            FadeInBanner.Begin();
        }

        private void UpdateBannerContent()
        {
             if (_latestBannerWallpapers == null || _latestBannerWallpapers.Count == 0) return;

            _currentBannerIndex = (_currentBannerIndex + 1) % _latestBannerWallpapers.Count;
            var currentWallpaper = _latestBannerWallpapers[_currentBannerIndex];

            LatestBannerImage.Source = new BitmapImage(new Uri(currentWallpaper.ImageUrl));
        }

        private async Task LoadDailyPopularWallpapersAsync()
        {
            try
            {
                var wallpapers = await FetchWallpapersAsync(FeaturePoolApiUrl);
                var popularWallpapers = wallpapers
                    .OrderByDescending(wallpaper => ParseMetric(wallpaper.Likes) + ParseMetric(wallpaper.Downloads))
                    .Take(12)
                    .Select(ToHomeWallpaper)
                    .ToList();

                _dailyWallpapers.Clear();
                foreach (var wallpaper in popularWallpapers)
                {
                    _dailyWallpapers.Add(wallpaper);
                }
            }
            catch
            {
                _dailyWallpapers.Clear();
            }
        }

        private static async Task<WallpaperItem?> LoadFirstWallpaperAsync(
            string apiUrl,
            Func<WallpaperItem, bool>? predicate = null)
        {
            var wallpapers = await FetchWallpapersAsync(apiUrl);
            return predicate == null
                ? wallpapers.FirstOrDefault()
                : wallpapers.FirstOrDefault(predicate);
        }

        private static async Task<List<WallpaperItem>> FetchWallpapersAsync(string apiUrl)
        {
            string jsonContent = await BackieeNetworkClient.GetStringAsync(apiUrl);
            var wallpapers = new List<WallpaperItem>();

            if (string.IsNullOrWhiteSpace(jsonContent))
            {
                return wallpapers;
            }

            using var doc = JsonDocument.Parse(jsonContent);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return wallpapers;
            }

            foreach (JsonElement wallpaperElement in doc.RootElement.EnumerateArray())
            {
                try
                {
                    var wallpaper = BackieeApiParser.CreateWallpaperItem(
                        wallpaperElement,
                        preferredImageProperty: "MediumPhotoUrl");

                    if (!string.IsNullOrWhiteSpace(wallpaper.Id) &&
                        !string.IsNullOrWhiteSpace(wallpaper.ImageUrl))
                    {
                        wallpapers.Add(wallpaper);
                    }
                }
                catch
                {
                }
            }

            return wallpapers;
        }

        private static Wallpaper ToHomeWallpaper(WallpaperItem wallpaper)
        {
            return new Wallpaper
            {
                Id = wallpaper.Id,
                Title = wallpaper.Title,
                ImagePath = wallpaper.ImageUrl,
                Resolution = wallpaper.Resolution,
                IsAIGenerated = wallpaper.IsAI,
                Likes = ParseMetric(wallpaper.Likes),
                Downloads = ParseMetric(wallpaper.Downloads),
                Category = string.IsNullOrWhiteSpace(wallpaper.QualityTag) ? "Backiee" : wallpaper.QualityTag,
                DateAdded = DateTime.Now
            };
        }

        private static int ParseMetric(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return 0;
            }

            var normalized = value.Trim().Replace(",", string.Empty);
            double multiplier = 1;

            if (normalized.EndsWith("K", StringComparison.OrdinalIgnoreCase))
            {
                multiplier = 1_000;
                normalized = normalized[..^1];
            }
            else if (normalized.EndsWith("M", StringComparison.OrdinalIgnoreCase))
            {
                multiplier = 1_000_000;
                normalized = normalized[..^1];
            }

            return double.TryParse(normalized, out var number)
                ? (int)Math.Round(number * multiplier)
                : 0;
        }

        private void LatestWallpapers_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            // Navigate to the Latest Wallpapers page
            this.Frame.Navigate(typeof(LatestWallpapersPage));
        }

        private void DailyWallpapersWrapGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is ItemsWrapGrid wrapGrid)
            {
                // Calculate number of columns based on available width
                double availableWidth = e.NewSize.Width;

                // Determine desired item width (considering margins)
                double desiredItemWidth = 280;  // Base item width
                double itemMargin = 4;          // Total margin between items

                // Calculate how many items can fit in the available width
                int columnsCount = Math.Max(1, (int)(availableWidth / (desiredItemWidth + itemMargin)));

                // Ensure we have a reasonable column count based on screen size
                columnsCount = Math.Min(columnsCount, 6);  // Limit to maximum 6 columns

                // Set the maximum columns
                wrapGrid.MaximumRowsOrColumns = columnsCount;

                // Calculate the new item width to fill the available space evenly
                // Accounting for margins between items
                double totalMarginWidth = (columnsCount - 1) * itemMargin;
                double newItemWidth = (availableWidth - totalMarginWidth) / columnsCount;

                // Ensure the width is not too small to maintain quality
                double finalWidth = Math.Max(180, newItemWidth);

                // Calculate proportional height based on typical wallpaper aspect ratio (16:9)
                double aspectRatio = 16.0 / 9.0;
                double finalHeight = finalWidth / aspectRatio;

                // Set item dimensions - use only the image height since stats are overlaid
                wrapGrid.ItemWidth = finalWidth;
                wrapGrid.ItemHeight = finalHeight; // Remove the +50 for info panel

                // Ensure alignment stretches to use all available space
                wrapGrid.HorizontalAlignment = HorizontalAlignment.Stretch;
            }
        }
    }
}
