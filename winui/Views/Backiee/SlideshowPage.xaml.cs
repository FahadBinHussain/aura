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
using System.Linq;

namespace Aura.Views.Backiee
{
    public sealed partial class SlideshowPage : Page
    {
        // Desktop slideshow settings
        private bool _desktopSlideshowEnabled = false;
        private List<string> _desktopPlatforms = new List<string>();
        private string _desktopCategory = "";
        private string _desktopBasis = "Latest";
        private List<string> _desktopCategories = new List<string>();

        // Lock screen slideshow settings
        private bool _lockScreenSlideshowEnabled = false;
        private List<string> _lockScreenPlatforms = new List<string>();
        private string _lockScreenCategory = "";
        private string _lockScreenBasis = "Latest";
        private List<string> _lockScreenCategories = new List<string>();

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

            // Delay loading settings until page is fully loaded
            this.Loaded += SlideshowPage_Loaded;
            this.Unloaded += SlideshowPage_Unloaded;

            // Start countdown timer
            StartCountdownTimer();
        }

        private void SubscribeServiceEvents()
        {
            // -= first: Loaded can fire again on the same instance and a singleton
            // event must never accumulate dead-page handlers
            SlideshowService.Instance.DesktopWallpaperChanged -= OnDesktopWallpaperChanged;
            SlideshowService.Instance.LockScreenWallpaperChanged -= OnLockScreenWallpaperChanged;
            SlideshowService.Instance.ErrorsChanged -= OnSlideshowErrorsChanged;
            SlideshowService.Instance.DesktopWallpaperChanged += OnDesktopWallpaperChanged;
            SlideshowService.Instance.LockScreenWallpaperChanged += OnLockScreenWallpaperChanged;
            SlideshowService.Instance.ErrorsChanged += OnSlideshowErrorsChanged;
        }

        private void UnsubscribeServiceEvents()
        {
            SlideshowService.Instance.DesktopWallpaperChanged -= OnDesktopWallpaperChanged;
            SlideshowService.Instance.LockScreenWallpaperChanged -= OnLockScreenWallpaperChanged;
            SlideshowService.Instance.ErrorsChanged -= OnSlideshowErrorsChanged;
        }

