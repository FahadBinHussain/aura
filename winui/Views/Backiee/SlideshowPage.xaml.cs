using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Dispatching;
using System;
using System.Collections.Generic;
using Aura.Services;
using System.Threading.Tasks;
using Aura.Models;
using System.IO;
using System.Text.Json;

namespace Aura.Views.Backiee
{
    public sealed partial class SlideshowPage : Page
    {
        // Desktop slideshow settings
        private bool _desktopSlideshowEnabled = false;
        private List<string> _desktopPlatforms = new List<string>();
        private string _desktopCategory = "";

        // Lock screen slideshow settings
        private bool _lockScreenSlideshowEnabled = false;
        private List<string> _lockScreenPlatforms = new List<string>();
        private string _lockScreenCategory = "";

        // Separate refresh intervals for desktop and lock screen
        private string _desktopRefreshInterval = "12 hours";
        private string _lockScreenRefreshInterval = "12 hours";
        
        // Current wallpaper items for navigation
        private WallpaperItem? _currentDesktopWallpaperItem = null;
        private WallpaperItem? _currentLockScreenWallpaperItem = null;
        
        // Countdown timers
        private DispatcherQueueTimer? _countdownTimer;

        public SlideshowPage()
        {
            this.InitializeComponent();
            
            // Subscribe to slideshow wallpaper change events
            SlideshowService.Instance.DesktopWallpaperChanged += OnDesktopWallpaperChanged;
            SlideshowService.Instance.LockScreenWallpaperChanged += OnLockScreenWallpaperChanged;
            
            // Delay loading settings until page is fully loaded
            this.Loaded += SlideshowPage_Loaded;
            this.Unloaded += SlideshowPage_Unloaded;
            
            // Start countdown timer
            StartCountdownTimer();
        }
        
        private void SlideshowPage_Unloaded(object sender, RoutedEventArgs e)
        {
            // Stop countdown timer when page is unloaded
            _countdownTimer?.Stop();
            _countdownTimer = null;
        }
        
        private void StartCountdownTimer()
        {
            _countdownTimer = DispatcherQueue.CreateTimer();
            _countdownTimer.Interval = TimeSpan.FromSeconds(1);
            _countdownTimer.IsRepeating = true;
            _countdownTimer.Tick += (s, e) => UpdateCountdowns();
            _countdownTimer.Start();
        }
        
        private void UpdateCountdowns()
        {
            // Update desktop countdown
            if (_desktopSlideshowEnabled && SlideshowService.Instance.DesktopNextChangeTime > DateTime.MinValue)
            {
                var timeRemaining = SlideshowService.Instance.DesktopNextChangeTime - DateTime.Now;
                
                if (timeRemaining.TotalSeconds > 0)
                {
                    DesktopCountdownText.Text = $"Next wallpaper in: {FormatTimeSpan(timeRemaining)}";
                    DesktopCountdownText.Visibility = Visibility.Visible;
                }
                else
                {
                    DesktopCountdownText.Text = "Changing wallpaper...";
                    DesktopCountdownText.Visibility = Visibility.Visible;
                }
            }
            else
            {
                DesktopCountdownText.Visibility = Visibility.Collapsed;
            }
            
            // Update lock screen countdown
            if (_lockScreenSlideshowEnabled && SlideshowService.Instance.LockScreenNextChangeTime > DateTime.MinValue)
            {
                var timeRemaining = SlideshowService.Instance.LockScreenNextChangeTime - DateTime.Now;
                
                if (timeRemaining.TotalSeconds > 0)
                {
                    LockScreenCountdownText.Text = $"Next wallpaper in: {FormatTimeSpan(timeRemaining)}";
                    LockScreenCountdownText.Visibility = Visibility.Visible;
                }
                else
                {
                    LockScreenCountdownText.Text = "Changing wallpaper...";
                    LockScreenCountdownText.Visibility = Visibility.Visible;
                }
            }
            else
            {
                LockScreenCountdownText.Visibility = Visibility.Collapsed;
            }
        }
        
