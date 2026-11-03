namespace KuaiJian.WinUI;

public static class SettingsSectionPolicy
{
    public static IReadOnlyList<string> NavigationSections { get; } = Array.AsReadOnly(
        new[] { "账号", "保存", "下载", "快捷键", "隐藏", "更新" });
}
