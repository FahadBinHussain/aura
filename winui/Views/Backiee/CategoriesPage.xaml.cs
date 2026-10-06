using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Aura.Models;
using Aura.Services;
using Aura.Views.AlphaCoders;
using Aura.Views.ArtStation;
using Aura.Views.PublicSources;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;

namespace Aura.Views.Backiee
{
    public sealed partial class CategoriesPage : Page
    {
        private const string CategoriesUrl = "https://backiee.com/categories";
        private const string DefaultAccentHex = "#FF8C00";

        // every platform the app implements - this array IS the scope menu.
        // per-site reversal evidence: automata-private/<site>/AGENTS.md
        private static readonly string[] CategoryPlatforms =
        {
            "Backiee", "AlphaCoders", "ArtStation", "Bing Wallpaper Archive",
            "Pexels", "Pixabay", "Simple Desktops", "Wallhaven", "WallpaperHub"
        };

        // artstation: query entries - its subject-matter taxonomy is CSRF-blocked for
        // anonymous sessions (automata-private/www.artstation.com/AGENTS.md)
        private static readonly (string Key, string Name)[] ArtStationCategories =
        {
            ("wallpaper", "Wallpaper"),
            ("landscape", "Landscape"),
            ("nature", "Nature"),
            ("space", "Space"),
            ("abstract", "Abstract"),
        };

        private static readonly BitmapImage PlaceholderImage =
            new BitmapImage(new Uri("ms-appx:///Assets/placeholder-wallpaper-1000.png"));

        // one category as it exists on a single platform (what a drill-down needs)
        public sealed class CategorySourceRef
        {
            public string Platform { get; set; } = string.Empty;
            public string Key { get; set; } = string.Empty; // backiee slug | alphacoders key | public mode name
            public BackieeCategory? Backiee { get; set; }
        }

        // one grid card = the same-named category merged across every platform
        public sealed class MergedCategory : INotifyPropertyChanged
        {
            public event PropertyChangedEventHandler? PropertyChanged;

            public string Name { get; set; } = string.Empty;
            public string AccentHex { get; set; } = DefaultAccentHex;
            public List<CategorySourceRef> Sources { get; } = new();

