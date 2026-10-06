using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Dispatching;
using System.Threading.Tasks;
using Aura.Models;
using System.Linq;
using System.Text.Json;
using Microsoft.UI.Xaml.Navigation;
using Aura.Services;

namespace Aura.Views.Backiee
{
    public sealed partial class CategoryWallpapersPage : Page
    {
        private BackieeCategory _category;
        private BackieeWallpaperSection _section;
        private string _activeSearchTerm;

        private ObservableCollection<WallpaperItem> _wallpapers;
        private HashSet<string> _seenIds;

        private BitmapImage _placeholderImage;
        private DispatcherQueue _dispatcherQueue;

        private bool _isLoading = false;
        private int _currentPage = 0;
        private int _itemsPerPage = 30;
        private bool _hasMoreItems = true;
        private double _loadMoreThreshold = 0.4;

        private const string ApiBaseUrl = "https://backiee.com/api/wallpaper/list.php";

        public CategoryWallpapersPage()
        {
            this.InitializeComponent();

            _wallpapers = new ObservableCollection<WallpaperItem>();
            _seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

            try
            {
                _placeholderImage = new BitmapImage();
                _placeholderImage.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                _placeholderImage.UriSource = new Uri("ms-appx:///Assets/placeholder-wallpaper-1000.png");
            }
            catch
            {
                _placeholderImage = new BitmapImage();
            }

            Loaded += CategoryWallpapersPage_Loaded;
            Unloaded += CategoryWallpapersPage_Unloaded;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is BackieeCategory category)
            {
                _category = category;
                _section = BackieeWallpaperSection.ForCategory(category.Slug, $"{category.Name} wallpapers");
                PageTitleTextBlock.Text = _section.Title;
            }
            else
            {
                PageTitleTextBlock.Text = "Category";
                ErrorTextBlock.Text = "No category selected - open one from the Categories grid.";
                ErrorTextBlock.Visibility = Visibility.Visible;
            }
        }

        private async void CategoryWallpapersPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (_category == null)
            {
                return;
            }

            WallpapersGridView.ItemsSource = _wallpapers;
            ResetPaging();

