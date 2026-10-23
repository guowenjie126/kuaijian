using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace KuaiJian.WinUI;

/// <summary>
/// Windows-only shell integration for the unpackaged desktop build.
/// It owns a message-only window so the tray icon and RegisterHotKey do not
/// depend on WinUI's internal window procedure.
/// </summary>
public sealed class WindowsShellService : IDisposable
{
    private const int WmApp = 0x8000;
    private const int TrayCallbackMessage = WmApp + 41;
    public const uint ActivateExistingInstanceMessage = WmApp + 42;
    private const int WmHotKey = 0x0312;
    private const int WmLButtonUp = 0x0202;
    private const int WmRButtonUp = 0x0205;
    private const int NifMessage = 0x00000001;
    private const int NifIcon = 0x00000002;
    private const int NifTip = 0x00000004;
    private const int NimAdd = 0x00000000;
    private const int NimDelete = 0x00000002;
    private const int NimSetVersion = 0x00000004;
    private const int NotifyIconVersion = 4;
    private const int TpmRightButton = 0x0002;
    private const int TpmReturnCommand = 0x0100;
    private const int MenuOpen = 1001;
    private const int MenuExit = 1002;
    private const int GlobalHotkeyId = 9001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint VkV = 0x56;
    private const int SwRestore = 9;
    private const int HwndMessage = -3;
    private const uint IconId = 1;
    private const uint ClassStyles = 0;
    private const uint WindowStyles = 0;
    private const uint ExWindowStyles = 0;
    private const string ActivationEventName = "Local\\KuaiJian.WinUI.ActivateExistingInstance";

    private static readonly WndProcDelegate WindowProcDelegate = WindowProc;
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<IntPtr, WindowsShellService> Instances = new();
    private static readonly string WindowClassName = "KuaiJian.WindowsShellMessageWindow";
    private static bool _classRegistered;

    private readonly Action _showWindow;
    private readonly Action _exitApplication;
    private readonly IntPtr _messageWindow;
    private readonly IntPtr _icon;
    private readonly EventWaitHandle _activationEvent;
    private readonly CancellationTokenSource _activationStop = new();
    private readonly Task _activationTask;
    private readonly bool _ownsIcon;
    private bool _hotkeyRegistered;
    private bool _disposed;

    public WindowsShellService(IntPtr mainWindow, Action showWindow, Action exitApplication)
    {
        _showWindow = showWindow;
        _exitApplication = exitApplication;
        EnsureWindowClass();
        _messageWindow = CreateWindowEx(
            ExWindowStyles, WindowClassName, "KuaiJian shell", WindowStyles,
            0, 0, 0, 0, new IntPtr(HwndMessage), IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
        if (_messageWindow == IntPtr.Zero) throw new InvalidOperationException("无法创建 Windows shell 消息窗口。");
        lock (SyncRoot) Instances[_messageWindow] = this;

        _icon = LoadApplicationIcon(out _ownsIcon);
        AddTrayIcon();
        _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName);
        _activationTask = Task.Run(WaitForActivationAsync);
    }

    public static bool TryActivateExistingInstance()
    {
        try
        {
            using var activation = EventWaitHandle.OpenExisting(ActivationEventName);
            if (activation.Set()) return true;
        }
        catch (WaitHandleCannotBeOpenedException) { }
        catch (UnauthorizedAccessException) { }

        var messageWindow = FindWindowEx(new IntPtr(HwndMessage), IntPtr.Zero, WindowClassName, null);
        if (messageWindow == IntPtr.Zero) return false;

        return PostMessage(messageWindow, ActivateExistingInstanceMessage, IntPtr.Zero, IntPtr.Zero);
    }

