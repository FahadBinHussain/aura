using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Aura.Models;
using Aura.Services;

using System.Text;

namespace Aura.Views.AlphaCoders
{
    public sealed partial class AlphaCodersGridPage : Page
    {
        private ObservableCollection<WallpaperItem> _wallpapers = new ObservableCollection<WallpaperItem>();
        private AlphaCodersService _alphaCodersService;
        private int _currentPage = 1;
        private bool _isLoading = false;
        private bool _hasMoreWallpapers = true;
        private StringBuilder _debugLog = new StringBuilder();
        private bool _debugVisible = false;
        private bool _isInitialized = false;
        private string _currentCategory = "4k"; // Track current category

        public AlphaCodersGridPage()
        {
            this.InitializeComponent();

            // Enable navigation caching to preserve page state
            this.NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;

            _alphaCodersService = new AlphaCodersService();
            
            // Set up debug logging
            AlphaCodersService.DebugLogger = AppendDebugLog;
            AppendDebugLog("AlphaCodersGridPage initialized");
            
            // Set ItemsSource after all initialization is complete
            try
            {
                if (WallpapersGridView != null)
                {
                    WallpapersGridView.ItemsSource = _wallpapers;
                }
            }
            catch (Exception ex)
            {
                AppendDebugLog($"Error setting ItemsSource: {ex.Message}");
            }
        }

        protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            // Only load wallpapers if this is the first time navigating to this page
            if (!_isInitialized)
            {
                _isInitialized = true;
                LoadWallpapers();
            }
            else
            {
                AppendDebugLog("Page already initialized, preserving state");
            }
        }

        private void CategoryButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                string category = button.Tag?.ToString() ?? "4k";
                
                // Update button styles
                UpdateCategoryButtonStyles(category);
                
                // Update title
                PageTitleTextBlock.Text = button.Content?.ToString() ?? "Alpha Coders Wallpapers";
                
                // Reset and reload wallpapers for new category
                _currentCategory = category;
                _wallpapers.Clear();
                _currentPage = 1;
                _hasMoreWallpapers = true;
                
