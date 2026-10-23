using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;
using WinRT.Interop;

namespace KuaiJian.WinUI;

public sealed partial class MainWindow : Window
{
    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr HwndNotTopmost = new(-2);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoZOrder = 0x0004;
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;
    private const uint MonitorDefaultToNearest = 2;
    private const int SwRestore = 9;
    private const int SwHide = 0;
    private RECT _restoredBounds;
    private bool _hasRestoredBounds;
    private bool _isCollapsed;
    private bool _isLoginLayout = true;
    private bool _explicitExitRequested;
    private WindowStateSnapshot? _lastMainWindowState;

    public IntPtr Hwnd { get; }
    public new AppWindow AppWindow { get; }
    public bool IsCollapsed => _isCollapsed;
    public WindowsShellService Shell { get; }
    private RootPage RootPage { get; }

    public MainWindow()
    {
        InitializeComponent();
        Hwnd = WindowNative.GetWindowHandle(this);
        AppWindow = GetAppWindow(Hwnd);
        AppWindow.Changed += OnAppWindowChanged;
        ApplySavedWindowState();
        AppWindow.Title = "快剪";
        RootPage = new RootPage(this);
        Shell = new WindowsShellService(Hwnd, ShowFromShell, ExitFromShell);
        AppWindow.Closing += OnAppWindowClosing;
        Closed += (_, _) =>
        {
            RootPage.Shutdown();
            SaveWindowState();
            Shell.Dispose();
        };
    }

    public void AttachContent()
    {
        RootHost.Children.Clear();
        RootHost.Children.Add(RootPage);
    }

    public void StartRootInitialization() => DispatcherQueue.TryEnqueue(RootPage.StartInitialization);

    public bool ConfigureShell(bool startupEnabled, bool globalHotkeyEnabled)
    {
        try { WindowsShellService.SetStartupEnabled(startupEnabled); } catch { }
        return Shell.SetGlobalHotkey(globalHotkeyEnabled);
    }

    public bool SetGlobalHotkey(bool enabled) => Shell.SetGlobalHotkey(enabled);

