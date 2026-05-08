using System;
using System.Collections.Generic;
using Aura.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Aura.Views.AlphaCoders;
using Aura.Views.ArtStation;
using Aura.Views.Backiee;
using Aura.Views.PublicSources;

namespace Aura.Views
{
    public sealed partial class PlatformSelectionPage : Page
    {
        private string selectedPlatform;

        public PlatformSelectionPage()
        {
            this.InitializeComponent();

            // Initialize platforms list
            List<string> platforms = new List<string>
            {
                "Alpha Coders",
                "ArtFol",
                "Artgram",
                "ArtStation",
                "Backiee",
                "Behance",
                "Bing Wallpaper Archive",
                "Cara",
                "CGSociety",
                "CharacterDesignReferences",
                "DesktopNexus",
                "DeviantArt",
                "Digital Blasphemy",
                "Dribbble",
                "HDwallpapers",
                "Kuvva",
                "NewGrounds",
                "Peakpx",
                "Pexels",
                "Pixabay",
                "Pixiv",
                "Simple Desktops",
                "Unsplash",
                "Vladstudio",
                "Wallhaven",
                "Wallpaper Cave",
                "Wallpaper Engine",
                "WallpaperHub"
            };

            // Set the ItemsSource for the ItemsRepeater
            PlatformsRepeater.ItemsSource = platforms;

            // Register for ElementPrepared event to attach hover events
            PlatformsRepeater.ElementPrepared += PlatformsRepeater_ElementPrepared;
        }

        private void PlatformButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                // Get the platform name directly from the DataContext
                selectedPlatform = button.DataContext?.ToString();
            }

            // Special handling for implemented platforms
            if (selectedPlatform == "Backiee" ||
                selectedPlatform == "Alpha Coders" ||
                selectedPlatform == "ArtStation" ||
                PublicWallpaperService.IsSupportedPlatform(selectedPlatform))
            {
                // Get the main window and navigate
                if (MainWindow.Instance != null)
                {
                    // Remember the selected platform for the Home button
                    MainWindow.LastSelectedPlatform = selectedPlatform;

                    // Navigate to the appropriate page
                    if (selectedPlatform == "Alpha Coders")
                    {
                        // Navigate to Alpha Coders grid page
                        MainWindow.Instance.NavigationFrame.Navigate(typeof(AlphaCodersGridPage));
                    }
                    else if (selectedPlatform == "ArtStation")
                    {
                        // Navigate to ArtStation grid page
                        MainWindow.Instance.NavigationFrame.Navigate(typeof(ArtStationGridPage));
                    }
                    else if (selectedPlatform == "Backiee")
                    {
                        // Navigate to Backiee home page
                        MainWindow.Instance.NavigationFrame.Navigate(typeof(HomePage));
                    }
                    else if (PublicWallpaperService.IsSupportedPlatform(selectedPlatform))
                    {
                        MainWindow.Instance.NavigationFrame.Navigate(typeof(PublicWallpaperGridPage), selectedPlatform);
                    }
                }
            }
            else
            {
                // For other platforms, show a message that they're not implemented yet
                ShowNotImplementedMessage();
            }
        }

        private void PlatformsRepeater_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
        {
            if (args.Element is Button button)
            {
                // Add hover events to the button
                button.PointerEntered += Button_PointerEntered;
                button.PointerExited += Button_PointerExited;
            }
        }

        private void Button_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            // We'll use a simpler approach without animations for now
            if (sender is Button button && button.Content is Grid grid)
            {
                // Find the HoverOverlay border in the grid
                for (int i = 0; i < grid.Children.Count; i++)
                {
                    if (grid.Children[i] is Border border && border.Name == "HoverOverlay")
                    {
                        border.Opacity = 0.2;
                        break;
                    }
                }
            }
        }

        private void Button_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            // We'll use a simpler approach without animations for now
            if (sender is Button button && button.Content is Grid grid)
            {
                // Find the HoverOverlay border in the grid
                for (int i = 0; i < grid.Children.Count; i++)
                {
                    if (grid.Children[i] is Border border && border.Name == "HoverOverlay")
                    {
                        border.Opacity = 0;
                        break;
                    }
                }
            }
        }

        private async void ShowNotImplementedMessage()
        {
            ContentDialog dialog = new ContentDialog
            {
                Title = "Platform Not Available",
                Content = $"The {selectedPlatform} platform is not implemented yet. Available now: Backiee, Alpha Coders, ArtStation, Bing Wallpaper Archive, Pexels, Pixabay, Simple Desktops, Wallhaven, and WallpaperHub.",
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };

            await dialog.ShowAsync();
        }
    }
}
