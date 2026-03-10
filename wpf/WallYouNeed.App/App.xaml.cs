using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using WallYouNeed.App.Pages;
using WallYouNeed.App.Services;
using WallYouNeed.Core.Services;
using WallYouNeed.Core.Services.Interfaces;
using Serilog;
using Serilog.Events;
using Wpf.Ui;
using Wpf.Ui.Appearance;
using WallYouNeed.Core;
using WallYouNeed.Core.Utils;

namespace WallYouNeed.App
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        private readonly IHost _host;
        private ILogger<App> _logger;
        private string _logFilePath;

        public IServiceProvider Services => _host.Services;

        public App()
        {
            // Set up logging directory
            string appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WallYouNeed");
            
            string logDir = Path.Combine(appDataPath, "Logs");
            Directory.CreateDirectory(logDir);
            
            _logFilePath = Path.Combine(logDir, $"app_{DateTime.Now:yyyyMMdd_HHmmss}.log");

            // Configure Serilog
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                .Enrich.FromLogContext()
                .WriteTo.File(_logFilePath, 
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            try
            {
                Log.Information("Starting Wall-You-Need application");
                
                _host = Host.CreateDefaultBuilder()
                    .ConfigureServices((context, services) =>
                    {
                        ConfigureServices(services);
                    })
                    .ConfigureLogging(logging =>
                    {
                        logging.ClearProviders();
                        logging.AddSerilog(dispose: true);
                    })
                    .Build();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Application failed to start");
                throw;
            }
        }

        private void ConfigureServices(IServiceCollection services)
        {
            // Create app data paths
            string appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WallYouNeed");
                
            string wallpapersPath = Path.Combine(appDataPath, "wallpapers.json");
            string collectionsPath = Path.Combine(appDataPath, "collections.json");
            
            // Register your services here
            services.AddSingleton<WallYouNeed.App.Services.ILogService, WallYouNeed.App.Services.LogService>();
            
            // Register database
            services.AddSingleton<LiteDB.LiteDatabase>(sp => {
                Directory.CreateDirectory(appDataPath);
                return new LiteDB.LiteDatabase(Path.Combine(appDataPath, "WallYouNeed.db"));
            });
            
            // Register repositories
            services.AddSingleton<WallYouNeed.Core.Repositories.IWallpaperRepository>(sp => 
                new WallYouNeed.Core.Repositories.WallpaperRepository(
                    wallpapersPath, 
                    sp.GetRequiredService<ILogger<WallYouNeed.Core.Repositories.WallpaperRepository>>()));
                    
            services.AddSingleton<WallYouNeed.Core.Repositories.ICollectionRepository>(sp => 
                new WallYouNeed.Core.Repositories.CollectionRepository(
                    collectionsPath, 
                    sp.GetRequiredService<ILogger<WallYouNeed.Core.Repositories.CollectionRepository>>()));
                    
            // Register the configuration service with proper path
            services.AddSingleton<WallYouNeed.Core.Services.IWallpaperConfigurationService>(sp => 
                new WallYouNeed.Core.Services.WallpaperConfigurationService(
                    appDataPath,
                    sp.GetRequiredService<ILogger<WallYouNeed.Core.Services.WallpaperConfigurationService>>()));
            
            // Register utilities
            services.AddSingleton<WallYouNeed.Core.Utils.WindowsWallpaperUtil>();
            
            // Configure HttpClient
            services.AddHttpClient("UnsplashApi", client => {
                client.BaseAddress = new Uri("https://api.unsplash.com/");
                client.DefaultRequestHeaders.Add("Accept-Version", "v1");
            });
            
            services.AddHttpClient("PexelsApi", client => {
                client.BaseAddress = new Uri("https://api.pexels.com/v1/");
            });
            
            // Add Backiee HTTP client
            services.AddHttpClient("Backiee", client => {
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
            });
            
            // Add default HttpClient
            services.AddHttpClient();
            
            // Add HtmlDownloader
            services.AddTransient<WallYouNeed.Core.Utils.HtmlDownloader>(sp => {
                var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
                var logger = sp.GetRequiredService<ILogger<WallYouNeed.Core.Utils.HtmlDownloader>>();
                return new WallYouNeed.Core.Utils.HtmlDownloader(httpClient, logger);
            });
            
            // Register core services
            services.AddSingleton<IWallpaperService, WallpaperService>();
            services.AddSingleton<ISettingsService, SettingsService>();
            services.AddSingleton<ICollectionService, CollectionService>();
            
            // Register UI services
            services.AddSingleton<Wpf.Ui.IThemeService, Wpf.Ui.ThemeService>();
            
            // Register pages
            services.AddTransient<MainWindow>();
            services.AddTransient<HomePage>();
            services.AddTransient<CollectionsPage>();
            services.AddTransient<SettingsPage>();
            services.AddTransient<CategoryPage>();
            services.AddTransient<LatestWallpapersPage>();
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            await _host.StartAsync();

            _logger = _host.Services.GetRequiredService<ILogger<App>>();
            _logger.LogInformation("Application starting up");

            // Register global exception handlers
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            System.Windows.Application.Current.DispatcherUnhandledException += Current_DispatcherUnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

            // Initialize theme based on saved settings
            await InitializeThemeFromSettings();

            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            mainWindow.Show();

            _logger.LogInformation("Main window shown");
            _logger.LogInformation($"Logs are being written to: {_logFilePath}");
            
            base.OnStartup(e);
        }

        /// <summary>
        /// Initialize application theme based on saved settings
        /// </summary>
        private async Task InitializeThemeFromSettings()
        {
            try
            {
                var settingsService = _host.Services.GetRequiredService<ISettingsService>();
                var themeService = _host.Services.GetRequiredService<Wpf.Ui.IThemeService>();
                
                var settings = await settingsService.LoadSettingsAsync();
                
                // Apply the theme from settings
                ApplicationTheme theme = settings.Theme == Core.Models.AppTheme.Light 
                    ? ApplicationTheme.Light 
                    : ApplicationTheme.Dark;
                
                _logger.LogInformation("Initializing application with theme: {Theme}", settings.Theme);
                themeService.SetTheme(theme);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize theme from settings");
                // Fall back to default theme (Dark)
            }
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            using (_host)
            {
                await _host.StopAsync();
            }

            Log.Information("Application shutting down");
            Log.CloseAndFlush();

            base.OnExit(e);
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = (Exception)e.ExceptionObject;
            LogUnhandledException(ex, "AppDomain.CurrentDomain.UnhandledException");
        }

        private void Current_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogUnhandledException(e.Exception, "Application.Current.DispatcherUnhandledException");
            e.Handled = true; // Mark as handled to prevent app crash
        }

        private void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            LogUnhandledException(e.Exception, "TaskScheduler.UnobservedTaskException");
            e.SetObserved(); // Mark as observed to prevent app crash
        }

        private void LogUnhandledException(Exception ex, string source)
        {
            string errorMessage = $"Unhandled exception caught from {source}: {ex.Message}";
            
            // Log to file
            Log.Error(ex, errorMessage);
            
            // Try to use the logger if available
            _logger?.LogError(ex, errorMessage);
            
            // Show message box for really critical errors
            if (source != "TaskScheduler.UnobservedTaskException") // These are usually less critical
            {
                System.Windows.MessageBox.Show(
                    $"An unexpected error occurred:\n{ex.Message}\n\nCheck log for details:\n{_logFilePath}",
                    "Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }
    }
} 