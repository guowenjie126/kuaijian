namespace KuaiJian.WinUI;

public static class DownloadCompletionPolicy
{
    public const string None = "none";
    public const string RevealInFolder = "revealInFolder";

    public static string Normalize(string? value) =>
        string.Equals(value, RevealInFolder, StringComparison.OrdinalIgnoreCase)
            ? RevealInFolder
            : None;

    public static bool ShouldRevealInFolder(string? value) => Normalize(value) == RevealInFolder;
}