        private string FormatTimeSpan(TimeSpan timeSpan)
        {
            if (timeSpan.TotalDays >= 1)
            {
                return $"{(int)timeSpan.TotalDays}d {timeSpan.Hours}h {timeSpan.Minutes}m";
            }
            else if (timeSpan.TotalHours >= 1)
            {
                return $"{(int)timeSpan.TotalHours}h {timeSpan.Minutes}m {timeSpan.Seconds}s";
            }
            else if (timeSpan.TotalMinutes >= 1)
            {
                return $"{(int)timeSpan.TotalMinutes}m {timeSpan.Seconds}s";
            }
            else
            {
                return $"{(int)timeSpan.TotalSeconds}s";
            }
        }
        
        private async void SlideshowPage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadSettings();
            await LoadCurrentWallpapers();
        }

        private void OnDesktopWallpaperChanged(object sender, string imageUrl)
        {
            DispatcherQueue.TryEnqueue(async () =>
            {
                await LoadDesktopWallpaper(imageUrl);
            });
        }

        private void OnLockScreenWallpaperChanged(object sender, string imageUrl)
        {
            DispatcherQueue.TryEnqueue(async () =>
            {
                await LoadLockScreenWallpaper(imageUrl);
            });
        }

        private async Task LoadCurrentWallpapers()
        {
            try
            {
                // Get current wallpaper URLs from SlideshowService
                string desktopUrl = SlideshowService.Instance.GetCurrentDesktopWallpaperUrl();
                string lockScreenUrl = SlideshowService.Instance.GetCurrentLockScreenWallpaperUrl();
                
                if (!string.IsNullOrEmpty(desktopUrl))
                {
                    await LoadDesktopWallpaper(desktopUrl);
                }
                
                if (!string.IsNullOrEmpty(lockScreenUrl))
                {
                    await LoadLockScreenWallpaper(lockScreenUrl);
                }
            }
            catch (Exception ex)
            {
            }
        }

        private async Task LoadDesktopWallpaper(string imageUrl)
        {
            try
            {
                if (string.IsNullOrEmpty(imageUrl)) return;
                
                var bitmap = new BitmapImage(new Uri(imageUrl));
                DesktopSlideshowImage.Source = bitmap;
            }
            catch (Exception ex)
            {
            }
        }

        private async Task LoadLockScreenWallpaper(string imageUrl)
        {
            try
            {
                if (string.IsNullOrEmpty(imageUrl)) return;
                
                var bitmap = new BitmapImage(new Uri(imageUrl));
                LockScreenSlideshowImage.Source = bitmap;
            }
            catch (Exception ex)
            {
            }
        }

        private void LogInfo(string message)
        {
            try
            {
                ((App)Application.Current).LogInfo($"[SlideshowPage] {message}");
            }
            catch
            {
            }
        }

        private void ExpandDesktopSlideshow_Click(object sender, RoutedEventArgs e)
        {
            
            // Get the current desktop wallpaper item
            var wallpaperItem = SlideshowService.Instance.GetCurrentDesktopWallpaperItem();
            
            if (wallpaperItem != null)
            {
                Frame.Navigate(typeof(WallpaperDetailPage), wallpaperItem);
            }
            else
            {
            }
        }

        private async void NextDesktopSlideshow_Click(object sender, RoutedEventArgs e)
        {
            await SlideshowService.Instance.NextDesktopWallpaper();
        }

        private async void EditDesktopSlideshow_Click(object sender, RoutedEventArgs e)
        {
            await ShowSlideshowSettingsDialog("Desktop");
        }

        private async void ScheduleDesktopSlideshow_Click(object sender, RoutedEventArgs e)
        {
            await ShowScheduleDialog("Desktop");
        }

        private void ExpandLockScreenSlideshow_Click(object sender, RoutedEventArgs e)
        {
            
            // Get the current lock screen wallpaper item
            var wallpaperItem = SlideshowService.Instance.GetCurrentLockScreenWallpaperItem();
            
            if (wallpaperItem != null)
            {
                Frame.Navigate(typeof(WallpaperDetailPage), wallpaperItem);
            }
            else
            {
            }
        }

