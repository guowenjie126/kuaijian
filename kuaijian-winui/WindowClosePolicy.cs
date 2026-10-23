namespace KuaiJian.WinUI;

public static class WindowClosePolicy
{
    public static bool ShouldHideToTray(bool closeToTrayEnabled, bool authenticated, bool explicitExit) =>
        closeToTrayEnabled && authenticated && !explicitExit;
}