        private void OnSlideshowErrorsChanged(object? sender, EventArgs e)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                UpdateStatusUI();
                UpdateCountdowns();
            });
        }

        private void SlideshowPage_Unloaded(object sender, RoutedEventArgs e)
        {
            // Stop countdown timer when page is unloaded
            _countdownTimer?.Stop();
            _countdownTimer = null;
            UnsubscribeServiceEvents();
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
            var service = SlideshowService.Instance;

            // Desktop countdown: while enabled it NEVER silently hides - the user
            // must always see WHEN the wallpaper is expected to change, or WHY
            // it is not running (the InfoBar below carries the reason).
            if (_desktopSlideshowEnabled)
            {
                DesktopCountdownText.Visibility = Visibility.Visible;
                DesktopCountdownText.Text = CountdownText(
                    service.DesktopStarting, service.DesktopRunning, service.DesktopNextChangeTime);
            }
            else
            {
                DesktopCountdownText.Visibility = Visibility.Collapsed;
            }

            // Lock screen countdown - same states, symmetric
            if (_lockScreenSlideshowEnabled)
            {
                LockScreenCountdownText.Visibility = Visibility.Visible;
                LockScreenCountdownText.Text = CountdownText(
                    service.LockScreenStarting, service.LockScreenRunning, service.LockScreenNextChangeTime);
            }
            else
            {
                LockScreenCountdownText.Visibility = Visibility.Collapsed;
            }
        }

        private string CountdownText(bool starting, bool running, DateTime nextChangeTime)
        {
            if (starting)
                return "Starting slideshow...";

            if (!running || nextChangeTime <= DateTime.MinValue)
                return "Slideshow is not running";

            var timeRemaining = nextChangeTime - DateTime.Now;
            if (timeRemaining.TotalSeconds > 0)
                return $"Next wallpaper at {nextChangeTime:HH:mm:ss} (in {FormatTimeSpan(timeRemaining)})";
            if (timeRemaining.TotalSeconds >= -60)
                return "Changing wallpaper...";
            return $"Overdue - expected at {nextChangeTime:HH:mm:ss}";
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
            SubscribeServiceEvents();
            if (_countdownTimer == null) StartCountdownTimer();
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
                // missing basis/categories = Latest = old files behave exactly as before
                if (settings.ContainsKey("DesktopSlideshowBasis"))
                {
                    _desktopBasis = settings["DesktopSlideshowBasis"].GetString() == "Category" ? "Category" : "Latest";
                }
                if (settings.ContainsKey("DesktopSlideshowCategories"))
                {
                    _desktopCategories = JsonSerializer.Deserialize<List<string>>(settings["DesktopSlideshowCategories"].GetRawText()) ?? new List<string>();
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
                // missing basis/categories = Latest = old files behave exactly as before
                if (settings.ContainsKey("LockScreenSlideshowBasis"))
                {
                    _lockScreenBasis = settings["LockScreenSlideshowBasis"].GetString() == "Category" ? "Category" : "Latest";
                }
                if (settings.ContainsKey("LockScreenSlideshowCategories"))
                {
                    _lockScreenCategories = JsonSerializer.Deserialize<List<string>>(settings["LockScreenSlideshowCategories"].GetRawText()) ?? new List<string>();
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
                // corrupt/unreadable settings file - LOUD, never a fake "No slideshow set"
                SlideshowService.Instance.ReportRestoreError($"Slideshow settings could not be read: {ex.Message}");
            }
            
            UpdateStatusUI();
        }
        
        private async Task UpdateStatusUIAsync()
        {
            var service = SlideshowService.Instance;

            // Update desktop slideshow status
            if (_desktopSlideshowEnabled && _desktopPlatforms.Count > 0 && !string.IsNullOrEmpty(_desktopCategory))
            {
                string platformsText = _desktopPlatforms.Count == 1 ? _desktopPlatforms[0] : $"{_desktopPlatforms.Count} platforms";
                string basisText = _desktopBasis == "Category"
                    ? (_desktopCategories.Count > 0 ? $"Category: {string.Join(", ", _desktopCategories)}" : "Category: (none ticked)")
                    : _desktopCategory;
                DesktopStatusText.Text = $"{platformsText} - {basisText} (Refresh: {_desktopRefreshInterval})";
            }
            else
            {
                DesktopStatusText.Text = "No slideshow set";
            }

            // Update lock screen slideshow status
            if (_lockScreenSlideshowEnabled && _lockScreenPlatforms.Count > 0 && !string.IsNullOrEmpty(_lockScreenCategory))
            {
                string platformsText = _lockScreenPlatforms.Count == 1 ? _lockScreenPlatforms[0] : $"{_lockScreenPlatforms.Count} platforms";
                string basisText = _lockScreenBasis == "Category"
                    ? (_lockScreenCategories.Count > 0 ? $"Category: {string.Join(", ", _lockScreenCategories)}" : "Category: (none ticked)")
                    : _lockScreenCategory;
                LockScreenStatusText.Text = $"{platformsText} - {basisText} (Refresh: {_lockScreenRefreshInterval})";
            }
            else
            {
                LockScreenStatusText.Text = "No slideshow set";
            }

            // Loud failure lines: a slideshow that cannot run says so here.
            // DesktopStarting/LockScreenStarting count as running: the skip
            // warning is raised BEFORE the timer is created (DesktopRunning is
            // timer-backed), so without this the bar mislabels a warning as
            // "not running" during the start window
            ApplyErrorBar(DesktopSlideshowInfoBar, service.DesktopError, service.DesktopRunning || service.DesktopStarting);
            ApplyErrorBar(LockScreenSlideshowInfoBar, service.LockScreenError, service.LockScreenRunning || service.LockScreenStarting);

            // Wait a moment for the service to update next change times
            await Task.Delay(100);
            
            // Force immediate countdown update
            UpdateCountdowns();
        }

        private static void ApplyErrorBar(InfoBar bar, string? error, bool running)
        {
            if (string.IsNullOrWhiteSpace(error))
            {
                bar.IsOpen = false;
                return;
            }
            bar.Title = running ? "Slideshow warning" : "Slideshow not running";
            bar.Message = error;
            bar.Severity = running ? InfoBarSeverity.Warning : InfoBarSeverity.Error;
            bar.IsOpen = true;
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
                    ["DesktopSlideshowBasis"] = _desktopBasis,
                    ["DesktopSlideshowCategories"] = _desktopCategories,
                    ["DesktopSlideshowInterval"] = string.IsNullOrEmpty(_desktopRefreshInterval) ? "12 hours" : _desktopRefreshInterval,
                    
                    ["LockScreenSlideshowEnabled"] = _lockScreenSlideshowEnabled,
                    ["LockScreenSlideshowPlatforms"] = _lockScreenPlatforms.Count > 0 ? _lockScreenPlatforms : new List<string> { "Backiee" },
                    ["LockScreenSlideshowCategory"] = string.IsNullOrEmpty(_lockScreenCategory) ? "Latest Wallpapers" : _lockScreenCategory,
                    ["LockScreenSlideshowBasis"] = _lockScreenBasis,
                    ["LockScreenSlideshowCategories"] = _lockScreenCategories,
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
                var service = SlideshowService.Instance;

                // Restore desktop slideshow if it was enabled - never restart one
                // that is already running or mid-start (each page visit used to
                // restart it, resetting the countdown and spamming history)
                if (_desktopSlideshowEnabled && _desktopPlatforms.Count > 0 && !string.IsNullOrEmpty(_desktopCategory)
                    && !service.DesktopRunning && !service.DesktopStarting)
                {
                    var interval = SlideshowService.ParseInterval(_desktopRefreshInterval);
                    await service.StartDesktopSlideshow(_desktopPlatforms, _desktopCategory, interval, App.MainDispatcherQueue ?? this.DispatcherQueue, _desktopBasis, _desktopCategories);
                }
                
                // Restore lock screen slideshow if it was enabled - same guard
                if (_lockScreenSlideshowEnabled && _lockScreenPlatforms.Count > 0 && !string.IsNullOrEmpty(_lockScreenCategory)
                    && !service.LockScreenRunning && !service.LockScreenStarting)
                {
                    var interval = SlideshowService.ParseInterval(_lockScreenRefreshInterval);
                    await service.StartLockScreenSlideshow(_lockScreenPlatforms, _lockScreenCategory, interval, App.MainDispatcherQueue ?? this.DispatcherQueue, _lockScreenBasis, _lockScreenCategories);
                }
            }
            catch (Exception ex)
            {
                SlideshowService.Instance.ReportRestoreError($"Slideshow restore failed: {ex.Message}");
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
            string currentBasis = slideshowType == "Desktop" ? _desktopBasis : _lockScreenBasis;
            List<string> currentCategories = slideshowType == "Desktop" ? _desktopCategories : _lockScreenCategories;

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

            // Basis selector: Latest (default) vs Category (multi-tick checklist).
            // the checklist universe = SlideshowCategoryCatalog (the same LIVE indexes
            // the Categories page uses), loaded LAZILY on first Category selection so a
            // Latest-only set never pays the network. a failed/empty index load is LOUD
            // here and makes a Category save impossible - never a silently empty list.
            string checklistState = "NotLoaded"; // NotLoaded | Loading | Ready | Failed
            string checklistError = "";
            var checklistPanel = new StackPanel { Spacing = 2 };
            var checklistStatus = new TextBlock
            {
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed
            };
            var checklistScroll = new ScrollViewer
            {
                MaxHeight = 300,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                ZoomMode = Microsoft.UI.Xaml.Controls.ZoomMode.Disabled,
                Content = checklistPanel,
                Visibility = Visibility.Collapsed
            };

            var basisLabel = new TextBlock
            {
                Text = "Slideshow basis",
                FontSize = 14,
                Margin = new Thickness(0, 8, 0, 4)
            };
            contentPanel.Children.Add(basisLabel);

            var basisLatest = new RadioButton
            {
                Content = "Latest",
                GroupName = $"Basis-{slideshowType}",
                IsChecked = currentBasis != "Category"
            };
            var basisCategory = new RadioButton
            {
                Content = "Category",
                GroupName = $"Basis-{slideshowType}",
                IsChecked = currentBasis == "Category"
            };
            contentPanel.Children.Add(basisLatest);
            contentPanel.Children.Add(basisCategory);

            var checklistHeader = new TextBlock
            {
                Text = "Pick one or more categories",
                FontSize = 13,
                Margin = new Thickness(24, 4, 0, 0)
            };
            contentPanel.Children.Add(checklistHeader);
            contentPanel.Children.Add(checklistStatus);
            contentPanel.Children.Add(checklistScroll);

            async Task EnsureChecklistAsync()
            {
                if (checklistState == "Ready" || checklistState == "Loading") return;
                checklistState = "Loading";
                checklistStatus.Text = "Loading categories...";
                checklistStatus.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray);
                checklistStatus.Visibility = Visibility.Visible;
                LogInfo("slideshow basis checklist: loading category universe...");
                try
                {
                    var universe = await SlideshowCategoryCatalog.Instance.GetUniverseAsync();
                    checklistPanel.Children.Clear();
                    foreach (var name in universe)
                    {
                        checklistPanel.Children.Add(new CheckBox
                        {
                            Content = name,
                            IsChecked = currentCategories.Any(c => c.Equals(name, StringComparison.OrdinalIgnoreCase))
                        });
                    }
                    checklistState = "Ready";
                    // the checklist only becomes visible through ApplyBasisVisibility -
                    // without this call a SUCCESSFUL load lands in a collapsed
                    // ScrollViewer (invisible to the user AND to UIA); found by the
                    // menuless probe 2026-10-10
                    ApplyBasisVisibility();
                    LogInfo($"slideshow basis checklist ready: {checklistPanel.Children.Count} names");
                }
                catch (Exception ex)
                {
                    checklistState = "Failed";
                    checklistError = ex.Message;
                    checklistStatus.Text = $"Category list failed to load: {ex.Message}";
                    checklistStatus.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.OrangeRed);
                    ApplyBasisVisibility();
                    LogInfo($"slideshow basis checklist FAILED: {ex.Message}");
                }
            }

            void ApplyBasisVisibility()
            {
                bool categoryBasis = basisCategory.IsChecked == true;
                checklistHeader.Visibility = categoryBasis ? Visibility.Visible : Visibility.Collapsed;
                checklistScroll.Visibility = categoryBasis && checklistState == "Ready" ? Visibility.Visible : Visibility.Collapsed;
                bool showStatus = categoryBasis && checklistState != "Ready";
                checklistStatus.Visibility = showStatus ? Visibility.Visible : Visibility.Collapsed;
            }

            basisCategory.Checked += (s, e) => { ApplyBasisVisibility(); _ = EnsureChecklistAsync(); };
            basisCategory.Unchecked += (s, e) => ApplyBasisVisibility();
            basisLatest.Checked += (s, e) => ApplyBasisVisibility();

            // initial visibility - the events above attach after construction, so the
            // starting state is applied by hand (and a saved Category basis kicks its
            // lazy load immediately)
            ApplyBasisVisibility();
            if (currentBasis == "Category")
            {
                _ = EnsureChecklistAsync();
            }

            // the dialog content (9 platform rows + basis + up to ~270 checklist rows)
            // must scroll - everything below the fold used to be silently cut off (the
            // Pixabay checkbox + old category dropdown were clipped)
            dialog.Content = new ScrollViewer
            {
                Content = contentPanel,
                MaxHeight = 460,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                ZoomMode = Microsoft.UI.Xaml.Controls.ZoomMode.Disabled
            };

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

                LogInfo($"Set button clicked. Selected platforms: {string.Join(", ", selectedPlatforms)}");

                // Validate at least one platform is selected
                if (selectedPlatforms.Count == 0)
                {
                    LogInfo("No platforms selected - showing error dialog");
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
                bool categoryBasis = basisCategory.IsChecked == true;
                string selectedBasis = categoryBasis ? "Category" : "Latest";
                var selectedCategories = new List<string>();
                string selectedCategory;

                if (categoryBasis)
                {
                    // loud validation: a Category basis can never save without a loaded,
                    // non-empty checklist (the error state is shown, never guessed)
                    if (checklistState == "Loading")
                    {
                        var errorDialog = new ContentDialog
                        {
                            XamlRoot = this.XamlRoot,
                            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                            Title = "Categories Still Loading",
                            Content = "The category list is still loading - wait for it to finish, then press Set again.",
                            CloseButtonText = "OK"
                        };
                        await errorDialog.ShowAsync();
                        return;
                    }
                    if (checklistState != "Ready")
                    {
                        var errorDialog = new ContentDialog
                        {
                            XamlRoot = this.XamlRoot,
                            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                            Title = "Category List Failed",
                            Content = $"The category list could not be loaded, so a Category basis cannot be set. {checklistError}",
                            CloseButtonText = "OK"
                        };
                        await errorDialog.ShowAsync();
                        return;
                    }

                    foreach (var child in checklistPanel.Children)
                    {
                        if (child is CheckBox box && box.IsChecked == true && box.Content is string name)
                        {
                            selectedCategories.Add(name);
                        }
                    }

                    if (selectedCategories.Count == 0)
                    {
                        var errorDialog = new ContentDialog
                        {
                            XamlRoot = this.XamlRoot,
                            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                            Title = "No Category Selected",
                            Content = "Tick at least one category for the slideshow, or switch to the Latest basis.",
                            CloseButtonText = "OK"
                        };
                        await errorDialog.ShowAsync();
                        return;
                    }

                    selectedCategory = string.Join(", ", selectedCategories);
                }
                else
                {
                    // canonical Latest display - the loader's mode mapping is unchanged
                    selectedCategory = "Latest Wallpapers";
                }
                
                LogInfo($"Toggle enabled: {isEnabled}, Basis: {selectedBasis}, Category: {selectedCategory}, Type: {slideshowType}");


                // Save to class fields and start/stop slideshow
                if (slideshowType == "Desktop")
                {
                    _desktopSlideshowEnabled = isEnabled;
                    _desktopPlatforms = selectedPlatforms;
                    _desktopCategory = selectedCategory;
                    _desktopBasis = selectedBasis;
                    if (categoryBasis)
                    {
                        // Latest keeps the last ticked list so switching back restores it
                        _desktopCategories = selectedCategories;
                    }
                    

                    // Start or stop slideshow
                    if (isEnabled && _desktopPlatforms.Count > 0 && !string.IsNullOrEmpty(_desktopCategory))
                    {
                        var interval = SlideshowService.ParseInterval(_desktopRefreshInterval);
                        await SlideshowService.Instance.StartDesktopSlideshow(_desktopPlatforms, _desktopCategory, interval, App.MainDispatcherQueue ?? this.DispatcherQueue, _desktopBasis, _desktopCategories);
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
                    _lockScreenBasis = selectedBasis;
                    if (categoryBasis)
                    {
                        // Latest keeps the last ticked list so switching back restores it
                        _lockScreenCategories = selectedCategories;
                    }

                    // Start or stop slideshow
                    if (isEnabled && _lockScreenPlatforms.Count > 0 && !string.IsNullOrEmpty(_lockScreenCategory))
                    {
                        var interval = SlideshowService.ParseInterval(_lockScreenRefreshInterval);
                        await SlideshowService.Instance.StartLockScreenSlideshow(_lockScreenPlatforms, _lockScreenCategory, interval, App.MainDispatcherQueue ?? this.DispatcherQueue, _lockScreenBasis, _lockScreenCategories);
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
                        await SlideshowService.Instance.StartDesktopSlideshow(_desktopPlatforms, _desktopCategory, interval, App.MainDispatcherQueue ?? this.DispatcherQueue, _desktopBasis, _desktopCategories);
                    }
                }
                else
                {
                    _lockScreenRefreshInterval = selectedInterval;
                    
                    // Restart lock screen slideshow with new interval if enabled
                    if (_lockScreenSlideshowEnabled && _lockScreenPlatforms.Count > 0 && !string.IsNullOrEmpty(_lockScreenCategory))
                    {
                        await SlideshowService.Instance.StartLockScreenSlideshow(_lockScreenPlatforms, _lockScreenCategory, interval, App.MainDispatcherQueue ?? this.DispatcherQueue, _lockScreenBasis, _lockScreenCategories);
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