        private async void NextLockScreenSlideshow_Click(object sender, RoutedEventArgs e)
        {
            await SlideshowService.Instance.NextLockScreenWallpaper();
        }

        private async void EditLockScreenSlideshow_Click(object sender, RoutedEventArgs e)
        {
            await ShowSlideshowSettingsDialog("Lock Screen");
        }

        private async void ScheduleLockScreenSlideshow_Click(object sender, RoutedEventArgs e)
        {
            await ShowScheduleDialog("Lock Screen");
        }

        private void LoadSettings()
        {
            try
            {
                var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Aura", "slideshow_settings.json");
                
                if (!File.Exists(settingsPath))
                {
                    return;
                }

                var json = File.ReadAllText(settingsPath);
                var settings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                
                if (settings == null)
                {
                    return;
                }
                
                // Load desktop slideshow settings
                if (settings.ContainsKey("DesktopSlideshowEnabled"))
                {
                    _desktopSlideshowEnabled = settings["DesktopSlideshowEnabled"].GetBoolean();
                }
                if (settings.ContainsKey("DesktopSlideshowPlatforms"))
                {
                    _desktopPlatforms = JsonSerializer.Deserialize<List<string>>(settings["DesktopSlideshowPlatforms"].GetRawText()) ?? new List<string>();
                }
                if (settings.ContainsKey("DesktopSlideshowCategory"))
                {
                    _desktopCategory = settings["DesktopSlideshowCategory"].GetString() ?? "Latest Wallpapers";
                }
                if (settings.ContainsKey("DesktopSlideshowInterval"))
                {
                    _desktopRefreshInterval = settings["DesktopSlideshowInterval"].GetString() ?? "12 hours";
                }
                
                // Load lock screen slideshow settings
                if (settings.ContainsKey("LockScreenSlideshowEnabled"))
                {
                    _lockScreenSlideshowEnabled = settings["LockScreenSlideshowEnabled"].GetBoolean();
                }
                if (settings.ContainsKey("LockScreenSlideshowPlatforms"))
                {
                    _lockScreenPlatforms = JsonSerializer.Deserialize<List<string>>(settings["LockScreenSlideshowPlatforms"].GetRawText()) ?? new List<string>();
                }
                if (settings.ContainsKey("LockScreenSlideshowCategory"))
                {
                    _lockScreenCategory = settings["LockScreenSlideshowCategory"].GetString() ?? "Latest Wallpapers";
                }
                if (settings.ContainsKey("LockScreenSlideshowInterval"))
                {
                    _lockScreenRefreshInterval = settings["LockScreenSlideshowInterval"].GetString() ?? "12 hours";
                }
                
                
                // Restart slideshows if they were enabled
                _ = RestoreSlideshows();
            }
            catch (Exception ex)
            {
            }
            
            UpdateStatusUI();
        }
        
        private async Task UpdateStatusUIAsync()
        {
            
            // Update desktop slideshow status
            if (_desktopSlideshowEnabled && _desktopPlatforms.Count > 0 && !string.IsNullOrEmpty(_desktopCategory))
            {
                string platformsText = _desktopPlatforms.Count == 1 ? _desktopPlatforms[0] : $"{_desktopPlatforms.Count} platforms";
                DesktopStatusText.Text = $"{platformsText} - {_desktopCategory} (Refresh: {_desktopRefreshInterval})";
            }
            else
            {
                DesktopStatusText.Text = "No slideshow set";
            }

            // Update lock screen slideshow status
            if (_lockScreenSlideshowEnabled && _lockScreenPlatforms.Count > 0 && !string.IsNullOrEmpty(_lockScreenCategory))
            {
                string platformsText = _lockScreenPlatforms.Count == 1 ? _lockScreenPlatforms[0] : $"{_lockScreenPlatforms.Count} platforms";
                LockScreenStatusText.Text = $"{platformsText} - {_lockScreenCategory} (Refresh: {_lockScreenRefreshInterval})";
            }
            else
            {
                LockScreenStatusText.Text = "No slideshow set";
            }
            
            // Wait a moment for the service to update next change times
            await Task.Delay(100);
            
            // Force immediate countdown update
            UpdateCountdowns();
        }
        
