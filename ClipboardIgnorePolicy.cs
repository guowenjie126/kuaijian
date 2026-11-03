namespace KuaiJian.WinUI;

public static class ClipboardIgnorePolicy
{
    public static bool ShouldIgnore(int expectedSequenceNumber, int currentSequenceNumber) =>
        expectedSequenceNumber != 0 && expectedSequenceNumber == currentSequenceNumber;
}
