using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Aura.Views;
using Aura.Views.Backiee;
using WinRT.Interop;
using Windows.Storage;
using Windows.Graphics;
using Windows.UI;   // Needed for Colors too (namespace collision needs explicit use)
using Microsoft.Win32;
using Windows.ApplicationModel;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Aura
{
    /// <summary>
    /// Main application window with navigation.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private AppWindow m_appWindow;

        // Add a flag to track if we're already handling a resize operation
        private bool isHandlingResize = false;
        private SizeInt32 lastAppliedSize;

        // Static reference to main window instance for navigation
        public static MainWindow Instance { get; private set; }

        // Public property to access ContentFrame for navigation
        public Frame NavigationFrame => ContentFrame;

        public MainWindow()
        {
            this.InitializeComponent();
            Instance = this;

            // Set up custom titlebar
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(CustomTitleBar);

            // Ensure the navbar selected brush is correctly initialized
            try
            {
                // Initialize the selected background brush with the desired color
                var selectedBgBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 234, 234, 234)); // #eaeaea

                // We need to access the resource dictionary through Content's Resources in WinUI 3
                if (Content is FrameworkElement element)
                {
                    // Try to add or replace the resource in the content's resource dictionary
                    if (element.Resources.TryGetValue("NavbarSelectedBgBrush", out _))
                    {
                        element.Resources["NavbarSelectedBgBrush"] = selectedBgBrush;
                    }
                    else
                    {
                        element.Resources.Add("NavbarSelectedBgBrush", selectedBgBrush);
                    }
                }
            }
            catch (Exception ex)
            {
                // Log any error but don't crash
                System.Diagnostics.Debug.WriteLine($"Failed to initialize selected brush: {ex.Message}");
            }

            // Change the window's title
            Title = "Aura";

            // Get the AppWindow for this window
            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            m_appWindow = AppWindow.GetFromWindowId(windowId);

            // Initialize the last applied size with default minimum values
            lastAppliedSize = new SizeInt32(800, 600);

            // *** Add TitleBar color customization ***
            if (AppWindowTitleBar.IsCustomizationSupported()) // Check if customization is supported
            {
                var titleBar = m_appWindow.TitleBar;
                titleBar.ExtendsContentIntoTitleBar = true; // Ensure this is true

                // Set transparent background for caption buttons
                titleBar.ButtonBackgroundColor = Colors.Transparent;
                titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

                // Register theme change handler
                if(this.Content is FrameworkElement rootElement)
                {
                    rootElement.ActualThemeChanged += RootElement_ActualThemeChanged;
                }

                // Set initial colors
                UpdateTitleBarColors(titleBar);
            }
            // *** End TitleBar color customization ***

            // Set window to maximized by default while keeping window controls
            isHandlingResize = true;
            try
            {
                // Maximize the window while preserving window controls
                if (m_appWindow.Presenter is OverlappedPresenter overlappedPresenter)
                {
                    overlappedPresenter.Maximize();
                }
            }
            finally
            {
                isHandlingResize = false;
            }

            // Register for window closing event
            m_appWindow.Closing += AppWindow_Closing;

            // Register for window resizing event
            m_appWindow.Changed += AppWindow_Changed;

            // Restore window position and size if available
            RestoreWindowPositionAndSize();

            // Navigate to the platform selection page as home
            ContentFrame.Navigate(typeof(Views.PlatformSelectionPage));
            
            // Register for frame navigation to control back button visibility
            ContentFrame.Navigated += ContentFrame_Navigated;

            // Apply the correct style to the default selected Home button
            if (HomeButton != null)
            {
                // Reset all buttons to ensure clean state
                ResetAllNavButtonStyles();

                // Apply the selected style with the correct text color to Home button
                ApplySelectedButtonStyle(HomeButton);
            }
        }

        private void ResetAllNavButtonStyles()
        {
            // Reset all navigation buttons in main nav panel
            ResetButtonsInNavPanel(NavPanel);

            // Reset footer buttons - do this by finding all buttons with MyAccount or Settings tags
            ResetSpecificButtons("MyAccount", "Settings");
        }

        private void ResetButtonsInNavPanel(StackPanel panel)
        {
            if (panel == null) return;

            foreach (var child in panel.Children)
            {
                if (child is Button button)
                {
                    try
                    {
                        ResetButtonStyle(button);
                    }
                    catch (Exception ex)
                    {
                        // Log error but continue
                        System.Diagnostics.Debug.WriteLine($"Error resetting button style: {ex.Message}");
                    }
                }
            }
        }

        private void ResetSpecificButtons(params string[] buttonTags)
        {
            // Find and reset specific buttons by their tag values
            try
            {
                if (MyAccountButton != null)
                    ResetButtonStyle(MyAccountButton);

                if (SettingsButton != null)
                    ResetButtonStyle(SettingsButton);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error resetting specific buttons: {ex.Message}");
            }
        }

        private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            // Save window position and size when closing
            SaveWindowPositionAndSize();
        }

        private void SaveWindowPositionAndSize()
        {
            try
            {
                // Check if the app is packaged
                if (IsPackaged())
                {
                    ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
                    SaveWindowSettingsToAppData(localSettings);
                }
                else
                {
                    // Use registry for unpackaged apps
                    SaveWindowSettingsToRegistry();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving window settings: {ex.Message}");
            }
        }

        private bool IsPackaged()
        {
            try
            {
                return Package.Current != null;
            }
            catch
            {
                return false;
            }
        }

        private void SaveWindowSettingsToAppData(ApplicationDataContainer localSettings)
        {

            // Get the presenter and save its state
            var presenter = m_appWindow.Presenter as OverlappedPresenter;
            if (presenter != null)
            {
                localSettings.Values["WindowState"] = (int)presenter.State; // Save state as integer

                // Only save size if the window is in Restored state
                if (presenter.State == OverlappedPresenterState.Restored)
                {
                    // Save position (always save position)
                    localSettings.Values["WindowPositionX"] = m_appWindow.Position.X;
                    localSettings.Values["WindowPositionY"] = m_appWindow.Position.Y;

                    // Save size only when restored
                    localSettings.Values["WindowWidth"] = m_appWindow.Size.Width;
                    localSettings.Values["WindowHeight"] = m_appWindow.Size.Height;
                }
                else
                {
                    // If maximized or minimized, remove potentially stale size settings
                    localSettings.Values.Remove("WindowWidth");
                    localSettings.Values.Remove("WindowHeight");
                    // Still save position, as it might be relevant when restoring from minimized
                    localSettings.Values["WindowPositionX"] = m_appWindow.Position.X;
                    localSettings.Values["WindowPositionY"] = m_appWindow.Position.Y;
                }
            }
            else
            {
                 // If presenter is not OverlappedPresenter, fallback to old behavior (or handle differently)
                 localSettings.Values["WindowPositionX"] = m_appWindow.Position.X;
                 localSettings.Values["WindowPositionY"] = m_appWindow.Position.Y;
                 localSettings.Values["WindowWidth"] = m_appWindow.Size.Width;
                 localSettings.Values["WindowHeight"] = m_appWindow.Size.Height;
                 localSettings.Values.Remove("WindowState"); // Ensure no stale state exists
            }
        }

        private void SaveWindowSettingsToRegistry()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Aura"))
                {
                    var presenter = m_appWindow.Presenter as OverlappedPresenter;
                    if (presenter != null)
                    {
                        key.SetValue("WindowState", (int)presenter.State);

                        if (presenter.State == OverlappedPresenterState.Restored)
                        {
                            key.SetValue("WindowPositionX", m_appWindow.Position.X);
                            key.SetValue("WindowPositionY", m_appWindow.Position.Y);
                            key.SetValue("WindowWidth", m_appWindow.Size.Width);
                            key.SetValue("WindowHeight", m_appWindow.Size.Height);
                        }
                        else
                        {
                            key.DeleteValue("WindowWidth", false);
                            key.DeleteValue("WindowHeight", false);
                            key.SetValue("WindowPositionX", m_appWindow.Position.X);
                            key.SetValue("WindowPositionY", m_appWindow.Position.Y);
                        }
                    }
                    else
                    {
                        key.SetValue("WindowPositionX", m_appWindow.Position.X);
                        key.SetValue("WindowPositionY", m_appWindow.Position.Y);
                        key.SetValue("WindowWidth", m_appWindow.Size.Width);
                        key.SetValue("WindowHeight", m_appWindow.Size.Height);
                        key.DeleteValue("WindowState", false);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving window settings to registry: {ex.Message}");
            }
        }

        private void RestoreWindowPositionAndSize()
        {
            try
            {
                // Check if the app is packaged
                if (IsPackaged())
                {
                    ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
                    RestoreWindowSettingsFromAppData(localSettings);
                }
                else
                {
                    // Use registry for unpackaged apps
                    RestoreWindowSettingsFromRegistry();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error restoring window settings: {ex.Message}");
                ApplyDefaultWindowSize();
            }
        }

        private void RestoreWindowSettingsFromAppData(ApplicationDataContainer localSettings)
        {
            bool settingsApplied = false;

            try
            {
                // Temporarily disable resize handling during restoration
                isHandlingResize = true;
                m_appWindow.Changed -= AppWindow_Changed; // Unsubscribe early

                var presenter = m_appWindow.Presenter as OverlappedPresenter;
                if (presenter == null)
                {
                    // If not OverlappedPresenter, maybe log or handle differently
                    // For now, fall back to default size setting below
                    System.Diagnostics.Debug.WriteLine("Presenter is not OverlappedPresenter, cannot restore state.");
                }
                else if (localSettings.Values.TryGetValue("WindowState", out object stateObj) && stateObj is int stateInt)
                {
                    var savedState = (OverlappedPresenterState)stateInt;

                    if (savedState == OverlappedPresenterState.Maximized)
                    {
                        presenter.Maximize();
                        settingsApplied = true;
                    }
                    else if (savedState == OverlappedPresenterState.Restored || savedState == OverlappedPresenterState.Minimized)
                    {
                        // Restore to 'Restored' state for both Restored and Minimized
                        if (localSettings.Values.TryGetValue("WindowPositionX", out object posXObj) && posXObj is int posX &&
                            localSettings.Values.TryGetValue("WindowPositionY", out object posYObj) && posYObj is int posY &&
                            localSettings.Values.TryGetValue("WindowWidth", out object widthObj) && widthObj is int width &&
                            localSettings.Values.TryGetValue("WindowHeight", out object heightObj) && heightObj is int height)
                        {
                            // Apply position
                            posX = Math.Max(posX, 0); // Basic validation
                            posY = Math.Max(posY, 0);
                            m_appWindow.Move(new PointInt32(posX, posY));

                            // Apply size
                            width = Math.Max(width, 800); // Enforce min size
                            height = Math.Max(height, 600);
                            lastAppliedSize = new SizeInt32(width, height);
                            m_appWindow.Resize(lastAppliedSize);

                            // Ensure the presenter is in the restored state (important if recovering from minimized)
                            if(presenter.State != OverlappedPresenterState.Restored)
                            {
                                presenter.Restore();
                            }
                            settingsApplied = true;
                        }
                    }
                }

                // Fallback if no state was saved or if Restored state failed to apply size/pos
                if (!settingsApplied)
                {
                    // Check if position and size are available (old format or Restored state with missing size)
                    if (localSettings.Values.TryGetValue("WindowPositionX", out object posXObj) && posXObj is int posX &&
                        localSettings.Values.TryGetValue("WindowPositionY", out object posYObj) && posYObj is int posY &&
                        localSettings.Values.TryGetValue("WindowWidth", out object widthObj) && widthObj is int width &&
                        localSettings.Values.TryGetValue("WindowHeight", out object heightObj) && heightObj is int height)
                    {
                         // Apply position
                        posX = Math.Max(posX, 0);
                        posY = Math.Max(posY, 0);
                        m_appWindow.Move(new PointInt32(posX, posY));

                        // Apply size
                        width = Math.Max(width, 800);
                        height = Math.Max(height, 600);
                        lastAppliedSize = new SizeInt32(width, height);
                        m_appWindow.Resize(lastAppliedSize);
                        settingsApplied = true;
                    }
                    else
                    {
                         // Ultimate fallback: Use default size if no settings could be applied
                        lastAppliedSize = new SizeInt32(1024, 768);
                        m_appWindow.Resize(lastAppliedSize);
                    }
                }
            }
            catch (Exception ex)
            {
                // Log error and apply default size as a final fallback
                System.Diagnostics.Debug.WriteLine($"Error restoring window state/size/pos: {ex.Message}");
                try
                {
                    if (!settingsApplied) // Only resize if no settings were successfully applied
                    {
                        lastAppliedSize = new SizeInt32(1024, 768);
                        m_appWindow.Resize(lastAppliedSize);
                    }
                }
                catch { /* Ignore final fallback error */ }
            }
            finally
            {
                // Always resubscribe and clear flag
                m_appWindow.Changed += AppWindow_Changed;
                isHandlingResize = false;
            }
        }

        private void RestoreWindowSettingsFromRegistry()
        {
            bool settingsApplied = false;

            try
            {
                isHandlingResize = true;
                m_appWindow.Changed -= AppWindow_Changed;

                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Aura"))
                {
                    if (key != null)
                    {
                        var presenter = m_appWindow.Presenter as OverlappedPresenter;
                        if (presenter == null)
                        {
                            System.Diagnostics.Debug.WriteLine("Presenter is not OverlappedPresenter, cannot restore state.");
                        }
                        else
                        {
                            object stateObj = key.GetValue("WindowState");
                            if (stateObj != null && int.TryParse(stateObj.ToString(), out int stateInt))
                            {
                                var savedState = (OverlappedPresenterState)stateInt;

                                if (savedState == OverlappedPresenterState.Maximized)
                                {
                                    presenter.Maximize();
                                    settingsApplied = true;
                                }
                                else if (savedState == OverlappedPresenterState.Restored || savedState == OverlappedPresenterState.Minimized)
                                {
                                    if (TryGetRegistryIntValues(key, out int posX, out int posY, out int width, out int height))
                                    {
                                        posX = Math.Max(posX, 0);
                                        posY = Math.Max(posY, 0);
                                        m_appWindow.Move(new PointInt32(posX, posY));

                                        width = Math.Max(width, 800);
                                        height = Math.Max(height, 600);
                                        lastAppliedSize = new SizeInt32(width, height);
                                        m_appWindow.Resize(lastAppliedSize);

                                        if (presenter.State != OverlappedPresenterState.Restored)
                                        {
                                            presenter.Restore();
                                        }
                                        settingsApplied = true;
                                    }
                                }
                            }
                        }

                        // Fallback if no state was saved
                        if (!settingsApplied)
                        {
                            if (TryGetRegistryIntValues(key, out int posX, out int posY, out int width, out int height))
                            {
                                posX = Math.Max(posX, 0);
                                posY = Math.Max(posY, 0);
                                m_appWindow.Move(new PointInt32(posX, posY));

                                width = Math.Max(width, 800);
                                height = Math.Max(height, 600);
                                lastAppliedSize = new SizeInt32(width, height);
                                m_appWindow.Resize(lastAppliedSize);
                                settingsApplied = true;
                            }
                        }
                    }
                }

                if (!settingsApplied)
                {
                    ApplyDefaultWindowSize();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error restoring window state from registry: {ex.Message}");
                if (!settingsApplied)
                {
                    ApplyDefaultWindowSize();
                }
            }
            finally
            {
                m_appWindow.Changed += AppWindow_Changed;
                isHandlingResize = false;
            }
        }

        private bool TryGetRegistryIntValues(RegistryKey key, out int posX, out int posY, out int width, out int height)
        {
            posX = posY = width = height = 0;

            object posXObj = key.GetValue("WindowPositionX");
            object posYObj = key.GetValue("WindowPositionY");
            object widthObj = key.GetValue("WindowWidth");
            object heightObj = key.GetValue("WindowHeight");

            return posXObj != null && int.TryParse(posXObj.ToString(), out posX) &&
                   posYObj != null && int.TryParse(posYObj.ToString(), out posY) &&
                   widthObj != null && int.TryParse(widthObj.ToString(), out width) &&
                   heightObj != null && int.TryParse(heightObj.ToString(), out height);
        }

        private void ApplyDefaultWindowSize()
        {
            try
            {
                lastAppliedSize = new SizeInt32(1024, 768);
                m_appWindow.Resize(lastAppliedSize);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error applying default window size: {ex.Message}");
            }
        }

        // Add a handler for window resizing to enforce minimum size
        private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
        {
            // Only handle size changes
            if (args.DidSizeChange && !isHandlingResize)
            {
                // Get current size
                var currentSize = m_appWindow.Size;

                // Check if resize is needed
                int newWidth = Math.Max(currentSize.Width, 800);
                int newHeight = Math.Max(currentSize.Height, 600);

                // Only resize if necessary and different from last applied size
                if ((newWidth != currentSize.Width || newHeight != currentSize.Height) &&
                    (newWidth != lastAppliedSize.Width || newHeight != lastAppliedSize.Height))
                {
                    // Set flag to prevent reentrancy and remember this size
                    isHandlingResize = true;
                    lastAppliedSize = new SizeInt32(newWidth, newHeight);

                    try
                    {
                        // Temporarily unsubscribe to prevent events while changing size
                        m_appWindow.Changed -= AppWindow_Changed;
                        m_appWindow.Resize(lastAppliedSize);
                    }
                    finally
                    {
                        // Always resubscribe and clear flag
                        m_appWindow.Changed += AppWindow_Changed;
                        isHandlingResize = false;
                    }
                }
            }
        }

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItemContainer != null)
            {
                string navItemTag = args.SelectedItemContainer.Tag.ToString();

                // Navigation logic based on the selected tag
                switch (navItemTag)
                {
                    case "Home":
                        ContentFrame.Navigate(typeof(HomePage));
                        break;
                    case "Slideshow":
                        ContentFrame.Navigate(typeof(Views.Backiee.SlideshowPage));
                        break;
                    case "Collections":
                    case "AIGenerated":
                    case "Personal":
                    case "InteractiveSlideshow":
                    case "Widgets":
                    case "UploadWallpaper":
                    case "MyAccount":
                    case "Settings":
                        // For now, we'll just navigate to Home for all options
                        // In a real app, you would navigate to different pages
                        ContentFrame.Navigate(typeof(HomePage));
                        break;
                }
            }
        }

        private void NavButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button button)
                {
                    string navItemTag = button.Tag?.ToString();
                    if (string.IsNullOrEmpty(navItemTag))
                        return;

                    // Reset all nav buttons to their default style
                    ResetAllNavButtonStyles();

                    // Apply selected style to the clicked button
                    ApplySelectedButtonStyle(button);

                    // Navigate based on the tag
                    switch (navItemTag)
                    {
                        case "Home":
                            ContentFrame.Navigate(typeof(HomePage));
                            break;
                        case "Slideshow":
                            ContentFrame.Navigate(typeof(Views.Backiee.SlideshowPage));
                            break;
                        case "Collections":
                        case "AIGenerated":
                        case "Personal":
                        case "InteractiveSlideshow":
                        case "Widgets":
                        case "UploadWallpaper":
                        case "MyAccount":
                        case "Settings":
                            // For now, navigate to Home page for all options
                            // In a real app, you would navigate to the appropriate page
                            ContentFrame.Navigate(typeof(HomePage));
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                // Log the exception but don't crash
                System.Diagnostics.Debug.WriteLine($"Navigation error: {ex.Message}");
            }
        }

        private void ResetButtonStyle(Button navButton)
        {
            // Get the stack panel inside the button
            if (navButton?.Content is not StackPanel buttonStack || buttonStack.Children.Count == 0)
                return;

            // Check if we're using the new Grid-based approach or the old Border approach
            if (buttonStack.Children[0] is Grid buttonGrid)
            {
                // Hide the background and orange indicator for inactive buttons
                foreach (var child in buttonGrid.Children)
                {
                    if (child is Border border)
                    {
                        border.Background = new SolidColorBrush(Colors.Transparent);
                    }
                    else if (child is Microsoft.UI.Xaml.Shapes.Rectangle rectangle)
                    {
                        rectangle.Visibility = Visibility.Collapsed;
                    }
                    else if (child is StackPanel contentPanel)
                    {
                        // Reset text color in the content panel
                        foreach (var contentChild in contentPanel.Children)
                        {
                            if (contentChild is TextBlock textBlock)
                            {
                                textBlock.Foreground = new SolidColorBrush(Colors.DarkGray);
                            }
                        }
                    }
                }
            }
            else if (buttonStack.Children[0] is Border buttonBorder)
            {
                // Old approach - reset border style
                bool isFooter = navButton.Tag?.ToString() == "MyAccount" || navButton.Tag?.ToString() == "Settings";

                // Apply appropriate style based on whether it's a footer button or not
                Style defaultStyle = null;
                try
                {
                    if (isFooter)
                    {
                        defaultStyle = Application.Current.Resources["FooterNavItemBorderStyle"] as Style;
                    }
                    else
                    {
                        defaultStyle = Application.Current.Resources["NavItemBorderStyle"] as Style;
                    }

                    // Apply the style if we found it
                    if (defaultStyle != null)
                    {
                        buttonBorder.Style = defaultStyle;
                    }
                }
                catch
                {
                    // If we can't get the style, just set some default properties directly
                    buttonBorder.Background = new SolidColorBrush(Colors.Transparent);
                    buttonBorder.BorderThickness = new Thickness(0);
                }

                // Reset text color
                FindAndUpdateTextBlock(buttonStack, new SolidColorBrush(Colors.DarkGray));
            }
        }

        private void ApplySelectedButtonStyle(Button button)
        {
            if (button?.Content is not StackPanel selectedStack || selectedStack.Children.Count == 0)
                return;

            // Check if we're using the new Grid-based approach or the old Border approach
            if (selectedStack.Children[0] is Grid selectedGrid)
            {
                // Set up the background and orange indicator for active buttons
                foreach (var child in selectedGrid.Children)
                {
                    if (child is Border border)
                    {
                        border.Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 234, 234, 234)); // #eaeaea
                    }
                    else if (child is Microsoft.UI.Xaml.Shapes.Rectangle rectangle)
                    {
                        rectangle.Visibility = Visibility.Visible;
                        rectangle.Fill = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 255, 87, 51)); // #FF5733
                    }
                    else if (child is StackPanel contentPanel)
                    {
                        // Set selected text color in the content panel
                        foreach (var contentChild in contentPanel.Children)
                        {
                            if (contentChild is TextBlock textBlock)
                            {
                                // Try to get the brush from resources first
                                SolidColorBrush textBrush;
                                try
                                {
                                    textBrush = Application.Current.Resources["NavbarSelectedTextBrush"] as SolidColorBrush;
                                }
                                catch
                                {
                                    // Fall back to creating the brush directly
                                    textBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 25, 25, 25)); // #191919
                                }

                                // If we couldn't get the brush from resources or caught an exception, create it directly
                                if (textBrush == null)
                                {
                                    textBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 25, 25, 25)); // #191919
                                }

                                textBlock.Foreground = textBrush;
                            }
                        }
                    }
                }
            }
            else if (selectedStack.Children[0] is Border selectedBorder)
            {
                // Old approach
                // Apply active style to border
                // Always directly set the background color to ensure consistency
                selectedBorder.Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 234, 234, 234)); // #eaeaea
                selectedBorder.BorderThickness = new Thickness(4, 0, 0, 0);
                selectedBorder.BorderBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 255, 87, 51)); // #FF5733

                // Try to get the style from different resource dictionaries
                Style activeStyle = null;

                try
                {
                    // Check if this is a footer button
                    bool isFooter = button.Tag?.ToString() == "MyAccount" || button.Tag?.ToString() == "Settings";

                    // Apply appropriate style based on whether it's a footer button or not
                    if (isFooter)
                    {
                        activeStyle = Application.Current.Resources["FooterNavItemActiveBorderStyle"] as Style;

                        // Make sure to set the height for footer items
                        selectedBorder.Height = 60;
                    }
                    else
                    {
                        activeStyle = Application.Current.Resources["NavItemActiveBorderStyle"] as Style;

                        // Make sure to set the height for regular items
                        selectedBorder.Height = 76;
                    }

                    // Apply the style if we found it
                    if (activeStyle != null)
                    {
                        selectedBorder.Style = activeStyle;

                        // Ensure the background is set to our desired color even after applying the style
                        selectedBorder.Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 234, 234, 234)); // #eaeaea
                    }
                }
                catch (Exception ex)
                {
                    // Log the error but continue
                    System.Diagnostics.Debug.WriteLine($"Failed to apply active style: {ex.Message}");
                }

                // Update selected text color
                SolidColorBrush selectedTextBrush;
                try
                {
                    // Try to get the brush from resources first
                    selectedTextBrush = Application.Current.Resources["NavbarSelectedTextBrush"] as SolidColorBrush;
                }
                catch
                {
                    // Fall back to creating the brush directly
                    selectedTextBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 25, 25, 25)); // #191919
                }

                // If we couldn't get the brush from resources or caught an exception, create it directly
                if (selectedTextBrush == null)
                {
                    selectedTextBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 25, 25, 25)); // #191919
                }

                FindAndUpdateTextBlock(selectedStack, selectedTextBrush);
            }
        }

        private void FindAndUpdateTextBlock(StackPanel stack, Brush foreground)
        {
            if (stack == null || foreground == null)
                return;

            TextBlock textBlock = null;

            // Try to find the TextBlock directly in the stack
            if (stack.Children.Count > 1 && stack.Children[1] is TextBlock tb1)
            {
                textBlock = tb1;
            }
            // Or look inside a Border if present
            else if (stack.Children.Count > 0 && stack.Children[0] is Border border &&
                     border.Child is StackPanel innerStack &&
                     innerStack.Children.Count > 1 &&
                     innerStack.Children[1] is TextBlock tb2)
            {
                textBlock = tb2;
            }

            // Update the foreground if we found a TextBlock
            if (textBlock != null)
            {
                textBlock.Foreground = foreground;
            }
        }

        // Event handler for the ContentFrame's Navigated event
        private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
        {
            // Show the back arrow button if we can go back, hide otherwise
            BackArrowButton.Visibility = ContentFrame.CanGoBack ? Visibility.Visible : Visibility.Collapsed;

            // Update the IsEnabled state (though binding should handle this, explicit update can be safer)
            BackArrowButton.IsEnabled = ContentFrame.CanGoBack;

            // Optional: Adjust margin of the AppTitlePanel based on back button visibility
            // This ensures the title stays visually centered or appropriately spaced
            AppTitlePanel.Margin = ContentFrame.CanGoBack ? new Thickness(0) : new Thickness(16, 0, 0, 0); // Add left margin only if back button is hidden
        }

        // Event handler for the Back Arrow Button click
        private void BackArrowButton_Click(object sender, RoutedEventArgs e)
        {
            if (ContentFrame.CanGoBack)
            {
                ContentFrame.GoBack();
            }
        }

        // *** Add Theme Change Handler and Color Update Method ***
        private void RootElement_ActualThemeChanged(FrameworkElement sender, object args)
        {
             if (AppWindowTitleBar.IsCustomizationSupported())
             {
                UpdateTitleBarColors(m_appWindow.TitleBar);
             }
        }

        private void UpdateTitleBarColors(AppWindowTitleBar titleBar)
        {
            if (Content is FrameworkElement rootElement)
            {
                // Check the current theme
                if (rootElement.ActualTheme == ElementTheme.Dark)
                {
                    // Dark theme colors
                    titleBar.ButtonForegroundColor = Colors.White;
                    titleBar.ButtonHoverForegroundColor = Colors.White;
                    titleBar.ButtonPressedForegroundColor = Colors.White;
                    titleBar.ButtonInactiveForegroundColor = Color.FromArgb(255, 160, 160, 160); // Lighter gray when inactive

                    // Subtle hover/press backgrounds for dark theme
                    titleBar.ButtonHoverBackgroundColor = Color.FromArgb(255, 60, 60, 60);
                    titleBar.ButtonPressedBackgroundColor = Color.FromArgb(255, 90, 90, 90);
                }
                else
                {
                    // Light theme colors
                    titleBar.ButtonForegroundColor = Colors.Black;
                    titleBar.ButtonHoverForegroundColor = Colors.Black;
                    titleBar.ButtonPressedForegroundColor = Colors.Black;
                    titleBar.ButtonInactiveForegroundColor = Color.FromArgb(255, 100, 100, 100); // Darker gray when inactive

                    // Subtle hover/press backgrounds for light theme
                    titleBar.ButtonHoverBackgroundColor = Color.FromArgb(20, 0, 0, 0); // Slightly darker transparent
                    titleBar.ButtonPressedBackgroundColor = Color.FromArgb(50, 0, 0, 0); // Darker transparent
                }
            }
        }
        // *** End Theme Change Handler ***
    }
}
