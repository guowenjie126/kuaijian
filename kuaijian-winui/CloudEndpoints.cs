namespace KuaiJian.WinUI;

/// <summary>
/// Single source of truth for public KuaiJian cloud endpoints.
/// </summary>
public static class CloudEndpoints
{
    public const string ApiBaseUrl = "https://api.kuaijian.jingronghao.com";
    public const string WebsiteBaseUrl = "https://kuaijian.jingronghao.com";

    public static Uri Api(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith('/'))
            throw new ArgumentException("云端路径必须以 / 开头。", nameof(path));

        return new Uri(ApiBaseUrl + path, UriKind.Absolute);
    }

    public static Uri WindowsUpdateManifest => Api("/updates/windows/latest.json");
}
