namespace KuaiJian.WinUI;

public static class AuthResponsePolicy
{
    public const int MaxAccessTokenLength = 8192;
    public const int MaxAccountIdLength = 128;

    public static bool IsInvalidRestoreSessionResponse(System.Net.HttpStatusCode statusCode, string? errorCode) =>
        statusCode == System.Net.HttpStatusCode.Unauthorized
        || errorCode is "unauthorized" or "invalid_token" or "token_expired";

    public static bool IsValidAccessToken(string? token) =>
        !string.IsNullOrWhiteSpace(token)
        && token.Length <= MaxAccessTokenLength
        && token.All(character => !char.IsControl(character) && !char.IsWhiteSpace(character));

    public static bool IsSafeAccountId(string? accountId)
    {
        if (string.IsNullOrWhiteSpace(accountId)) return false;
        var deviceName = accountId.Split('.')[0];
        var reservedDeviceName = deviceName.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || deviceName.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || deviceName.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || deviceName.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || (deviceName.Length == 4
                && (deviceName.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                    || deviceName.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
                && deviceName[3] is >= '1' and <= '9');

        if (accountId.Length > MaxAccountIdLength
            || accountId is "." or ".."
            || Path.IsPathRooted(accountId)
            || !string.Equals(Path.GetFileName(accountId), accountId, StringComparison.Ordinal)
            || accountId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || accountId.Any(char.IsControl)
            || accountId.EndsWith(' ') || accountId.EndsWith('.')
            || reservedDeviceName)
            return false;

        return !Guid.TryParse(accountId, out var parsed) || parsed != Guid.Empty;
    }
}
