using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Navigation;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Appearance;
using WallYouNeed.Core.Models;
using WallYouNeed.Core.Services.Interfaces;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.Linq;
using WallYouNeed.App.Services;

namespace WallYouNeed.App.Pages
{
    /// <summary>
    /// Interaction logic for SettingsPage.xaml
    /// </summary>
    public partial class SettingsPage : Page
    {
        private bool _isNotificationPanelExpanded = false;

        public SettingsPage()
        {
            InitializeComponent();
            
            // Set up event handlers
            Loaded += SettingsPage_Loaded;
        }

        private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            // Set initial values and state
            LoadNotificationSettings();
        }

        private void LoadNotificationSettings()
        {
            // Load saved notification settings
            // For now, using default values
            NewWallpapersToggle.IsChecked = true;
            DailySelectionsToggle.IsChecked = true;
            NewWallpapersToggle2.IsChecked = true;
        }

        private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Theme selection changed
        }

        private void AutoLaunchToggle_Checked(object sender, RoutedEventArgs e)
        {
            // Auto launch enabled
        }

        private void AutoLaunchToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            // Auto launch disabled
        }

        private void RemoveWidgetsButton_Click(object sender, RoutedEventArgs e)
        {
            // Widgets removed
        }

        private void NotificationExpandButton_Click(object sender, RoutedEventArgs e)
        {
            // Toggle notification settings panel visibility
            _isNotificationPanelExpanded = !_isNotificationPanelExpanded;
            
            if (_isNotificationPanelExpanded)
            {
                NotificationSettingsPanel.Visibility = Visibility.Visible;
                
                // Rotate the icon to point up (180 degrees)
                ExpandCollapseIcon.RenderTransform = new System.Windows.Media.RotateTransform(180);
            }
            else
            {
                NotificationSettingsPanel.Visibility = Visibility.Collapsed;
                
                // Return icon to normal (point down)
                ExpandCollapseIcon.RenderTransform = new System.Windows.Media.RotateTransform(0);
            }
        }

        private void QuickLikeToggle_Checked(object sender, RoutedEventArgs e)
        {
            // Quick like enabled
        }

        private void QuickLikeToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            // Quick like disabled
        }

        private void SyncToggle_Checked(object sender, RoutedEventArgs e)
        {
            // Synchronization enabled
        }

        private void SyncToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            // Synchronization disabled
        }

        private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Language changed
        }

        private void ShowHintsButton_Click(object sender, RoutedEventArgs e)
        {
            // Hints will be shown again
        }

        #region Notification Settings

        private void NewWallpapersToggle_Checked(object sender, RoutedEventArgs e)
        {
            // Enable notifications for new wallpapers
            SaveNotificationSetting("NewWallpapers", true);
        }

        private void NewWallpapersToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            // Disable notifications for new wallpapers
            SaveNotificationSetting("NewWallpapers", false);
        }

        private void DailySelectionsToggle_Checked(object sender, RoutedEventArgs e)
        {
            // Enable notifications for daily wallpaper selections
            SaveNotificationSetting("DailySelections", true);
        }

        private void DailySelectionsToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            // Disable notifications for daily wallpaper selections
            SaveNotificationSetting("DailySelections", false);
        }

        private void NewWallpapersToggle2_Checked(object sender, RoutedEventArgs e)
        {
            // Enable notifications for new wallpapers (second toggle)
            SaveNotificationSetting("NewWallpapers2", true);
        }

        private void NewWallpapersToggle2_Unchecked(object sender, RoutedEventArgs e)
        {
            // Disable notifications for new wallpapers (second toggle)
            SaveNotificationSetting("NewWallpapers2", false);
        }

        private void SaveNotificationSetting(string settingName, bool isEnabled)
        {
            // Save notification setting to user preferences
            // This is a placeholder - implement with your actual settings storage mechanism
            Debug.WriteLine($"Notification setting changed: {settingName} = {isEnabled}");
        }

        #endregion
    }
} 