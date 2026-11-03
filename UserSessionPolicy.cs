namespace KuaiJian.WinUI;

public static class UserSessionPolicy
{
    public static bool OwnsData(string? capturedUserId, string? authenticatedUserId, string? dataUserId) =>
        !string.IsNullOrWhiteSpace(capturedUserId)
        && AuthResponsePolicy.IsSafeAccountId(capturedUserId)
        && string.Equals(capturedUserId, authenticatedUserId, StringComparison.Ordinal)
        && string.Equals(capturedUserId, dataUserId, StringComparison.Ordinal);
}
