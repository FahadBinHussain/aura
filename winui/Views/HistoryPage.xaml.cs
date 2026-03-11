using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Aura.Services;
using System;

namespace Aura.Views
{
    public sealed partial class HistoryPage : Page
    {
        public HistoryPage()
        {
            this.InitializeComponent();
            this.Loaded += HistoryPage_Loaded;
            WallpaperHistoryService.Instance.HistoryChanged += OnHistoryChanged;
        }

        private void HistoryPage_Loaded(object sender, RoutedEventArgs e)
        {
            RebuildList();
        }

        private void OnHistoryChanged(object sender, EventArgs e)
        {
            // Rebuild list on UI thread when a new entry is added
            DispatcherQueue.TryEnqueue(() => RebuildList());
        }

        private void RebuildList()
        {
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
        }

        private Border BuildEntryCard(HistoryEntry entry)
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

            // Source badge (Manual / Slideshow)
            var sourceColor = entry.Source == "Manual"
                ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.SteelBlue)
                : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.SeaGreen);

            var sourceBadge = new Border
            {
                Background = sourceColor,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(5, 2, 5, 2),
                Child = new TextBlock
                {
                    Text = entry.Source,
                    FontSize = 10,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White)
                }
            };

            // Type badge (Desktop / Lock Screen)
            var typeBadge = new Border
            {
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(5, 2, 5, 2),
                Child = new TextBlock
                {
                    Text = entry.WallpaperType,
                    FontSize = 10,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White)
                }
            };

            var badgeRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4
            };
            badgeRow.Children.Add(typeBadge);
            badgeRow.Children.Add(sourceBadge);

            // Title + timestamp
            var titleText = new TextBlock
            {
                Text = entry.Title,
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            var timeText = new TextBlock
            {
                Text = entry.Timestamp.ToString("MMM d, yyyy  h:mm tt"),
                FontSize = 11,
                Opacity = 0.55
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

            return new Border
            {
                Padding = new Thickness(12, 8, 12, 8),
                CornerRadius = new CornerRadius(8),
                Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                BorderThickness = new Thickness(1),
                Child = row
            };
        }

        private void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            WallpaperHistoryService.Instance.Entries.Clear();
            RebuildList();
        }
    }
}
