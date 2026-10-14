using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Aura
{
    public partial class App : Application
    {
        private Window? m_window;
        private readonly string logFile = Path.Combine(AppContext.BaseDirectory, "app.log");

        // single-instance guard: the first run holds the mutex, a second launch just
        // signals it to show the window and exits
        private Mutex? _instanceMutex;
        private EventWaitHandle? _showSignal;
        private const string SingleInstanceMutexName = "Aura_SingleInstance";
        private const string ShowSignalName = "Aura_ShowWindow";
        private const string MainWindowTitle = "Aura";

        // App-level dispatcher queue for use by background services
        public static Microsoft.UI.Dispatching.DispatcherQueue? MainDispatcherQueue { get; private set; }

#if DEBUG
        // the debug console that opens beside the app. OFF for now - flip this to true
        // to bring it back. nothing is lost while it is off: LogInfo and LogException
        // always write app.log FIRST, and the Console mirror further down is only a
        // duplicate of that same text, so the file log is unaffected either way.
        private const bool ShowDebugConsole = false;

        [DllImport("kernel32.dll")]
        private static extern bool AllocConsole();
#endif

        public App()
        {
#if DEBUG
            if (ShowDebugConsole)
                AllocConsole();
#endif
            this.InitializeComponent();

            // Catch exceptions on UI and background threads
            UnhandledException += App_UnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            try
            {
                // Capture the main UI thread dispatcher queue before creating the window
                MainDispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

                // Single instance: a second launch of Aura.exe just tells the running
                // instance to show its window, then exits - never two tickers fighting
                // over the wallpaper.
                _instanceMutex = new Mutex(true, SingleInstanceMutexName, out bool createdNew);
                if (!createdNew)
                {
                    SignalRunningInstance();
                    BringRunningWindowForward();
                    Environment.Exit(0);
                    return;
                }
                _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
                ThreadPool.RegisterWaitForSingleObject(_showSignal, OnShowSignal, null, Timeout.Infinite, false);

                m_window = new MainWindow();
                m_window.Activate();

            }
            catch (Exception ex)
            {
                LogException("OnLaunched", ex);
                throw;
            }
        }

        private void OnShowSignal(object? state, bool timedOut)
        {
            MainDispatcherQueue?.TryEnqueue(() =>
            {
                if (m_window is MainWindow mainWindow) mainWindow.ShowFromTray();
            });
        }

        private void SignalRunningInstance()
        {
            try
            {
                using var signal = EventWaitHandle.OpenExisting(ShowSignalName);
                signal.Set();
                LogInfo("[SingleInstance] second launch - signaled the running instance to show its window");
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                // the first instance is still starting up (mutex exists, signal handle not
                // yet created) - it is about to show its own window anyway
                LogInfo("[SingleInstance] second launch - running instance still starting, no signal sent");
            }
            catch (Exception ex)
            {
                LogInfo($"[SingleInstance] second launch - show signal failed: {ex.Message}");
            }
        }

        private static void BringRunningWindowForward()
        {
            // the click that launched us gives THIS process foreground rights - hand them
            // to the running instance's window as soon as it is visible again (its own
            // Activate() may be denied by the foreground lock rules)
            for (int i = 0; i < 40; i++)
            {
                IntPtr hwnd = FindWindowW(null, MainWindowTitle);
                if (hwnd != IntPtr.Zero && IsWindowVisible(hwnd))
                {
                    SetForegroundWindow(hwnd);
                    if (GetForegroundWindow() == hwnd) return;
                }
                Thread.Sleep(50);
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindWindowW")]
        private static extern IntPtr FindWindowW(string? lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

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
