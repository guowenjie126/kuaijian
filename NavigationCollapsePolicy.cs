namespace KuaiJian.WinUI;

public static class NavigationCollapsePolicy
{
    public static bool ShouldToggle(bool enabled, bool isCollapsed, bool isInteractiveSource, bool isWithinPane) =>
        enabled && (isCollapsed || (!isInteractiveSource && isWithinPane));
}
