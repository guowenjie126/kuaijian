using System.Net;
using System.Text.Json;

namespace KuaiJian.WinUI;

public static class CloudSyncResponsePolicy
{
    private const int MaxErrorBodyBytes = 64 * 1024;

    public static bool ShouldExpireSession(HttpStatusCode statusCode, string? errorCode) =>
        AuthResponsePolicy.IsInvalidRestoreSessionResponse(statusCode, errorCode);

    public static async Task<bool> ShouldExpireSessionAsync(
        HttpResponseMessage response, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        string? errorCode = null;
        if (response.StatusCode == HttpStatusCode.Forbidden
            && response.Content.Headers.ContentLength is not > MaxErrorBodyBytes)
        {
            try
            {
                var body = await BoundedHttpContentReader.ReadBytesAsync(
                    response.Content, MaxErrorBodyBytes, cancellationToken);
                errorCode = ReadErrorCode(body);
            }
            catch (InvalidDataException) { }
            catch (JsonException) { }
        }

        return ShouldExpireSession(response.StatusCode, errorCode);
    }

    public static string? ReadErrorCode(byte[] body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Name.Equals("error", StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == JsonValueKind.String)
                    return property.Value.GetString();
            }
        }
        catch (JsonException) { }
        return null;
    }
}
