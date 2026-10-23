namespace KuaiJian.WinUI;

public static class YtDlpResumePolicy
{
    public static bool HasResumableArtifacts(string stagingDirectory)
    {
        try
        {
            return Directory.Exists(stagingDirectory)
                && Directory.EnumerateFiles(stagingDirectory, "*", SearchOption.AllDirectories)
                    .Any(path => new FileInfo(path).Length > 0);
        }
        catch
        {
            return false;
        }
    }
}
