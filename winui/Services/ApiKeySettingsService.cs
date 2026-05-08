using System;
using System.IO;
using System.Text.Json;

namespace Aura.Services
{
    public static class ApiKeySettingsService
    {
        private static readonly object SyncLock = new();

        public static string GetPexelsApiKey()
        {
            return GetApiKey(settings => settings.PexelsApiKey, "PEXELS_API_KEY");
        }

        public static string GetPixabayApiKey()
        {
            return GetApiKey(settings => settings.PixabayApiKey, "PIXABAY_API_KEY");
        }

        public static string GetStoredPexelsApiKey()
        {
            return LoadSettings().PexelsApiKey;
        }

        public static string GetStoredPixabayApiKey()
        {
            return LoadSettings().PixabayApiKey;
        }

        public static void SaveApiKeys(string pexelsApiKey, string pixabayApiKey)
        {
            lock (SyncLock)
            {
                var settings = LoadSettings();
                settings.PexelsApiKey = NormalizeApiKey(pexelsApiKey);
                settings.PixabayApiKey = NormalizeApiKey(pixabayApiKey);
                SaveSettings(settings);
            }
        }

        public static void ClearApiKeys()
        {
            lock (SyncLock)
            {
                if (File.Exists(LocalFileStorageService.ApiKeySettingsFilePath))
                {
                    File.Delete(LocalFileStorageService.ApiKeySettingsFilePath);
                }
            }
        }

        private static string GetApiKey(Func<ApiKeySettings, string> settingSelector, string environmentVariableName)
        {
            var storedValue = settingSelector(LoadSettings());
            if (!string.IsNullOrWhiteSpace(storedValue))
            {
                return storedValue;
            }

            return Environment.GetEnvironmentVariable(environmentVariableName) ?? string.Empty;
        }

        private static ApiKeySettings LoadSettings()
        {
            lock (SyncLock)
            {
                try
                {
                    var settingsFilePath = LocalFileStorageService.ApiKeySettingsFilePath;
                    if (!File.Exists(settingsFilePath))
                    {
                        return new ApiKeySettings();
                    }

                    var json = File.ReadAllText(settingsFilePath);
                    return JsonSerializer.Deserialize<ApiKeySettings>(json) ?? new ApiKeySettings();
                }
                catch
                {
                    return new ApiKeySettings();
                }
            }
        }

        private static void SaveSettings(ApiKeySettings settings)
        {
            Directory.CreateDirectory(LocalFileStorageService.AppDataFolderPath);

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(LocalFileStorageService.ApiKeySettingsFilePath, json);
        }

        private static string NormalizeApiKey(string value)
        {
            return value?.Trim() ?? string.Empty;
        }

        private sealed class ApiKeySettings
        {
            public string PexelsApiKey { get; set; } = string.Empty;
            public string PixabayApiKey { get; set; } = string.Empty;
        }
    }
}
