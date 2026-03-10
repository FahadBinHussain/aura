using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wpf.Ui;
using Wpf.Ui.Controls;
using WallYouNeed.Core.Models;
using WallYouNeed.Core.Services.Interfaces;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Collections.Concurrent;
using WallYouNeed.App.Pages;

namespace WallYouNeed.App.Pages
{
    public partial class CategoryPage : Page, INavigableView<CategoryPage>
    {
        private readonly ILogger<CategoryPage> _logger;
        private readonly IWallpaperService _wallpaperService;
        private readonly ISettingsService _settingsService;
        
        private string _currentCategory = string.Empty;
        private volatile bool _isLoadingMore = false;
        private readonly SemaphoreSlim _loadingSemaphore = new SemaphoreSlim(1, 1);
        private readonly int _batchSize = 20;
        private readonly int _scrollThreshold = 400;
        private CancellationTokenSource _cts;
        private DateTime _lastScrollCheck = DateTime.MinValue;
        private readonly TimeSpan _scrollDebounceTime = TimeSpan.FromMilliseconds(250);
        private HashSet<string> _loadedUrls = new HashSet<string>();
        
        public ObservableCollection<Core.Models.Wallpaper> Wallpapers { get; } = new();
        public ObservableCollection<WallpaperItem> Images { get; set; }
        
        public string CategoryTitle { get; private set; } = "Category";
        public string CategoryDescription { get; private set; } = "Browse wallpapers by category.";
        
        public CategoryPage ViewModel => this;
        
        public CategoryPage(
            ILogger<CategoryPage> logger,
            IWallpaperService wallpaperService,
            ISettingsService settingsService)
        {
            _logger = logger;
            _wallpaperService = wallpaperService;
            _settingsService = settingsService;
            
            InitializeComponent();
            Images = new ObservableCollection<WallpaperItem>();
            DataContext = this;
        }
        
        public void SetCategory(string category)
        {
            _currentCategory = category;
            
            // Update the UI with the selected category
            switch (category.ToLowerInvariant())
            {
                case "weekly":
                    CategoryTitle = "Weekly Picks";
                    CategoryDescription = "Our picks for this week's best wallpapers.";
                    break;
                case "monthly":
                    CategoryTitle = "Monthly Showcase";
                    CategoryDescription = "The most popular wallpapers from this month.";
                    break;
                case "nature":
                    CategoryTitle = "Nature";
                    CategoryDescription = "Beautiful landscapes, wildlife, and natural wonders.";
                    break;
                case "architecture":
                    CategoryTitle = "Architecture";
                    CategoryDescription = "Urban landscapes, buildings, and architectural wonders.";
                    break;
                case "abstract":
                    CategoryTitle = "Abstract";
                    CategoryDescription = "Creative, abstract, and artistic wallpapers.";
                    break;
                default:
                    CategoryTitle = $"{category}";
                    CategoryDescription = $"Wallpapers in the {category} category.";
                    break;
            }
            
            // Refresh UI to show the new title and description
            if (CategoryTitleTextBlock != null)
                CategoryTitleTextBlock.Text = CategoryTitle;
                
            if (CategoryDescriptionTextBlock != null)
                CategoryDescriptionTextBlock.Text = CategoryDescription;
            
            // Load wallpapers for this category when it's set
            LoadWallpapersForCategory(category);
        }
        
