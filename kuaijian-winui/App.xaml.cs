using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;

namespace KuaiJian.WinUI;

public partial class App : Application
{
    public static MainWindow? MainWindow { get; private set; }
    private static Mutex? _singleInstanceMutex;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _singleInstanceMutex = new Mutex(true, "KuaiJian.WinUI.SingleInstance", out var firstInstance);
        if (!firstInstance)
        {
            if (!WindowsShellService.TryActivateExistingInstance())
            {
                var existing = FindWindow(null, "快剪");
                if (existing != IntPtr.Zero)
                {
                    ShowWindow(existing, 9);
                    SetForegroundWindow(existing);
                }
            }
            Environment.Exit(0);
            return;
        }

        MainWindow ??= new MainWindow();
        MainWindow.AttachContent();
        MainWindow.Activate();
        MainWindow.StartRootInitialization();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
