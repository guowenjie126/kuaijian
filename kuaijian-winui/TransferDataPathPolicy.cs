namespace KuaiJian.WinUI;

public static class TransferDataPathPolicy
{
    public static string SendCheckpointRoot(string appDataRoot, string userId) =>
        Path.Combine(UserDataPathPolicy.GetUserRoot(appDataRoot, userId), "transfer-send-checkpoints");

    public static string ReceiveStagingRoot(string appDataRoot, string userId) =>
        Path.Combine(UserDataPathPolicy.GetUserRoot(appDataRoot, userId), "transfer-receive-staging");
}