        private async void LoadWallpapersForCategory(string category)
        {
            try
            {
                _logger.LogInformation("Loading wallpapers for category: {Category}", category);
                
                // Show loading animation
                if (LoadingProgressRing != null)
                    LoadingProgressRing.Visibility = Visibility.Visible;
                
                // Clear existing wallpapers
                Wallpapers.Clear();
                
                // Also clear the UI panel
                if (WallpapersPanel != null)
                    WallpapersPanel.Children.Clear();
                
                // Handle different category types
                switch (category.ToLowerInvariant())
                {
                    case "latest":
                        await LoadWallpapersAsync();
                        break;
                    case "weekly":
                    case "monthly":
                        // For demo purposes, we'll just use recent wallpapers
                        var recentWallpapers = await _wallpaperService.GetRecentWallpapersAsync(20);
                        foreach (var wallpaper in recentWallpapers)
                        {
                            Wallpapers.Add(wallpaper);
                            AddWallpaperCardToUI(wallpaper);
                        }
                        break;
                    default:
                        // For other categories, load existing wallpapers with matching tags
                        var wallpapers = await _wallpaperService.GetWallpapersByTagAsync(category);
                        foreach (var wallpaper in wallpapers)
                        {
                            Wallpapers.Add(wallpaper);
                            AddWallpaperCardToUI(wallpaper);
                        }
                        
                        // If no wallpapers are found, show a message
                        if (!wallpapers.Any())
                        {
                            _logger.LogInformation("No wallpapers found in the {Category} category.", category);
                            
                            // Show the empty state message
                            if (NoWallpapersMessage != null)
                                NoWallpapersMessage.Visibility = Visibility.Visible;
                        }
                        break;
                }
                
                _logger.LogInformation("Loaded {Count} wallpapers for category: {Category}", 
                    Wallpapers.Count, category);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading wallpapers for category: {Category}", category);
            }
            finally
            {
                // Hide loading animation when done
                if (LoadingProgressRing != null)
                    LoadingProgressRing.Visibility = Visibility.Collapsed;
            }
        }
        
        private async Task LoadWallpapersAsync()
        {
            try
            {
                string jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "wallpapers_pretty.json");
                _logger?.LogInformation($"Loading wallpapers from JSON file: {jsonPath}");

                if (!File.Exists(jsonPath))
                {
                    _logger?.LogError($"JSON file not found: {jsonPath}");
                    return;
                }

                string jsonContent = await File.ReadAllTextAsync(jsonPath);
                var wallpaperItems = JsonSerializer.Deserialize<List<dynamic>>(jsonContent);

                if (wallpaperItems == null)
                {
                    _logger?.LogError("Failed to deserialize wallpapers from JSON");
                    return;
                }

                foreach (var item in wallpaperItems)
                {
                    // Create a wallpaper object from the JSON structure
                    var wallpaper = new Core.Models.Wallpaper
                    {
                        Id = item.GetProperty("ID").GetString() ?? "",
                        Title = item.GetProperty("Title").GetString() ?? "",
                        Description = item.GetProperty("Description").GetString() ?? "",
                        SourceUrl = item.GetProperty("WallpaperUrl").GetString() ?? "",
                        ThumbnailUrl = item.GetProperty("MiniPhotoUrl").GetString() ?? "",
                        // Extract tags - assuming we collect specific fields as tags
                        Tags = new List<string>(),
                        Width = 0,
                        Height = 0,
                        Likes = int.TryParse(item.GetProperty("Rating").GetString(), out int likes) ? likes : 0,
                        Downloads = int.TryParse(item.GetProperty("Downloads").GetString(), out int downloads) ? downloads : 0,
                    };

                    // Add tags based on properties
                    if (item.GetProperty("UltraHD").GetString() == "1")
                    {
                        var uhdType = item.GetProperty("UltraHDType").GetString();
                        if (!string.IsNullOrEmpty(uhdType))
                        {
                            wallpaper.Tags.Add(uhdType);
                        }
                    }

                    // Add AI tag if appropriate
                    if (item.GetProperty("AIGenerated").GetString() == "1")
                    {
                        wallpaper.Tags.Add("ai");
                    }

                    // Add likes tag
                    wallpaper.Tags.Add($"likes:{wallpaper.Likes}");

                    // Add downloads tag
                    wallpaper.Tags.Add($"downloads:{wallpaper.Downloads}");

                    // Extract resolution
                    string resolution = item.GetProperty("Resolution").GetString() ?? "";
                    if (!string.IsNullOrEmpty(resolution) && resolution.Contains('x'))
                    {
                        var parts = resolution.Split('x');
                        if (parts.Length == 2 && int.TryParse(parts[0], out int width) && int.TryParse(parts[1], out int height))
                        {
                            wallpaper.Width = width;
                            wallpaper.Height = height;
                        }
                    }

                    ConvertAndAddWallpaper(wallpaper);
                }

                _logger?.LogInformation($"Successfully loaded {Images.Count} wallpapers");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error loading wallpapers from JSON file");
            }
        }

