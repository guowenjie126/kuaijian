namespace KuaiJian.WinUI;

public static class ClipboardCapturePolicy
{
    public static bool ShouldCaptureImage(bool saveImages) => saveImages;

    public static bool IsSnapshotCurrent(uint snapshotSequenceNumber, uint currentSequenceNumber) =>
        snapshotSequenceNumber == currentSequenceNumber;

    public static bool ShouldCaptureText(bool isUrl, bool saveText, bool saveUrls) =>
        isUrl ? saveUrls : saveText;

    public static bool ShouldCaptureFilePaths(bool saveText) => saveText;
}
