namespace KuaiJian.WinUI;

public static class ClipboardUrlPolicy
{
    public static bool TryCreateWebUri(string? value, out Uri? uri)
    {
        uri = null;
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var parsed)) return false;
        if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return false;
        if (string.IsNullOrWhiteSpace(parsed.Host)) return false;

        uri = parsed;
        return true;
    }
}
