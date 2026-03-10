using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using WallYouNeed.Core.Models;
using WallYouNeed.Core.Services.Interfaces;
using System.Net.Http;
using Microsoft.Win32;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace WallYouNeed.App.Pages
{
    /// <summary>
    /// Interaction logic for LatestWallpapersPage.xaml
    /// </summary>
    public partial class LatestWallpapersPage : Page
    {
        private readonly ILogger<LatestWallpapersPage> _logger;
        private readonly ISettingsService _settingsService;
        private ObservableCollection<WallpaperItem> _wallpapers;
        private double _itemWidth = 300; // Default width for each wallpaper item
        private double _itemHeight = 180; // Default height for each wallpaper item
        private const int ScrollThreshold = 600; // Increased threshold for preemptive loading
        
        // Variables for JSON loading and infinite scrolling
        private HashSet<string> _loadedUrls = new HashSet<string>();
        private HashSet<int> _attemptedIds = new HashSet<int>();
        private int _currentImageId = -1; // Will be initialized properly after loading JSON
        private DateTime _lastScrollCheck = DateTime.MinValue;
        private TimeSpan _scrollDebounceTime = TimeSpan.FromMilliseconds(300); // Reduced debounce time
        private SemaphoreSlim _loadingSemaphore = new SemaphoreSlim(1, 1);
        private bool _isLoadingMore = false;
        private CancellationTokenSource _cts;
        private bool _isPageLoaded = false;
        private bool _shouldRestoreScrollPosition = true;
        private bool _isBackgroundLoadingEnabled = true; // Enable background loading
        private bool _isPrefetchingEnabled = true; // Enable prefetching of images

        // Simulated test data for wallpapers (as fallback)
        private readonly List<string> _resolutions = new List<string> { "4K", "5K", "8K" };
        private readonly Random _random = new Random();

        // Stats counters for tracking HTTP requests
        private int _totalRequests = 0;
        private int _successfulRequests = 0;
        private int _failedRequests = 0;
        private readonly int _batchSize = 20; // Number of images to check at once
        
        // Queue for background processing
        private Queue<Task> _backgroundTasks = new Queue<Task>();
        private readonly int _maxConcurrentBackgroundTasks = 2;
        private int _runningBackgroundTasks = 0;
        private readonly object _backgroundTaskLock = new object();

        private readonly string _apiBaseUrl = "https://backiee.com/api/wallpaper/list.php";
        private int _currentApiPage = 1;
        private const int _apiPageSize = 30;
        private string _apiCategory = "all";
        private string _apiAiFilter = "all"; // Options: all, 0 (non-AI), 1 (AI only)
        private string _apiSortBy = "latest"; // Options: latest, popularity, downloads
        private bool _useApiForLoading = true; // Enable API loading by default

        public LatestWallpapersPage(ILogger<LatestWallpapersPage> logger = null, ISettingsService settingsService = null)
        {
            _logger = logger;
            _settingsService = settingsService;
            _logger?.LogInformation("LatestWallpapersPage constructor called");

            InitializeComponent();
            _wallpapers = new ObservableCollection<WallpaperItem>();

            // Create a new cancellation token source for infinite scrolling
            _cts = new CancellationTokenSource();
            
            // Initialize API page counter
            _currentApiPage = 1;
            
            // Explicitly disable scroll position restoration
            _shouldRestoreScrollPosition = false;
            
            // Ensure other scroll-related variables are properly initialized
            _isPageLoaded = false;
            _isLoadingMore = false;
            _lastScrollCheck = DateTime.MinValue;

            // Register events
            Loaded += LatestWallpapersPage_Loaded;
            SizeChanged += LatestWallpapersPage_SizeChanged;
            Unloaded += LatestWallpapersPage_Unloaded;
        }

        private async void LatestWallpapersPage_Loaded(object sender, RoutedEventArgs e)
        {
            _logger?.LogInformation("LatestWallpapersPage loaded");
            
            // Show loading indicators
            StatusTextBlock.Visibility = Visibility.Visible;
            LoadingProgressBar.Visibility = Visibility.Visible;

            // Create a new cancellation token source
            _cts = new CancellationTokenSource();
            
            // Explicitly ensure page counter is reset to avoid issues with scroll state
            _currentApiPage = 1;
            _logger?.LogInformation("Starting with fresh page counter (reset to 1)");

            // Load settings
            await LoadSettingsAsync();

            // Initialize with JSON data
            await LoadInitialWallpapers();
            
            // Set flag to indicate page is loaded
            _isPageLoaded = true;
            
            // Start preemptive loading after initial load
            await Task.Delay(500); // Short delay to allow UI to render
            
            // Instead of trying to simulate a scroll event with ScrollChangedEventArgs,
            // directly queue up the next page load to ensure infinite scroll works on first load
            if (_useApiForLoading && !_isLoadingMore)
            {
                // Only if we're not already loading and API loading is enabled
                _logger?.LogInformation("Preemptively triggering next page load to ensure infinite scroll works");
                
                // Try to acquire the semaphore without blocking
                if (await _loadingSemaphore.WaitAsync(0))
                {
                    try
                    {
                        _isLoadingMore = true;
                        await LoadMoreWallpapersFromApiAsync(_cts.Token);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Error preemptively loading next page");
                    }
                    finally
                    {
                        _isLoadingMore = false;
                        _loadingSemaphore.Release();
                    }
                }
            }
            
            // Then start background loading
            StartBackgroundLoading();
        }

        private void LatestWallpapersPage_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            _logger?.LogInformation($"Window size changed to: {e.NewSize.Width}x{e.NewSize.Height}");
            
            // Get the parent window
            Window parentWindow = Window.GetWindow(this);
            if (parentWindow != null)
            {
                // Define the maximum width threshold (adjust as needed)
                const double maxWidthThreshold = 1800;
                
                // If the window width exceeds the threshold, make it unresizable
                if (e.NewSize.Width > maxWidthThreshold)
                {
                    _logger?.LogInformation($"Window width ({e.NewSize.Width}) exceeds threshold ({maxWidthThreshold}). Making window unresizable.");
                    parentWindow.ResizeMode = ResizeMode.NoResize;
                    
                    // Show a message to the user
                    StatusTextBlock.Text = "Maximum window size reached";
                    StatusTextBlock.Visibility = Visibility.Visible;
                    
                    // Hide the message after a delay
                    Task.Delay(2000).ContinueWith(_ => 
                    {
                        Dispatcher.Invoke(() => 
                        {
                            StatusTextBlock.Visibility = Visibility.Collapsed;
                        });
                    });
                }
                else
                {
                    // Reset to normal resizing mode if below threshold
                    parentWindow.ResizeMode = ResizeMode.CanResize;
                }
            }
            
            // Update the layout when the window size changes
            AdjustItemSizes();
            
            // Save settings when page is fully loaded
            if (_isPageLoaded)
            {
                SaveSettingsQuietly();
            }
        }
        
        private void LatestWallpapersPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _logger?.LogInformation("LatestWallpapersPage unloaded, saving settings");
            
            // Save settings when page is unloaded
            SaveSettings();
            
            // Cancel any ongoing operations
            _cts?.Cancel();
        }

        private async Task LoadSettingsAsync()
        {
            try
            {
                if (_settingsService == null)
                {
                    _logger?.LogWarning("SettingsService is null, using default settings");
                    return;
                }
                
                _logger?.LogInformation("Loading LatestWallpapersPage settings");
                var settings = await _settingsService.LoadSettingsAsync();
                
                // Set item sizes from settings
                if (settings.LatestWallpapersItemWidth > 50)
                {
                    _itemWidth = settings.LatestWallpapersItemWidth;
                }
                
                if (settings.LatestWallpapersItemHeight > 30)
                {
                    _itemHeight = settings.LatestWallpapersItemHeight;
                }
                
                _logger?.LogInformation($"Loaded settings - Item size: {_itemWidth}x{_itemHeight}, Scroll: {settings.LatestWallpapersScrollPosition}");

                // Restore scroll position if needed
                if (settings.LatestWallpapersScrollPosition > 0)
                {
                    MainScrollViewer.ScrollToVerticalOffset(settings.LatestWallpapersScrollPosition);
                }
                
                // Restore item size if specified
                if (settings.LatestWallpapersItemWidth > 0 && settings.LatestWallpapersItemHeight > 0)
                {
                    _itemWidth = settings.LatestWallpapersItemWidth;
                    _itemHeight = settings.LatestWallpapersItemHeight;
                    
                    // Adjust all existing items
                    AdjustItemSizes();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error loading LatestWallpapersPage settings");
            }
        }
        
        private async void SaveSettingsQuietly()
        {
            try
            {
                await SaveSettingsAsync();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error quietly saving LatestWallpapersPage settings");
            }
        }
        
        private void SaveSettings()
        {
            try
            {
                SaveSettingsQuietly();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error saving LatestWallpapersPage settings on unload");
            }
        }
        
        private async Task SaveSettingsAsync()
        {
            try
            {
                if (_settingsService == null || !_isPageLoaded)
                {
                    return;
                }
                
                // No longer save scroll position
                // double scrollPosition = MainScrollViewer.VerticalOffset;
                
                _logger?.LogInformation($"Saving LatestWallpapersPage settings - Item size: {_itemWidth}x{_itemHeight}");
                
                // Update settings with LatestWallpapersPage values
                await _settingsService.UpdateSettingsAsync(settings => 
                {
                    settings.LatestWallpapersItemWidth = _itemWidth;
                    settings.LatestWallpapersItemHeight = _itemHeight;
                    // No longer save scroll position
                    // settings.LatestWallpapersScrollPosition = scrollPosition;
                });
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error saving LatestWallpapersPage settings");
            }
        }

        private async Task LoadInitialWallpapers()
        {
            try
            {
                _logger?.LogInformation("Loading initial wallpapers");
                
                // Ensure API loading is enabled
                _useApiForLoading = true;
                
                // Clear existing items
                WallpaperContainer.Children.Clear();
                _wallpapers.Clear();
                _loadedUrls.Clear();
                _attemptedIds.Clear();
                
                // Explicitly reset page counter to ensure correct sequence
                _currentApiPage = 1; // This maps to API index 0
                _logger?.LogInformation("Reset page counter to 1 (API index 0)");
                
                StatusTextBlock.Text = "Loading wallpapers...";
                
                // Load the first batch of wallpapers (page 0)
                _logger?.LogInformation("Loading first batch of wallpapers (page 0)");
                bool wallpapersLoaded = await LoadWallpapersFromApiAsync();
                
                // After first load, _currentApiPage should be 2
                _logger?.LogInformation("After first load, page counter = {0}", _currentApiPage);
                
                if (wallpapersLoaded)
                {
                    // Explicitly ensure we're on page 2 (API index 1)
                    if (_currentApiPage != 2)
                    {
                        _logger?.LogWarning("Page counter unexpected value: {0}, forcing to 2", _currentApiPage);
                        _currentApiPage = 2;
                    }
                    
                    // Immediately load the second batch (page 1)
                    _logger?.LogInformation("Loading second batch of wallpapers (page 1)");
                    await LoadMoreWallpapersFromApiAsync(_cts.Token);
                    
                    // After second load, _currentApiPage should be 3
                    _logger?.LogInformation("After second load, page counter = {0}", _currentApiPage);
                    
                    // Ensure it's now 3 for the next load to be page 2
                    if (_currentApiPage != 3)
                    {
                        _logger?.LogWarning("Page counter unexpected value after second load: {0}, forcing to 3", _currentApiPage);
                        _currentApiPage = 3;
                    }
                }
                
                // If API loading failed, use test data as fallback
                if (!wallpapersLoaded)
                {
                    _logger?.LogWarning("Failed to load from API, falling back to test data");
                    StatusTextBlock.Text = "Using sample data (API not available)";
                    await Task.Delay(1000); // Show message briefly
                    
                    // Generate test data
                    for (int i = 0; i < 20; i++)
                    {
                        await AddTestWallpaperItem();
                    }
                }

                // Hide loading indicators
                StatusTextBlock.Visibility = Visibility.Collapsed;
                LoadingProgressBar.Visibility = Visibility.Collapsed;

                // Adjust item sizes based on current window width
                AdjustItemSizes();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error initializing wallpaper grid");
                StatusTextBlock.Text = "Error loading wallpapers";
                StatusTextBlock.Visibility = Visibility.Visible;
                LoadingProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        private async Task<bool> LoadImagesFromJsonFile()
        {
            try
            {
                // Look for the JSON file in the Data directory
                string jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "wallpapers_pretty.json");
                var fullPath = Path.GetFullPath(jsonPath);
                _logger?.LogInformation($"Looking for JSON file at: {fullPath}");
                
                if (!File.Exists(fullPath))
                {
                    _logger?.LogInformation("JSON file not found, switching to API loading");
                    return await LoadWallpapersFromApiAsync();
                }

                _logger?.LogInformation($"Found JSON file at: {fullPath}");

                // Read the JSON content
                string jsonContent = await File.ReadAllTextAsync(fullPath);
                
                // Use a simple approach for parsing the wallpapers
                var wallpapers = new List<SimpleWallpaper>();
                
                using (JsonDocument doc = JsonDocument.Parse(jsonContent))
                {
                    foreach (JsonElement item in doc.RootElement.EnumerateArray())
                    {
                        var wallpaper = new SimpleWallpaper();
                        
                        // Get the image URL - using MiniPhotoUrl for faster loading
                        if (item.TryGetProperty("MiniPhotoUrl", out JsonElement urlElement) && 
                            urlElement.ValueKind == JsonValueKind.String)
                        {
                            wallpaper.Url = urlElement.GetString() ?? "";
                        }
                        
                        // Get the quality (resolution info)
                        if (item.TryGetProperty("UltraHDType", out JsonElement qualityElement) && 
                            qualityElement.ValueKind == JsonValueKind.String)
                        {
                            wallpaper.Quality = qualityElement.GetString() ?? "";
                        }
                        
                        // Get the AI status
                        if (item.TryGetProperty("AIGenerated", out JsonElement aiElement))
                        {
                            switch (aiElement.ValueKind)
                            {
                                case JsonValueKind.String:
                                    var strValue = aiElement.GetString() ?? "";
                                    wallpaper.IsAI = strValue.Equals("1", StringComparison.OrdinalIgnoreCase);
                                    break;
                                case JsonValueKind.Number:
                                    wallpaper.IsAI = aiElement.GetInt32() != 0;
                                    break;
                            }
                        }
                        
                        // Get likes (Rating in the new structure)
                        if (item.TryGetProperty("Rating", out JsonElement likesElement))
                        {
                            switch (likesElement.ValueKind)
                            {
                                case JsonValueKind.String:
                                    if (int.TryParse(likesElement.GetString(), out int likesValue))
                                    {
                                        wallpaper.Likes = likesValue;
                                    }
                                    break;
                                case JsonValueKind.Number:
                                    wallpaper.Likes = likesElement.GetInt32();
                                    break;
                            }
                        }
                        
                        // Get downloads
                        if (item.TryGetProperty("Downloads", out JsonElement downloadsElement))
                        {
                            switch (downloadsElement.ValueKind)
                            {
                                case JsonValueKind.String:
                                    if (int.TryParse(downloadsElement.GetString(), out int downloadsValue))
                                    {
                                        wallpaper.Downloads = downloadsValue;
                                    }
                                    break;
                                case JsonValueKind.Number:
                                    wallpaper.Downloads = downloadsElement.GetInt32();
                                    break;
                            }
                        }
                        
                        wallpapers.Add(wallpaper);
                    }
                }
                
                // Log the first few for debugging
                for (int i = 0; i < Math.Min(wallpapers.Count, 5); i++)
                {
                    _logger?.LogInformation($"Wallpaper[{i}]: URL={wallpapers[i].Url}, Quality={wallpapers[i].Quality}, IsAI={wallpapers[i].IsAI}");
                }
                
                // Add the wallpapers to the UI
                foreach (var wallpaper in wallpapers)
                {
                    if (string.IsNullOrEmpty(wallpaper.Url))
                        continue;
                        
                    // Normalize the URL to lowercase for consistent comparison
                    string normalizedUrl = wallpaper.Url.ToLowerInvariant();
                    
                    // Skip if we already have this URL
                    if (_loadedUrls.Contains(normalizedUrl))
                        continue;
                        
                    // Extract image ID from URL
                    string imageId = GetImageIdFromUrl(normalizedUrl);
                    
                    // Skip if we already have this imageId
                    if (_wallpapers.Any(img => img.ImageId == imageId))
                        continue;
                    
                    // Create a wallpaper item
                    var image = new WallpaperItem
                    {
                        ImageUrl = normalizedUrl,
                        ImageId = imageId,
                        IsAI = wallpaper.IsAI,
                        Likes = wallpaper.Likes,
                        Downloads = wallpaper.Downloads
                    };
                    
                    // Set resolution based on quality
                    image.Resolution = "1920x1080"; // Default
                    
                    if (!string.IsNullOrEmpty(wallpaper.Quality))
                    {
                        image.ResolutionLabel = wallpaper.Quality;
                        
                        switch (wallpaper.Quality)
                        {
                            case "4K":
                                image.Resolution = "3840x2160";
                                break;
                            case "5K":
                                image.Resolution = "5120x2880";
                                break;
                            case "8K":
                                image.Resolution = "7680x4320";
                                break;
                        }
                    }
                    
                    _wallpapers.Add(image);
                    _loadedUrls.Add(normalizedUrl);
                    
                    // Create and add UI element
                    var wallpaperElement = CreateWallpaperElement(image);
                    WallpaperContainer.Children.Add(wallpaperElement);
                    
                    // Also track the ID to avoid re-attempting it
                    if (int.TryParse(imageId, out int parsedId))
                    {
                        _attemptedIds.Add(parsedId);
                    }
                }
                
                // Set the current imageId for infinite scrolling based on our loaded images
                if (_wallpapers.Count > 0)
                {
                    // Find all valid numeric IDs
                    var numericIds = _wallpapers
                        .Where(i => int.TryParse(i.ImageId, out _))
                        .Select(i => int.Parse(i.ImageId))
                        .ToList();
                    
                    if (numericIds.Any())
                    {
                        // Set the starting point to one less than the minimum ID already loaded
                        // This ensures we start loading the next sequential images
                        _currentImageId = numericIds.Min() - 1;
                        _logger?.LogInformation($"Set next imageId to {_currentImageId} - will load in sequential order from here");
                    }
                    else
                    {
                        // If no numeric IDs, start from a reasonable default
                        _currentImageId = 200000;
                        _logger?.LogInformation($"No numeric IDs found in JSON, set next imageId to default: {_currentImageId}");
                    }
                }
                else
                {
                    _currentImageId = 200000;
                    _logger?.LogInformation($"No wallpapers loaded from JSON, set next imageId to default: {_currentImageId}");
                }
                
                _logger?.LogInformation($"Successfully loaded {_wallpapers.Count} images from JSON file");
                return _wallpapers.Count > 0;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error loading images from JSON file: " + ex.Message);
                _currentImageId = 200000; // Default to a high value if JSON loading failed
                return false;
            }
        }
        
        private string GetImageIdFromUrl(string url)
        {
            try
            {
                // Extract ID from URL like https://backiee.com/static/wallpapers/560x315/123456.jpg
                string filename = Path.GetFileNameWithoutExtension(url);
                return filename;
            }
            catch
            {
                return string.Empty;
            }
        }

        private async Task AddTestWallpaperItem()
        {
            // Create a new wallpaper item with random properties
            var wallpaper = new WallpaperItem
            {
                ImageUrl = GetRandomImageUrl(),
                ImageId = _random.Next(100000, 999999).ToString(),
                Resolution = $"{_random.Next(1920, 7680)}x{_random.Next(1080, 4320)}",
                ResolutionLabel = _resolutions[_random.Next(_resolutions.Count)],
                IsAI = _random.Next(2) == 1,
                Likes = _random.Next(1, 100),
                Downloads = _random.Next(1, 500)
            };

            _wallpapers.Add(wallpaper);

            // Create UI element for this wallpaper
            var wallpaperElement = CreateWallpaperElement(wallpaper);
            
            // Add to container
            WallpaperContainer.Children.Add(wallpaperElement);

            // Simulate network delay for realistic testing
            await Task.Delay(50);
        }

        private string GetRandomImageUrl()
        {
            // For testing, use some placeholder image URLs
            string[] imageUrls = new string[]
            {
                "https://wallpapercave.com/wp/wp2555030.jpg",
                "https://wallpaperaccess.com/full/51363.jpg",
                "https://images.pexels.com/photos/1366919/pexels-photo-1366919.jpeg",
                "https://wallpapercave.com/wp/wp4676582.jpg",
                "https://images.pexels.com/photos/1242348/pexels-photo-1242348.jpeg",
                "https://wallpapercave.com/wp/wp2581576.jpg",
                "https://images.pexels.com/photos/733745/pexels-photo-733745.jpeg",
                "https://wallpaperaccess.com/full/1091424.jpg",
                "https://images.pexels.com/photos/1323550/pexels-photo-1323550.jpeg",
                "https://wallpapercave.com/wp/wp7486693.jpg"
            };

            return imageUrls[_random.Next(imageUrls.Length)];
        }

        private FrameworkElement CreateWallpaperElement(WallpaperItem wallpaper)
        {
            // Create a Grid as the container
            var containerGrid = new Grid
            {
                Width = _itemWidth,
                Height = _itemHeight,
                Margin = new Thickness(4),
                Tag = wallpaper,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            
            // Create a Rectangle with rounded corners
            var roundedRectangle = new System.Windows.Shapes.Rectangle
            {
                RadiusX = 20,
                RadiusY = 20,
                Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 30, 30)),
                Stroke = new SolidColorBrush(Colors.LightGray),
                StrokeThickness = 1
            };
            containerGrid.Children.Add(roundedRectangle);
            
            // Create a Grid for the content
            var contentGrid = new Grid
            {
                ClipToBounds = true
            };
            
            // Apply clipping to the content grid to match the rounded rectangle
            contentGrid.Clip = new RectangleGeometry
            {
                Rect = new Rect(0, 0, _itemWidth, _itemHeight),
                RadiusX = 6,
                RadiusY = 6
            };
            
            containerGrid.Children.Add(contentGrid);
            
            // Create a loading indicator
            var loadingIndicator = new System.Windows.Controls.ProgressBar
            {
                IsIndeterminate = true,
                Width = 50,
                Height = 5,
                Foreground = new SolidColorBrush(Colors.White),
                Background = new SolidColorBrush(Colors.Transparent),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 0)
            };
            contentGrid.Children.Add(loadingIndicator);

            // Create and add the image (with placeholder until loaded)
            var image = new System.Windows.Controls.Image
            {
                Stretch = Stretch.UniformToFill,
                Opacity = 0 // Start with invisible image
            };
            
            // Use BitmapImage with events for loading
            var bitmapImage = new BitmapImage();
            bitmapImage.BeginInit();
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad; // Fully load in memory
            bitmapImage.UriSource = new Uri(wallpaper.ImageUrl);
            bitmapImage.EndInit();
            
            // Clip the image to match the rounded rectangle
            image.Clip = new RectangleGeometry
            {
                Rect = new Rect(0, 0, _itemWidth, _itemHeight),
                RadiusX = 10,
                RadiusY = 10
            };
            
            // Handle the image loading events
            bitmapImage.DownloadCompleted += (s, e) => 
            {
                // When the image is loaded, fade it in and hide the placeholder
                image.Opacity = 1;
                roundedRectangle.Opacity = 0;
                loadingIndicator.Visibility = Visibility.Collapsed;
            };
            
            bitmapImage.DownloadFailed += (s, e) => 
            {
                // If download fails, show a error placeholder
                roundedRectangle.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(50, 30, 30)); // Dark red background
                loadingIndicator.Visibility = Visibility.Collapsed;
                
                // Add an error icon or text
                var errorText = new System.Windows.Controls.TextBlock
                {
                    Text = "!",
                    FontSize = 24,
                    Foreground = new SolidColorBrush(Colors.White),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                };
                contentGrid.Children.Add(errorText);
            };
            
            image.Source = bitmapImage;
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            contentGrid.Children.Add(image);

            // Create a panel for resolution badges
            var badgesPanel = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                VerticalAlignment = System.Windows.VerticalAlignment.Top,
                Margin = new Thickness(0)
            };
            contentGrid.Children.Add(badgesPanel);

            // Add appropriate resolution badge
            string badgeSource = wallpaper.ResolutionLabel switch
            {
                "4K" => "/Assets/4k_logo.png",
                "5K" => "/Assets/5k_logo.png",
                "8K" => "/Assets/8k_logo.png",
                _ => string.Empty
            };

            if (!string.IsNullOrEmpty(badgeSource))
            {
                var badge = new System.Windows.Controls.Image
                {
                    Source = new BitmapImage(new Uri(badgeSource, UriKind.Relative)),
                    Height = 48,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                    VerticalAlignment = System.Windows.VerticalAlignment.Top,
                    Margin = new Thickness(0)
                };
                
                // Set Z-index to ensure it's above other elements
                System.Windows.Controls.Panel.SetZIndex(badge, 10);
                
                badgesPanel.Children.Add(badge);
            }

            // Add AI badge if needed
            if (wallpaper.IsAI)
            {
                var aiPanel = new System.Windows.Controls.Image
                {
                    Source = new BitmapImage(new Uri("/Assets/aigenerated-icon.png", UriKind.Relative)),
                    Height = 36,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                    VerticalAlignment = System.Windows.VerticalAlignment.Top,
                    Margin = new Thickness(0, 5, 20, 0)
                };
                contentGrid.Children.Add(aiPanel);
            }

            // Add stats
            var statsPanel = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 0)
            };
            
            // Add likes counter with Apple-style heart icon
            var likesPanel = new StackPanel 
            { 
                Orientation = System.Windows.Controls.Orientation.Horizontal, 
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            
            // Use a simpler heart icon that resembles Apple's style
            var likesIcon = new System.Windows.Controls.Image
            {
                Source = new BitmapImage(new Uri("/Assets/heart_icon.png", UriKind.Relative)),
                Width = 16,
                Height = 16,
                Margin = new Thickness(0, 0, 4, 0)
            };
            
            // If the heart icon image isn't available, fall back to a Path
            if (likesIcon.Source.ToString().Contains("heart_icon.png"))
            {
                try
                {
                    likesIcon = null;
                    var heartPath = new System.Windows.Shapes.Path
                    {
                        Data = Geometry.Parse("M12,21.35L10.55,20.03C5.4,15.36 2,12.27 2,8.5C2,5.41 4.42,3 7.5,3C9.24,3 10.91,3.81 12,5.08C13.09,3.81 14.76,3 16.5,3C19.58,3 22,5.41 22,8.5C22,12.27 18.6,15.36 13.45,20.03L12,21.35Z"),
                        Fill = new SolidColorBrush(System.Windows.Media.Colors.White),
                        Width = 16,
                        Height = 16,
                        Margin = new Thickness(0, 0, 4, 0),
                        Stretch = Stretch.Uniform
                    };
                    likesPanel.Children.Add(heartPath);
                }
                catch
                {
                    // Just in case the path geometry is invalid
                    var textHeart = new System.Windows.Controls.TextBlock
                    {
                        Text = "♥",
                        Foreground = new SolidColorBrush(System.Windows.Media.Colors.White),
                        FontSize = 14,
                        Margin = new Thickness(0, 0, 4, 0)
                    };
                    likesPanel.Children.Add(textHeart);
                }
            }
            else
            {
                likesPanel.Children.Add(likesIcon);
            }
            
            // Add text for likes count
            var likesText = new System.Windows.Controls.TextBlock 
            { 
                Text = wallpaper.Likes.ToString(), 
                Foreground = new SolidColorBrush(System.Windows.Media.Colors.White),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            likesPanel.Children.Add(likesText);
            statsPanel.Children.Add(likesPanel);
            
            // Add downloads counter
            var downloadsPanel = new StackPanel 
            { 
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            
            // Use a simpler download icon that resembles Apple's style
            var downloadsIcon = new System.Windows.Controls.Image
            {
                Source = new BitmapImage(new Uri("/Assets/download_icon.png", UriKind.Relative)),
                Width = 16,
                Height = 16,
                Margin = new Thickness(0, 0, 4, 0)
            };
            
            // If the download icon image isn't available, fall back to a Path
            if (downloadsIcon.Source.ToString().Contains("download_icon.png"))
            {
                try
                {
                    downloadsIcon = null;
                    var downloadPath = new System.Windows.Shapes.Path
                    {
                        Data = Geometry.Parse("M12,15L7,10H10V6H14V10H17L12,15M19.35,10.03C18.67,6.59 15.64,4 12,4C9.11,4 6.6,5.64 5.35,8.03C2.34,8.36 0,10.9 0,14A6,6 0 0,0 6,20H19A5,5 0 0,0 24,15C24,12.36 21.95,10.22 19.35,10.03Z"),
                        Fill = new SolidColorBrush(System.Windows.Media.Colors.White),
                        Width = 16,
                        Height = 16,
                        Margin = new Thickness(0, 0, 4, 0),
                        Stretch = System.Windows.Media.Stretch.Uniform
                    };
                    downloadsPanel.Children.Add(downloadPath);
                }
                catch
                {
                    // Just in case the path geometry is invalid, use a simple arrow
                    var downloadPath = new System.Windows.Shapes.Path
                    {
                        Data = Geometry.Parse("M5,20H19V18H5M19,9H15V3H9V9H5L12,16L19,9Z"),
                        Fill = new SolidColorBrush(System.Windows.Media.Colors.White),
                        Width = 16,
                        Height = 16,
                        Margin = new Thickness(0, 0, 4, 0),
                        Stretch = System.Windows.Media.Stretch.Uniform
                    };
                    downloadsPanel.Children.Add(downloadPath);
                }
            }
            else
            {
                downloadsPanel.Children.Add(downloadsIcon);
            }
            
            // Add text for download count
            var downloadsText = new System.Windows.Controls.TextBlock 
            { 
                Text = wallpaper.Downloads.ToString(), 
                Foreground = new SolidColorBrush(System.Windows.Media.Colors.White),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            downloadsPanel.Children.Add(downloadsText);
            statsPanel.Children.Add(downloadsPanel);
            
            // Add a semi-transparent background to ensure visibility
            var statsBg = new Border
            {
                Background = null,
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 4, 8, 4),
                Child = statsPanel,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 10, 10)
            };
            
            // Add the stats to the grid
            contentGrid.Children.Add(statsBg);

            // Handle click event
            containerGrid.MouseLeftButtonUp += ImageBorder_MouseLeftButtonUp;

            return containerGrid;
        }

        private void AdjustItemSizes()
        {
            // Get the current width of the container
            double containerWidth = MainScrollViewer.ActualWidth;
            _logger?.LogInformation($"Container width: {containerWidth}");

            if (containerWidth <= 0)
                return;

            // Calculate how many items should fit in each row
            int itemsPerRow;
            if (containerWidth < 600)
                itemsPerRow = 1;
            else if (containerWidth < 900)
                itemsPerRow = 2;
            else if (containerWidth < 1200)
                itemsPerRow = 3;
            else if (containerWidth < 1500)
                itemsPerRow = 4;
            else
                itemsPerRow = 5;

            // Calculate new item width (accounting for margins)
            double newItemWidth = (containerWidth / itemsPerRow) - 10; // 10px for margins
            double newItemHeight = newItemWidth * 0.6; // 16:9 aspect ratio

            _logger?.LogInformation($"Adjusting items to width: {newItemWidth}, items per row: {itemsPerRow}");

            // Update all wallpaper items with new size
            foreach (FrameworkElement child in WallpaperContainer.Children)
            {
                // Handle both Border (old implementation) and Grid (new implementation)
                if (child is Border border)
                {
                    border.Width = newItemWidth;
                    border.Height = newItemHeight;
                }
                else if (child is Grid grid && grid.Tag is WallpaperItem)
                {
                    // Update grid size
                    grid.Width = newItemWidth;
                    grid.Height = newItemHeight;
                    
                    // Update clip on the image if present
                    foreach (var gridChild in grid.Children)
                    {
                        // Look for the content grid
                        if (gridChild is Grid contentGrid)
                        {
                            // Update the content grid's clip
                            if (contentGrid.Clip is RectangleGeometry contentClip)
                            {
                                contentClip.Rect = new Rect(0, 0, newItemWidth, newItemHeight);
                            }
                            
                            foreach (var contentItem in contentGrid.Children)
                            {
                                // Find the Image element and update its clip
                                if (contentItem is System.Windows.Controls.Image image)
                                {
                                    if (image.Clip is RectangleGeometry imageClip)
                                    {
                                        imageClip.Rect = new Rect(0, 0, newItemWidth, newItemHeight);
                                    }
                                }
                            }
                        }
                        // Update the rectangle dimensions
                        else if (gridChild is System.Windows.Shapes.Rectangle rectangle)
                        {
                            // Rectangle should auto-size, but ensure properties are maintained
                            rectangle.Width = double.NaN; // Auto width
                            rectangle.Height = double.NaN; // Auto height
                        }
                    }
                }
            }

            // Store new sizes for new items
            _itemWidth = newItemWidth;
            _itemHeight = newItemHeight;
            
            // Save settings when page is fully loaded and items have been adjusted
            if (_isPageLoaded)
            {
                SaveSettingsQuietly();
            }
        }

        private async void MainScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            try
            {
                // Make sure the page is fully loaded before processing scroll events
                if (!_isPageLoaded || _cts == null || _cts.IsCancellationRequested)
                {
                    return;
                }
                
                // Debounce scroll events for infinite scrolling
                if ((DateTime.Now - _lastScrollCheck) < _scrollDebounceTime)
                {
                    return;
                }
                _lastScrollCheck = DateTime.Now;

                // Calculate how far from the bottom we are (as a percentage)
                double scrollPercentage = (e.VerticalOffset + e.ViewportHeight) / e.ExtentHeight;
                
                // Check if we're within the preemptive loading threshold
                // This loads more images before reaching the bottom
                if (scrollPercentage > 0.6) // Start loading when 60% scrolled (reduced threshold)
                {
                    if (!_isLoadingMore)
                    {
                        // Try to acquire the semaphore without blocking
                        if (await _loadingSemaphore.WaitAsync(0))
                        {
                            try
                            {
                                _isLoadingMore = true;
                                int currentPageBeforeScroll = _currentApiPage;
                                _logger?.LogInformation("Preemptive loading triggered at {0}% scroll (current page: {1})", 
                                    (scrollPercentage * 100).ToString("0"), _currentApiPage);
                                
                                // Show loading status but make it less intrusive
                                StatusTextBlock.Text = "Loading more wallpapers...";
                                StatusTextBlock.Visibility = Visibility.Visible;
                                LoadingProgressBar.Visibility = Visibility.Visible;
                                
                                // Load more wallpapers
                                if (_useApiForLoading)
                                {
                                    await LoadMoreWallpapersFromApiAsync(_cts.Token);
                                    _logger?.LogInformation("After scroll-triggered loading, page counter progressed from {0} to {1}", 
                                        currentPageBeforeScroll, _currentApiPage);
                                }
                                else
                                {
                                    await LoadMoreImagesAsync(_cts.Token);
                                }
                                
                                // Queue background loading for the next batch
                                if (_isPrefetchingEnabled)
                                {
                                    QueueBackgroundLoading();
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger?.LogError(ex, "Error loading more wallpapers in scroll handler");
                            }
                            finally
                            {
                                _isLoadingMore = false;
                                _loadingSemaphore.Release();
                                
                                // Hide loading indicators
                                StatusTextBlock.Visibility = Visibility.Collapsed;
                                LoadingProgressBar.Visibility = Visibility.Collapsed;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in scroll changed handler");
            }
        }
        
        private void StartBackgroundLoading()
        {
            if (_isBackgroundLoadingEnabled && !_isLoadingMore)
            {
                QueueBackgroundLoading();
            }
        }
        
        private void QueueBackgroundLoading()
        {
            lock (_backgroundTaskLock)
            {
                if (_runningBackgroundTasks < _maxConcurrentBackgroundTasks)
                {
                    _runningBackgroundTasks++;
                    
                    // Use a delay before starting background loading
                    // This allows the UI to remain responsive and prevents too many concurrent requests
                    var task = Task.Run(async () =>
                    {
                        try
                        {
                            // Short delay before starting background loading
                            // This gives the UI thread time to process and helps maintain order
                            await Task.Delay(300);
                            
                            // Take a snapshot of the current imageId to avoid race conditions
                            int startingImageId;
                            lock (_backgroundTaskLock)
                            {
                                startingImageId = _currentImageId;
                            }
                            
                            _logger?.LogInformation($"Background loading starting from imageId: {startingImageId}");
                            
                            await LoadMoreImagesAsync(_cts.Token, isBackgroundLoading: true);
                        }
                        finally
                        {
                            // Decrement running task count
                            lock (_backgroundTaskLock)
                            {
                                _runningBackgroundTasks--;
                            }
                        }
                    });
                    
                    _backgroundTasks.Enqueue(task);
                }
            }
        }
        
        private async Task LoadMoreImagesAsync(CancellationToken cancellationToken, bool isBackgroundLoading = false)
        {
            try
            {
                _logger?.LogInformation("LoadMoreImagesAsync called, current loading mode: {mode}, page: {page}", 
                    _useApiForLoading ? "API" : "Sequential HTTP Check", _currentApiPage);
                
                // If API loading is enabled, use that method
                if (_useApiForLoading)
                {
                    // Ensure we're loading the correct page
                    _logger?.LogInformation("Loading API page {page} (API index: {index})", _currentApiPage, _currentApiPage - 1);
                    await LoadMoreWallpapersFromApiAsync(cancellationToken);
                    return;
                }
                
                // Otherwise, fall back to the existing sequential HTTP check approach
                _logger?.LogInformation("Using sequential HTTP check approach, current imageId: {ImageId}, background: {isBackground}", 
                    _currentImageId, isBackgroundLoading);
                
                // Show status only if not background loading
                if (!isBackgroundLoading)
                {
                    StatusTextBlock.Text = $"Loading wallpapers... (ID: {_currentImageId})";
                }
                
                // Create a list to hold the successful images in this batch
                List<WallpaperItem> batchImages = new List<WallpaperItem>();
                int imagesFound = 0;
                int consecutiveFailures = 0;
                const int maxConsecutiveFailures = 50; // Threshold to jump back if too many failures in a row

                // Use HttpClient for parallel requests
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(10);

                    var tasks = new List<Task<Tuple<int, bool, string>>>();
                    var currentBatchIds = new List<int>();

                    // Prepare batch of IDs to check - strictly decreasing from _currentImageId
                    for (int i = 0; i < _batchSize; i++)
                    {
                        int imageId = _currentImageId - i;
                        if (imageId <= 0) // Don't check negative or zero IDs
                            continue;
                            
                        if (_attemptedIds.Contains(imageId)) // Skip IDs we've already tried
                            continue;

                        currentBatchIds.Add(imageId);
                        // Normalize URL format by ensuring consistent casing
                        string imageUrl = $"https://backiee.com/static/wallpapers/560x315/{imageId}.jpg".ToLowerInvariant();
                        
                        // Skip if we already have this URL
                        if (_loadedUrls.Contains(imageUrl))
                            continue;
                            
                        tasks.Add(CheckImageExistsAsync(client, imageId, imageUrl, cancellationToken));
                    }

                    // Wait for all tasks to complete
                    if (tasks.Any())
                    {
                        var completedTasks = await Task.WhenAll(tasks);

                        // Create a dictionary to collect all successful images by ID for easier sorting later
                        Dictionary<int, WallpaperItem> foundWallpapers = new Dictionary<int, WallpaperItem>();

                        // Process results in the order of IDs to maintain consistency (highest to lowest)
                        foreach (var id in currentBatchIds.OrderByDescending(x => x))
                        {
                            var result = completedTasks.FirstOrDefault(r => r?.Item1 == id);
                            if (result == null) continue;

                            bool exists = result.Item2;
                            string imageUrl = result.Item3;

                            // Add to attempted IDs before checking existence
                            _attemptedIds.Add(id);
                            _totalRequests++;

                            // Double-check to avoid duplicates (check by ID as well)
                            if (exists && !_loadedUrls.Contains(imageUrl) && 
                                !_wallpapers.Any(img => img.ImageId == id.ToString()))
                            {
                                _successfulRequests++;
                                imagesFound++;
                                consecutiveFailures = 0; // Reset consecutive failures counter

                                var wallpaper = new WallpaperItem
                                {
                                    ImageUrl = imageUrl,
                                    ImageId = id.ToString(),
                                    Resolution = "1920x1080",
                                    // Randomly assign a resolution label (since we don't know the real quality)
                                    ResolutionLabel = _resolutions[_random.Next(_resolutions.Count)],
                                    IsAI = _random.Next(2) == 1, // Randomly assign AI status
                                    Likes = _random.Next(1, 100),
                                    Downloads = _random.Next(1, 500)
                                };
                                
                                _logger?.LogInformation($"Creating image: ID={wallpaper.ImageId}, URL={wallpaper.ImageUrl}");
                                
                                // Update resolution based on label
                                switch (wallpaper.ResolutionLabel)
                                {
                                    case "4K":
                                        wallpaper.Resolution = "3840x2160";
                                        break;
                                    case "5K":
                                        wallpaper.Resolution = "5120x2880";
                                        break;
                                    case "8K":
                                        wallpaper.Resolution = "7680x4320";
                                        break;
                                }
                                
                                // Store in the dictionary
                                foundWallpapers[id] = wallpaper;
                                _loadedUrls.Add(imageUrl);
                            }
                            else
                            {
                                _failedRequests++;
                                consecutiveFailures++;
                            }
                        }

                        // Update the current ID to continue from - strictly sequentially
                        // Find the minimum ID we just checked and continue from one below that
                        if (currentBatchIds.Any())
                        {
                            _currentImageId = currentBatchIds.Min() - 1;
                            _logger?.LogInformation($"Updated next imageId to {_currentImageId} for sequential loading");
                        }

                        // Sort wallpapers by ID in descending order (highest to lowest) for consistent ordering
                        batchImages = foundWallpapers.OrderByDescending(kvp => kvp.Key)
                                                    .Select(kvp => kvp.Value)
                                                    .ToList();

                        // Ensuring consistent order between multiple loading operations
                        lock (_backgroundTaskLock)
                        {
                            // Acquire a common lock before dispatching to UI thread
                            // This prevents different loading operations from interleaving their images
                        }
                        
                        // Add images to the UI on the UI thread
                        await Dispatcher.InvokeAsync(() =>
                        {
                            foreach (var wallpaper in batchImages)
                            {
                                _wallpapers.Add(wallpaper);
                                
                                // Create and add UI element
                                var wallpaperElement = CreateWallpaperElement(wallpaper);
                                WallpaperContainer.Children.Add(wallpaperElement);
                            }
                            
                            // Update status only if not background loading
                            if (!isBackgroundLoading)
                            {
                                // Update the status
                                StatusTextBlock.Text = $"Loaded {imagesFound} new wallpapers (Total: {_wallpapers.Count})";
                                StatusTextBlock.Text += $" | Success: {_successfulRequests}/{_totalRequests} ({_failedRequests} failed)";
                            }
                        });
                        
                        _logger?.LogInformation("Added {Count} images to collection. Total: {Total}", 
                            imagesFound, _wallpapers.Count);

                        // If we got too many consecutive failures, jump back by a significant amount
                        // This helps skip large gaps in the ID sequence while still loading in order
                        if (imagesFound == 0 && consecutiveFailures > maxConsecutiveFailures)
                        {
                            int jumpAmount = 5000;
                            int oldId = _currentImageId;
                            _currentImageId -= jumpAmount;
                            if (_currentImageId < 0) _currentImageId = 200000; // Reset if we hit zero
                            
                            _logger?.LogInformation($"Too many consecutive failures, jumping from {oldId} to {_currentImageId}");
                            
                            if (!isBackgroundLoading)
                            {
                                await Dispatcher.InvokeAsync(() => {
                                    StatusTextBlock.Text += $" | Jumping to ID: {_currentImageId}";
                                });
                            }
                        }
                        
                        // If we found images and background loading is enabled, queue next batch
                        // Only start a new background task if we're not already in background mode
                        if (imagesFound > 0 && _isBackgroundLoadingEnabled && !isBackgroundLoading)
                        {
                            QueueBackgroundLoading();
                        }
                    }
                    else
                    {
                        // If no tasks were created (all IDs are already attempted)
                        // Jump back to find a new range of IDs
                        int jumpAmount = 10000;
                        int oldId = _currentImageId;
                        _currentImageId -= jumpAmount;
                        if (_currentImageId < 0) _currentImageId = 200000; // Reset if we hit zero
                        
                        _logger?.LogInformation($"No new IDs to check, jumping from {oldId} to {_currentImageId}");
                        
                        if (!isBackgroundLoading)
                        {
                            await Dispatcher.InvokeAsync(() => {
                                StatusTextBlock.Text = $"Searching for more wallpapers... (ID: {_currentImageId})";
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in LoadMoreImagesAsync");
                
                if (!isBackgroundLoading)
                {
                    await Dispatcher.InvokeAsync(() => {
                        StatusTextBlock.Text = $"Error loading images: {ex.Message}";
                    });
                }
            }
        }
        
        private async Task<Tuple<int, bool, string>> CheckImageExistsAsync(HttpClient client, int imageId, string imageUrl, CancellationToken cancellationToken)
        {
            try
            {
                // Ensure URL is normalized
                imageUrl = imageUrl.ToLowerInvariant();
                
                // Make a HEAD request first to check if the image exists
                var request = new HttpRequestMessage(HttpMethod.Head, imageUrl);
                var response = await client.SendAsync(request, cancellationToken);
                
                // Return the result - true if the image exists, false otherwise
                return new Tuple<int, bool, string>(imageId, response.IsSuccessStatusCode, imageUrl);
            }
            catch (Exception)
            {
                // If there's an error, assume the image doesn't exist
                return new Tuple<int, bool, string>(imageId, false, imageUrl);
            }
        }

        private void ImageBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is WallpaperItem wallpaper)
            {
                _logger?.LogInformation($"Wallpaper clicked: {wallpaper.ResolutionLabel} (ID: {wallpaper.ImageId})");
                
                // Show a popup with wallpaper details
                System.Windows.MessageBox.Show($"Clicked on {wallpaper.ResolutionLabel} wallpaper\nResolution: {wallpaper.Resolution}\nAI Generated: {wallpaper.IsAI}\nImage ID: {wallpaper.ImageId}", 
                    "Wallpaper Details", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                
                // Implement setting wallpaper functionality
                DownloadAndSetWallpaper(wallpaper);
            }
            else if (sender is Grid grid && grid.Tag is WallpaperItem gridWallpaper)
            {
                _logger?.LogInformation($"Wallpaper clicked: {gridWallpaper.ResolutionLabel} (ID: {gridWallpaper.ImageId})");
                
                // Show a popup with wallpaper details
                var result = System.Windows.MessageBox.Show(
                    $"Would you like to set this {gridWallpaper.ResolutionLabel} wallpaper as your desktop background?\n\nResolution: {gridWallpaper.Resolution}\nAI Generated: {gridWallpaper.IsAI}\nImage ID: {gridWallpaper.ImageId}", 
                    "Set Wallpaper", 
                    System.Windows.MessageBoxButton.YesNo, 
                    System.Windows.MessageBoxImage.Question);
                
                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    // Download and set wallpaper
                    DownloadAndSetWallpaper(gridWallpaper);
                }
            }
        }
        
        private async void DownloadAndSetWallpaper(WallpaperItem wallpaper)
        {
            try
            {
                _logger?.LogInformation($"Downloading wallpaper: {wallpaper.ImageUrl}");
                
                // Show loading status
                StatusTextBlock.Text = "Downloading wallpaper...";
                StatusTextBlock.Visibility = Visibility.Visible;
                LoadingProgressBar.Visibility = Visibility.Visible;
                
                // Get the high-resolution URL by modifying the thumbnail URL
                // Backiee uses a specific format for full-size images
                string highResUrl = GetHighResolutionUrl(wallpaper.ImageUrl, wallpaper.ImageId);
                
                // Create temp directory if it doesn't exist
                string tempDir = Path.Combine(Path.GetTempPath(), "WallYouNeed");
                if (!Directory.Exists(tempDir))
                {
                    Directory.CreateDirectory(tempDir);
                }
                
                // Create a unique filename based on wallpaper ID and timestamp
                string wallpaperFile = Path.Combine(tempDir, $"wallpaper_{wallpaper.ImageId}_{DateTime.Now.Ticks}.jpg");
                
                // Download the wallpaper
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "WallYouNeed/1.0");
                    
                    // Update status
                    StatusTextBlock.Text = $"Downloading wallpaper {wallpaper.ImageId}...";
                    
                    // Download the image
                    var imageBytes = await client.GetByteArrayAsync(highResUrl);
                    
                    // Save to file
                    await File.WriteAllBytesAsync(wallpaperFile, imageBytes);
                    
                    _logger?.LogInformation($"Wallpaper saved to: {wallpaperFile}");
                }
                
                // Set as desktop wallpaper using Windows API
                StatusTextBlock.Text = "Setting wallpaper...";
                
                bool wallpaperSet = SetWindowsWallpaper(wallpaperFile);
                
                if (wallpaperSet)
                {
                    _logger?.LogInformation("Wallpaper set successfully");
                    StatusTextBlock.Text = "Wallpaper set successfully";
                    
                    // Track the download count increment (would be sent to API in a real implementation)
                    wallpaper.Downloads += 1;
                }
                else
                {
                    _logger?.LogError("Failed to set wallpaper");
                    StatusTextBlock.Text = "Failed to set wallpaper";
                }
                
                // Hide status after a delay
                await Task.Delay(2000);
                StatusTextBlock.Visibility = Visibility.Collapsed;
                LoadingProgressBar.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error setting wallpaper");
                StatusTextBlock.Text = $"Error: {ex.Message}";
                await Task.Delay(2000);
                StatusTextBlock.Visibility = Visibility.Collapsed;
                LoadingProgressBar.Visibility = Visibility.Collapsed;
            }
        }
        
        private string GetHighResolutionUrl(string thumbnailUrl, string imageId)
        {
            try
            {
                // Convert thumbnail URL to high-resolution URL
                // Example: https://backiee.com/static/wallpapers/560x315/123456.jpg
                // to https://backiee.com/static/wallpapers/wide/123456.jpg
                
                if (string.IsNullOrEmpty(thumbnailUrl) || string.IsNullOrEmpty(imageId))
                {
                    return thumbnailUrl; // Return original if we can't convert
                }
                
                // For Backiee, high-resolution wallpapers are in the "wide" directory
                return $"https://backiee.com/static/wallpapers/wide/{imageId}.jpg";
            }
            catch
            {
                return thumbnailUrl; // Return original URL if any error occurs
            }
        }
        
        private bool SetWindowsWallpaper(string wallpaperFilePath)
        {
            try
            {
                // Use Windows API to set wallpaper
                Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", true);
                
                // Set wallpaper style to Fill (2 is stretch, 0 is center, 6 is fit, 10 is fill, 22 is span)
                key.SetValue("WallpaperStyle", "10");
                key.SetValue("TileWallpaper", "0");
                
                // Set the wallpaper
                bool result = NativeMethods.SystemParametersInfo(
                    NativeMethods.SPI_SETDESKWALLPAPER,
                    0,
                    wallpaperFilePath,
                    NativeMethods.SPIF_UPDATEINIFILE | NativeMethods.SPIF_SENDCHANGE);
                
                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error setting wallpaper using Windows API");
                return false;
            }
        }

        private void FilterButton_Click(object sender, RoutedEventArgs e)
        {
            _logger?.LogInformation("Filter button clicked");
            
            // Create a filter dialog
            var filterWindow = new System.Windows.Window
            {
                Title = "Filter Wallpapers",
                Width = 400,
                Height = 450,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize
            };
            
            // Create the main panel with stacked controls
            var stackPanel = new System.Windows.Controls.StackPanel { Margin = new Thickness(20) };
            
            // Add category filter
            stackPanel.Children.Add(new System.Windows.Controls.TextBlock { 
                Text = "Category:", 
                FontWeight = FontWeights.SemiBold, 
                Margin = new Thickness(0, 10, 0, 5) 
            });
            
            var categoryComboBox = new System.Windows.Controls.ComboBox { 
                Margin = new Thickness(0, 0, 0, 15) 
            };
            
            // Add default "All" option
            categoryComboBox.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = "All", Tag = "all", IsSelected = true });
            
            // Add common categories
            string[] categories = {
                "Abstract", "Animals", "Anime", "Architecture", "Art", "Cars", "City", 
                "Fantasy", "Flowers", "Food", "Games", "Holidays", "Landscape", "Movies", 
                "Music", "Nature", "Space", "Sports", "Technology"
            };
            
            foreach (var category in categories)
            {
                categoryComboBox.Items.Add(new System.Windows.Controls.ComboBoxItem { 
                    Content = category, 
                    Tag = category.ToLowerInvariant() 
                });
            }
            
            stackPanel.Children.Add(categoryComboBox);
            
            // Add sort by filter
            stackPanel.Children.Add(new System.Windows.Controls.TextBlock { 
                Text = "Sort By:", 
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 10, 0, 5) 
            });
            
            var sortByComboBox = new System.Windows.Controls.ComboBox { 
                Margin = new Thickness(0, 0, 0, 15) 
            };
            
            // Add sorting options
            sortByComboBox.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = "Latest", Tag = "latest", IsSelected = true });
            sortByComboBox.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = "Most Popular", Tag = "popularity" });
            sortByComboBox.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = "Most Downloads", Tag = "downloads" });
            
            stackPanel.Children.Add(sortByComboBox);
            
            // Add AI filter
            stackPanel.Children.Add(new System.Windows.Controls.TextBlock { 
                Text = "AI Generation:", 
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 10, 0, 5) 
            });
            
            var aiComboBox = new System.Windows.Controls.ComboBox { 
                Margin = new Thickness(0, 0, 0, 15) 
            };
            
            aiComboBox.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = "Show All", Tag = "all", IsSelected = true });
            aiComboBox.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = "Only AI Generated", Tag = "1" });
            aiComboBox.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = "No AI Generated", Tag = "0" });
            
            stackPanel.Children.Add(aiComboBox);
            
            // Add resolution filter (checkbox for 4K, 5K, 8K)
            stackPanel.Children.Add(new System.Windows.Controls.TextBlock { 
                Text = "Resolution:", 
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 10, 0, 5) 
            });
            
            var resolutionPanel = new System.Windows.Controls.StackPanel { Margin = new Thickness(0, 0, 0, 15) };
            
            // This filter will only apply to the API calls, not to wallpapers already loaded
            var checkbox4K = new System.Windows.Controls.CheckBox { Content = "Include 4K", IsChecked = true };
            var checkbox5K = new System.Windows.Controls.CheckBox { Content = "Include 5K", IsChecked = true };
            var checkbox8K = new System.Windows.Controls.CheckBox { Content = "Include 8K", IsChecked = true };
            
            resolutionPanel.Children.Add(checkbox4K);
            resolutionPanel.Children.Add(checkbox5K);
            resolutionPanel.Children.Add(checkbox8K);
            
            stackPanel.Children.Add(resolutionPanel);
            
            // Add API toggle
            stackPanel.Children.Add(new System.Windows.Controls.TextBlock { 
                Text = "Data Source:", 
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 10, 0, 5) 
            });
            
            var apiToggle = new System.Windows.Controls.CheckBox { 
                Content = "Use API (recommended)",
                IsChecked = _useApiForLoading,
                Margin = new Thickness(0, 0, 0, 15)
            };
            
            stackPanel.Children.Add(apiToggle);
            
            // Add buttons panel
            var buttonsPanel = new System.Windows.Controls.StackPanel { 
                Orientation = System.Windows.Controls.Orientation.Horizontal, 
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                Margin = new Thickness(0, 15, 0, 0)
            };
            
            var cancelButton = new System.Windows.Controls.Button { 
                Content = "Cancel",
                Margin = new Thickness(0, 0, 10, 0)
            };
            
            var applyButton = new System.Windows.Controls.Button { 
                Content = "Apply Filters"
            };
            
            // Handle cancel button click
            cancelButton.Click += (s, args) => { filterWindow.Close(); };
            
            // Handle apply button click
            applyButton.Click += async (s, args) => 
            {
                try
                {
                    // Get selected category
                    var selectedCategory = categoryComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem;
                    if (selectedCategory != null && selectedCategory.Tag is string categoryTag)
                    {
                        _apiCategory = categoryTag;
                    }
                    
                    // Get selected sort by
                    var selectedSortBy = sortByComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem;
                    if (selectedSortBy != null && selectedSortBy.Tag is string sortByTag)
                    {
                        _apiSortBy = sortByTag;
                    }
                    
                    // Get selected AI filter
                    var selectedAI = aiComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem;
                    if (selectedAI != null && selectedAI.Tag is string aiTag)
                    {
                        _apiAiFilter = aiTag;
                    }
                    
                    // Get API toggle state
                    _useApiForLoading = apiToggle.IsChecked ?? true;
                    
                    // Reset to first page
                    _currentApiPage = 1;
                    
                    _logger?.LogInformation($"Applying filters: Category={_apiCategory}, SortBy={_apiSortBy}, AI={_apiAiFilter}, UseAPI={_useApiForLoading}");
                    
                    // Close the dialog
                    filterWindow.Close();
                    
                    // Clear existing wallpapers and load with new filters
                    await LoadInitialWallpapers();
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error applying filters");
                    System.Windows.MessageBox.Show(
                        $"Error applying filters: {ex.Message}", 
                        "Error", 
                        System.Windows.MessageBoxButton.OK, 
                        System.Windows.MessageBoxImage.Error);
                }
            };
            
            buttonsPanel.Children.Add(cancelButton);
            buttonsPanel.Children.Add(applyButton);
            stackPanel.Children.Add(buttonsPanel);
            
            // Set the content of the window
            filterWindow.Content = stackPanel;
            
            // Show the dialog
            filterWindow.ShowDialog();
        }

        private void SetAsSlideshowButton_Click(object sender, RoutedEventArgs e)
        {
            _logger?.LogInformation("Slideshow button clicked");
            
            // Show a simple message that this feature would be implemented
            System.Windows.MessageBox.Show(
                "The slideshow feature would:\n\n" +
                "1. Download wallpapers from API\n" +
                "2. Set up a Windows scheduled task\n" +
                "3. Change wallpapers automatically\n\n" +
                "This will be implemented in a future update.",
                "Slideshow Feature",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }
        
        private async Task SetupSlideshowAsync(int intervalMinutes, bool useFilters, bool useAiOnly)
        {
            try
            {
                // Show progress
                StatusTextBlock.Text = "Setting up slideshow...";
                StatusTextBlock.Visibility = Visibility.Visible;
                LoadingProgressBar.Visibility = Visibility.Visible;
                
                // Create a folder to store slideshow wallpapers
                string slideshowDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WallYouNeed", "Slideshow");
                if (!Directory.Exists(slideshowDir))
                {
                    Directory.CreateDirectory(slideshowDir);
                }
                
                // Create a folder for the script
                string scriptDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WallYouNeed", "Scripts");
                if (!Directory.Exists(scriptDir))
                {
                    Directory.CreateDirectory(scriptDir);
                }
                
                // Create PowerShell script to change wallpaper
                string scriptPath = Path.Combine(scriptDir, "ChangeWallpaper.ps1");
                string scriptContent = @"
                param (
                    [string]$WallpaperDirectory
                )

                # Function to set wallpaper
                function Set-Wallpaper {
                    param (
                        [string]$WallpaperPath
                    )
                    
                    Add-Type -TypeDefinition @""
                    using System;
                    using System.Runtime.InteropServices;

                    public class Wallpaper {
                        [DllImport(""user32.dll"", CharSet = CharSet.Auto)]
                        public static extern int SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);
                    }
                ""@

                    $SPI_SETDESKWALLPAPER = 0x0014
                    $SPIF_UPDATEINIFILE = 0x01
                    $SPIF_SENDCHANGE = 0x02
                    
                    # Set the wallpaper style in registry
                    Set-ItemProperty -Path 'HKCU:\\Control Panel\\Desktop' -Name WallpaperStyle -Value '10'
                    Set-ItemProperty -Path 'HKCU:\\Control Panel\\Desktop' -Name TileWallpaper -Value '0'
                    
                    # Set the wallpaper
                    [Wallpaper]::SystemParametersInfo($SPI_SETDESKWALLPAPER, 0, $WallpaperPath, $SPIF_UPDATEINIFILE -bor $SPIF_SENDCHANGE)
                }

                # Get all image files from the specified directory
                $wallpapers = Get-ChildItem -Path $WallpaperDirectory -Filter *.jpg

                if ($wallpapers.Count -gt 0) {
                    # Select a random wallpaper
                    $randomWallpaper = $wallpapers | Get-Random
                    $wallpaperPath = $randomWallpaper.FullName
                    
                    # Set it as the desktop wallpaper
                    Set-Wallpaper -WallpaperPath $wallpaperPath
                    
                    # Write to log
                    $logPath = Join-Path -Path $WallpaperDirectory -ChildPath 'slideshow_log.txt'
                    $timestamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
                    ""$timestamp - Set wallpaper: $wallpaperPath"" | Out-File -FilePath $logPath -Append
                }
                ";
                
                // Write the script
                await File.WriteAllTextAsync(scriptPath, scriptContent);
                
                // Download some initial wallpapers for the slideshow
                await DownloadSlideshowWallpapersAsync(slideshowDir, useFilters, useAiOnly);
                
                // Create task scheduler task
                string taskName = "WallYouNeedSlideshow";
                string powershellPath = "powershell.exe";
                string arguments = $"-ExecutionPolicy Bypass -File \"{scriptPath}\" -WallpaperDirectory \"{slideshowDir}\"";
                
                // Delete the task if it already exists
                var processInfo = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/Delete /TN \"{taskName}\" /F",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                
                try
                {
                    var process = Process.Start(processInfo);
                    await process.WaitForExitAsync();
                }
                catch
                {
                    // Ignore errors if task doesn't exist
                }
                
                // Create the new task
                string repeat = "/SC MINUTE /MO " + intervalMinutes;
                processInfo = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/Create /TN \"{taskName}\" /TR \"\\'{powershellPath}\\' {arguments}\" {repeat} /F",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                
                var createProcess = Process.Start(processInfo);
                await createProcess.WaitForExitAsync();
                
                if (createProcess.ExitCode != 0)
                {
                    throw new Exception($"Failed to create scheduled task. Exit code: {createProcess.ExitCode}");
                }
                
                // Run the task once immediately to set initial wallpaper
                processInfo = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/Run /TN \"{taskName}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                
                var runProcess = Process.Start(processInfo);
                await runProcess.WaitForExitAsync();
                
                // Show success message
                StatusTextBlock.Text = $"Slideshow setup successfully. Wallpaper will change every {intervalMinutes} minutes.";
                await Task.Delay(3000);
                StatusTextBlock.Visibility = Visibility.Collapsed;
                LoadingProgressBar.Visibility = Visibility.Collapsed;
                
                // Show MessageBox confirmation
                System.Windows.MessageBox.Show(
                    $"Wallpaper slideshow has been set up successfully. Your wallpaper will change every {intervalMinutes} minutes.\n\nA task has been created in Windows Task Scheduler.", 
                    "Slideshow Activated", 
                    System.Windows.MessageBoxButton.OK, 
                    System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error setting up slideshow");
                StatusTextBlock.Text = $"Error: {ex.Message}";
                await Task.Delay(3000);
                StatusTextBlock.Visibility = Visibility.Collapsed;
                LoadingProgressBar.Visibility = Visibility.Collapsed;
                
                throw; // Re-throw for the calling method to handle
            }
        }
        
        private async Task DownloadSlideshowWallpapersAsync(string slideshowDir, bool useFilters, bool useAiOnly)
        {
            try
            {
                StatusTextBlock.Text = "Downloading wallpapers for slideshow...";
                
                // For initial setup, download 10 wallpapers
                int wallpapersToDownload = 10;
                int downloaded = 0;
                
                // If we have wallpapers, use them first
                if (_wallpapers.Count > 0)
                {
                    // Apply filters if requested
                    var filteredWallpapers = _wallpapers.ToList();
                    
                    if (useAiOnly)
                    {
                        filteredWallpapers = filteredWallpapers.Where(w => w.IsAI).ToList();
                    }
                    
                    // Shuffle and take a subset
                    var random = new Random();
                    var selectedWallpapers = filteredWallpapers
                        .OrderBy(x => random.Next())
                        .Take(wallpapersToDownload)
                        .ToList();
                    
                    // Download each wallpaper
                    foreach (var wallpaper in selectedWallpapers)
                    {
                        try
                        {
                            // Get high resolution URL
                            string highResUrl = GetHighResolutionUrl(wallpaper.ImageUrl, wallpaper.ImageId);
                            
                            // Create a unique filename
                            string wallpaperFile = Path.Combine(slideshowDir, $"wallpaper_{wallpaper.ImageId}.jpg");
                            
                            // Download if doesn't exist already
                            if (!File.Exists(wallpaperFile))
                            {
                                using (var client = new HttpClient())
                                {
                                    client.DefaultRequestHeaders.Add("User-Agent", "WallYouNeed/1.0");
                                    
                                    // Download the image
                                    var imageBytes = await client.GetByteArrayAsync(highResUrl);
                                    
                                    // Save to file
                                    await File.WriteAllBytesAsync(wallpaperFile, imageBytes);
                                    
                                    downloaded++;
                                    StatusTextBlock.Text = $"Downloaded {downloaded} of {wallpapersToDownload} wallpapers...";
                                }
                            }
                            else
                            {
                                // Count existing files toward our total
                                downloaded++;
                                StatusTextBlock.Text = $"Wallpaper {wallpaper.ImageId} already exists ({downloaded} of {wallpapersToDownload})";
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogError(ex, $"Error downloading wallpaper {wallpaper.ImageId} for slideshow");
                            // Continue with next wallpaper
                        }
                    }
                }
                else
                {
                    // If no wallpapers are loaded, fetch some from API directly
                    string apiUrl;
                    
                    if (useFilters)
                    {
                        // Use current filters
                        apiUrl = $"{_apiBaseUrl}?action=paging_list&list_type={_apiSortBy}&page=1&page_size={wallpapersToDownload}&category={_apiCategory}&is_ai={(useAiOnly ? "1" : _apiAiFilter)}";
                    }
                    else
                    {
                        // Use default parameters but respect AI setting
                        apiUrl = $"{_apiBaseUrl}?action=paging_list&list_type=latest&page=1&page_size={wallpapersToDownload}&category=all&is_ai={(useAiOnly ? "1" : "all")}";
                    }
                    
                    using (var client = new HttpClient())
                    {
                        client.DefaultRequestHeaders.Add("User-Agent", "WallYouNeed/1.0");
                        
                        // Fetch wallpapers from API
                        var response = await client.GetAsync(apiUrl);
                        response.EnsureSuccessStatusCode();
                        
                        string jsonContent = await response.Content.ReadAsStringAsync();
                        
                        // Deserialize response
                        var options = new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true,
                            AllowTrailingCommas = true
                        };
                        
                        List<BackieeApiWallpaper> apiWallpapers = JsonSerializer.Deserialize<List<BackieeApiWallpaper>>(
                            jsonContent,
                            options
                        );
                        
                        if (apiWallpapers != null && apiWallpapers.Count > 0)
                        {
                            // Download each wallpaper
                            foreach (var apiWallpaper in apiWallpapers)
                            {
                                try
                                {
                                    if (string.IsNullOrEmpty(apiWallpaper.FullPhotoUrl))
                                        continue;
                                        
                                    // Create a unique filename
                                    string wallpaperFile = Path.Combine(slideshowDir, $"wallpaper_{apiWallpaper.ID}.jpg");
                                    
                                    // Download if doesn't exist already
                                    if (!File.Exists(wallpaperFile))
                                    {
                                        // Download the image
                                        var imageBytes = await client.GetByteArrayAsync(apiWallpaper.FullPhotoUrl);
                                        
                                        // Save to file
                                        await File.WriteAllBytesAsync(wallpaperFile, imageBytes);
                                        
                                        downloaded++;
                                        StatusTextBlock.Text = $"Downloaded {downloaded} of {wallpapersToDownload} wallpapers...";
                                    }
                                    else
                                    {
                                        // Count existing files toward our total
                                        downloaded++;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger?.LogError(ex, $"Error downloading wallpaper {apiWallpaper.ID} for slideshow");
                                    // Continue with next wallpaper
                                }
                            }
                        }
                    }
                }
                
                StatusTextBlock.Text = $"Slideshow setup: {downloaded} wallpapers downloaded.";
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error downloading wallpapers for slideshow");
                StatusTextBlock.Text = $"Error downloading wallpapers: {ex.Message}";
                await Task.Delay(3000);
                // Continue with setup even if downloads fail
            }
        }

        private async Task<bool> LoadWallpapersFromApiAsync()
        {
            try
            {
                // Build the API URL with parameters
                string apiUrl = BuildApiUrl();
                int currentPageBeforeLoading = _currentApiPage;
                _logger?.LogInformation($"Fetching wallpapers from API: {apiUrl} (Current page: {_currentApiPage})");

                // Create debug directory in Documents folder for easy access
                string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string debugDir = Path.Combine(documentsPath, "WallYouNeed", "Debug");
                try
                {
                    if (!Directory.Exists(debugDir))
                    {
                        Directory.CreateDirectory(debugDir);
                        _logger?.LogInformation($"Created debug directory at: {debugDir}");
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to create debug directory");
                }
                
                using (HttpClient client = new HttpClient())
                {
                    // Set User-Agent header to avoid potential blocking
                    client.DefaultRequestHeaders.Add("User-Agent", "WallYouNeed/1.0");
                    
                    // Send GET request to the API
                    _logger?.LogInformation("Sending API request...");
                    HttpResponseMessage response = await client.GetAsync(apiUrl);
                    _logger?.LogInformation($"API response status: {response.StatusCode}");
                    
                    // Check if the request was successful
                    if (!response.IsSuccessStatusCode)
                    {
                        _logger?.LogError($"API request failed with status code: {response.StatusCode}");
                        return false;
                    }
                    
                    // Read the response content
                    string jsonContent = await response.Content.ReadAsStringAsync();
                    _logger?.LogInformation($"Received API response, length: {jsonContent.Length} characters");

                    try
                    {
                        // Save raw response
                        string debugFile = Path.Combine(debugDir, $"api_response_{DateTime.Now:yyyyMMdd_HHmmss}_page{currentPageBeforeLoading}.json");
                        await File.WriteAllTextAsync(debugFile, jsonContent);
                        _logger?.LogInformation($"Saved API response to: {debugFile}");
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Failed to save API response debug file");
                    }
                    
                    // Deserialize JSON into list of BackieeApiWallpaper objects
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        AllowTrailingCommas = true
                    };

                    _logger?.LogInformation("Deserializing API response...");
                    List<BackieeApiWallpaper> apiWallpapers = JsonSerializer.Deserialize<List<BackieeApiWallpaper>>(
                        jsonContent,
                        options
                    );

                    if (apiWallpapers == null || apiWallpapers.Count == 0)
                    {
                        _logger?.LogWarning("No wallpapers found in the API response");
                        return false;
                    }

                    _logger?.LogInformation($"Successfully loaded {apiWallpapers.Count} wallpapers from API");
                    
                    try
                    {
                        // Save parsed data
                        string debugDataFile = Path.Combine(debugDir, $"api_parsed_{DateTime.Now:yyyyMMdd_HHmmss}_page{currentPageBeforeLoading}.txt");
                        using (var writer = new StreamWriter(debugDataFile))
                        {
                            await writer.WriteLineAsync($"API URL: {apiUrl}");
                            await writer.WriteLineAsync($"Total wallpapers: {apiWallpapers.Count}");
                            await writer.WriteLineAsync("-------------------");
                            
                            foreach (var wallpaper in apiWallpapers)
                            {
                                await writer.WriteLineAsync($"ID: {wallpaper.ID}");
                                await writer.WriteLineAsync($"Title: {wallpaper.Title}");
                                await writer.WriteLineAsync($"MiniPhotoUrl: {wallpaper.MiniPhotoUrl}");
                                await writer.WriteLineAsync($"FullPhotoUrl: {wallpaper.FullPhotoUrl}");
                                await writer.WriteLineAsync($"Resolution: {wallpaper.Resolution}");
                                await writer.WriteLineAsync($"UltraHD: {wallpaper.UltraHD}");
                                await writer.WriteLineAsync($"UltraHDType: {wallpaper.UltraHDType}");
                                await writer.WriteLineAsync($"AIGenerated: {wallpaper.AIGenerated}");
                                await writer.WriteLineAsync("-------------------");
                            }
                        }
                        _logger?.LogInformation($"Saved parsed data to: {debugDataFile}");
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Failed to save parsed data debug file");
                    }
                    
                    // Convert API wallpapers to our model and add to the UI
                    foreach (var apiWallpaper in apiWallpapers)
                    {
                        if (string.IsNullOrEmpty(apiWallpaper.MiniPhotoUrl))
                            continue;
                            
                        // Normalize the URL to lowercase for consistent comparison
                        string normalizedUrl = apiWallpaper.MiniPhotoUrl.ToLowerInvariant();
                        
                        // Skip if we already have this URL
                        if (_loadedUrls.Contains(normalizedUrl))
                            continue;
                            
                        // Extract image ID from API data
                        string imageId = apiWallpaper.ID ?? GetImageIdFromUrl(normalizedUrl);
                        
                        // Skip if we already have this imageId
                        if (_wallpapers.Any(img => img.ImageId == imageId))
                            continue;
                        
                        // Create a wallpaper item
                        var image = new WallpaperItem
                        {
                            ImageUrl = normalizedUrl,
                            ImageId = imageId,
                            IsAI = apiWallpaper.AIGenerated == "1",
                            Likes = int.TryParse(apiWallpaper.Rating, out int likesValue) ? likesValue : 0,
                            Downloads = int.TryParse(apiWallpaper.Downloads, out int downloadsValue) ? downloadsValue : 0
                        };
                        
                        // Set resolution based on quality
                        image.Resolution = apiWallpaper.Resolution ?? "1920x1080"; // Default
                        
                        if (!string.IsNullOrEmpty(apiWallpaper.UltraHDType))
                        {
                            image.ResolutionLabel = apiWallpaper.UltraHDType;
                        }
                        else if (apiWallpaper.UltraHD == "1")
                        {
                            image.ResolutionLabel = "4K"; // Default for UltraHD if type not specified
                        }
                        
                        _wallpapers.Add(image);
                        _loadedUrls.Add(normalizedUrl);
                        
                        // Create and add UI element
                        var wallpaperElement = CreateWallpaperElement(image);
                        WallpaperContainer.Children.Add(wallpaperElement);
                        
                        // Also track the ID to avoid re-attempting it
                        if (int.TryParse(imageId, out int parsedId))
                        {
                            _attemptedIds.Add(parsedId);
                        }
                    }
                    
                    // Increment the current page for next load
                    _currentApiPage++;
                    _logger?.LogInformation($"*** Incremented page counter from {currentPageBeforeLoading} to {_currentApiPage} for next API load ***");
                    
                    return _wallpapers.Count > 0;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error loading wallpapers from API: " + ex.Message);
                return false;
            }
        }
        
        private async Task LoadMoreWallpapersFromApiAsync(CancellationToken cancellationToken)
        {
            try
            {
                // Build the API URL with the current page
                string apiUrl = BuildApiUrl();
                int currentPageBeforeLoading = _currentApiPage;
                _logger?.LogInformation($"Loading more wallpapers from API: {apiUrl} (Current page: {_currentApiPage}, API index: {_currentApiPage-1})");
                
                StatusTextBlock.Text = $"Loading more wallpapers from API (Page: {_currentApiPage})...";
                StatusTextBlock.Visibility = Visibility.Visible;
                LoadingProgressBar.Visibility = Visibility.Visible;
                
                using (HttpClient client = new HttpClient())
                {
                    // Set User-Agent header
                    client.DefaultRequestHeaders.Add("User-Agent", "WallYouNeed/1.0");
                    
                    // Send GET request to the API
                    HttpResponseMessage response = await client.GetAsync(apiUrl, cancellationToken);
                    
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }
                    
                    // Check if the request was successful
                    if (!response.IsSuccessStatusCode)
                    {
                        _logger?.LogError($"API request failed with status code: {response.StatusCode}");
                        
                        StatusTextBlock.Text = $"Failed to load wallpapers from API: {response.StatusCode}";
                        await Task.Delay(2000, cancellationToken); // Show error briefly
                        StatusTextBlock.Visibility = Visibility.Collapsed;
                        LoadingProgressBar.Visibility = Visibility.Collapsed;
                        
                        return;
                    }
                    
                    // Read the response content
                    string jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }
                    
                    // Deserialize JSON
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        AllowTrailingCommas = true
                    };

                    List<BackieeApiWallpaper> apiWallpapers = JsonSerializer.Deserialize<List<BackieeApiWallpaper>>(
                        jsonContent,
                        options
                    );

                    if (apiWallpapers == null || apiWallpapers.Count == 0)
                    {
                        _logger?.LogWarning("No additional wallpapers found in the API response");
                        
                        StatusTextBlock.Text = "No more wallpapers available";
                        await Task.Delay(2000, cancellationToken); // Show message briefly
                        StatusTextBlock.Visibility = Visibility.Collapsed;
                        LoadingProgressBar.Visibility = Visibility.Collapsed;
                        
                        return;
                    }

                    int newWallpapersCount = 0;
                    
                    // Create a list to hold the new wallpaper elements
                    var newWallpaperElements = new List<FrameworkElement>();
                    
                    // Convert API wallpapers to our model
                    foreach (var apiWallpaper in apiWallpapers)
                    {
                        if (string.IsNullOrEmpty(apiWallpaper.MiniPhotoUrl))
                            continue;
                            
                        string normalizedUrl = apiWallpaper.MiniPhotoUrl.ToLowerInvariant();
                        
                        if (_loadedUrls.Contains(normalizedUrl))
                            continue;
                            
                        string imageId = apiWallpaper.ID ?? GetImageIdFromUrl(normalizedUrl);
                        
                        if (_wallpapers.Any(img => img.ImageId == imageId))
                            continue;
                        
                        var image = new WallpaperItem
                        {
                            ImageUrl = normalizedUrl,
                            ImageId = imageId,
                            IsAI = apiWallpaper.AIGenerated == "1",
                            Likes = int.TryParse(apiWallpaper.Rating, out int likesValue) ? likesValue : 0,
                            Downloads = int.TryParse(apiWallpaper.Downloads, out int downloadsValue) ? downloadsValue : 0
                        };
                        
                        image.Resolution = apiWallpaper.Resolution ?? "1920x1080";
                        
                        if (!string.IsNullOrEmpty(apiWallpaper.UltraHDType))
                        {
                            image.ResolutionLabel = apiWallpaper.UltraHDType;
                        }
                        else if (apiWallpaper.UltraHD == "1")
                        {
                            image.ResolutionLabel = "4K";
                        }
                        
                        _wallpapers.Add(image);
                        _loadedUrls.Add(normalizedUrl);
                        newWallpapersCount++;
                        
                        // Create UI element
                        var wallpaperElement = CreateWallpaperElement(image);
                        newWallpaperElements.Add(wallpaperElement);
                        
                        if (int.TryParse(imageId, out int parsedId))
                        {
                            _attemptedIds.Add(parsedId);
                        }
                    }
                    
                    // Add all new elements to the UI
                    foreach (var element in newWallpaperElements)
                    {
                        WallpaperContainer.Children.Add(element);
                    }
                    
                    _logger?.LogInformation($"Added {newWallpapersCount} new wallpapers from API (Page: {_currentApiPage})");
                    
                    // Increment the page number for the next API call
                    _currentApiPage++;
                    _logger?.LogInformation($"*** Incremented page counter from {currentPageBeforeLoading} to {_currentApiPage} for next API load ***");
                    
                    StatusTextBlock.Text = $"Loaded {newWallpapersCount} new wallpapers (Total: {_wallpapers.Count})";
                    await Task.Delay(1000, cancellationToken); // Show message briefly
                    StatusTextBlock.Visibility = Visibility.Collapsed;
                    LoadingProgressBar.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error loading more wallpapers from API");
                
                StatusTextBlock.Text = $"Error loading wallpapers: {ex.Message}";
                await Task.Delay(2000); // Show error briefly
                StatusTextBlock.Visibility = Visibility.Collapsed;
                LoadingProgressBar.Visibility = Visibility.Collapsed;
            }
        }
        
        private string BuildApiUrl()
        {
            // Page number starts from 0 in the API
            int apiPageIndex = _currentApiPage - 1;
            if (apiPageIndex < 0) apiPageIndex = 0;
            
            _logger?.LogInformation($"Building API URL for page {_currentApiPage} (API index: {apiPageIndex})");

            return $"{_apiBaseUrl}?action=paging_list&list_type={_apiSortBy}&page={apiPageIndex}&page_size={_apiPageSize}" +
                   $"&category={_apiCategory}&is_ai={_apiAiFilter}&sort_by=popularity" +
                   $"&4k=false&5k=false&8k=false&status=active&args=";
        }
    }

    /// <summary>
    /// Model class for wallpaper items in the grid
    /// </summary>
    public class WallpaperItem
    {
        public string ImageUrl { get; set; } = string.Empty;
        public string ImageId { get; set; } = string.Empty;
        public string Resolution { get; set; } = string.Empty;
        public string ResolutionLabel { get; set; } = string.Empty;
        public bool IsAI { get; set; }
        public int Likes { get; set; }
        public int Downloads { get; set; }
    }
    
    // Simple class to hold wallpaper data without any serialization complexities
    internal class SimpleWallpaper
    {
        public string Url { get; set; } = "";
        public string Quality { get; set; } = "";
        public bool IsAI { get; set; }
        public int Likes { get; set; }
        public int Downloads { get; set; }
    }

    // API model class to match the Backiee API response
    internal class BackieeApiWallpaper
    {
        public string? ID { get; set; }
        public string? Title { get; set; }
        public string? ThemeCat { get; set; }
        public string? Resolution { get; set; }
        public string? Rating { get; set; }
        public string? Downloads { get; set; }
        public string? UltraHD { get; set; }
        public string? UltraHDType { get; set; }
        public string? Uploaded { get; set; }
        public string? AIGenerated { get; set; }
        public string? FullPhotoUrl { get; set; }
        public string? MiniPhotoUrl { get; set; }
        public string? WallpaperUrl { get; set; }
    }

    // Native methods for Windows API calls
    internal static class NativeMethods
    {
        public const int SPI_SETDESKWALLPAPER = 0x0014;
        public const int SPIF_UPDATEINIFILE = 0x01;
        public const int SPIF_SENDCHANGE = 0x02;
        
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        public static extern bool SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);
    }
} 