        private void ConvertAndAddWallpaper(Core.Models.Wallpaper wallpaper)
        {
            if (wallpaper == null) return;

            var wallpaperItem = new WallpaperItem
            {
                ImageUrl = wallpaper.ThumbnailUrl,
                ImageId = wallpaper.Id,
                Resolution = $"{wallpaper.Width}x{wallpaper.Height}"
            };

            Images.Add(wallpaperItem);
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Navigate back to the home page
                if (Window.GetWindow(this) is MainWindow mainWindow &&
                    mainWindow.FindName("MainFrame") is Frame mainFrame)
                {
                    if (mainFrame.CanGoBack)
                    {
                        mainFrame.GoBack();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error navigating back from category page");
            }
        }
        
        // Placeholder implementations for XAML event handlers to avoid build errors
        private void SortButton_Click(object sender, RoutedEventArgs e)
        {
            _logger.LogInformation("Sorting functionality is not implemented yet.");
        }
        
        private void AddWallpaperButton_Click(object sender, RoutedEventArgs e)
        {
            _logger.LogInformation("Add wallpaper functionality is not implemented yet.");
        }
        
        // Add a new method to create and add wallpaper cards to the UI
        private void AddWallpaperCardToUI(Core.Models.Wallpaper wallpaper)
        {
            try
            {
                if (WallpapersPanel == null)
                {
                    _logger.LogWarning("WallpapersPanel is null, cannot add wallpaper card to UI");
                    return;
                }
                
                _logger.LogInformation("Creating wallpaper card for ID: {Id}, Title: {Title}", wallpaper.Id, wallpaper.Title);
                _logger.LogDebug("Thumbnail URL: {ThumbnailUrl}", wallpaper.ThumbnailUrl);
                _logger.LogDebug("Source URL: {SourceUrl}", wallpaper.SourceUrl);
                
                // Create the card
                var card = new Wpf.Ui.Controls.Card
                {
                    Margin = new Thickness(8),
                    Width = 280,
                    Height = 200
                };
                
                // Create the grid to hold the image and info
                var grid = new Grid();
                
                // Create the image
                var image = new System.Windows.Controls.Image
                {
                    Stretch = Stretch.UniformToFill
                };
                
                bool imageLoaded = false;
                
                // Load the image from URL
                try
                {
                    _logger.LogDebug("Attempting to load thumbnail URL: {Url}", wallpaper.ThumbnailUrl);
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    
                    // Add a handler for DownloadFailed event
                    bitmap.DownloadFailed += (s, e) => {
                        _logger.LogError("Image download failed for URL: {Url}, Error: {Error}", 
                            wallpaper.ThumbnailUrl, e.ErrorException?.Message ?? "Unknown error");
                    };
                    
                    // Create a new Uri with UriKind.Absolute to ensure it's treated as an absolute URL
                    bitmap.UriSource = new Uri(wallpaper.ThumbnailUrl, UriKind.Absolute);
                    bitmap.EndInit();
                    
                    image.Source = bitmap;
                    imageLoaded = true;
                    _logger.LogInformation("Successfully loaded thumbnail from URL: {Url}", wallpaper.ThumbnailUrl);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error loading thumbnail for wallpaper {Id}: {Url}", 
                        wallpaper.Id, wallpaper.ThumbnailUrl);
                    
                    // Try the source URL if thumbnail fails
                    try
                    {
                        _logger.LogDebug("Attempting to load source URL: {Url}", wallpaper.SourceUrl);
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        
                        // Add a handler for DownloadFailed event
                        bitmap.DownloadFailed += (s, e) => {
                            _logger.LogError("Image download failed for URL: {Url}, Error: {Error}", 
                                wallpaper.SourceUrl, e.ErrorException?.Message ?? "Unknown error");
                        };
                        
                        // Create a new Uri with UriKind.Absolute to ensure it's treated as an absolute URL
                        bitmap.UriSource = new Uri(wallpaper.SourceUrl, UriKind.Absolute);
                        bitmap.EndInit();
                        
                        image.Source = bitmap;
                        imageLoaded = true;
                        _logger.LogInformation("Successfully loaded image from source URL: {Url}", wallpaper.SourceUrl);
                    }
                    catch (Exception innerEx)
                    {
                        _logger.LogError(innerEx, "Also failed to load source URL: {Url}", wallpaper.SourceUrl);
                        
                        // Create a very simple colored rectangle with text as a fallback
                        DrawFallbackImage(image, wallpaper);
                    }
                }
                
                // Create the info panel at the bottom
                var infoBorder = new Border
                {
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(176, 0, 0, 0)), // #B0000000
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Padding = new Thickness(10, 8, 10, 8)
                };
                
                var infoPanel = new StackPanel();
                
                // Title
                var title = new System.Windows.Controls.TextBlock
                {
                    Text = wallpaper.Title ?? "Untitled Wallpaper",
                    Foreground = System.Windows.Media.Brushes.White,
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold
                };
                
                // Source info
                string sourceInfo = "Source: Unknown";
                if (wallpaper.Metadata != null && wallpaper.Metadata.ContainsKey("Source"))
                {
                    sourceInfo = $"Source: {wallpaper.Metadata["Source"]}";
                }
                
                var source = new System.Windows.Controls.TextBlock
                {
                    Text = sourceInfo,
                    Foreground = System.Windows.Media.Brushes.LightGray,
                    FontSize = 12,
                    Margin = new Thickness(0, 4, 0, 0)
                };
                
                // Action buttons
                var buttonsPanel = new StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Horizontal,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                    Margin = new Thickness(0, 8, 0, 0)
                };
                
