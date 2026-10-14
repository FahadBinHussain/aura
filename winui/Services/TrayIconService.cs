using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace Aura.Services
{
    /// <summary>
    /// System tray icon, hand-built on raw Shell_NotifyIconW (WinUI 3 ships no tray icon).
    /// A hidden top-level host window receives the shell callbacks: left click opens the
    /// window, right click shows the Open Aura / Exit menu. Every failure throws with the
    /// exact win32 reason - the caller surfaces it to the user, never a silently missing tray.
    /// </summary>
    public sealed class TrayIconService : IDisposable
    {
        public event Action? OpenRequested;
        public event Action? ExitRequested;

        private const string HostClassName = "AuraTrayHostWindow";
        private const uint CallbackMessage = 0x8001; // WM_APP+1

        private const uint NimAdd = 0x00000000;
        private const uint NimDelete = 0x00000002;
        private const uint NifMessage = 0x00000001;
        private const uint NifIcon = 0x00000002;
        private const uint NifTip = 0x00000004;

        private const uint WmNull = 0x00000000;
        private const uint WmLButtonUp = 0x0202;
        private const uint WmLButtonDblClk = 0x0203;
        private const uint WmRButtonUp = 0x0205;

        private const uint WsExToolWindow = 0x00000080;
        private const uint WsOverlapped = 0x00000000;
        private const uint ImageIcon = 1;
        private const uint LrLoadFromFile = 0x00000010;
        private const uint LrDefaultSize = 0x00000040;

        private const uint MfString = 0x00000000;
        private const uint TpmRightButton = 0x0002;
        private const uint TpmReturnCmd = 0x0100;
        private const int IdmOpen = 1001;
        private const int IdmExit = 1002;

        private const uint ErrorClassAlreadyExists = 1410;
        private const uint MbIconError = 0x00000010;
        private const uint MbIconInformation = 0x00000040;
        private const uint MbSetForeground = 0x00010000;
        private const uint SrcCopy = 0x00CC0020;
        private const int IconInset = 4;
        private const double PaintedSdFloor = 6.0;
        private const int PaintedRangeFloor = 20;
        private const string AuraRegPath = @"SOFTWARE\Aura";
        private const string VisibilityNoticeValue = "TrayIconVisibilityNotice";

        private IntPtr _hwnd;
        private IntPtr _hIcon;
        private bool _iconAdded;
        private bool _disposed;

        // keep the WndProc delegate alive for as long as the host window lives, and route
        // its messages to the one live instance (single tray per process)
        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        private static WndProcDelegate? s_hostProc;
        private static TrayIconService? s_current;

        // registered once per process; 0 = registration failed (guarded at the compare site)
        private static readonly uint TaskbarCreatedMessage = RegisterWindowMessageW("TaskbarCreated");

        public TrayIconService()
        {
            s_hostProc ??= HostWndProc;
            s_current = this;

            string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "logo.ico");
            _hIcon = LoadImageW(IntPtr.Zero, iconPath, ImageIcon, 0, 0, LrLoadFromFile | LrDefaultSize);
            if (_hIcon == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                throw new Win32Exception(err, $"tray icon failed to load from '{iconPath}' (win32 {err})");
            }

            _hwnd = CreateHostWindow();
            AddIcon();

            LogInfo($"tray icon added (Shell_NotifyIcon NIM_ADD ok, host hwnd 0x{_hwnd.ToInt64():X})");
            StartVisibilityCheck();
        }

        private IntPtr CreateHostWindow()
        {
            var wndClass = new WndClassEx
            {
                cbSize = (uint)Marshal.SizeOf<WndClassEx>(),
                lpfnWndProc = s_hostProc,
                hInstance = GetModuleHandleW(null),
                lpszClassName = HostClassName
            };

            if (RegisterClassExW(ref wndClass) == 0)
            {
                int err = Marshal.GetLastWin32Error();
                if (err != ErrorClassAlreadyExists)
                    throw new Win32Exception(err, $"RegisterClassExW('{HostClassName}') failed (win32 {err})");
            }

            // hidden top-level window (never shown, WS_EX_TOOLWINDOW keeps it out of the
            // taskbar/alt-tab) - the shell needs a real hwnd to deliver tray callbacks to
            IntPtr hwnd = CreateWindowExW(
                WsExToolWindow, HostClassName, "AuraTrayHost",
                WsOverlapped, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero,
                GetModuleHandleW(null), IntPtr.Zero);

            if (hwnd == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                throw new Win32Exception(err, $"CreateWindowExW('{HostClassName}') failed (win32 {err})");
            }
            return hwnd;
        }

        private void AddIcon()
        {
            var nid = BuildData();
            if (!Shell_NotifyIconW(NimAdd, ref nid))
            {
                int err = Marshal.GetLastWin32Error();
                throw new Win32Exception(err, $"Shell_NotifyIcon NIM_ADD failed (win32 {err}) - tray icon not shown");
            }
            _iconAdded = true;
        }

        /// <summary>
        /// explorer restarts (crash, update, manual kill) broadcast TaskbarCreated and drop
        /// every registered tray icon. re-add ours so the icon comes back without an app
        /// restart - NIM_ADD is idempotent against the fresh shell. loud on failure.
        /// </summary>
        private void ReAddIconAfterExplorerRestart()
        {
            if (_disposed) return;
            var nid = BuildData();
            if (Shell_NotifyIconW(NimAdd, ref nid))
            {
                _iconAdded = true;
                LogInfo("tray icon re-added after explorer restart");
            }
            else
            {
                int err = Marshal.GetLastWin32Error();
                LogInfo($"tray icon re-add after explorer restart failed (win32 {err})");
            }
        }

        private NotifyIconData BuildData()
        {
            return new NotifyIconData
            {
                cbSize = Marshal.SizeOf<NotifyIconData>(),
                hWnd = _hwnd,
                uID = 1,
                uFlags = NifMessage | NifIcon | NifTip,
                uCallbackMessage = CallbackMessage,
                hIcon = _hIcon,
                szTip = "Aura",
                szInfo = string.Empty,
                szInfoTitle = string.Empty
            };
        }

        private static IntPtr HostWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            var self = s_current;
            if (self != null)
            {
                // the shell delivers uCallbackMessage as msg and the actual mouse button
                // (WM_LBUTTONUP / WM_LBUTTONDBLCLK / WM_RBUTTONUP / ...) in lParam. we must
                // dispatch on lParam - comparing msg against the mouse constants never
                // matches (msg is always CallbackMessage=0x8001), so every tray click used
                // to fall through to DefWindowProc and the icon was dead: left-click did
                // nothing, right-click never opened the Exit menu.
                if (msg == CallbackMessage)
                    return self.HandleMessage((uint)(lParam.ToInt32() & 0xFFFF));

                // explorer restarts broadcast TaskbarCreated and drop every registered
                // icon; re-add ours so the tray survives without an app restart.
                if (TaskbarCreatedMessage != 0 && msg == TaskbarCreatedMessage)
                {
                    self.ReAddIconAfterExplorerRestart();
                    return IntPtr.Zero;
                }
            }
            return DefWindowProcW(hWnd, msg, wParam, lParam);
        }

        private IntPtr HandleMessage(uint msg)
        {
            switch (msg)
            {
                case WmLButtonUp:
                case WmLButtonDblClk:
                    OpenRequested?.Invoke();
                    return IntPtr.Zero;
                case WmRButtonUp:
                    ShowContextMenu();
                    return IntPtr.Zero;
                default:
                    return IntPtr.Zero;
            }
        }

        private void ShowContextMenu()
        {
            GetCursorPos(out NativePoint pt);
            IntPtr menu = CreatePopupMenu();
            if (menu == IntPtr.Zero) return;

            AppendMenuW(menu, MfString, (UIntPtr)IdmOpen, "Open Aura");
            AppendMenuW(menu, MfString, (UIntPtr)IdmExit, "Exit");

            // classic tray-menu recipe: foreground our host window so the menu dismisses
            // on the next outside click, and pump the WM_NULL the shell expects after it
            SetForegroundWindow(_hwnd);
            int cmd = (int)TrackPopupMenu(menu, TpmRightButton | TpmReturnCmd, pt.X, pt.Y, 0, _hwnd, IntPtr.Zero);
            PostMessageW(_hwnd, WmNull, IntPtr.Zero, IntPtr.Zero);
            DestroyMenu(menu);

            if (cmd == IdmOpen) OpenRequested?.Invoke();
            else if (cmd == IdmExit) ExitRequested?.Invoke();
        }

        /// <summary>
        /// Visible startup-error notice when the tray cannot start. Native MessageBox: it
        /// needs no XamlRoot, so it works even though the failure happens mid-window-init.
        /// </summary>
        public static void ShowStartupFailure(string reason)
        {
            MessageBoxW(
                IntPtr.Zero,
                "Aura's tray icon could not start:\n\n" + reason +
                "\n\nClosing the window will EXIT Aura instead of hiding it to the tray.",
                "Aura - tray icon failed",
                MbIconError);
        }

        /// <summary>
        /// Win11 26100 registers a new tray icon (NIM_ADD + GetRect succeed) but never
        /// paints it until the user promotes it in Settings, and NO API promotes it
        /// (verified live: ITrayNotify SetPreference, notification callbacks, NIF_INFO
        /// balloon, registry IsPromoted, NIM_GET's own NIS_HIDDEN flag - all no-op).
        /// The only ground truth on this build is pixels: sample our slot after a
        /// settle delay, compare against the flat taskbar next to it, log the verdict,
        /// and show a one-time-per-machine notice when Windows draws nothing.
        /// </summary>
        private void StartVisibilityCheck()
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    // explorer needs a moment after NIM_ADD to paint (if it ever paints)
                    await Task.Delay(2000).ConfigureAwait(false);
                    if (_disposed) return;
                    CheckVisibility();
                }
                catch (Exception ex)
                {
                    LogInfo($"visibility check failed: {ex.Message}");
                }
            });
        }

        private void CheckVisibility()
        {
            var id = new NotifyIconIdentifier
            {
                cbSize = Marshal.SizeOf<NotifyIconIdentifier>(),
                hWnd = _hwnd,
                uID = 1
            };
            if (Shell_NotifyIconGetRect(ref id, out NativeRect slot) != 0 || slot.Right <= slot.Left)
            {
                LogInfo("visibility check: Shell_NotifyIconGetRect failed - cannot verify whether Windows paints the tray icon");
                return;
            }

            int w = (slot.Right - slot.Left) - 2 * IconInset;
            int h = (slot.Bottom - slot.Top) - 2 * IconInset;
            if (w < 4 || h < 4)
            {
                LogInfo($"visibility check: icon slot {slot.Right - slot.Left}x{slot.Bottom - slot.Top} too small to sample");
                return;
            }
            int sx = slot.Left + IconInset, sy = slot.Top + IconInset;

            if (!TrySampleStats(sx, sy, w, h, out double slotSd, out int slotRange))
            {
                LogInfo($"visibility check: screen capture of the icon slot ({sx},{sy} {w}x{h}) failed - verdict unavailable");
                return;
            }

            // background = the flattest of two same-size taskbar patches 80/160px to the
            // LEFT of our slot (our phantom slot sits left of the painted strip; a patch
            // that lands on a foreign icon is skipped by the min). a real glyph adds far
            // more variance than taskbar texture, hence the ratio test on top of the
            // absolute floors - brightness alone proves nothing here (#E9E9E9 trap).
            double bgSd = 0;
            int bgCount = 0;
            for (int back = 80; back <= 160; back += 80)
            {
                if (slot.Left - back < 0) continue;
                if (TrySampleStats(slot.Left - back + IconInset, sy, w, h, out double bSd, out _))
                {
                    if (bgCount == 0 || bSd < bgSd) bgSd = bSd;
                    bgCount++;
                }
            }

            bool painted = slotSd >= PaintedSdFloor
                        && slotRange >= PaintedRangeFloor
                        && slotSd >= bgSd * 1.5;

            string bgText = bgCount > 0
                ? $"taskbar sd {bgSd:F2} ({bgCount} patch{(bgCount > 1 ? "es" : "")})"
                : "no taskbar patch available - absolute floors only";
            string verdict = painted ? "PAINTED" : "NOT painted";
            LogInfo($"tray icon visibility: {verdict} - slot sd {slotSd:F2}, range {slotRange} vs {bgText}");

            if (painted) return;

            LogInfo(
                "Windows registered the tray icon but draws nothing over its slot - Windows 11 hides new tray " +
                "icons until the user turns them on in Settings > Taskbar > Other system tray icons. " +
                "Close still hides to tray and relaunching Aura.exe brings the window back.");
            ShowVisibilityNoticeOnce();
        }

        /// <summary>
        /// One-time-per-machine loud notice. MessageBox (the channel proven to render
        /// on this build - NIF_INFO balloons return TRUE but draw nothing) shown at
        /// most once; the marker is written AFTER dismissal so an interrupted notice
        /// retries on the next launch instead of vanishing.
        /// </summary>
        private static void ShowVisibilityNoticeOnce()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(AuraRegPath, false))
                {
                    if (key != null && key.GetValue(VisibilityNoticeValue) is int shown && shown == 1)
                    {
                        LogInfo("tray icon visibility notice: already shown on this machine - not repeating");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                LogInfo($"tray icon visibility notice: marker read failed ({ex.Message}) - showing the notice");
            }

            MessageBoxW(
                IntPtr.Zero,
                "Windows is not showing Aura's tray icon.\n\n" +
                "The icon registered successfully, but Windows 11 hides new tray icons until you allow them, " +
                "so the icon draws nothing on the taskbar.\n\n" +
                "Aura still runs with the window closed (close = hide to tray, slideshow keeps running). " +
                "To reopen the window, launch Aura.exe again - the running instance comes forward.\n\n" +
                "To make the tray icon visible: Windows Settings > Taskbar > Other system tray icons, turn Aura on. " +
                "The tray icon's right-click > Exit becomes usable then (until then, quitting = end the Aura process).\n\n" +
                "This notice appears once per machine.",
                "Aura - tray icon hidden by Windows",
                MbIconInformation | MbSetForeground);

            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(AuraRegPath))
                {
                    key?.SetValue(VisibilityNoticeValue, 1, Microsoft.Win32.RegistryValueKind.DWord);
                }
                LogInfo("tray icon visibility notice: shown, marker written (once per machine)");
            }
            catch (Exception ex)
            {
                LogInfo($"tray icon visibility notice: marker write failed ({ex.Message}) - the notice will repeat next launch");
            }
        }

        /// <summary>
        /// Luminance stddev + range of a screen rect (GDI, no dependencies).
        /// </summary>
        private static bool TrySampleStats(int x, int y, int w, int h, out double sd, out int range)
        {
            sd = 0; range = 0;
            IntPtr screen = GetDC(IntPtr.Zero);
            if (screen == IntPtr.Zero) return false;
            IntPtr mem = CreateCompatibleDC(screen);
            IntPtr bmp = CreateCompatibleBitmap(screen, w, h);
            IntPtr old = SelectObject(mem, bmp);
            bool ok = BitBlt(mem, 0, 0, w, h, screen, x, y, SrcCopy);
            if (ok)
            {
                double sum = 0, sum2 = 0;
                int min = 255, max = 0;
                for (int j = 0; j < h; j++)
                {
                    for (int i = 0; i < w; i++)
                    {
                        uint p = GetPixel(mem, i, j);
                        double lum = 0.299 * (p & 0xFF) + 0.587 * ((p >> 8) & 0xFF) + 0.114 * ((p >> 16) & 0xFF);
                        sum += lum;
                        sum2 += lum * lum;
                        int v = (int)lum;
                        if (v < min) min = v;
                        if (v > max) max = v;
                    }
                }
                int n = w * h;
                double mean = sum / n;
                sd = Math.Sqrt(Math.Max(0, sum2 / n - mean * mean));
                range = max - min;
            }
            SelectObject(mem, old);
            DeleteObject(bmp);
            DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, screen);
            return ok;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_iconAdded)
            {
                var nid = BuildData();
                if (!Shell_NotifyIconW(NimDelete, ref nid))
                {
                    int err = Marshal.GetLastWin32Error();
                    LogInfo($"Shell_NotifyIcon NIM_DELETE failed (win32 {err}) - icon drops when the host window dies with the process");
                }
                _iconAdded = false;
            }

            if (_hIcon != IntPtr.Zero) { DestroyIcon(_hIcon); _hIcon = IntPtr.Zero; }
            if (_hwnd != IntPtr.Zero) { DestroyWindow(_hwnd); _hwnd = IntPtr.Zero; }
            if (s_current == this) s_current = null;
        }

        private static void LogInfo(string message)
        {
            try { ((App)Application.Current).LogInfo($"[TrayIconService] {message}"); }
            catch { }
        }

        #region native

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WndClassEx
        {
            public uint cbSize;
            public uint style;
            public WndProcDelegate lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string lpszMenuName;
            public string lpszClassName;
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NotifyIconData
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            // hIcon MUST live here: in NOTIFYICONDATAW it comes right after szTip and
            // before dwState. Skipping it shifts every later field and leaves the shell
            // reading a NULL icon handle - NIM_ADD still succeeds and reserves a slot,
            // so the icon is registered but paints NOTHING (verified by capture).
            public IntPtr hIcon;
            public uint dwState;
            public uint dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public uint uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public uint dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NotifyIconIdentifier
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uID;
            public Guid guidItem;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "Shell_NotifyIconW")]
        private static extern bool Shell_NotifyIconW(uint dwMessage, ref NotifyIconData lpData);

        [DllImport("shell32.dll", ExactSpelling = true, EntryPoint = "Shell_NotifyIconGetRect")]
        private static extern int Shell_NotifyIconGetRect(ref NotifyIconIdentifier identifier, out NativeRect iconLocation);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleBitmap(IntPtr hDC, int nWidth, int nHeight);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(IntPtr hDCDest, int nXDest, int nYDest, int nWidth, int nHeight,
            IntPtr hDCSrc, int nXSrc, int nYSrc, uint dwRop);

        [DllImport("gdi32.dll")]
        private static extern uint GetPixel(IntPtr hDC, int nXPos, int nYPos);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hDC);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "RegisterClassExW")]
        private static extern ushort RegisterClassExW(ref WndClassEx lpwcx);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateWindowExW")]
        private static extern IntPtr CreateWindowExW(
            uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
            int x, int y, int nWidth, int nHeight,
            IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "DefWindowProcW")]
        private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "LoadImageW")]
        private static extern IntPtr LoadImageW(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint RegisterWindowMessageW(string lpString);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll")]
        private static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out NativePoint lpPoint);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreatePopupMenu")]
        private static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "AppendMenuW")]
        private static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, string lpNewItem);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "TrackPopupMenu")]
        private static extern int TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

        [DllImport("user32.dll")]
        private static extern bool DestroyMenu(IntPtr hMenu);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true, EntryPoint = "PostMessageW")]
        private static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
        private static extern int MessageBoxW(IntPtr hWnd, string lpText, string lpCaption, uint uType);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetModuleHandleW")]
        private static extern IntPtr GetModuleHandleW(string? lpModuleName);

        #endregion
    }
}
