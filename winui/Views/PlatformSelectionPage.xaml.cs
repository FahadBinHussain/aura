using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Aura.Views.AlphaCoders;
using Aura.Views.Backiee;

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
                "Artstation",
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

            // Special handling for Backiee and Alpha Coders platforms
            if (selectedPlatform == "Backiee" || selectedPlatform == "Alpha Coders")
            {
                // Get the main window and navigate
                if (MainWindow.Instance != null)
                {
                    // Navigate to the appropriate page
                    if (selectedPlatform == "Alpha Coders")
                    {
                        // Navigate to Alpha Coders grid page
                        MainWindow.Instance.NavigationFrame.Navigate(typeof(AlphaCodersGridPage));
                    }
                    else if (selectedPlatform == "Backiee")
                    {
                        // Navigate to Backiee home page
                        MainWindow.Instance.NavigationFrame.Navigate(typeof(HomePage));
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
                Content = $"The {selectedPlatform} platform is not implemented yet. Please select Backiee or Alpha Coders for now.",
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };

            await dialog.ShowAsync();
        }
    }
}
