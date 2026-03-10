using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Animation;
using Aura.Models;
using Aura.Services;

namespace Aura.Views.Backiee
{
    public sealed partial class HomePage : Page
    {
        private WallpaperService _wallpaperService;
        private ObservableCollection<Wallpaper> _dailyWallpapers;

        // Fields for banner rotation
        private readonly HttpClient _httpClient = new HttpClient();
        private List<WallpaperItem> _latestBannerWallpapers = new List<WallpaperItem>();
        private int _currentBannerIndex = -1;
        private DispatcherTimer _bannerTimer;

        // API URL
        private const string BannerApiUrl = "https://backiee.com/api/wallpaper/list.php?action=paging_list&list_type=latest&page=0&page_size=30&category=all&is_ai=all&sort_by=popularity&4k=false&5k=false&8k=false&status=active&args=";

        public HomePage()
        {
            this.InitializeComponent();
            _wallpaperService = new WallpaperService();
            _dailyWallpapers = new ObservableCollection<Wallpaper>();

            Loaded += HomePage_Loaded;
            Unloaded += HomePage_Unloaded;
        }

        private async void HomePage_Loaded(object sender, RoutedEventArgs e)
        {
            // Load static banners first (UltraHD, AI)
            LoadStaticBannerImages();
            // Load daily popular wallpapers
            LoadDailyPopularWallpapers();
            // Asynchronously load and start rotating the main banner
            await LoadLatestBannerWallpapersAsync();
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
                // Only load static banners here
                UltraHdImage.Source = new BitmapImage(new Uri("ms-appx:///Assets/placeholder-dark.png"));
                AiGeneratedImage.Source = new BitmapImage(new Uri("ms-appx:///Assets/placeholder-dark.png"));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading static banner images: {ex.Message}");
                // Use placeholder if image loading fails
                UltraHdImage.Source = new BitmapImage(new Uri("ms-appx:///Assets/StoreLogo.png"));
                AiGeneratedImage.Source = new BitmapImage(new Uri("ms-appx:///Assets/StoreLogo.png"));
            }
        }

        private async Task LoadLatestBannerWallpapersAsync()
        {
            try
            {
                HttpResponseMessage response = await _httpClient.GetAsync(BannerApiUrl);
                if (response.IsSuccessStatusCode)
                {
                    string jsonContent = await response.Content.ReadAsStringAsync();
                    using (JsonDocument doc = JsonDocument.Parse(jsonContent))
                    {
                        List<WallpaperItem> fetchedWallpapers = new List<WallpaperItem>();
                        foreach (JsonElement wallpaperElement in doc.RootElement.EnumerateArray())
                        {
                            try
                            {
                                // Use MediumPhotoUrl for potentially better quality in banner
                                string imageUrl = wallpaperElement.TryGetProperty("MediumPhotoUrl", out var urlProp) && urlProp.ValueKind == JsonValueKind.String
                                                  ? urlProp.GetString()
                                                  : wallpaperElement.GetProperty("MiniPhotoUrl").GetString(); // Fallback

                                var wallpaper = new WallpaperItem
                                {
                                    Id = wallpaperElement.GetProperty("ID").GetString(),
                                    Title = wallpaperElement.GetProperty("Title").GetString(),
                                    ImageUrl = imageUrl, // Store URL
                                    // Extract description if available, fallback to title
                                    Description = wallpaperElement.TryGetProperty("Description", out var descProp) && descProp.ValueKind == JsonValueKind.String
                                                  ? descProp.GetString()
                                                  : wallpaperElement.GetProperty("Title").GetString(),
                                };
                                fetchedWallpapers.Add(wallpaper);
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Error parsing wallpaper item: {ex.Message}");
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
                else
                {
                     System.Diagnostics.Debug.WriteLine($"API request failed: {response.StatusCode}");
                     // Optionally load a default banner image on failure
                     LatestBannerImage.Source = new BitmapImage(new Uri("ms-appx:///Assets/placeholder-dark.png"));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading latest banner wallpapers: {ex.Message}");
                // Optionally load a default banner image on failure
                LatestBannerImage.Source = new BitmapImage(new Uri("ms-appx:///Assets/placeholder-dark.png"));
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

        private void LoadDailyPopularWallpapers()
        {
            // Get daily popular wallpapers from service
            var wallpapers = _wallpaperService.GetDailyPopularWallpapers();

            // Clear existing collection
            _dailyWallpapers.Clear();

            // Add wallpapers to collection
            foreach (var wallpaper in wallpapers)
            {
                _dailyWallpapers.Add(wallpaper);
            }

            // Set up the GridView
            DailyWallpapersGridView.ItemsSource = _dailyWallpapers;
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