                // Apply button
                var applyButton = new Wpf.Ui.Controls.Button
                {
                    Content = "Apply",
                    Appearance = Wpf.Ui.Controls.ControlAppearance.Primary,
                    Padding = new Thickness(8, 4, 8, 4),
                    Margin = new Thickness(0, 0, 4, 0),
                    FontSize = 12
                };
                
                applyButton.Click += (s, e) => ApplyWallpaper(wallpaper);
                
                // Favorite button
                var favoriteButton = new Wpf.Ui.Controls.Button
                {
                    Content = "♡",
                    Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary,
                    Padding = new Thickness(8, 4, 8, 4),
                    Margin = new Thickness(4, 0, 4, 0),
                    FontSize = 12
                };
                
                favoriteButton.Click += (s, e) => ToggleFavorite(wallpaper);
                
                // More options button
                var moreButton = new Wpf.Ui.Controls.Button
                {
                    Content = "⋮",
                    Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary,
                    Padding = new Thickness(8, 4, 8, 4),
                    Margin = new Thickness(4, 0, 0, 0),
                    FontSize = 12
                };
                
                moreButton.Click += (s, e) => ShowMoreOptions(wallpaper);
                
                // Add buttons to panel
                buttonsPanel.Children.Add(applyButton);
                buttonsPanel.Children.Add(favoriteButton);
                buttonsPanel.Children.Add(moreButton);
                
                // Add elements to info panel
                infoPanel.Children.Add(title);
                infoPanel.Children.Add(source);
                infoPanel.Children.Add(buttonsPanel);
                
                // Add info panel to border
                infoBorder.Child = infoPanel;
                
                // Add elements to grid
                grid.Children.Add(image);
                grid.Children.Add(infoBorder);
                
                // Add grid to card
                card.Content = grid;
                
                // Add click event to the card for viewing details
                card.MouseLeftButtonUp += (s, e) => ViewWallpaperDetails(wallpaper);
                
                // Add card to the panel
                WallpapersPanel.Children.Add(card);
                
