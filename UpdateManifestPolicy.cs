namespace KuaiJian.WinUI;

public static class UpdateManifestPolicy
{
    public const int MaximumManifestBytes = 256 * 1024;
    public const long MaximumInstallerBytes = 512L * 1024 * 1024;
    private const int MaximumUrlLength = 2048;
    private const int MaximumNotesLength = 8192;

    public static bool IsValidWindowsX64(
        string? platform, string? version, string? downloadUrl, string? sha256, string? notes)
    {
        if (!string.Equals(platform, "windows-x64", StringComparison.OrdinalIgnoreCase)) return false;
        if (string.IsNullOrWhiteSpace(version) || version.Length > 32 || !Version.TryParse(version, out _)) return false;
        if (string.IsNullOrWhiteSpace(downloadUrl) || downloadUrl.Length > MaximumUrlLength
            || !Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(Path.GetExtension(uri.AbsolutePath), ".exe", StringComparison.OrdinalIgnoreCase))
            return false;
        if (sha256 is null || sha256.Length != 64 || !sha256.All(Uri.IsHexDigit)) return false;
        return notes is null || notes.Length <= MaximumNotesLength;
    }
}