    private void ShowFromShell()
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(ShowFromShell);
            return;
        }

        if (_isCollapsed)
        {
            RestoreWindow();
            RootPage.RestoreNavigationAfterWindowExpand();
        }
        ShowWindow(Hwnd, SwRestore);
        Activate();
        SetForegroundWindow(Hwnd);
    }

    private void ExitFromShell()
    {
        _explicitExitRequested = true;
        Close();
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!WindowClosePolicy.ShouldHideToTray(
                RootPage.CloseToTrayEnabled, RootPage.Auth.CurrentUser is not null, _explicitExitRequested))
            return;

        args.Cancel = true;
        ShowWindow(Hwnd, SwHide);
    }

    public void SetLoginLayout(bool login)
    {
        if (_isCollapsed) return;
        if (login)
        {
            if (!_isLoginLayout) CaptureMainWindowState();
            // Set the layout mode before resizing so AppWindow.Changed applies
            // the login minimum (420x700), not the previous main-window minimum.
            _isLoginLayout = true;
            AppWindow.Resize(new Windows.Graphics.SizeInt32(420, 700));
        }
        else
        {
            var state = _lastMainWindowState;
            // Switch the mode before Resize; WinUI may raise Changed during the
            // resize and must enforce the main-window minimum (415x620).
            _isLoginLayout = false;
            AppWindow.Resize(new Windows.Graphics.SizeInt32(
                state?.Width is >= 415 and <= 3000 ? state.Width : 900,
                state?.Height is >= 620 and <= 2200 ? state.Height : 650));
            if (state is not null)
                AppWindow.Move(new Windows.Graphics.PointInt32(state.X, state.Y));
        }
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidSizeChange || _isCollapsed) return;
        var minimumWidth = _isLoginLayout ? 420 : 415;
        var minimumHeight = _isLoginLayout ? 700 : 620;
        var size = sender.Size;
        if (size.Width >= minimumWidth && size.Height >= minimumHeight) return;
        sender.Resize(new Windows.Graphics.SizeInt32(
            Math.Max(size.Width, minimumWidth),
            Math.Max(size.Height, minimumHeight)));
    }

    public void ApplyAlwaysOnTop(bool enabled)
    {
        SetWindowPos(Hwnd, enabled ? HwndTopmost : HwndNotTopmost,
            0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    public void ToggleCollapsed()
    {
        if (_isCollapsed) RestoreWindow();
        else CollapseWindow();
    }

    private void CollapseWindow()
    {
        if (_isCollapsed) return;
        if (!GetWindowRect(Hwnd, out _restoredBounds)) return;
        _hasRestoredBounds = true;
        var width = 84;
        var height = 368;
        var workArea = GetCurrentMonitorWorkArea();
        var x = workArea.Right - width - 12;
        var y = workArea.Top + Math.Max(0, (workArea.Bottom - workArea.Top - height) / 2);
        SetWindowPos(Hwnd, IntPtr.Zero, x, y, width, height, SwpNoActivate | SwpNoZOrder);
        _isCollapsed = true;
    }

    private void RestoreWindow()
    {
        if (!_isCollapsed || !_hasRestoredBounds) return;
        var bounds = _restoredBounds;
        SetWindowPos(Hwnd, IntPtr.Zero, bounds.Left, bounds.Top,
            bounds.Right - bounds.Left, bounds.Bottom - bounds.Top, SwpNoActivate | SwpNoZOrder);
        _isCollapsed = false;
    }

    private void ApplySavedWindowState()
    {
        var saved = WindowStateStore.Load();
        if (saved is not null)
        {
            // Migrate the pre-layout-state file format, where the 420x700 login window
            // was indistinguishable from a saved main window.
            var looksLikeLegacyLogin = saved.IsLoginLayout is null
                && saved.Width is >= 420 and <= 430
                && saved.Height is >= 680 and <= 760;
            if (saved.IsLoginLayout == false)
                _lastMainWindowState = saved;
            else if (saved.IsLoginLayout is null)
            {
                saved.IsLoginLayout = looksLikeLegacyLogin;
                if (!looksLikeLegacyLogin) _lastMainWindowState = saved;
            }
        }
        var isLoginLayout = saved is null || saved.IsLoginLayout == true;
        var minimumWidth = isLoginLayout ? 420 : 415;
        var minimumHeight = isLoginLayout ? 700 : 620;
        var defaultWidth = isLoginLayout ? 420 : 900;
        var defaultHeight = isLoginLayout ? 700 : 650;
        var width = saved is not null && saved.Width >= minimumWidth && saved.Width <= 3000 ? saved.Width : defaultWidth;
        var height = saved is not null && saved.Height >= minimumHeight && saved.Height <= 2200 ? saved.Height : defaultHeight;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(width, height));
        if (saved is null) return;

        var virtualLeft = GetSystemMetrics(SmXVirtualScreen);
        var virtualTop = GetSystemMetrics(SmYVirtualScreen);
        var virtualWidth = Math.Max(GetSystemMetrics(SmCxVirtualScreen), width);
        var virtualHeight = Math.Max(GetSystemMetrics(SmCyVirtualScreen), height);
        var x = Math.Clamp(saved.X, virtualLeft, virtualLeft + virtualWidth - width);
        var y = Math.Clamp(saved.Y, virtualTop, virtualTop + virtualHeight - height);
        AppWindow.Move(new Windows.Graphics.PointInt32(x, y));
    }

    private void SaveWindowState()
    {
        if (_isLoginLayout && _lastMainWindowState is not null)
        {
            WindowStateStore.Save(new WindowStateSnapshot
            {
                X = _lastMainWindowState.X,
                Y = _lastMainWindowState.Y,
                Width = _lastMainWindowState.Width,
                Height = _lastMainWindowState.Height,
                IsLoginLayout = false
            });
            return;
        }

        if (_isCollapsed && _hasRestoredBounds)
        {
            var restored = _restoredBounds;
            WindowStateStore.Save(new WindowStateSnapshot
            {
                X = restored.Left,
                Y = restored.Top,
                Width = Math.Max(415, restored.Right - restored.Left),
                Height = Math.Max(620, restored.Bottom - restored.Top),
                IsLoginLayout = false
            });
            return;
        }

        if (!GetWindowRect(Hwnd, out var bounds)) return;
        WindowStateStore.Save(new WindowStateSnapshot
        {
            X = bounds.Left,
            Y = bounds.Top,
            Width = Math.Max(_isLoginLayout ? 420 : 415, bounds.Right - bounds.Left),
            Height = Math.Max(_isLoginLayout ? 700 : 620, bounds.Bottom - bounds.Top),
            IsLoginLayout = _isLoginLayout
        });
    }

    private void CaptureMainWindowState()
    {
        if (_isCollapsed || !GetWindowRect(Hwnd, out var bounds)) return;
        _lastMainWindowState = new WindowStateSnapshot
        {
            X = bounds.Left,
            Y = bounds.Top,
            Width = Math.Max(415, bounds.Right - bounds.Left),
            Height = Math.Max(620, bounds.Bottom - bounds.Top),
            IsLoginLayout = false
        };
    }

    private static AppWindow GetAppWindow(IntPtr hwnd)
    {
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        return AppWindow.GetFromWindowId(windowId);
    }

    private RECT GetCurrentMonitorWorkArea()
    {
        var monitor = MonitorFromWindow(Hwnd, MonitorDefaultToNearest);
        if (monitor != IntPtr.Zero)
        {
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(monitor, ref info)) return info.rcWork;
        }

        var left = GetSystemMetrics(SmXVirtualScreen);
        var top = GetSystemMetrics(SmYVirtualScreen);
        return new RECT
        {
            Left = left,
            Top = top,
            Right = left + Math.Max(1, GetSystemMetrics(SmCxVirtualScreen)),
            Bottom = top + Math.Max(1, GetSystemMetrics(SmCyVirtualScreen))
        };
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }
}
