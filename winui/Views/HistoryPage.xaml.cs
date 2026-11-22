using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Aura.Models;
using Aura.Services;
using Microsoft.UI.Input;
using System;

namespace Aura.Views
{
    public sealed partial class HistoryPage : Page
    {
        private bool _isInitialized = false;

        // a scroll restore waiting for the next layout pass (see RebuildList):
        // -1 means none pending. one hook serves every pending rebuild - a
        // later rebuild just overwrites the offset, the last one wins.
        private double _pendingScrollOffset = -1;
        private bool _scrollRestoreHooked = false;

        public HistoryPage()
        {
            this.InitializeComponent();

            // Enable navigation caching to preserve page state (same as
            // AlphaCodersGridPage): without this, going back from a detail
            // page created a FRESH HistoryPage, and Loaded rebuilt the list
            // from scratch - so the scroll position always reset to the top.
            // with the cache, back navigation reuses this instance and its
            // ScrollViewer keeps its offset. side effect: the HistoryChanged
            // subscription below now lives on one instance for the app's
            // lifetime instead of leaking one per visit.
            this.NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;

            this.Loaded += HistoryPage_Loaded;
            WallpaperHistoryService.Instance.HistoryChanged += OnHistoryChanged;
        }

        private void HistoryPage_Loaded(object sender, RoutedEventArgs e)
        {
            // Loaded fires on EVERY return to a cached page (it re-enters
            // the visual tree), so build the list only the first time -
            // rebuilding here would wipe the scroll position the cache
            // just preserved. later data changes come via OnHistoryChanged.
            if (_isInitialized) return;
            _isInitialized = true;
            RebuildList();
        }

        private void OnHistoryChanged(object sender, EventArgs e)
        {
            // Rebuild list on UI thread when a new entry is added
            DispatcherQueue.TryEnqueue(() => RebuildList());
        }