                // Log success
                _logger.LogInformation("Added wallpaper card to UI for wallpaper ID: {Id}, Image loaded: {ImageLoaded}", 
                    wallpaper.Id, imageLoaded);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding wallpaper card to UI for wallpaper {Id}", wallpaper.Id);
            }
        }
        
        // Helper method to create a fallback image when loading fails
        private void DrawFallbackImage(System.Windows.Controls.Image imageControl, Core.Models.Wallpaper wallpaper)
        {
            try
            {
                _logger.LogInformation("Creating fallback image for wallpaper ID: {Id}", wallpaper.Id);
                
                // Create a drawing visual
                var drawingVisual = new DrawingVisual();
                using (var drawingContext = drawingVisual.RenderOpen())
                {
                    // Draw a colored background
                    drawingContext.DrawRectangle(
                        new SolidColorBrush(System.Windows.Media.Color.FromRgb(96, 125, 139)), // Material Design Blue Gray 500
                        null,
                        new Rect(0, 0, 280, 200));
                    
                    // Draw the wallpaper ID text
                    var idText = new FormattedText(
                        $"ID: {wallpaper.Id}",
                        System.Globalization.CultureInfo.CurrentCulture,
                        System.Windows.FlowDirection.LeftToRight,
                        new Typeface("Segoe UI"),
                        14,
                        System.Windows.Media.Brushes.White,
                        VisualTreeHelper.GetDpi(drawingVisual).PixelsPerDip);
                    
                    drawingContext.DrawText(idText, new System.Windows.Point(10, 10));
                    
                    // Draw the error message
                    var errorText = new FormattedText(
                        "Image Failed to Load",
                        System.Globalization.CultureInfo.CurrentCulture,
                        System.Windows.FlowDirection.LeftToRight,
                        new Typeface("Segoe UI Semibold"),
                        18,
                        System.Windows.Media.Brushes.White,
                        VisualTreeHelper.GetDpi(drawingVisual).PixelsPerDip);
                    
                    // Center the text
                    drawingContext.DrawText(
                        errorText, 
                        new System.Windows.Point((280 - errorText.Width) / 2, (200 - errorText.Height) / 2));
                    
                    // Draw the URL hint at the bottom
                    var urlText = new FormattedText(
                        "Check URL format in logs",
                        System.Globalization.CultureInfo.CurrentCulture,
                        System.Windows.FlowDirection.LeftToRight,
                        new Typeface("Segoe UI"),
                        12,
                        System.Windows.Media.Brushes.White,
                        VisualTreeHelper.GetDpi(drawingVisual).PixelsPerDip);
                    
                    drawingContext.DrawText(urlText, new System.Windows.Point(10, 170));
                }
                
                // Convert drawing to bitmap
                var renderTarget = new RenderTargetBitmap(
                    280, 200, 96, 96, PixelFormats.Pbgra32);
                renderTarget.Render(drawingVisual);
                
                // Set as image source
                imageControl.Source = renderTarget;
                
                _logger.LogInformation("Fallback image created successfully for wallpaper ID: {Id}", wallpaper.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating fallback image for wallpaper ID: {Id}", wallpaper.Id);
            }
        }
        
        // Placeholder methods for wallpaper actions (to be implemented later)
        private void ApplyWallpaper(Core.Models.Wallpaper wallpaper)
        {
            _logger.LogInformation("Apply wallpaper clicked: {Id}", wallpaper.Id);
            // Implementation for applying wallpaper
        }
        
        private void ToggleFavorite(Core.Models.Wallpaper wallpaper)
        {
            _logger.LogInformation("Toggle favorite clicked: {Id}", wallpaper.Id);
            // Implementation for toggling favorite status
        }
        
        private void ShowMoreOptions(Core.Models.Wallpaper wallpaper)
        {
            _logger.LogInformation("More options clicked: {Id}", wallpaper.Id);
            // Implementation for showing more options
        }
        
        private void ViewWallpaperDetails(Core.Models.Wallpaper wallpaper)
        {
            _logger.LogInformation("View wallpaper details clicked: {Id}", wallpaper.Id);
            // Implementation for viewing wallpaper details
        }
    }
}