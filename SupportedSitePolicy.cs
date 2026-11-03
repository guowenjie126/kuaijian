namespace KuaiJian.WinUI;

public static class SupportedSitePolicy
{
    private static readonly string[] Domains =
    {
        "youtube.com", "youtube-nocookie.com", "youtu.be",
        "bilibili.com", "bilibili.tv", "douyin.com", "tiktok.com",
        "xiaohongshu.com", "xhslink.com", "kuaishou.com", "weibo.com", "weibo.cn",
        "ixigua.com", "youku.com", "acfun.cn", "acfun.com",
        "instagram.com", "facebook.com", "fb.watch", "vimeo.com",
        "twitter.com", "x.com", "reddit.com", "redd.it", "soundcloud.com",
        "pinterest.com", "pin.it"
    };

    public static bool IsSupported(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri) return false;
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return false;
        var host = uri.IdnHost.TrimEnd('.');
        return Domains.Any(domain =>
            string.Equals(host, domain, StringComparison.OrdinalIgnoreCase)
            || host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase));
    }
}