            await LoadChipsAsync();
            await LoadMoreWallpapers();
        }

        private void CategoryWallpapersPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _wallpapers.Clear();
            _seenIds.Clear();
        }

        private async Task LoadChipsAsync()
        {
            ChipsItemsControl.Items.Clear();
            ChipsScrollViewer.Visibility = Visibility.Collapsed;
            ChipsStatusText.Visibility = Visibility.Collapsed;

            if (_category == null)
            {
                return;
            }

            try
            {
                var url = $"https://backiee.com/categories/{_category.Slug}";
                var html = await BackieeNetworkClient.GetStringAsync(url);
                var terms = BackieeHtmlParser.ParseSearchChips(html);

                if (terms.Count == 0)
                {
                    ChipsStatusText.Text = "No subcategory filters found for this category - backiee may have changed the page markup.";
                    ChipsStatusText.Visibility = Visibility.Visible;
                    return;
                }

                ChipsItemsControl.Items.Add(MakeChip($"All {_category.Name}", null, isActive: true));
                foreach (var term in terms)
                {
                    ChipsItemsControl.Items.Add(MakeChip(term, term, isActive: false));
                }

                ChipsScrollViewer.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                ChipsStatusText.Text = $"Subcategory filters failed to load: {ex.Message}";
                ChipsStatusText.Visibility = Visibility.Visible;
            }
        }

        private Button MakeChip(string label, string searchTerm, bool isActive)
        {
            var button = new Button
            {
                Content = label,
                Tag = searchTerm,
                Padding = new Thickness(14, 6, 14, 6),
                Margin = new Thickness(0),
                CornerRadius = new CornerRadius(16)
            };
            button.Click += ChipButton_Click;
            ApplyChipStyle(button, isActive);
            return button;
        }

        private void ApplyChipStyle(Button button, bool isActive)
        {
            if (isActive)
            {
                button.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 165, 0));
                button.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));
                button.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            }
            else
            {
                // never assign null to Foreground - a null LOCAL value beats
                // the theme style setter in WinUI, so the chip label painted
                // nothing (UIA still exposed the text; the eye saw blanks).
                // ClearValue removes the local override and restores the
                // theme default foreground. Background stays null on purpose:
                // transparent fill with the default outline.
                button.Background = null;
                button.ClearValue(Control.ForegroundProperty);
                button.ClearValue(Control.FontWeightProperty);
            }
        }

        private async void ChipButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button)
            {
                return;
            }

            var searchTerm = button.Tag as string;
            var isAlreadyActive = searchTerm == null
                ? _activeSearchTerm == null
                : string.Equals(searchTerm, _activeSearchTerm, StringComparison.OrdinalIgnoreCase);

            if (isAlreadyActive)
            {
                return;
            }

            _activeSearchTerm = searchTerm;

            foreach (var item in ChipsItemsControl.Items.OfType<Button>())
            {
                ApplyChipStyle(item, ReferenceEquals(item, button));
            }

            ErrorTextBlock.Visibility = Visibility.Collapsed;
            ResetPaging();
            await LoadMoreWallpapers();
        }

        private void ResetPaging()
        {
            _wallpapers.Clear();
            _seenIds.Clear();
            _currentPage = _activeSearchTerm == null ? 0 : 1;
            _hasMoreItems = true;
        }

        private async Task LoadMoreWallpapers()
        {
            if (_category == null || _isLoading || !_hasMoreItems)
            {
                return;
            }

            try
            {
                _isLoading = true;
                LoadingProgressBar.Visibility = Visibility.Visible;

                if (_activeSearchTerm == null)
                {
                    await LoadCategoryPageAsync();
                }
                else
                {
                    await LoadSearchPageAsync();
                }

                _currentPage++;
            }
            catch (Exception ex)
            {
                _hasMoreItems = false;
                if (_wallpapers.Count == 0)
                {
                    ErrorTextBlock.Text = $"Couldn't load wallpapers: {ex.Message}";
                    ErrorTextBlock.Visibility = Visibility.Visible;
                }
            }
            finally
            {
                _isLoading = false;
                LoadingProgressBar.Visibility = Visibility.Collapsed;

                if (_wallpapers.Count > 0)
                {
                    ErrorTextBlock.Visibility = Visibility.Collapsed;
                }
            }
        }

        private async Task LoadCategoryPageAsync()
        {
            var apiUrl = _section.BuildApiUrl(ApiBaseUrl, _currentPage, _itemsPerPage);
            var jsonContent = await BackieeNetworkClient.GetStringAsync(apiUrl);

            if (string.IsNullOrWhiteSpace(jsonContent))
            {
                _hasMoreItems = false;
                return;
            }

            using (JsonDocument doc = JsonDocument.Parse(jsonContent))
            {
                foreach (JsonElement wallpaperElement in doc.RootElement.EnumerateArray())
                {
                    try
                    {
                        var wallpaper = BackieeApiParser.CreateWallpaperItem(wallpaperElement, _placeholderImage);
                        if (!string.IsNullOrEmpty(wallpaper.Id) &&
                            !string.IsNullOrEmpty(wallpaper.ImageUrl) &&
                            _seenIds.Add(wallpaper.Id))
                        {
                            _wallpapers.Add(wallpaper);
                        }
                    }
                    catch
                    {
                    }
                }
            }

            _hasMoreItems = true;
        }

        private async Task LoadSearchPageAsync()
        {
            var searchUrl = $"https://backiee.com/search/{Uri.EscapeDataString(_activeSearchTerm)}?page={_currentPage}";
            var html = await BackieeNetworkClient.GetStringAsync(searchUrl);
            var results = BackieeHtmlParser.ParseWallpaperCards(html);

            if (results.Count == 0)
            {
                _hasMoreItems = false;

                if (_wallpapers.Count == 0)
                {
                    ErrorTextBlock.Text = $"No results found for \"{_activeSearchTerm}\".";
                    ErrorTextBlock.Visibility = Visibility.Visible;
                }

                return;
            }

            foreach (var wallpaper in results)
            {
                if (_seenIds.Add(wallpaper.Id))
                {
                    _wallpapers.Add(wallpaper);
                }
            }

            _hasMoreItems = true;
        }

        private async void MainScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                double verticalOffset = scrollViewer.VerticalOffset;
                double maxVerticalOffset = scrollViewer.ScrollableHeight;

                if (maxVerticalOffset > 0 &&
                    verticalOffset >= maxVerticalOffset * _loadMoreThreshold &&
                    !_isLoading)
                {
                    await LoadMoreWallpapers();
                }
            }
        }

        private void WallpapersGridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is WallpaperItem wallpaper)
            {
                this.Frame.Navigate(typeof(WallpaperDetailPage), wallpaper);
            }
        }

        private void WallpapersWrapGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is ItemsWrapGrid wrapGrid)
            {
                double availableWidth = e.NewSize.Width;

                double desiredItemWidth = 300;
                double itemMargin = 8;

                int columnsCount = Math.Max(1, (int)(availableWidth / desiredItemWidth));
                columnsCount = Math.Min(columnsCount, 6);
                wrapGrid.MaximumRowsOrColumns = columnsCount;

                double totalMarginWidth = (columnsCount - 1) * itemMargin;
                double newItemWidth = (availableWidth - totalMarginWidth) / columnsCount;
                double finalWidth = Math.Max(200, newItemWidth);

                double aspectRatio = 16.0 / 9.0;
                double finalHeight = finalWidth / aspectRatio;

                wrapGrid.ItemWidth = finalWidth;
                wrapGrid.ItemHeight = finalHeight;

                wrapGrid.HorizontalAlignment = HorizontalAlignment.Stretch;
            }
        }

        private void WallpapersGridView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.InRecycleQueue)
            {
                var templateRoot = args.ItemContainer.ContentTemplateRoot as Grid;
                var image = templateRoot.FindName("ItemImage") as Image;
                if (image != null)
                {
                    image.Source = _placeholderImage;
                    image.Tag = null;
                }
                return;
            }

            if (args.Phase == 0)
            {
                args.RegisterUpdateCallback(ShowImage);
                args.Handled = true;
            }
        }

        private async void ShowImage(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.Phase == 1)
            {
                var wallpaper = args.Item as WallpaperItem;
                if (wallpaper == null) return;

                var templateRoot = args.ItemContainer.ContentTemplateRoot as Grid;
                if (templateRoot == null) return;

                var image = templateRoot.FindName("ItemImage") as Image;
                if (image == null) return;

                string imageKey = $"image_{wallpaper.Id}_{args.ItemContainer.GetHashCode()}";

                try
                {
                    image.Tag = imageKey;
                    image.Source = _placeholderImage;

                    SetItemMetadata(templateRoot, wallpaper);

                    await LoadImageForItemAsync(image, wallpaper, imageKey);
                }
                catch
                {
                }
            }
        }

        private async Task LoadImageForItemAsync(Image imageControl, WallpaperItem wallpaper, string requestKey)
        {
            try
            {
                var bitmap = await wallpaper.LoadImageAsync();

                if (bitmap != null)
                {
                    bitmap.ImageOpened += (s, e) =>
                    {
                        wallpaper.ImageSource = bitmap;

                        if (_dispatcherQueue.HasThreadAccess && imageControl.Tag?.ToString() == requestKey)
                        {
                            imageControl.Source = bitmap;
                        }
                    };

                    bitmap.ImageFailed += (s, e) =>
                    {
                        if (_dispatcherQueue.HasThreadAccess && imageControl.Tag?.ToString() == requestKey)
                        {
                            imageControl.Source = _placeholderImage;
                        }
                    };

                    if (imageControl.Tag?.ToString() == requestKey)
                    {
                        if (_dispatcherQueue.HasThreadAccess)
                        {
                            imageControl.Source = bitmap;
                        }
                        else
                        {
                            _dispatcherQueue.TryEnqueue(() =>
                            {
                                if (imageControl.Tag?.ToString() == requestKey)
                                {
                                    imageControl.Source = bitmap;
                                }
                            });
                        }
                    }
                }
            }
            catch
            {
                if (imageControl.Tag?.ToString() == requestKey && _dispatcherQueue.HasThreadAccess)
                {
                    imageControl.Source = _placeholderImage;
                }
            }
        }

        private void SetItemMetadata(Grid templateRoot, WallpaperItem wallpaper)
        {
            var qualityTagBorder = templateRoot.FindName("QualityTagBorder") as Border;
            var qualityImage = templateRoot.FindName("QualityImage") as Image;
            if (qualityTagBorder != null && qualityImage != null && !string.IsNullOrEmpty(wallpaper.QualityTag))
            {
                qualityTagBorder.Visibility = Visibility.Visible;
                string qualityImagePath = wallpaper.QualityLogoPath;
                if (!string.IsNullOrEmpty(qualityImagePath))
                {
                    qualityImage.Source = new BitmapImage(new Uri(qualityImagePath));
                }
            }

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
    }
}
