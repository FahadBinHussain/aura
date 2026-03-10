using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Dispatching;
using System.Threading.Tasks;
using Aura.Models;
using System.Windows.Input;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using Microsoft.UI.Xaml.Media.Animation; // For Storyboard
using Microsoft.UI.Xaml.Data; // For value converter

namespace Aura.Views.Backiee
{
    public sealed partial class LatestWallpapersPage : Page
    {
        // Collection to hold the wallpapers
        private ObservableCollection<WallpaperItem> _wallpapers;

        // For HTTP requests
        private readonly HttpClient _httpClient = new HttpClient();

        // Preloaded placeholder image for faster loading
        private BitmapImage _placeholderImage;

        // For simulating delayed loading
        private DispatcherQueue _dispatcherQueue;

        // Timer for periodically checking placeholder images
        private DispatcherQueueTimer _placeholderTimer;

        // Properties for infinite scrolling
        private bool _isLoading = false;
        private int _currentPage = 0;
        private int _itemsPerPage = 30; // Exactly 30 items per page as requested
        private bool _hasMoreItems = true;
        private double _loadMoreThreshold = 0.4; // 40% of the scroll viewer height
        // No max pages limit - truly infinite scrolling

        // Flag to track if initial load is complete
        private bool _initialLoadComplete = false;

        // Base API URL for wallpaper requests
        private const string ApiBaseUrl = "https://backiee.com/api/wallpaper/list.php";

        public LatestWallpapersPage()
        {
            this.InitializeComponent();

            // Initialize the wallpapers collection
            _wallpapers = new ObservableCollection<WallpaperItem>();

            // Get the dispatcher queue for this thread
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

            // Preload the placeholder image
            InitializePlaceholderImage();

            // We're using a fully asynchronous approach now, so no timer is needed
            // _placeholderTimer = _dispatcherQueue.CreateTimer();
            // _placeholderTimer.Interval = TimeSpan.FromMilliseconds(1000);
            // _placeholderTimer.Tick += (s, e) => EnsurePlaceholdersForVisibleItems();
            // _placeholderTimer.Start();

            // Register for events
            Loaded += LatestWallpapersPage_Loaded;
            Unloaded += LatestWallpapersPage_Unloaded;
        }

        private void InitializePlaceholderImage()
        {
            try
            {
                _placeholderImage = new BitmapImage();
                _placeholderImage.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                _placeholderImage.UriSource = new Uri("ms-appx:///Assets/placeholder-wallpaper-1000.png");

                // Log success
                System.Diagnostics.Debug.WriteLine("Placeholder image initialized successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing placeholder image: {ex.Message}");
                // Create a fallback placeholder if needed
                _placeholderImage = new BitmapImage();
            }
        }

        private async void LatestWallpapersPage_Loaded(object sender, RoutedEventArgs e)
        {
            // Reset the initial load flag
            _initialLoadComplete = false;

            // Initialize the GridView with wallpapers collection
            WallpapersGridView.ItemsSource = _wallpapers;

            // Reset paging variables
            _currentPage = 0;
            _hasMoreItems = true;
            _wallpapers.Clear();

            // Load first page of wallpapers
            await LoadMoreWallpapers();

            // Mark initial load as complete
            _initialLoadComplete = true;
        }

        private void LatestWallpapersPage_Unloaded(object sender, RoutedEventArgs e)
        {
            // Clean up
            _wallpapers.Clear();

            // Stop timer
            if (_placeholderTimer != null)
            {
                _placeholderTimer.Stop();
                _placeholderTimer = null;
            }
        }

