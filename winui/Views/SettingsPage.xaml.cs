using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;

namespace Aura.Views
{
    public sealed partial class SettingsPage : Page
    {
        private const string RegistryRunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "Aura";
        private bool _isInitializing = false;

        public SettingsPage()
        {
            this.InitializeComponent();
            LoadSettings();
        }

        private void LoadSettings()
        {
            _isInitializing = true;
            try
            {
                StartWithWindowsToggle.IsOn = IsStartupEnabled();
            }
            catch { }
            finally
            {
                _isInitializing = false;
            }
        }

        private bool IsStartupEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryRunKey, writable: false);
                return key?.GetValue(AppName) != null;
            }
            catch
            {
                return false;
            }
        }

        private void SetStartupEnabled(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryRunKey, writable: true);
                if (key == null) return;

                if (enable)
                {
                    // Use the current executable path
                    string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
                                     ?? System.Environment.ProcessPath
                                     ?? string.Empty;

                    if (!string.IsNullOrEmpty(exePath))
                        key.SetValue(AppName, $"\"{exePath}\"");
                }
                else
                {
                    if (key.GetValue(AppName) != null)
                        key.DeleteValue(AppName, throwOnMissingValue: false);
                }
            }
            catch { }
        }

        private void StartWithWindowsToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            SetStartupEnabled(StartWithWindowsToggle.IsOn);
        }
    }
}