        private void UpdateStatusUI()
        {
            _ = UpdateStatusUIAsync();
        }
        
        private void SaveSettings()
        {
            try
            {
                var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Aura", "slideshow_settings.json");
                var directory = Path.GetDirectoryName(settingsPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory!);
                }

                var settings = new Dictionary<string, object>
                {
                    ["DesktopSlideshowEnabled"] = _desktopSlideshowEnabled,
                    ["DesktopSlideshowPlatforms"] = _desktopPlatforms.Count > 0 ? _desktopPlatforms : new List<string> { "Backiee" },
                    ["DesktopSlideshowCategory"] = string.IsNullOrEmpty(_desktopCategory) ? "Latest Wallpapers" : _desktopCategory,
                    ["DesktopSlideshowInterval"] = string.IsNullOrEmpty(_desktopRefreshInterval) ? "12 hours" : _desktopRefreshInterval,
                    
                    ["LockScreenSlideshowEnabled"] = _lockScreenSlideshowEnabled,
                    ["LockScreenSlideshowPlatforms"] = _lockScreenPlatforms.Count > 0 ? _lockScreenPlatforms : new List<string> { "Backiee" },
                    ["LockScreenSlideshowCategory"] = string.IsNullOrEmpty(_lockScreenCategory) ? "Latest Wallpapers" : _lockScreenCategory,
                    ["LockScreenSlideshowInterval"] = string.IsNullOrEmpty(_lockScreenRefreshInterval) ? "12 hours" : _lockScreenRefreshInterval
                };

                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(settingsPath, json);
                
            }
            catch (Exception ex)
            {
            }
        }
        
        private async Task RestoreSlideshows()
        {
            try
            {
                // Restore desktop slideshow if it was enabled
                if (_desktopSlideshowEnabled && _desktopPlatforms.Count > 0 && !string.IsNullOrEmpty(_desktopCategory))
                {
                    var interval = SlideshowService.ParseInterval(_desktopRefreshInterval);
                    await SlideshowService.Instance.StartDesktopSlideshow(_desktopPlatforms, _desktopCategory, interval, App.MainDispatcherQueue ?? this.DispatcherQueue);
                }
                
                // Restore lock screen slideshow if it was enabled
                if (_lockScreenSlideshowEnabled && _lockScreenPlatforms.Count > 0 && !string.IsNullOrEmpty(_lockScreenCategory))
                {
                    var interval = SlideshowService.ParseInterval(_lockScreenRefreshInterval);
                    await SlideshowService.Instance.StartLockScreenSlideshow(_lockScreenPlatforms, _lockScreenCategory, interval, App.MainDispatcherQueue ?? this.DispatcherQueue);
                }
            }
            catch (Exception ex)
            {
            }
        }

        private async System.Threading.Tasks.Task ShowSlideshowSettingsDialog(string slideshowType)
        {
            // Create the content dialog
            var dialog = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                Title = $"Set {slideshowType.ToLower()} slideshow",
                PrimaryButtonText = "Set",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary
            };

            // Create the content
            var contentPanel = new StackPanel
            {
                Spacing = 16,
                Margin = new Thickness(0, 12, 0, 12)
            };

            // Enable slideshow toggle
            var toggleCard = new Border
            {
                Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 12, 16, 12)
            };

            var toggleGrid = new Grid();
            toggleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            toggleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var toggleLabel = new TextBlock
            {
                Text = "Enable slideshow",
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 16
            };
            Grid.SetColumn(toggleLabel, 0);

            // Load existing settings for this slideshow type
            bool currentEnabled = slideshowType == "Desktop" ? _desktopSlideshowEnabled : _lockScreenSlideshowEnabled;
            List<string> currentPlatforms = slideshowType == "Desktop" ? _desktopPlatforms : _lockScreenPlatforms;
            string currentCategory = slideshowType == "Desktop" ? _desktopCategory : _lockScreenCategory;

            var toggleSwitch = new ToggleSwitch
            {
                IsOn = currentEnabled,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(toggleSwitch, 1);

            toggleGrid.Children.Add(toggleLabel);
            toggleGrid.Children.Add(toggleSwitch);
            toggleCard.Child = toggleGrid;
            contentPanel.Children.Add(toggleCard);

            // Change slideshow section
            var changeSlideshowLabel = new TextBlock
            {
                Text = "Change slideshow",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 16,
                Margin = new Thickness(0, 8, 0, 4)
            };
            contentPanel.Children.Add(changeSlideshowLabel);

            // Platform selection with checkboxes
            var platformLabel = new TextBlock
            {
                Text = "Select Platforms",
                FontSize = 14,
                Margin = new Thickness(0, 8, 0, 4)
            };
            contentPanel.Children.Add(platformLabel);

            var platformCheckBoxBackiee = new CheckBox
            {
                Content = "Backiee",
                IsChecked = currentPlatforms.Contains("Backiee")
            };
            contentPanel.Children.Add(platformCheckBoxBackiee);

            var platformCheckBoxAlphaCoders = new CheckBox
            {
                Content = "AlphaCoders",
                IsChecked = currentPlatforms.Contains("AlphaCoders")
            };
            contentPanel.Children.Add(platformCheckBoxAlphaCoders);

            var platformCheckBoxArtStation = new CheckBox
            {
                Content = "ArtStation",
                IsChecked = currentPlatforms.Contains("ArtStation")
            };
            contentPanel.Children.Add(platformCheckBoxArtStation);

            var platformCheckBoxWallhaven = new CheckBox
            {
                Content = "Wallhaven",
                IsChecked = currentPlatforms.Contains("Wallhaven")
            };
            contentPanel.Children.Add(platformCheckBoxWallhaven);

            var platformCheckBoxBing = new CheckBox
            {
                Content = "Bing Wallpaper Archive",
                IsChecked = currentPlatforms.Contains("Bing Wallpaper Archive")
            };
            contentPanel.Children.Add(platformCheckBoxBing);

            var platformCheckBoxSimpleDesktops = new CheckBox
            {
                Content = "Simple Desktops",
                IsChecked = currentPlatforms.Contains("Simple Desktops")
            };
            contentPanel.Children.Add(platformCheckBoxSimpleDesktops);

            var platformCheckBoxWallpaperHub = new CheckBox
            {
                Content = "WallpaperHub",
                IsChecked = currentPlatforms.Contains("WallpaperHub")
            };
            contentPanel.Children.Add(platformCheckBoxWallpaperHub);

            var platformCheckBoxPexels = new CheckBox
            {
                Content = "Pexels",
                IsChecked = currentPlatforms.Contains("Pexels")
            };
            contentPanel.Children.Add(platformCheckBoxPexels);

            var platformCheckBoxPixabay = new CheckBox
            {
                Content = "Pixabay",
                IsChecked = currentPlatforms.Contains("Pixabay")
            };
            contentPanel.Children.Add(platformCheckBoxPixabay);

            // Category dropdown
            var categoryComboBox = new ComboBox
            {
                Header = "Select Category",
                PlaceholderText = "Choose a category",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinWidth = 400
            };
            
            // Initialize categories based on current platforms selection
            var updateCategories = new Action(() =>
            {
                categoryComboBox.Items.Clear();
                bool hasBackiee = platformCheckBoxBackiee.IsChecked == true;
                bool hasAlphaCoders = platformCheckBoxAlphaCoders.IsChecked == true;
                bool hasArtStation = platformCheckBoxArtStation.IsChecked == true;
                bool hasWallhaven = platformCheckBoxWallhaven.IsChecked == true;
                bool hasBing = platformCheckBoxBing.IsChecked == true;
                bool hasSimpleDesktops = platformCheckBoxSimpleDesktops.IsChecked == true;
                bool hasWallpaperHub = platformCheckBoxWallpaperHub.IsChecked == true;
                bool hasPexels = platformCheckBoxPexels.IsChecked == true;
                bool hasPixabay = platformCheckBoxPixabay.IsChecked == true;
                
                // Backiee categories
                if (hasBackiee)
                {
                    categoryComboBox.Items.Add("Latest Wallpapers");
                    categoryComboBox.Items.Add("8K UltraHD");
                    categoryComboBox.Items.Add("AI Generated");
                }
                
                // AlphaCoders categories
                if (hasAlphaCoders)
                {
                    categoryComboBox.Items.Add("4K Wallpapers");
                    categoryComboBox.Items.Add("Harvest Wallpapers");
                    categoryComboBox.Items.Add("Rain Wallpapers");
                }
                
                // For other platforms, add a generic "All" category
                if (hasArtStation || hasWallhaven || hasBing || hasSimpleDesktops || hasWallpaperHub || hasPexels || hasPixabay)
                {
                    if (categoryComboBox.Items.Count == 0 || !categoryComboBox.Items.Contains("All"))
                    {
                        categoryComboBox.Items.Add("All");
                    }
                }
                
                // Set selected category based on saved settings
                if (!string.IsNullOrEmpty(currentCategory))
                {
                    for (int i = 0; i < categoryComboBox.Items.Count; i++)
                    {
                        if (categoryComboBox.Items[i]?.ToString() == currentCategory)
                        {
                            categoryComboBox.SelectedIndex = i;
                            return;
                        }
                    }
                }
                
                if (categoryComboBox.Items.Count > 0)
                {
                    categoryComboBox.SelectedIndex = 0;
                }
            });
            
            // Update categories when platform checkboxes change
            platformCheckBoxBackiee.Checked += (s, e) => updateCategories();
            platformCheckBoxBackiee.Unchecked += (s, e) => updateCategories();
            platformCheckBoxAlphaCoders.Checked += (s, e) => updateCategories();
            platformCheckBoxAlphaCoders.Unchecked += (s, e) => updateCategories();
            platformCheckBoxArtStation.Checked += (s, e) => updateCategories();
            platformCheckBoxArtStation.Unchecked += (s, e) => updateCategories();
            platformCheckBoxWallhaven.Checked += (s, e) => updateCategories();
            platformCheckBoxWallhaven.Unchecked += (s, e) => updateCategories();
            platformCheckBoxBing.Checked += (s, e) => updateCategories();
            platformCheckBoxBing.Unchecked += (s, e) => updateCategories();
            platformCheckBoxSimpleDesktops.Checked += (s, e) => updateCategories();
            platformCheckBoxSimpleDesktops.Unchecked += (s, e) => updateCategories();
            platformCheckBoxWallpaperHub.Checked += (s, e) => updateCategories();
            platformCheckBoxWallpaperHub.Unchecked += (s, e) => updateCategories();
            platformCheckBoxPexels.Checked += (s, e) => updateCategories();
            platformCheckBoxPexels.Unchecked += (s, e) => updateCategories();
            platformCheckBoxPixabay.Checked += (s, e) => updateCategories();
            platformCheckBoxPixabay.Unchecked += (s, e) => updateCategories();
            
            // Initial category setup
            updateCategories();
            
            contentPanel.Children.Add(categoryComboBox);

            dialog.Content = contentPanel;

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                // Collect selected platforms
                var selectedPlatforms = new List<string>();
                if (platformCheckBoxBackiee.IsChecked == true)
                    selectedPlatforms.Add("Backiee");
                if (platformCheckBoxAlphaCoders.IsChecked == true)
                    selectedPlatforms.Add("AlphaCoders");
                if (platformCheckBoxArtStation.IsChecked == true)
                    selectedPlatforms.Add("ArtStation");
                if (platformCheckBoxWallhaven.IsChecked == true)
                    selectedPlatforms.Add("Wallhaven");
                if (platformCheckBoxBing.IsChecked == true)
                    selectedPlatforms.Add("Bing Wallpaper Archive");
                if (platformCheckBoxSimpleDesktops.IsChecked == true)
                    selectedPlatforms.Add("Simple Desktops");
                if (platformCheckBoxWallpaperHub.IsChecked == true)
                    selectedPlatforms.Add("WallpaperHub");
                if (platformCheckBoxPexels.IsChecked == true)
                    selectedPlatforms.Add("Pexels");
                if (platformCheckBoxPixabay.IsChecked == true)
                    selectedPlatforms.Add("Pixabay");

                // Validate at least one platform is selected
                if (selectedPlatforms.Count == 0)
                {
                    var errorDialog = new ContentDialog
                    {
                        XamlRoot = this.XamlRoot,
                        Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                        Title = "No Platform Selected",
                        Content = "Please select at least one platform for the slideshow.",
                        CloseButtonText = "OK"
                    };
                    await errorDialog.ShowAsync();
                    return;
                }

                // Save slideshow settings
                bool isEnabled = toggleSwitch.IsOn;
                string selectedCategory = categoryComboBox.SelectedItem?.ToString() ?? "Latest Wallpapers";


                // Save to class fields and start/stop slideshow
                if (slideshowType == "Desktop")
                {
                    _desktopSlideshowEnabled = isEnabled;
                    _desktopPlatforms = selectedPlatforms;
                    _desktopCategory = selectedCategory;
                    

                    // Start or stop slideshow
                    if (isEnabled && _desktopPlatforms.Count > 0 && !string.IsNullOrEmpty(_desktopCategory))
                    {
                        var interval = SlideshowService.ParseInterval(_desktopRefreshInterval);
                        await SlideshowService.Instance.StartDesktopSlideshow(_desktopPlatforms, _desktopCategory, interval, App.MainDispatcherQueue ?? this.DispatcherQueue);
                    }
                    else
                    {
                        SlideshowService.Instance.StopDesktopSlideshow();
                    }
                }
                else
                {
                    _lockScreenSlideshowEnabled = isEnabled;
                    _lockScreenPlatforms = selectedPlatforms;
                    _lockScreenCategory = selectedCategory;

                    // Start or stop slideshow
                    if (isEnabled && _lockScreenPlatforms.Count > 0 && !string.IsNullOrEmpty(_lockScreenCategory))
                    {
                        var interval = SlideshowService.ParseInterval(_lockScreenRefreshInterval);
                        await SlideshowService.Instance.StartLockScreenSlideshow(_lockScreenPlatforms, _lockScreenCategory, interval, App.MainDispatcherQueue ?? this.DispatcherQueue);
                    }
                    else
                    {
                        SlideshowService.Instance.StopLockScreenSlideshow();
                    }
                }
                
                // Save settings to local storage and update UI
                SaveSettings();
                await UpdateStatusUIAsync();
            }
        }

        private async System.Threading.Tasks.Task ShowScheduleDialog(string slideshowType)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                Title = "Slideshow refresh interval",
                PrimaryButtonText = "Set",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary
            };

            // Create the content panel
            var contentPanel = new StackPanel
            {
                Spacing = 16,
                Margin = new Thickness(0, 12, 0, 12)
            };

            // Description text
            var descriptionText = new TextBlock
            {
                Text = "This setting applies to both desktop and lock screen slideshows.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14
            };

            // Horizontal panel for number + unit
            var intervalPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12
            };

            // Number input
            var numberBox = new Microsoft.UI.Xaml.Controls.NumberBox
            {
                PlaceholderText = "Enter value",
                Minimum = 1,
                Maximum = 999,
                SpinButtonPlacementMode = Microsoft.UI.Xaml.Controls.NumberBoxSpinButtonPlacementMode.Inline,
                Width = 200
            };

            // Unit dropdown
            var unitComboBox = new ComboBox
            {
                PlaceholderText = "Unit",
                MinWidth = 150
            };

            // Add unit options
            unitComboBox.Items.Add("Seconds");
            unitComboBox.Items.Add("Minutes");
            unitComboBox.Items.Add("Hours");
            unitComboBox.Items.Add("Days");

            // Parse current interval based on slideshow type to set defaults
            string currentInterval = slideshowType == "Desktop" ? _desktopRefreshInterval : _lockScreenRefreshInterval;
            ParseIntervalString(currentInterval, out double value, out string unit);
            numberBox.Value = value;
            
            // Set unit dropdown
            int unitIndex = unit switch
            {
                "Seconds" => 0,
                "Minutes" => 1,
                "Hours" => 2,
                "Days" => 3,
                _ => 2 // default to Hours
            };
            unitComboBox.SelectedIndex = unitIndex;

            intervalPanel.Children.Add(numberBox);
            intervalPanel.Children.Add(unitComboBox);

            contentPanel.Children.Add(descriptionText);
            contentPanel.Children.Add(intervalPanel);

            dialog.Content = contentPanel;

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary && unitComboBox.SelectedItem != null && numberBox.Value > 0)
            {
                
                double intervalValue = numberBox.Value;
                string intervalUnit = unitComboBox.SelectedItem.ToString();
                string selectedInterval = $"{intervalValue} {intervalUnit}";
                
                
                // Parse and validate minimum 10 seconds
                var interval = SlideshowService.ParseInterval(selectedInterval);
                
                if (interval.TotalSeconds < 10)
                {
                    
                    var errorDialog = new ContentDialog
                    {
                        XamlRoot = this.XamlRoot,
                        Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                        Title = "Invalid Interval",
                        Content = "The interval must be at least 10 seconds.",
                        CloseButtonText = "OK"
                    };
                    await errorDialog.ShowAsync();
                    return;
                }
                
                // Save to appropriate class field based on slideshow type
                if (slideshowType == "Desktop")
                {
                    _desktopRefreshInterval = selectedInterval;
                    
                    // Restart desktop slideshow with new interval if enabled
                    if (_desktopSlideshowEnabled && _desktopPlatforms.Count > 0 && !string.IsNullOrEmpty(_desktopCategory))
                    {
                        await SlideshowService.Instance.StartDesktopSlideshow(_desktopPlatforms, _desktopCategory, interval, App.MainDispatcherQueue ?? this.DispatcherQueue);
                    }
                }
                else
                {
                    _lockScreenRefreshInterval = selectedInterval;
                    
                    // Restart lock screen slideshow with new interval if enabled
                    if (_lockScreenSlideshowEnabled && _lockScreenPlatforms.Count > 0 && !string.IsNullOrEmpty(_lockScreenCategory))
                    {
                        await SlideshowService.Instance.StartLockScreenSlideshow(_lockScreenPlatforms, _lockScreenCategory, interval, App.MainDispatcherQueue ?? this.DispatcherQueue);
                    }
                }
                
                // Save settings to local storage and update UI
                SaveSettings();
                
                await UpdateStatusUIAsync();
            }
        }

        // Helper method to parse interval string like "12 Hours" or "30 Minutes"
        private void ParseIntervalString(string intervalStr, out double value, out string unit)
        {
            // Default values
            value = 12;
            unit = "Hours";

            if (string.IsNullOrWhiteSpace(intervalStr))
                return;

            var parts = intervalStr.Trim().Split(' ');
            if (parts.Length >= 2)
            {
                if (double.TryParse(parts[0], out double parsedValue))
                {
                    value = parsedValue;
                }
                
                // Capitalize first letter to match ComboBox items
                string unitPart = parts[1].Trim();
                if (!string.IsNullOrEmpty(unitPart))
                {
                    unit = char.ToUpper(unitPart[0]) + unitPart.Substring(1).ToLower();
                }
            }
        }
    }
}