        private async Task LoadMoreWallpapers()
        {
            // Prevent multiple concurrent loading operations
            if (_isLoading || !_hasMoreItems)
                return;

            try
            {
                _isLoading = true;

                // Show loading indicator for every API call
                LoadingProgressBar.Visibility = Visibility.Visible;

                // Start the API call immediately
                string apiUrl = $"{ApiBaseUrl}?action=paging_list&list_type=latest&page={_currentPage}&page_size={_itemsPerPage}&category=all&is_ai=all&sort_by=popularity&4k=false&5k=false&8k=false&status=active&args=";

                // Fetch data from API
                HttpResponseMessage response = await _httpClient.GetAsync(apiUrl);

                if (response.IsSuccessStatusCode)
                {
                    string jsonContent = await response.Content.ReadAsStringAsync();
                    using (JsonDocument doc = JsonDocument.Parse(jsonContent))
                    {
                        // Process API response and create wallpaper items
                        List<WallpaperItem> newWallpapers = new List<WallpaperItem>();

                        foreach (JsonElement wallpaperElement in doc.RootElement.EnumerateArray())
                        {
                            // Extract wallpaper details from JSON
                            string id = wallpaperElement.GetProperty("ID").GetString();
                            string title = wallpaperElement.GetProperty("Title").GetString();

                            // Debug output the full JSON for this wallpaper
                            System.Diagnostics.Debug.WriteLine($"Wallpaper {id} JSON: {wallpaperElement.ToString()}");

                            // Try to get MiniPhotoUrl
                            string imageUrl;
                            try
                            {
                                imageUrl = wallpaperElement.GetProperty("MiniPhotoUrl").GetString();
                            }
                            catch (KeyNotFoundException)
                            {
                                // If MiniPhotoUrl doesn't exist, use a default or the regular photo URL from the API
                                imageUrl = wallpaperElement.GetProperty("FullPhotoUrl").GetString();
                                System.Diagnostics.Debug.WriteLine($"MiniPhotoUrl not found for wallpaper ID {id}, using regular photo URL instead");
                            }

                            // Get FullPhotoUrl with proper error handling
                            string fullPhotoUrl = string.Empty;
                            try
                            {
                                fullPhotoUrl = wallpaperElement.GetProperty("FullPhotoUrl").GetString();
                                System.Diagnostics.Debug.WriteLine($"FullPhotoUrl for ID {id}: {fullPhotoUrl}");
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Error getting FullPhotoUrl for ID {id}: {ex.Message}");
                                // Fallback to using the thumbnail URL if full photo URL is not available
                                fullPhotoUrl = imageUrl;
                            }

                            string resolution = wallpaperElement.GetProperty("Resolution").GetString();

                            // Extract tag data
                            string qualityTag = wallpaperElement.GetProperty("UltraHDType").GetString();
                            bool isAI = wallpaperElement.GetProperty("AIGenerated").GetString() == "1";
                            string likesCount = wallpaperElement.GetProperty("Rating").GetString();
                            string downloadsCount = wallpaperElement.GetProperty("Downloads").GetString();

                            // Create wallpaper item - don't try to load the image yet, just store the URL
                            var wallpaper = new WallpaperItem
                            {
                                Id = id,
                                Title = title,
                                ImageUrl = imageUrl, // Store URL instead of trying to load now
                                ImageSource = _placeholderImage, // Initially use placeholder
                                Resolution = resolution,
                                QualityTag = qualityTag,
                                IsAI = isAI,
                                Likes = likesCount,
                                Downloads = downloadsCount,
                                FullPhotoUrl = fullPhotoUrl // Store the full photo URL
                            };

                            newWallpapers.Add(wallpaper);
                        }

                        // Add all items at once for maximum speed
                        foreach (var wallpaper in newWallpapers)
                        {
                            _wallpapers.Add(wallpaper);
                        }
                    }

                    // Increment page counter for next load
                    _currentPage++;

                    // If we received fewer items than requested, we've reached the end
                    _hasMoreItems = true; // Always true for this API as it has many pages
                }
                else
                {
                    // If API request failed, mark that there are no more items
                    _hasMoreItems = false;
                }
            }
            catch (Exception ex)
            {
                // Log the exception
                System.Diagnostics.Debug.WriteLine($"Error loading wallpapers: {ex.Message}");
                _hasMoreItems = false;
            }
            finally
            {
                _isLoading = false;

                // Hide loading indicator
                LoadingProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        private async void MainScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            // Check if we need to load more items
            if (sender is ScrollViewer scrollViewer)
            {
                // Check if we're approaching the bottom
                double verticalOffset = scrollViewer.VerticalOffset;
                double maxVerticalOffset = scrollViewer.ScrollableHeight;

                // Load more items when the scrollbar is at the defined threshold of the scrollable content
                if (maxVerticalOffset > 0 &&
                    verticalOffset >= maxVerticalOffset * _loadMoreThreshold &&
                    !_isLoading)
                {
                    // LoadMoreWallpapers without showing the progress bar
                    await LoadMoreWallpapers();
                }
            }
        }

        private void WallpapersGridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is WallpaperItem wallpaper)
            {
                // Navigate to the WallpaperDetailPage and pass the full WallpaperItem object
                this.Frame.Navigate(typeof(WallpaperDetailPage), wallpaper);
            }
        }