    private async Task WaitForActivationAsync()
    {
        try
        {
            while (!_activationStop.IsCancellationRequested)
            {
                var wasSignaled = await Task.Run(() => WaitForActivationSignal(_activationEvent, 500), _activationStop.Token);
                if (_activationStop.IsCancellationRequested) break;
                if (wasSignaled) _showWindow();
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    internal static bool WaitForActivationSignal(WaitHandle signal, int millisecondsTimeout) => signal.WaitOne(millisecondsTimeout);

    public bool SetGlobalHotkey(bool enabled)
    {
        if (_disposed) return false;
        if (!enabled)
        {
            if (_hotkeyRegistered) UnregisterHotKey(_messageWindow, GlobalHotkeyId);
            _hotkeyRegistered = false;
            return true;
        }

        if (_hotkeyRegistered) return true;
        _hotkeyRegistered = RegisterHotKey(_messageWindow, GlobalHotkeyId, ModControl | ModShift, VkV);
        return _hotkeyRegistered;
    }

    public static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryPath, false);
            return key?.GetValue(StartupValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
        catch { return false; }
    }

    public static void SetStartupEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(StartupRegistryPath);
        if (enabled)
        {
            var executable = Environment.ProcessPath ?? throw new InvalidOperationException("无法确定当前程序路径。");
            key.SetValue(StartupValueName, $"\"{executable}\"");
        }
        else
        {
            key.DeleteValue(StartupValueName, false);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        SetGlobalHotkey(false);
        _disposed = true;
        _activationStop.Cancel();
        try { _activationEvent.Set(); } catch { }
        try { _activationTask.Wait(TimeSpan.FromSeconds(1)); } catch { }
        _activationEvent.Dispose();
        _activationStop.Dispose();
        if (_messageWindow != IntPtr.Zero)
        {
            var data = BuildNotifyData(NimDelete);
            Shell_NotifyIcon(NimDelete, ref data);
            lock (SyncRoot) Instances.Remove(_messageWindow);
            DestroyWindow(_messageWindow);
        }
        if (_ownsIcon && _icon != IntPtr.Zero) DestroyIcon(_icon);
        GC.SuppressFinalize(this);
    }

    private void AddTrayIcon()
    {
        var data = BuildNotifyData(NimAdd);
        if (!Shell_NotifyIcon(NimAdd, ref data)) return;
        data = BuildNotifyData(NimSetVersion);
        Shell_NotifyIcon(NimSetVersion, ref data);
    }

    private static IntPtr LoadApplicationIcon(out bool ownsIcon)
    {
        ownsIcon = false;
        var executable = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(executable))
        {
            var smallIcons = new IntPtr[1];
            var extracted = ExtractIconEx(executable, 0, null, smallIcons, 1);
            if (extracted > 0 && smallIcons[0] != IntPtr.Zero)
            {
                ownsIcon = true;
                return smallIcons[0];
            }
        }
        return LoadIcon(IntPtr.Zero, new IntPtr(32512)); // IDI_APPLICATION fallback.
    }

    private NOTIFYICONDATA BuildNotifyData(int operation)
    {
        return new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _messageWindow,
            uID = IconId,
            uFlags = NifMessage | NifIcon | NifTip,
            uCallbackMessage = TrayCallbackMessage,
            hIcon = _icon,
            szTip = "快剪",
            szInfo = "",
            szInfoTitle = "",
            uVersion = (uint)(operation == NimSetVersion ? NotifyIconVersion : 0)
        };
    }

    private void HandleMessage(uint windowsMessage, IntPtr wParam, IntPtr lParam)
    {
        if (windowsMessage == ActivateExistingInstanceMessage)
        {
            _showWindow();
            return;
        }

        if (windowsMessage == WmHotKey)
        {
            if (wParam.ToInt32() == GlobalHotkeyId) _showWindow();
            return;
        }
        var message = unchecked((int)lParam.ToInt64());
        if (message == WmLButtonUp)
        {
            _showWindow();
            return;
        }
        if (message == WmRButtonUp)
        {
            ShowContextMenu();
            return;
        }
    }

    private void ShowContextMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;
        AppendMenu(menu, 0, MenuOpen, "打开快剪");
        AppendMenu(menu, 0, MenuExit, "退出快剪");
        GetCursorPos(out var point);
        SetForegroundWindow(_messageWindow);
        var command = TrackPopupMenu(menu, TpmRightButton | TpmReturnCommand, point.X, point.Y, 0, _messageWindow, IntPtr.Zero);
        DestroyMenu(menu);
        if (command == MenuOpen) _showWindow();
        else if (command == MenuExit) _exitApplication();
    }

    private static void EnsureWindowClass()
    {
        lock (SyncRoot)
        {
            if (_classRegistered) return;
            var classInfo = new WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<WNDCLASSEX>(),
                style = ClassStyles,
                lpfnWndProc = WindowProcDelegate,
                hInstance = GetModuleHandle(null),
                lpszClassName = WindowClassName
            };
            RegisterClassEx(ref classInfo);
            _classRegistered = true;
        }
    }

    private static IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        lock (SyncRoot)
        {
            if (Instances.TryGetValue(hwnd, out var instance))
            {
                if (message == (uint)TrayCallbackMessage
                    || message == WmHotKey
                    || message == ActivateExistingInstanceMessage)
                    instance.HandleMessage(message, wParam, lParam);
            }
        }
        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    private const string StartupRegistryPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
    private const string StartupValueName = "KuaiJian";

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public int cbSize;
        public uint style;
        public WndProcDelegate lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX windowClass);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? className, string? windowName);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr instance, IntPtr iconName);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, IntPtr[]? largeIcons, IntPtr[]? smallIcons, uint iconCount);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int message, ref NOTIFYICONDATA data);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr menu, uint flags, int id, string text);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
