using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Serilog;
using System.Diagnostics;

namespace WallYouNeed.App.Services
{
    public interface ILogService
    {
        void LogDebug(string message, params object[] args);
        void LogInfo(string message, params object[] args);
        void LogWarning(string message, params object[] args);
        void LogError(Exception exception, string message, params object[] args);
        void LogCritical(Exception exception, string message, params object[] args);
        
        void LogUIAction(string controlName, string action);
        void LogNavigationEvent(string fromPage, string toPage);
        void LogWallpaperEvent(string wallpaperId, string action);
        
        Task<string> ExportLogsAsync();
        void OpenLogDirectory();
    }

    public class LogService : ILogService
    {
        private readonly ILogger<LogService> _logger;
        private readonly string _logDirectory;

        public LogService(ILogger<LogService> logger)
        {
            _logger = logger;
            
            _logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WallYouNeed", "Logs");
                
            // Ensure log directory exists
            if (!Directory.Exists(_logDirectory))
            {
                Directory.CreateDirectory(_logDirectory);
            }
        }

        public void LogDebug(string message, params object[] args)
        {
            _logger.LogDebug(message, args);
        }

        public void LogInfo(string message, params object[] args)
        {
            _logger.LogInformation(message, args);
        }

        public void LogWarning(string message, params object[] args)
        {
            _logger.LogWarning(message, args);
        }

        public void LogError(Exception exception, string message, params object[] args)
        {
            _logger.LogError(exception, message, args);
        }

        public void LogCritical(Exception exception, string message, params object[] args)
        {
            _logger.LogCritical(exception, message, args);
        }

        // UI specific logging methods
        public void LogUIAction(string controlName, string action)
        {
            _logger.LogInformation("UI Action: {ControlName} - {Action}", controlName, action);
        }

        public void LogNavigationEvent(string fromPage, string toPage)
        {
            _logger.LogInformation("Navigation: {FromPage} → {ToPage}", fromPage, toPage);
        }

        public void LogWallpaperEvent(string wallpaperId, string action)
        {
            _logger.LogInformation("Wallpaper: {WallpaperId} - {Action}", wallpaperId, action);
        }

        public async Task<string> ExportLogsAsync()
        {
            try
            {
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string exportPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    $"WallYouNeed_Logs_{timestamp}.zip");

                // Create a temporary directory to collect all logs
                string tempDir = Path.Combine(Path.GetTempPath(), $"WYN_LogExport_{timestamp}");
                Directory.CreateDirectory(tempDir);

                // Copy all log files to temp directory
                foreach (var logFile in Directory.GetFiles(_logDirectory, "*.log"))
                {
                    File.Copy(logFile, Path.Combine(tempDir, Path.GetFileName(logFile)));
                }

                // Create a summary file with system information
                await CreateSystemInfoFileAsync(tempDir);

                // Create zip file
                System.IO.Compression.ZipFile.CreateFromDirectory(tempDir, exportPath);

                // Clean up temp directory
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch
                {
                    // Ignore cleanup errors
                }

                return exportPath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to export logs");
                throw;
            }
        }

        private async Task CreateSystemInfoFileAsync(string directory)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== SYSTEM INFORMATION ===");
            sb.AppendLine($"Timestamp: {DateTime.Now}");
            sb.AppendLine($"OS: {Environment.OSVersion}");
            sb.AppendLine($".NET Version: {Environment.Version}");
            sb.AppendLine($"64-bit OS: {Environment.Is64BitOperatingSystem}");
            sb.AppendLine($"Machine Name: {Environment.MachineName}");
            sb.AppendLine($"Processor Count: {Environment.ProcessorCount}");
            sb.AppendLine($"System Directory: {Environment.SystemDirectory}");
            sb.AppendLine($"User Domain Name: {Environment.UserDomainName}");
            sb.AppendLine($"User Name: {Environment.UserName}");
            sb.AppendLine($"Working Set: {Environment.WorkingSet}");
            sb.AppendLine();
            sb.AppendLine("=== LOADED ASSEMBLIES ===");
            
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                sb.AppendLine($"{assembly.FullName}");
            }

            string infoPath = Path.Combine(directory, "system_info.txt");
            await File.WriteAllTextAsync(infoPath, sb.ToString());
        }

        public void OpenLogDirectory()
        {
            try
            {
                if (Directory.Exists(_logDirectory))
                {
                    Process.Start("explorer.exe", _logDirectory);
                }
                else
                {
                    System.Windows.MessageBox.Show("Log directory not found.", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open log directory");
                System.Windows.MessageBox.Show($"Failed to open log directory: {ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
    }
} 