        private void RebuildList()
        {
            // a rebuild (slideshow apply landing while browsing, or while
            // on a detail page) must not move the user's scroll position:
            // keep the offset and put it back after the new cards lay out.
            double savedOffset = HistoryScrollViewer?.VerticalOffset ?? 0;

            HistoryListPanel.Children.Clear();

            var entries = WallpaperHistoryService.Instance.Entries;

            if (entries.Count == 0)
            {
                var empty = new TextBlock
                {
                    Text = "No wallpaper changes yet.",
                    Opacity = 0.5,
                    Margin = new Thickness(0, 32, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                HistoryListPanel.Children.Add(empty);
                return;
            }

            SubtitleText.Text = $"{entries.Count} wallpaper change{(entries.Count == 1 ? "" : "s")}";

            foreach (var entry in entries)
            {
                HistoryListPanel.Children.Add(BuildEntryCard(entry));
            }

            RestoreScrollOffset(savedOffset);
        }

        // the new cards have no laid-out extent yet, so a ChangeView right
        // now would clamp to 0 - wait for the next layout pass instead.
        // one-shot: the handler unhooks itself after it runs. if the page
        // is off-screen (cached, user on a detail page) no layout pass runs
        // until it returns, and the restore simply waits for that.
        private void RestoreScrollOffset(double offset)
        {
            if (offset <= 0) return;
            _pendingScrollOffset = offset;
            if (_scrollRestoreHooked) return;
            _scrollRestoreHooked = true;
            HistoryListPanel.LayoutUpdated += OnPanelLayoutUpdatedRestoreScroll;
        }

        private void OnPanelLayoutUpdatedRestoreScroll(object sender, object e)
        {
            HistoryListPanel.LayoutUpdated -= OnPanelLayoutUpdatedRestoreScroll;
            _scrollRestoreHooked = false;
            double offset = _pendingScrollOffset;
            _pendingScrollOffset = -1;
            if (offset > 0)
            {
                // instant jump, no scroll animation on the way back
                HistoryScrollViewer?.ChangeView(null, offset, null, true);
            }
        }

        private UIElement BuildEntryCard(HistoryEntry entry)
        {
            // Thumbnail image wrapped in a clipped border for rounded corners
            var image = new Image
            {
                Width = 100,
                Height = 60,
                Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill
            };

            if (!string.IsNullOrEmpty(entry.ImageUrl))
            {
                try
                {
                    Uri imageUri;
                    if (System.IO.File.Exists(entry.ImageUrl))
                        imageUri = new Uri(entry.ImageUrl);
                    else if (entry.ImageUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        imageUri = new Uri(entry.ImageUrl);
                    else
                        imageUri = null;

                    if (imageUri != null)
                        image.Source = new BitmapImage(imageUri);
                }
                catch { /* ignore image load errors */ }
            }

            var imageBorder = new Border
            {
                Width = 100,
                Height = 60,
                CornerRadius = new CornerRadius(6),
                Child = image
            };

            // Badges: the platform name and the category name
            var badgeRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4
            };

            var site = WallpaperHistoryService.DeriveSite(entry);
            if (string.IsNullOrEmpty(site) && !string.IsNullOrEmpty(entry.Source) && entry.Source != "Manual" && entry.Source != "Slideshow")
            {
                site = entry.Source;
            }

            if (!string.IsNullOrEmpty(site))
            {
                badgeRow.Children.Add(MakeBadge(site, Microsoft.UI.Colors.MediumPurple));
            }
            else if (!string.IsNullOrEmpty(entry.Source))
            {
                badgeRow.Children.Add(MakeBadge(entry.Source, Microsoft.UI.Colors.MediumPurple));
            }

            if (!string.IsNullOrEmpty(entry.Category))
            {
                badgeRow.Children.Add(MakeBadge(entry.Category, Microsoft.UI.Colors.Chocolate));
            }

            // Title + timestamp
            var titleText = new TextBlock
            {
                Text = entry.Title,
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"],
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            var timeText = new TextBlock
            {
                Text = entry.Timestamp.ToString("MMM d, yyyy  h:mm tt"),
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            };

            var textStack = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            textStack.Children.Add(badgeRow);
            textStack.Children.Add(titleText);
            textStack.Children.Add(timeText);

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            row.Children.Add(imageBorder);
            row.Children.Add(textStack);

            var card = new Border
            {
                Padding = new Thickness(12, 8, 12, 8),
                CornerRadius = new CornerRadius(8),
                Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                BorderThickness = new Thickness(1),
                Child = row
            };

            // the whole row is one button: click = navigate to the wallpaper's
            // detail page. Name = title so UIA (and keyboard users) see a clean
            // label instead of the concatenated badge soup.
            var button = new HistoryEntryButton
            {
                Content = card,
                Tag = entry, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                Padding = new Thickness(0),
                BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
                VerticalContentAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center,
                CornerRadius = new CornerRadius(8)
            };
            AutomationProperties.SetName(button, entry.Title);
            button.PointerEntered += (s, args) =>
                card.Background = (Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"];
            button.PointerExited += (s, args) =>
                card.Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
            button.Click += HistoryEntry_Click;
            return button;
        }

        private static Border MakeBadge(string text, Windows.UI.Color background) =>
            new Border
            {
                Background = new SolidColorBrush(background),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(5, 2, 5, 2),
                Child = new TextBlock
                {
                    Text = text,
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.White)
                }
            };

        // site derivation lives in WallpaperHistoryService.DeriveSite now - the
        // pill here and the click-time navigation rebuild read the same chain

        // Button subclass: ProtectedCursor is a protected member, so only the
        // deriving type can set the hand cursor.
        private sealed class HistoryEntryButton : Button
        {
            public HistoryEntryButton()
            {
                ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
            }
        }

        private void HistoryEntry_Click(object sender, RoutedEventArgs e)
        {
            var entry = (sender as Button)?.Tag as HistoryEntry;
            if (entry == null)
            {
                return;
            }

            // stored snapshot first; rows saved without one (slideshow-set rows
            // + pre-feature rows) rebuild it here from the live slideshow batch
            // or the row's own applied local file - see RebuildNavigation
            var navigation = entry.Navigation?.Wallpaper != null && !string.IsNullOrEmpty(entry.Navigation.Page)
                ? entry.Navigation
                : WallpaperHistoryService.RebuildNavigation(
                    entry,
                    SlideshowService.Instance.DesktopBatch,
                    SlideshowService.Instance.LockScreenBatch);

            if (navigation?.Wallpaper == null || string.IsNullOrEmpty(navigation.Page))
            {
                ShowStatus(
                    "No wallpaper data is stored for this entry - nothing to open.",
                    InfoBarSeverity.Error);
                return;
            }

            var item = navigation.Wallpaper.ToWallpaperItem();
            bool navigated = navigation.Page switch
            {
                "Public" => Frame.Navigate(typeof(PublicSources.PublicWallpaperDetailPage),
                    new PublicWallpaperNavigationParameter(navigation.Platform, item)),
                "Backiee" => Frame.Navigate(typeof(Backiee.WallpaperDetailPage), item),
                "AlphaCoders" => Frame.Navigate(typeof(AlphaCoders.WallpaperDetailPage), item),
                "ArtStation" => Frame.Navigate(typeof(ArtStation.ArtStationDetailPage), item),
                _ => false
            };

            if (navigated)
            {
                StatusInfoBar.IsOpen = false;
            }
            else
            {
                ShowStatus(
                    navigation.Page is "Public" or "Backiee" or "AlphaCoders" or "ArtStation"
                        ? $"Could not open the detail page for \"{entry.Title}\"."
                        : $"Unknown detail page '{navigation.Page}' stored for \"{entry.Title}\".",
                    InfoBarSeverity.Error);
            }
        }

        private void ShowStatus(string message, InfoBarSeverity severity)
        {
            StatusInfoBar.Message = message;
            StatusInfoBar.Severity = severity;
            StatusInfoBar.IsOpen = true;
        }

        private void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            WallpaperHistoryService.Instance.Entries.Clear();
            RebuildList();
        }
    }
}