        private void FilterButton_Click(object sender, RoutedEventArgs e)
        {
            // Just log to debug
            System.Diagnostics.Debug.WriteLine("Filter button clicked");
        }

        private void SetAsSlideshowButton_Click(object sender, RoutedEventArgs e)
        {
            // Just log to debug
            System.Diagnostics.Debug.WriteLine("Set as slideshow button clicked");
        }

        private void WallpapersWrapGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is ItemsWrapGrid wrapGrid)
            {
                // Calculate number of columns based on available width
                double availableWidth = e.NewSize.Width;

                // Determine desired item width (considering margins)
                double desiredItemWidth = 300;  // Base item width
                double itemMargin = 8;          // Total margin between items (4px on each side)

                // Calculate how many items can fit in the available width
                int columnsCount = Math.Max(1, (int)(availableWidth / desiredItemWidth));

                // Ensure we have a reasonable column count based on screen size
                columnsCount = Math.Min(columnsCount, 6);  // Limit to maximum 6 columns

                // Set the maximum columns
                wrapGrid.MaximumRowsOrColumns = columnsCount;

                // Calculate the new item width to fill the available space with margins
                double totalMarginWidth = (columnsCount - 1) * itemMargin;
                double newItemWidth = (availableWidth - totalMarginWidth) / columnsCount;

                // Set a reasonable minimum width
                double finalWidth = Math.Max(200, newItemWidth);

                // Calculate proportional height based on typical wallpaper aspect ratio (16:9)
                double aspectRatio = 16.0 / 9.0;
                double finalHeight = finalWidth / aspectRatio;

                // Set item dimensions - use only the image height since we removed the info panel
                wrapGrid.ItemWidth = finalWidth;
                wrapGrid.ItemHeight = finalHeight;

                // Make sure the grid fills all available space
                wrapGrid.HorizontalAlignment = HorizontalAlignment.Stretch;
            }
        }

        private void WallpapersGridView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            // Add logging to track container lifecycle
            System.Diagnostics.Debug.WriteLine($"Container changing for phase {args.Phase}, recycling: {args.InRecycleQueue}");

            if (args.InRecycleQueue)
            {
                // Use placeholder image for items that are being recycled
                var templateRoot = args.ItemContainer.ContentTemplateRoot as Grid;
                var image = templateRoot.FindName("ItemImage") as Image;
                if (image != null)
                {
                    // Always set to placeholder when recycling, don't try to reuse
                    image.Source = _placeholderImage;

                    // Clear the tag to prevent confusion
                    image.Tag = null;
                }
                return;
            }

            if (args.Phase == 0)
            {
                // Register for the next phase to load the actual image
                // Don't set placeholder here - it's already set in XAML
                args.RegisterUpdateCallback(ShowImage);
                args.Handled = true;
            }
        }

        private async void ShowImage(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.Phase == 1)
            {
                // Get the wallpaper item
                var wallpaper = args.Item as WallpaperItem;
                if (wallpaper == null) return;

                // Find the Image control
                var templateRoot = args.ItemContainer.ContentTemplateRoot as Grid;
                if (templateRoot == null) return;

                var image = templateRoot.FindName("ItemImage") as Image;
                if (image == null) return;

                // Get a unique identifier for this specific instance
                string imageKey = $"image_{wallpaper.Id}_{args.ItemContainer.GetHashCode()}";

                try
                {
                    // Set a tag to track the current loading request
                    image.Tag = imageKey;

                    // Make sure placeholder is showing while we load
                    image.Source = _placeholderImage;

                    // Handle the metadata display
                    SetItemMetadata(templateRoot, wallpaper);

                    // Now trigger an asynchronous load of the actual image
                    // This is done after setting metadata so UI is responsive
                    await LoadImageForItemAsync(image, wallpaper, imageKey);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error in ShowImage: {ex.Message}");
                }
            }
        }

        private async Task LoadImageForItemAsync(Image imageControl, WallpaperItem wallpaper, string requestKey)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Starting image load for {wallpaper.Id} with key {requestKey}");

                // Load the image asynchronously
                var bitmap = await wallpaper.LoadImageAsync();

                // If we got a valid bitmap
                if (bitmap != null)
                {
                    // Set up event handlers to track loading
                    bitmap.ImageOpened += (s, e) =>
                    {
                        System.Diagnostics.Debug.WriteLine($"SUCCESS: Image loaded for {wallpaper.Id}");

                        // After successful load, update the item's ImageSource
                        wallpaper.ImageSource = bitmap;

                        // Double check tag to ensure we're setting the right image
                        if (_dispatcherQueue.HasThreadAccess && imageControl.Tag?.ToString() == requestKey)
                        {
                            imageControl.Source = bitmap;
                        }
                    };

                    bitmap.ImageFailed += (s, e) =>
                    {
                        System.Diagnostics.Debug.WriteLine($"FAILED: Image failed to load for {wallpaper.Id}: {e.ErrorMessage}");

                        // If image fails to load, ensure we keep the placeholder
                        if (_dispatcherQueue.HasThreadAccess && imageControl.Tag?.ToString() == requestKey)
                        {
                            imageControl.Source = _placeholderImage;
                        }
                    };

                    // Check if this image control is still showing the same item
                    if (imageControl.Tag?.ToString() == requestKey)
                    {
                        // Always update on UI thread to avoid threading issues
                        if (_dispatcherQueue.HasThreadAccess)
                        {
                            System.Diagnostics.Debug.WriteLine($"Setting image source for {wallpaper.Id}");
                            imageControl.Source = bitmap;
                        }
                        else
                        {
                            _dispatcherQueue.TryEnqueue(() =>
                            {
                                if (imageControl.Tag?.ToString() == requestKey)
                                {
                                    System.Diagnostics.Debug.WriteLine($"Setting image source via dispatcher for {wallpaper.Id}");
                                    imageControl.Source = bitmap;
                                }
                            });
                        }
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to create bitmap for {wallpaper.Id}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Exception loading image for {wallpaper.Id}: {ex.Message}");

                // Keep the placeholder on failure
                if (imageControl.Tag?.ToString() == requestKey && _dispatcherQueue.HasThreadAccess)
                {
                    imageControl.Source = _placeholderImage;
                }
            }
        }

        // Extract metadata setup to a separate method for clarity
        private void SetItemMetadata(Grid templateRoot, WallpaperItem wallpaper)
        {
            // Handle quality tag
            var qualityTagBorder = templateRoot.FindName("QualityTagBorder") as Border;
            var qualityImage = templateRoot.FindName("QualityImage") as Image;
            if (qualityTagBorder != null && qualityImage != null && !string.IsNullOrEmpty(wallpaper.QualityTag))
            {
                qualityTagBorder.Visibility = Visibility.Visible;
                // Set the quality image source
                string qualityImagePath = wallpaper.QualityLogoPath;
                if (!string.IsNullOrEmpty(qualityImagePath))
                {
                    qualityImage.Source = new BitmapImage(new Uri(qualityImagePath));
                }
            }

            // Handle AI tag
            var aiTagBorder = templateRoot.FindName("AITagBorder") as Border;
            var aiImage = templateRoot.FindName("AIImage") as Image;
            if (aiTagBorder != null && aiImage != null)
            {
                aiTagBorder.Visibility = wallpaper.IsAI ? Visibility.Visible : Visibility.Collapsed;
                if (wallpaper.IsAI)
                {
                    aiImage.Source = new BitmapImage(new Uri("ms-appx:///Assets/aigenerated-icon.png"));
                }
            }

            // Handle likes and downloads text
            var likesText = templateRoot.FindName("LikesText") as TextBlock;
            if (likesText != null)
            {
                likesText.Text = wallpaper.Likes;
            }

            var downloadsText = templateRoot.FindName("DownloadsText") as TextBlock;
            if (downloadsText != null)
            {
                downloadsText.Text = wallpaper.Downloads;
            }
        }

        // Make sure we always set placeholders for all visible items
        private void EnsurePlaceholdersForVisibleItems()
        {
            try
            {
                if (WallpapersGridView.ItemsPanelRoot == null) return;

                foreach (var item in WallpapersGridView.ItemsPanelRoot.Children)
                {
                    var container = item as GridViewItem;
                    if (container != null)
                    {
                        var templateRoot = container.ContentTemplateRoot as Grid;
                        var image = templateRoot?.FindName("ItemImage") as Image;
                        if (image != null)
                        {
                            // Only set placeholder if there is absolutely no image showing
                            if (image.Source == null)
                            {
                                image.Source = _placeholderImage;
                                System.Diagnostics.Debug.WriteLine("Set placeholder for idle item with no source");
                            }
                            // Don't override images that are already being loaded or displayed
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error setting placeholders: {ex.Message}");
            }
        }
    }
}
