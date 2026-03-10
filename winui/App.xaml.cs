using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Aura
{
    public partial class App : Application
    {
        private Window? m_window;
        private readonly string logFile = Path.Combine(AppContext.BaseDirectory, "app.log");

#if DEBUG
        [DllImport("kernel32.dll")]
        private static extern bool AllocConsole();
#endif

        public App()
        {
#if DEBUG
            // Allocate console for debug logging
            AllocConsole();
#endif
            this.InitializeComponent();

            // Catch exceptions on UI and background threads
            UnhandledException += App_UnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

            LogInfo("Application initialized");
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            try
            {
                LogInfo("OnLaunched started");

                m_window = new MainWindow();
                m_window.Activate();

                LogInfo("Main window activated");
            }
            catch (Exception ex)
            {
                LogException("OnLaunched", ex);
                throw;
            }
        }

        private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            LogException("UI Thread", e.Exception);
            e.Handled = true;
            ShowErrorDialog(e.Exception);
        }

        private void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
                LogException("Background Thread", ex);
        }

        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            LogException("Async Task", e.Exception);
            e.SetObserved();
        }

        /// <summary>
        /// Log exceptions to file and console
        /// </summary>
        private void LogException(string source, Exception ex)
        {
            try
            {
                var text = $"[{DateTime.Now}] [ERROR] [{source}] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}\n";
                File.AppendAllText(logFile, text);

#if DEBUG
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(text);
                Console.ResetColor();
#endif
            }
            catch { }
        }

        /// <summary>
        /// Log normal runtime info to file and console
        /// </summary>
        public void LogInfo(string message)
        {
            try
            {
                var text = $"[{DateTime.Now}] [INFO] {message}\n";
                File.AppendAllText(logFile, text);

#if DEBUG
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(text);
                Console.ResetColor();
#endif
            }
            catch { }
        }

        private async void ShowErrorDialog(Exception ex)
        {
            try
            {
                var dialog = new ContentDialog
                {
                    Title = "Unexpected Error",
                    Content = $"{ex.Message}\n\n{ex.StackTrace}",
                    CloseButtonText = "OK",
                    XamlRoot = (m_window?.Content as FrameworkElement)?.XamlRoot
                };
                await dialog.ShowAsync();
            }
            catch { }
        }
    }
}