                AppendDebugLog($"Switched to category: {category}");
                LoadWallpapers();
            }
        }

        private void UpdateCategoryButtonStyles(string selectedCategory)
        {
            // Reset all buttons to default style
            FourKButton.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
            FourKButton.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
            HarvestButton.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
            HarvestButton.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
            RainButton.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
            RainButton.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
            
            // Highlight selected button
            Button selectedButton = selectedCategory switch
            {
                "4k" => FourKButton,
                "harvest" => HarvestButton,
                "rain" => RainButton,
                _ => FourKButton
            };
            
            selectedButton.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
            selectedButton.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
        }

        private async void LoadWallpapers()
        {
            if (_isLoading || !_hasMoreWallpapers)
                return;

            try
            {
                _isLoading = true;
                LoadingProgressBar.Visibility = Visibility.Visible;
                AppendDebugLog($"Loading page {_currentPage} for category {_currentCategory}");

                var newWallpapers = await _alphaCodersService.GetWallpapersByCategoryAsync(_currentCategory, _currentPage);
                AppendDebugLog($"Received {newWallpapers.Count} wallpapers");

                if (newWallpapers.Count == 0)
                {
                    _hasMoreWallpapers = false;
                    AppendDebugLog("No more wallpapers available");
                }
                else
                {
                    foreach (var wallpaper in newWallpapers)
                    {
                        _wallpapers.Add(wallpaper);
                    }
                    _currentPage++;
                    AppendDebugLog($"Added {newWallpapers.Count} wallpapers, page: {_currentPage}");
                }
            }
            catch (Exception ex)
            {
                AppendDebugLog($"Error loading wallpapers: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Error loading wallpapers: {ex.Message}");
            }
            finally
            {
                _isLoading = false;
                LoadingProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        private void MainScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            // Load more wallpapers when scrolling near the bottom
            if (!e.IsIntermediate && MainScrollViewer.VerticalOffset >= MainScrollViewer.ScrollableHeight - 200)
            {
                LoadWallpapers();
            }
        }

        private void WallpapersGridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            try
            {
                AppendDebugLog("ItemClick event triggered");

                if (e?.ClickedItem is WallpaperItem wallpaper)
                {
                    AppendDebugLog($"Clicked item is WallpaperItem with ID: {wallpaper.Id}");

                    // Use MainWindow static instance to access NavigationFrame
                    if (MainWindow.Instance?.NavigationFrame == null)
                    {
                        AppendDebugLog("MainWindow.Instance or NavigationFrame is null - cannot navigate");
                        return;
                    }

                    AppendDebugLog($"Navigating to detail page for wallpaper {wallpaper.Id}");

                    // Navigate using the main window's NavigationFrame to the AlphaCoders WallpaperDetailPage
                    bool navigationResult = MainWindow.Instance.NavigationFrame.Navigate(typeof(WallpaperDetailPage), wallpaper);

                    if (!navigationResult)
                    {
                        AppendDebugLog("Navigation failed - Frame.Navigate returned false");
                    }
                    else
                    {
                        AppendDebugLog("Navigation succeeded - Frame.Navigate returned true");
                    }
                }
                else
                {
                    AppendDebugLog($"ClickedItem is not WallpaperItem. Type: {e?.ClickedItem?.GetType()?.Name ?? "null"}");
                }
            }
            catch (Exception ex)
            {
                AppendDebugLog($"Error during navigation: {ex.Message}");
                AppendDebugLog($"Stack trace: {ex.StackTrace}");
                System.Diagnostics.Debug.WriteLine($"Navigation error: {ex.Message}");
            }
        }

        private void WallpapersGridView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.InRecycleQueue)
                return;

            if (args.ItemIndex >= 0 && args.Item is WallpaperItem wallpaper)
            {
                // Load image individually as soon as container is ready
                args.RegisterUpdateCallback(async (s, e) =>
                {
                    if (e.Item is WallpaperItem w && w.ImageSource == null)
                    {
                        try
                        {
                            w.ImageSource = await w.LoadImageAsync();
                            if (w.ImageSource != null)
                            {
                                // Force UI update
                                var container = (GridViewItem)WallpapersGridView.ContainerFromItem(w);
                                container?.UpdateLayout();
                            }
                            else
                            {
                                AppendDebugLog($"Failed to load image for wallpaper {w.Id}");
                            }
                        }
                        catch (Exception ex)
                        {
                            AppendDebugLog($"Error loading image {w.Id}: {ex.Message}");
                        }
                    }
                });
            }
        }

        private void WallpapersWrapGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var panel = (ItemsWrapGrid)sender;
            var containerWidth = e.NewSize.Width;

            // Calculate the number of columns based on available width
            // Adjust these values based on your design preferences
            double desiredItemWidth = 300; // Target width for each item
            double minItemWidth = 200;     // Minimum width for each item

            // Calculate how many items can fit in a row
            int columns = Math.Max(1, (int)(containerWidth / desiredItemWidth));
            double itemWidth = Math.Max(minItemWidth, containerWidth / columns - 16); // 16 for margins

            // Set the item width and height (maintain aspect ratio 16:9)
            panel.ItemWidth = itemWidth;
            panel.ItemHeight = itemWidth * 9 / 16;
        }

        private void ToggleDebugButton_Click(object sender, RoutedEventArgs e)
        {
            _debugVisible = !_debugVisible;
            DebugPanel.Visibility = _debugVisible ? Visibility.Visible : Visibility.Collapsed;
            ToggleDebugButton.Content = _debugVisible ? "Hide Debug" : "Show Debug";
        }

        private void AppendDebugLog(string message)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                _debugLog.AppendLine($"[{DateTime.Now:HH:mm:ss}] {message}");
                DebugTextBlock.Text = _debugLog.ToString();
            });
        }

        private async void CopyDebugButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
                dataPackage.SetText(_debugLog.ToString());
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);

                // Show confirmation
                var dialog = new ContentDialog()
                {
                    Title = "Debug Log Copied",
                    Content = "Debug log has been copied to clipboard.",
                    CloseButtonText = "OK",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.XamlRoot
                };
                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                AppendDebugLog($"Error copying debug log: {ex.Message}");
            }
        }
    }

    // Custom TextWriter to capture debug output
    public class DebugWriter : System.IO.TextWriter
    {
        private readonly Action<string> _writeAction;

        public DebugWriter(Action<string> writeAction)
        {
            _writeAction = writeAction;
        }

        public override void WriteLine(string value)
        {
            _writeAction?.Invoke(value);
        }

        public override void Write(string value)
        {
            _writeAction?.Invoke(value);
        }

        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
    }
}