            private BitmapImage? _imageSource = PlaceholderImage;
            public BitmapImage? ImageSource
            {
                get => _imageSource;
                set
                {
                    if (_imageSource != value)
                    {
                        _imageSource = value;
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ImageSource)));
                    }
                }
            }
        }

        private readonly ObservableCollection<MergedCategory> _categories;
        private readonly List<BackieeCategory> _backieeCategories = new();
        private List<MergedCategory> _merged = new();
        private string? _scopePlatform; // null = Global (all platforms)
        private string? _sourceError;
        private bool _isLoading;

        public CategoriesPage()
        {
            this.InitializeComponent();
            _categories = new ObservableCollection<MergedCategory>();
            CategoriesGridView.ItemsSource = _categories;
            Loaded += CategoriesPage_Loaded;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
        }

        private async void CategoriesPage_Loaded(object sender, RoutedEventArgs e)
        {
            BuildScopeMenu();
            await LoadCategoriesAsync();
        }

        private async Task LoadCategoriesAsync()
        {
            if (_isLoading)
            {
                return;
            }

            _isLoading = true;
            _categories.Clear();
            _backieeCategories.Clear();
            ErrorTextBlock.Visibility = Visibility.Collapsed;
            LoadingProgressBar.Visibility = Visibility.Visible;

            var errors = new List<string>();
            try
            {
                var html = await BackieeNetworkClient.GetStringAsync(CategoriesUrl);
                var parsed = BackieeHtmlParser.ParseCategories(html);

                if (parsed.Count == 0)
                {
                    errors.Add("backiee returned no categories - the /categories markup may have changed.");
                }
                else
                {
                    _backieeCategories.AddRange(parsed);
                    int failures = await LoadThumbnailsAsync(parsed);
                    if (failures == parsed.Count)
                    {
                        errors.Add("Every category thumbnail failed to load - backiee is likely blocking image requests.");
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Couldn't load backiee categories: {ex.Message}");
            }
            finally
            {
                LoadingProgressBar.Visibility = Visibility.Collapsed;
                _isLoading = false;
            }

            _sourceError = errors.Count > 0 ? string.Join(Environment.NewLine, errors) : null;
            BuildMerged();
            ApplyScope();
        }

        private async Task<int> LoadThumbnailsAsync(List<BackieeCategory> categories)
        {
            int failures = 0;

            var tasks = new List<Task>();
            foreach (var category in categories)
            {
                tasks.Add(LoadThumbnailAsync(category, () => failures++));
            }

            await Task.WhenAll(tasks);
            return failures;
        }

        private async Task LoadThumbnailAsync(BackieeCategory category, Action onFailure)
        {
            try
            {
                category.ImageSource = await category.LoadImageAsync();
            }
            catch
            {
                onFailure();
            }
        }

        // merge every platform's categories by case-insensitive name into one grid model
        private void BuildMerged()
        {
            _merged = new List<MergedCategory>();
            var byName = new Dictionary<string, MergedCategory>(StringComparer.OrdinalIgnoreCase);

            void Add(string platform, string key, string name, string accentHex, BackieeCategory? backiee = null)
            {
                var trimmed = name.Trim();
                if (!byName.TryGetValue(trimmed, out var card))
                {
                    card = new MergedCategory
                    {
                        Name = trimmed,
                        AccentHex = string.IsNullOrEmpty(accentHex) ? DefaultAccentHex : accentHex,
                    };
                    byName[trimmed] = card;
                    _merged.Add(card);
                }

                card.Sources.Add(new CategorySourceRef { Platform = platform, Key = key, Backiee = backiee });

                if (backiee?.ImageSource != null && card.ImageSource == PlaceholderImage)
                {
                    card.ImageSource = backiee.ImageSource;
                }
            }

            foreach (var backiee in _backieeCategories)
            {
                Add("Backiee", backiee.Slug, backiee.Name, backiee.AccentHex, backiee);
            }
            foreach (var (key, name) in AlphaCodersService.Categories)
            {
                Add("AlphaCoders", key, name, DefaultAccentHex);
            }
            foreach (var (key, name) in ArtStationCategories)
            {
                Add("ArtStation", key, name, DefaultAccentHex);
            }
            // every other implemented platform contributes its GetModes() entries as categories
            // (pixabay docs list, pexels query modes, wallhaven bitmask, wallpaperhub collections,
            //  bing/simpledesktops single honest entry - key = the mode the fetcher consumes)
            foreach (var platform in new[] { "Bing Wallpaper Archive", "Pexels", "Pixabay", "Simple Desktops", "Wallhaven", "WallpaperHub" })
            {
                foreach (var mode in PublicWallpaperService.GetModes(platform))
                {
                    Add(platform, mode, mode, DefaultAccentHex);
                }
            }
        }

        private void ApplyScope()
        {
            _categories.Clear();

            IEnumerable<MergedCategory> view = _merged;
            if (_scopePlatform != null)
            {
                view = _merged.Where(c => c.Sources.Any(s => s.Platform == _scopePlatform));
            }

            foreach (var category in view)
            {
                _categories.Add(category);
            }

            ScopeButtonTextBlock.Text = _scopePlatform == null ? "Global" : $"Local · {_scopePlatform}";

            // show the source error only while it affects what is on screen
            if (_sourceError != null && (_scopePlatform == null || _scopePlatform == "Backiee"))
            {
                ErrorTextBlock.Text = _sourceError;
                ErrorTextBlock.Visibility = Visibility.Visible;
            }
            else
            {
                ErrorTextBlock.Visibility = Visibility.Collapsed;
            }
        }

        private void BuildScopeMenu()
        {
            var flyout = new MenuFlyout();

            var globalItem = new RadioMenuFlyoutItem
            {
                Text = "Global (all platforms)",
                IsChecked = _scopePlatform == null,
                GroupName = "CategoryScope",
            };
            globalItem.Click += (_, _) => SetScope(null);
            flyout.Items.Add(globalItem);
            flyout.Items.Add(new MenuFlyoutSeparator());

            foreach (var platform in CategoryPlatforms)
            {
                var item = new RadioMenuFlyoutItem
                {
                    Text = platform,
                    IsChecked = _scopePlatform == platform,
                    GroupName = "CategoryScope",
                };
                var selected = platform;
                item.Click += (_, _) => SetScope(selected);
                flyout.Items.Add(item);
            }

            ScopeButton.Flyout = flyout;
        }

        private void SetScope(string? platform)
        {
            _scopePlatform = platform;
            ApplyScope();
            BuildScopeMenu();
        }

        private void ShowError(string message)
        {
            ErrorTextBlock.Text = message;
            ErrorTextBlock.Visibility = Visibility.Visible;
        }

        private void CategoriesGridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is not MergedCategory category || category.Sources.Count == 0)
            {
                return;
            }

            if (category.Sources.Count == 1)
            {
                NavigateToSource(category.Sources[0]);
                return;
            }

            // same-named category on several platforms: pick the platform first
            var flyout = new MenuFlyout();
            foreach (var source in category.Sources)
            {
                var item = new MenuFlyoutItem { Text = source.Platform };
                var selected = source;
                item.Click += (_, _) => NavigateToSource(selected);
                flyout.Items.Add(item);
            }

            var anchor = (CategoriesGridView.ContainerFromItem(category) as FrameworkElement) ?? CategoriesGridView;
            flyout.ShowAt(anchor);
        }

        private void NavigateToSource(CategorySourceRef source)
        {
            switch (source.Platform)
            {
                case "Backiee":
                    if (source.Backiee == null)
                    {
                        ShowError("That backiee category lost its payload - reopen Categories.");
                        break;
                    }
                    Frame.Navigate(typeof(CategoryWallpapersPage), source.Backiee);
                    break;
                case "AlphaCoders":
                    Frame.Navigate(typeof(AlphaCodersGridPage), source.Key);
                    break;
                case "ArtStation":
                    // source.Key = the search query the reversed GET endpoint accepts
                    Frame.Navigate(typeof(ArtStationGridPage), source.Key);
                    break;
                case "Pixabay":
                case "Pexels":
                case "Bing Wallpaper Archive":
                case "Simple Desktops":
                case "Wallhaven":
                case "WallpaperHub":
                    Frame.Navigate(typeof(PublicWallpaperGridPage), new PublicGridNavigationParameter(source.Platform, source.Key));
                    break;
                default:
                    ShowError($"No drill-down page exists for {source.Platform} categories.");
                    break;
            }
        }

        private void CategoriesWrapGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is ItemsWrapGrid wrapGrid)
            {
                double availableWidth = e.NewSize.Width;

                double desiredItemWidth = 260;
                double itemMargin = 8;

                int columnsCount = Math.Max(1, (int)(availableWidth / desiredItemWidth));
                columnsCount = Math.Min(columnsCount, 6);
                wrapGrid.MaximumRowsOrColumns = columnsCount;

                double totalMarginWidth = (columnsCount - 1) * itemMargin;
                double newItemWidth = (availableWidth - totalMarginWidth) / columnsCount;
                double finalWidth = Math.Max(200, newItemWidth);

                double aspectRatio = 16.0 / 9.0;
                double imageHeight = finalWidth / aspectRatio;

                wrapGrid.ItemWidth = finalWidth;
                wrapGrid.ItemHeight = imageHeight + 44;

                wrapGrid.HorizontalAlignment = HorizontalAlignment.Stretch;
            }
        }
    }
}
