using Microsoft.Extensions.Logging;
using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using WallYouNeed.Core.Services.Interfaces;
using WallYouNeed.Core.Models;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using WallYouNeed.App.Pages;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Windows.Forms;

namespace WallYouNeed.App
{
    public partial class MainWindow : Window
    {
        private readonly ILogger<MainWindow> _logger;
        private readonly IWallpaperService _wallpaperService;
        private readonly ISettingsService _settingsService;
        private System.Windows.Controls.Button _currentActiveButton;
        private bool _isWindowLoaded = false;

        public MainWindow(
            ILogger<MainWindow> logger,
            IWallpaperService wallpaperService,
            ISettingsService settingsService)
        {
            _logger = logger;
            _wallpaperService = wallpaperService;
            _settingsService = settingsService;

            InitializeComponent();
            
            // Set minimum window width to prevent resizing issues
            this.MinWidth = 200; // Slightly larger than the previous 150 to ensure UI elements have enough space
            
            // Set the current active button to Home by default
            _currentActiveButton = HomeButton;

            // Setup window controls
            SetupWindowControls();
            
            // Setup search box behavior
            SetupSearchBox();

            // Register window events
            this.Loaded += MainWindow_Loaded;
            this.Closing += MainWindow_Closing;
            this.SizeChanged += MainWindow_SizeChanged;
            this.LocationChanged += MainWindow_LocationChanged;
            
            // Navigate to Home page by default
            NavigateToPage("Home");
            SetActiveButton(HomeButton);
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Load settings and restore window position
            LoadSettingsAndRestorePosition();
        }
        
        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_isWindowLoaded && this.WindowState != WindowState.Minimized)
            {
                // We no longer need to enforce minimum width here since MinWidth property handles it
                
                _logger.LogDebug("Window size changed: {Width}x{Height}", this.Width, this.Height);
                SaveWindowPositionQuietly();
            }
        }
        
        private void MainWindow_LocationChanged(object sender, EventArgs e)
        {
            if (_isWindowLoaded && this.WindowState != WindowState.Minimized)
            {
                _logger.LogDebug("Window position changed: {Left},{Top}", this.Left, this.Top);
                SaveWindowPositionQuietly();
            }
        }

        private void SetupWindowControls()
        {
            // We now use default Windows controls, so most of this functionality is removed
            
            // Keep only the search box functionality
            // We no longer need to handle custom window buttons
        }

        private async void LoadSettingsAndRestorePosition()
        {
            try
            {
                _logger.LogInformation("Loading settings and restoring window position");
                var settings = await _settingsService.LoadSettingsAsync();
                _logger.LogInformation("Settings loaded successfully");
                
                _logger.LogInformation("Stored window settings: Left={Left}, Top={Top}, Width={Width}, Height={Height}, State={State}", 
                    settings.WindowLeft, settings.WindowTop, settings.WindowWidth, settings.WindowHeight, settings.WindowState);
                
                // Restore window size and position
                RestoreWindowPosition(settings);
                
                // Mark window as loaded after restoration
                _isWindowLoaded = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load settings");
                _isWindowLoaded = true; // Still mark as loaded even if restoration failed
            }
        }
        
        private void RestoreWindowPosition(AppSettings settings)
        {
            try
            {
                _logger.LogInformation("Restoring window position");
                
                // Make sure the dimensions are valid
                if (settings.WindowWidth <= 50) settings.WindowWidth = 1200;
                if (settings.WindowHeight <= 50) settings.WindowHeight = 800;
                
                // Check if the saved position is visible on any available screen
                bool isOnScreen = false;
                
                // If we're trying to restore to (0,0), likely the position was never saved,
                // so use CenterScreen instead
                if (settings.WindowLeft == 0 && settings.WindowTop == 0)
                {
                    this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                    this.Width = settings.WindowWidth;
                    this.Height = settings.WindowHeight;
                    _logger.LogInformation("Using center screen position");
                    isOnScreen = true;
                }
                else
                {
                    // Check if the window would be visible on any screen
                    foreach (Screen screen in Screen.AllScreens)
                    {
                        var workingArea = screen.WorkingArea;
                        if (settings.WindowLeft + 50 < workingArea.Right && 
                            settings.WindowTop + 50 < workingArea.Bottom && 
                            settings.WindowLeft + settings.WindowWidth - 50 > workingArea.Left && 
                            settings.WindowTop + settings.WindowHeight - 50 > workingArea.Top)
                        {
                            isOnScreen = true;
                            break;
                        }
                    }
                }
                
                if (isOnScreen)
                {
                    // Set startup location to manual so we can position the window
                    this.WindowStartupLocation = WindowStartupLocation.Manual;
                    
                    // Apply settings
                    this.Width = settings.WindowWidth;
                    this.Height = settings.WindowHeight;
                    this.Left = settings.WindowLeft;
                    this.Top = settings.WindowTop;
                    
                    // Restore window state
                    if (Enum.TryParse<System.Windows.WindowState>(settings.WindowState, out var windowState) && 
                        windowState != WindowState.Minimized)
                    {
                        this.WindowState = windowState;
                    }
                    
                    _logger.LogInformation("Window position restored: Left={Left}, Top={Top}, Width={Width}, Height={Height}, State={State}", 
                        this.Left, this.Top, this.Width, this.Height, this.WindowState);
                }
                else
                {
                    // Use default center screen position
                    this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                    this.Width = settings.WindowWidth;
                    this.Height = settings.WindowHeight;
                    _logger.LogWarning("Saved window position is off-screen, using center screen position");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error restoring window position");
            }
        }
        
        private async void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            try
            {
                // Save window size and position
                if (_isWindowLoaded)
                {
                    _logger.LogInformation("Window closing, saving position");
                    await SaveWindowPosition();
                    _logger.LogInformation("Window position saved on closing");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving window position on closing");
            }
        }
        
        private async void SaveWindowPositionQuietly()
        {
            try
            {
                await SaveWindowPosition();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error quietly saving window position");
            }
        }
        
        private async Task SaveWindowPosition()
        {
            // Only update if the window is loaded and not minimized
            if (!_isWindowLoaded || this.WindowState == WindowState.Minimized)
            {
                return;
            }

            try
            {
                double width = this.Width;
                double height = this.Height;
                double left = this.Left;
                double top = this.Top;
                string windowState = this.WindowState.ToString();

                // If window is maximized, we want to save the restored size
                if (this.WindowState == WindowState.Maximized)
                {
                    width = this.RestoreBounds.Width;
                    height = this.RestoreBounds.Height;
                    left = this.RestoreBounds.Left;
                    top = this.RestoreBounds.Top;
                }

                // Log the window state we're about to save
                _logger.LogInformation("Saving window position: Left={Left}, Top={Top}, Width={Width}, Height={Height}, State={State}", 
                    left, top, width, height, windowState);

                // Use UpdateSettingsAsync to update the settings
                await _settingsService.UpdateSettingsAsync(settings =>
                {
                    settings.WindowWidth = width;
                    settings.WindowHeight = height;
                    settings.WindowLeft = left;
                    settings.WindowTop = top;
                    settings.WindowState = windowState;
                });
                
                // Verify the settings were updated
                var currentSettings = await _settingsService.GetSettingsAsync();
                _logger.LogDebug("Settings after save: Left={Left}, Top={Top}, Width={Width}, Height={Height}, State={State}", 
                    currentSettings.WindowLeft, currentSettings.WindowTop, 
                    currentSettings.WindowWidth, currentSettings.WindowHeight, 
                    currentSettings.WindowState);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving window position to settings");
                throw;
            }
        }

        private void HomeButton_Click(object sender, RoutedEventArgs e)
        {
            NavigateToPage("Home");
            SetActiveButton(HomeButton);
        }

        private void CollectionButton_Click(object sender, RoutedEventArgs e)
        {
            NavigateToPage("Collections");
            SetActiveButton(CollectionButton);
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            NavigateToPage("Settings");
            SetActiveButton(SettingsButton);
        }

        private void TestGridButton_Click(object sender, RoutedEventArgs e)
        {
            // This method is now unused - the button has been removed
        }

        private void BackieeContentButton_Click(object sender, RoutedEventArgs e)
        {
            // This method is now unused - the button has been removed
        }

        private void InfiniteWallpaperButton_Click(object sender, RoutedEventArgs e)
        {
            // This method is now unused - the button has been removed
        }

        private void SetActiveButton(System.Windows.Controls.Button button)
        {
            if (_currentActiveButton != null)
            {
                // Remove active style from current button
                _currentActiveButton.Style = FindResource("NavButton") as Style;
            }
            
            // Set new active button
            _currentActiveButton = button;
            
            if (_currentActiveButton != null)
            {
                // Apply active style to new button
                _currentActiveButton.Style = FindResource("ActiveNavButton") as Style;
            }
            
            _logger.LogDebug("Active navigation button changed to: {Button}", _currentActiveButton?.Tag?.ToString() ?? "Unknown");
        }

        private void NavigateToPage(string pageName)
        {
            _logger.LogInformation("Navigating to page: {PageName}", pageName);
            
            try
            {
                // Get the right page from service provider
                var app = System.Windows.Application.Current as App;
                System.Windows.Controls.Page page = null;
                
                switch (pageName)
                {
                    case "Home":
                        page = app.Services.GetRequiredService<HomePage>();
                        break;
                    case "Collections":
                        page = app.Services.GetRequiredService<CollectionsPage>();
                        break;
                    case "Settings":
                        page = app.Services.GetRequiredService<SettingsPage>();
                        break;
                    case "Backiee Content":
                        page = app.Services.GetRequiredService<CategoryPage>();
                        (page as CategoryPage)?.SetCategory("Backiee Content");
                        break;
                    case "Infinite Wallpapers":
                        page = app.Services.GetRequiredService<LatestWallpapersPage>();
                        break;
                    case "Latest Wallpapers":
                        page = app.Services.GetRequiredService<LatestWallpapersPage>();
                        break;
                }
                
                if (page != null)
                {
                    // Use Frame navigation
                    ContentFrame.Navigate(page);
                    _logger.LogInformation("Successfully navigated to {PageName}", pageName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error navigating to page: {PageName}", pageName);
                System.Windows.MessageBox.Show($"Error navigating to page: {ex.Message}", 
                    "Navigation Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private async void ApplyRandomWallpaper_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _logger.LogInformation("Applying random wallpaper");
                
                // Get all wallpapers and pick a random one
                var wallpapers = await _wallpaperService.GetAllWallpapersAsync();
                var wallpapersList = wallpapers.ToList();
                
                if (wallpapersList.Count == 0)
                {
                    _logger.LogWarning("No wallpaper found to apply");
                    System.Windows.MessageBox.Show("No wallpaper found to apply. Add some wallpapers first!", 
                        "No Wallpaper", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                    return;
                }
                
                // Select a random wallpaper
                var random = new Random();
                var wallpaper = wallpapersList[random.Next(wallpapersList.Count)];

                bool success = await _wallpaperService.ApplyWallpaperAsync(wallpaper.Id);
                if (success)
                {
                    _logger.LogInformation("Applied random wallpaper: {WallpaperId}", wallpaper.Id);
                    System.Windows.MessageBox.Show($"Wallpaper applied successfully!", 
                        "Success", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
                else
                {
                    _logger.LogWarning("Failed to apply wallpaper: {WallpaperId}", wallpaper.Id);
                    System.Windows.MessageBox.Show("Failed to apply wallpaper. Please try again.", 
                        "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error applying random wallpaper");
                System.Windows.MessageBox.Show($"Error applying wallpaper: {ex.Message}", 
                    "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void NavigateToCategoryPage(string categoryName)
        {
            try
            {
                // Get the CategoryPage from service provider
                var app = System.Windows.Application.Current as App;
                var categoryPage = app.Services.GetRequiredService<CategoryPage>();
                
                // Set the category for the page
                categoryPage.SetCategory(categoryName);
                
                // Navigate to the page
                ContentFrame.Navigate(categoryPage);
                _logger.LogInformation("Successfully navigated to category: {CategoryName}", categoryName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error navigating to category: {CategoryName}", categoryName);
                System.Windows.MessageBox.Show($"Error navigating to category: {ex.Message}", 
                    "Navigation Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);
            // We've switched to the default window controls, so no need to update button state
        }

        private void SetupSearchBox()
        {
            var searchBox = this.FindName("SearchBox") as System.Windows.Controls.TextBox;
            var clearButton = this.FindName("ClearSearchButton") as System.Windows.Controls.Button;
            var searchButton = this.FindName("SearchButton") as System.Windows.Controls.Button;
            var clearButtonBorder = this.FindName("ClearButtonBorder") as System.Windows.Controls.Border;
            
            if (searchBox != null && clearButton != null && searchButton != null)
            {
                // Wire up search text changed event to show/hide clear button
                searchBox.TextChanged += (s, e) => 
                {
                    // If this is the first character being typed and the text was the placeholder
                    if (searchBox.Text != "Search..." && searchBox.Text.Length == 1)
                    {
                        // User started typing, so we want proper text color
                        searchBox.Foreground = (SolidColorBrush)System.Windows.Application.Current.Resources["TextFillColorPrimaryBrush"];
                    }

                    // Show clear button only when there's text and it's not the placeholder
                    if (!string.IsNullOrWhiteSpace(searchBox.Text) && searchBox.Text != "Search...")
                    {
                        clearButtonBorder.Visibility = System.Windows.Visibility.Visible;
                        _logger.LogDebug("Clear button shown - text: {Text}", searchBox.Text);
                    }
                    else
                    {
                        clearButtonBorder.Visibility = System.Windows.Visibility.Collapsed;
                        _logger.LogDebug("Clear button hidden - text: {Text}", searchBox.Text);
                    }
                };
                
                // Set focus behavior - don't clear placeholder text on focus
                searchBox.GotFocus += (s, e) => 
                {
                    // Only change the foreground color to show it's focused
                    if (searchBox.Text == "Search...")
                    {
                        // Don't clear the placeholder text, just change the caret position
                        searchBox.SelectionStart = 0;
                        searchBox.SelectionLength = 0;
                    }
                };
                
                // Prevent selecting placeholder text
                searchBox.PreviewMouseLeftButtonDown += (s, e) =>
                {
                    if (searchBox.Text == "Search...")
                    {
                        // Set focus without allowing selection
                        searchBox.Focus();
                        searchBox.SelectionStart = 0;
                        searchBox.SelectionLength = 0;
                        
                        // Mark the event as handled to prevent default selection behavior
                        e.Handled = true;
                    }
                };
                
                // Reset selection if placeholder text
                searchBox.SelectionChanged += (s, e) =>
                {
                    if (searchBox.Text == "Search..." && 
                        (searchBox.SelectionStart > 0 || searchBox.SelectionLength > 0))
                    {
                        // Reset cursor to beginning, prevent selection
                        searchBox.SelectionStart = 0;
                        searchBox.SelectionLength = 0;
                    }
                };
                
                // Lost focus behavior
                searchBox.LostFocus += (s, e) => 
                {
                    if (string.IsNullOrWhiteSpace(searchBox.Text))
                    {
                        searchBox.Text = "Search...";
                        searchBox.Foreground = (SolidColorBrush)System.Windows.Application.Current.Resources["TextFillColorSecondaryBrush"];
                        clearButtonBorder.Visibility = System.Windows.Visibility.Collapsed;
                    }
                };
                
                // Handle key press to clear placeholder when typing starts
                searchBox.PreviewKeyDown += (s, e) => 
                {
                    if (searchBox.Text == "Search..." && 
                        (e.Key >= Key.A && e.Key <= Key.Z || // Letters
                         e.Key >= Key.D0 && e.Key <= Key.D9 || // Numbers
                         e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9 || // Numpad
                         e.Key == Key.Space ||
                         e.Key == Key.OemMinus ||
                         e.Key == Key.OemPeriod))
                    {
                        // Clear placeholder text before the character is typed
                        searchBox.Text = "";
                        searchBox.Foreground = (SolidColorBrush)System.Windows.Application.Current.Resources["TextFillColorPrimaryBrush"];
                        
                        // Don't handle special keys like Enter or Tab
                        if (e.Key == Key.Tab || e.Key == Key.Enter)
                        {
                            return;
                        }
                    }
                };
                
                // Handle search submission with Enter key
                searchBox.KeyDown += (s, e) =>
                {
                    if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(searchBox.Text) && searchBox.Text != "Search...")
                    {
                        PerformSearch(searchBox.Text);
                    }
                };
                
                // Search button click
                searchButton.Click += (s, e) => 
                {
                    if (!string.IsNullOrWhiteSpace(searchBox.Text) && searchBox.Text != "Search...")
                    {
                        PerformSearch(searchBox.Text);
                    }
                };
                
                // Clear button click
                clearButton.Click += (s, e) => 
                {
                    _logger.LogDebug("Clear button clicked");
                    searchBox.Text = "";
                    searchBox.Foreground = (SolidColorBrush)System.Windows.Application.Current.Resources["TextFillColorPrimaryBrush"];
                    clearButtonBorder.Visibility = System.Windows.Visibility.Collapsed;
                    searchBox.Focus();
                };
                
                // Initialize the clear button (hidden by default)
                clearButtonBorder.Visibility = System.Windows.Visibility.Collapsed;
                _logger.LogDebug("Search box controls initialized");
            }
            else
            {
                _logger.LogWarning("Search box setup failed - one or more controls not found");
            }
        }
        
        private void PerformSearch(string searchQuery)
        {
            // TODO: Implement search functionality
            _logger.LogInformation("Performing search for: {SearchQuery}", searchQuery);
            // For now, just log the search query
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Allow dragging the window when clicking and dragging the title bar
            if (e.ClickCount == 1)
            {
                this.DragMove();
            }
            else if (e.ClickCount == 2)
            {
                // Double-click to maximize/restore
                MaximizeButton_Click(sender, e);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (this.WindowState == WindowState.Maximized)
            {
                this.WindowState = WindowState.Normal;
                // Use Content Property directly without referencing by name
                if (sender is System.Windows.Controls.Button button)
                {
                    button.Content = "□";
                }
            }
            else
            {
                this.WindowState = WindowState.Maximized;
                // Use Content Property directly without referencing by name
                if (sender is System.Windows.Controls.Button button)
                {
                    button.Content = "❐";
                }
            }
        }
    }
} 