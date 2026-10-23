namespace KuaiJian.WinUI;

public static class DownloadBrowserPolicy
{
    public static string Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "chrome" => "Chrome",
        "edge" => "Edge",
        "firefox" => "Firefox",
        _ => "none"
    };

    public static IReadOnlyList<string> CookieArguments(string? browser)
    {
        var normalized = Normalize(browser);
        return normalized == "none"
            ? Array.Empty<string>()
            : new[] { "--cookies-from-browser", normalized.ToLowerInvariant() };
    }
}
