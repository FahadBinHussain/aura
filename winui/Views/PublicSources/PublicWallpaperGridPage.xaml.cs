using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Aura.Models;
using Aura.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;

namespace Aura.Views.PublicSources
{
    public sealed partial class PublicWallpaperGridPage : Page
    {
        private readonly ObservableCollection<WallpaperItem> _wallpapers = new();
        private readonly PublicWallpaperService _wallpaperService = new();
        private string _platformName = "Wallhaven";
        private string _currentMode = string.Empty;
        private int _currentPage = 1;
        private bool _isLoading;
        private bool _hasMoreWallpapers = true;

        public PublicWallpaperGridPage()
        {
            InitializeComponent();
            WallpapersGridView.ItemsSource = _wallpapers;
        }

        public ObservableCollection<WallpaperItem> Wallpapers => _wallpapers;

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is string platformName && PublicWallpaperService.IsSupportedPlatform(platformName))
            {
                _platformName = platformName;
            }

            MainWindow.LastSelectedPlatform = _platformName;
            _currentMode = PublicWallpaperService.GetDefaultMode(_platformName);
            TitleTextBlock.Text = _platformName;
            DescriptionTextBlock.Text = PublicWallpaperService.GetPlatformDescription(_platformName);
            BuildModeButtons();

            if (_wallpapers.Count == 0)
            {
                await ReloadAsync();
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await ReloadAsync();
        }

        private async void ModeButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button)
            {
                return;
            }

            var mode = button.Tag?.ToString() ?? string.Empty;
            if (string.Equals(mode, _currentMode, StringComparison.OrdinalIgnoreCase) && _wallpapers.Count > 0)
            {
                return;
            }

            _currentMode = mode;
            UpdateModeButtonStyles();
            await ReloadAsync();
        }

        private async Task ReloadAsync()
        {
            _currentPage = 1;
            _hasMoreWallpapers = true;
            _wallpapers.Clear();
            await LoadWallpapersAsync();
        }

        private async Task LoadWallpapersAsync()
        {
            if (_isLoading || !_hasMoreWallpapers)
            {
                return;
            }

            try
            {
                _isLoading = true;
                LoadingProgressBar.Visibility = Visibility.Visible;
                StatusInfoBar.IsOpen = false;

                var newWallpapers = await _wallpaperService.GetWallpapersAsync(_platformName, _currentPage, _currentMode);
                if (newWallpapers.Count == 0)
                {
                    _hasMoreWallpapers = false;
                    if (_wallpapers.Count == 0)
                    {
                        ShowStatus($"No wallpapers were returned from {_platformName}.", InfoBarSeverity.Warning);
                    }
                    return;
                }

                foreach (var wallpaper in newWallpapers)
                {
                    _wallpapers.Add(wallpaper);
                }

                _currentPage++;
            }
            catch (Exception ex)
            {
                _hasMoreWallpapers = false;
                ShowStatus(ex.Message, InfoBarSeverity.Error);
            }
            finally
            {
                _isLoading = false;
                LoadingProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        private void BuildModeButtons()
        {
            ModeButtonsPanel.Children.Clear();
            var modes = PublicWallpaperService.GetModes(_platformName);
            ModeButtonsPanel.Visibility = modes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            foreach (var mode in modes)
            {
                var button = new Button
                {
                    Content = mode,
                    Tag = mode,
                    Padding = new Thickness(14, 7, 14, 7),
                    CornerRadius = new CornerRadius(8)
                };
                button.Click += ModeButton_Click;
                ModeButtonsPanel.Children.Add(button);
            }

            UpdateModeButtonStyles();
        }

        private void UpdateModeButtonStyles()
        {
            foreach (var child in ModeButtonsPanel.Children.OfType<Button>())
            {
                var isSelected = string.Equals(child.Tag?.ToString(), _currentMode, StringComparison.OrdinalIgnoreCase);
                child.Background = isSelected
                    ? (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"]
                    : (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
                child.Foreground = isSelected
                    ? new SolidColorBrush(Microsoft.UI.Colors.White)
                    : (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
            }
        }

        private void WallpapersGridView_Loaded(object sender, RoutedEventArgs e)
        {
            if (FindDescendant<ScrollViewer>(WallpapersGridView) is ScrollViewer scrollViewer)
            {
                scrollViewer.ViewChanged -= WallpapersScrollViewer_ViewChanged;
                scrollViewer.ViewChanged += WallpapersScrollViewer_ViewChanged;
            }
        }

        private async void WallpapersScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer &&
                !e.IsIntermediate &&
                scrollViewer.ScrollableHeight > 0 &&
                scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - 360)
            {
                await LoadWallpapersAsync();
            }
        }

        private void WallpapersGridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is WallpaperItem wallpaper)
            {
                Frame.Navigate(typeof(PublicWallpaperDetailPage), new PublicWallpaperNavigationParameter(_platformName, wallpaper));
            }
        }

        private void WallpapersGridView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.InRecycleQueue)
            {
                return;
            }

            if (args.Phase == 0)
            {
                args.RegisterUpdateCallback(ShowWallpaperImage);
                args.Handled = true;
            }
        }

        private async void ShowWallpaperImage(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.Item is not WallpaperItem wallpaper)
            {
                return;
            }

            var templateRoot = args.ItemContainer.ContentTemplateRoot as Grid;
            var image = templateRoot?.FindName("WallpaperImage") as Image;
            if (image == null)
            {
                return;
            }

            var requestKey = $"{wallpaper.Id}_{args.ItemContainer.GetHashCode()}";
            image.Tag = requestKey;

            try
            {
                var bitmap = await wallpaper.LoadImageAsync();
                if (image.Tag?.ToString() == requestKey && bitmap != null)
                {
                    image.Source = bitmap;
                }
            }
            catch
            {
                if (image.Tag?.ToString() == requestKey)
                {
                    image.Source = new BitmapImage(new Uri("ms-appx:///Assets/placeholder-wallpaper-1000.png"));
                }
            }
        }

        private void WallpapersWrapGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is not ItemsWrapGrid wrapGrid)
            {
                return;
            }

            var availableWidth = e.NewSize.Width;
            var columns = Math.Max(1, Math.Min(6, (int)(availableWidth / 300)));
            var finalWidth = Math.Max(220, (availableWidth - ((columns - 1) * 12)) / columns);

            wrapGrid.ItemWidth = finalWidth;
            wrapGrid.ItemHeight = finalWidth * 9 / 16;
            wrapGrid.HorizontalAlignment = HorizontalAlignment.Stretch;
        }

        private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            var childCount = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T typedChild)
                {
                    return typedChild;
                }

                var descendant = FindDescendant<T>(child);
                if (descendant != null)
                {
                    return descendant;
                }
            }

            return null;
        }

        private void ShowStatus(string message, InfoBarSeverity severity)
        {
            StatusInfoBar.Message = message;
            StatusInfoBar.Severity = severity;
            StatusInfoBar.IsOpen = true;
        }
    }
}
