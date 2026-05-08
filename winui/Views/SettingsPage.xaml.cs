using System;
using Aura.Services;
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
                PexelsApiKeyBox.Password = ApiKeySettingsService.GetStoredPexelsApiKey();
                PixabayApiKeyBox.Password = ApiKeySettingsService.GetStoredPixabayApiKey();
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

        private void SaveApiKeysButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ApiKeySettingsService.SaveApiKeys(PexelsApiKeyBox.Password, PixabayApiKeyBox.Password);
                ShowApiKeyStatus("API keys saved. Platform pages will use them the next time they load.", InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                ShowApiKeyStatus($"Could not save API keys: {ex.Message}", InfoBarSeverity.Error);
            }
        }

        private void ClearApiKeysButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ApiKeySettingsService.ClearApiKeys();
                PexelsApiKeyBox.Password = string.Empty;
                PixabayApiKeyBox.Password = string.Empty;
                ShowApiKeyStatus("Saved API keys cleared.", InfoBarSeverity.Informational);
            }
            catch (Exception ex)
            {
                ShowApiKeyStatus($"Could not clear API keys: {ex.Message}", InfoBarSeverity.Error);
            }
        }

        private void ShowApiKeyStatus(string message, InfoBarSeverity severity)
        {
            ApiKeyStatusInfoBar.Message = message;
            ApiKeyStatusInfoBar.Severity = severity;
            ApiKeyStatusInfoBar.IsOpen = true;
        }
    }
}
