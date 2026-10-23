namespace KuaiJian.WinUI;

public static class QaClipboardReadPolicy
{
    public static bool CanReplaceQuestion(bool hasManualEdit) => !hasManualEdit;
}
