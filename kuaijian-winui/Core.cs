using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace KuaiJian.WinUI;

public static class AppInfo
{
    public const string CurrentVersion = "0.4.0";
    public const string WindowsUpdateManifest = "https://api.kuaijian.jingronghao.com/updates/latest.json";
}

public enum ClipKind
{
    Text,
    Image,
    Url
}

public sealed class AccountUser
{
    public string Id { get; set; } = Guid.Empty.ToString();
    public string Phone { get; set; } = "";
    public string? Email { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ClipboardItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public ClipKind Kind { get; set; }
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public string? ImagePath { get; set; }
    public string? Html { get; set; }
    public string? RtfPath { get; set; }
    public List<string> FileUrls { get; set; } = new();
    public string Source { get; set; } = "来源未知";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public bool Favorite { get; set; }
    public string Note { get; set; } = "";
    public bool Pinned { get; set; }
}

public enum DownloadState
{
    Queued,
    Running,
    Finished,
    Failed,
    Cancelled
}

public sealed class DownloadTaskItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SourceUrl { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DownloadState State { get; set; } = DownloadState.Queued;
    public double Progress { get; set; }
    public string Message { get; set; } = "正在准备…";
    public string? OutputPath { get; set; }
    public long ResumeBytes { get; set; }
    public long? ResumeTotalBytes { get; set; }
    public string? ResumeFileName { get; set; }
    public string? ResumeETag { get; set; }
    public DateTimeOffset? ResumeLastModified { get; set; }
}

public static class AppPaths
{
    public static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KuaiJian");

    public static string UserRoot(string userId)
    {
        return UserDataPathPolicy.GetUserRoot(Root, userId);
    }

    public static string ClipboardFile(string userId) => Path.Combine(UserRoot(userId), "clipboard-items.json");

    public static string DeletedClipboardIdsFile(string userId) => Path.Combine(UserRoot(userId), "deleted-clipboard-ids.json");

    public static string QaFile(string userId) => Path.Combine(UserRoot(userId), "qa-entries.json");

    public static string CalculatorFile(string userId) => Path.Combine(UserRoot(userId), "calculator-history.json");

    public static string DownloadsFile(string userId) => Path.Combine(UserRoot(userId), "download-tasks.json");

    public static string DownloadPartialFile(string userId, string taskId)
    {
        if (!Guid.TryParse(taskId, out var id)) throw new InvalidDataException("下载任务标识格式无效。");
        return Path.Combine(UserRoot(userId), "download-partials", id.ToString("N") + ".part");
    }

    public static string YtDlpStagingDirectory(string userId, string taskId)
    {
        if (!Guid.TryParse(taskId, out var id)) throw new InvalidDataException("下载任务标识格式无效。");
        return Path.Combine(UserRoot(userId), "download-partials", id.ToString("N") + "-yt-dlp");
    }

    public static string Images(string userId) => Path.Combine(UserRoot(userId), "images");

    public static string RichData(string userId) => Path.Combine(UserRoot(userId), "rich-data");

    public static string TransferSendCheckpoints(string userId) =>
        TransferDataPathPolicy.SendCheckpointRoot(Root, userId);

    public static string TransferReceiveStaging(string userId) =>
        TransferDataPathPolicy.ReceiveStagingRoot(Root, userId);

    public static string WindowStateFile => Path.Combine(Root, "window-state.json");
}

public sealed class WindowStateSnapshot
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    // Nullable keeps old state files (which predate this field) distinguishable
    // from current files that explicitly saved the main-window layout.
    public bool? IsLoginLayout { get; set; }
}

public static class WindowStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static WindowStateSnapshot? Load()
    {
        try
        {
            if (!File.Exists(AppPaths.WindowStateFile)) return null;
            return JsonSerializer.Deserialize<WindowStateSnapshot>(File.ReadAllText(AppPaths.WindowStateFile), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static void Save(WindowStateSnapshot snapshot)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            var temp = AppPaths.WindowStateFile + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, AppPaths.WindowStateFile, true);
        }
        catch
        {
            // Window state is best-effort and must never prevent application shutdown.
        }
    }
}

public sealed class LocalDataStore
{
    private readonly DeletedClipboardIdStore _deletedClipboardIdStore = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string? UserId { get; private set; }
    public List<string> LoadWarnings { get; } = new();

    public void SetUser(string? userId)
    {
        if (!string.IsNullOrWhiteSpace(userId) && !AuthResponsePolicy.IsSafeAccountId(userId))
            throw new InvalidDataException("账号标识格式无效。");
        if (!string.Equals(UserId, userId, StringComparison.Ordinal)) LoadWarnings.Clear();
        UserId = userId;
        if (!string.IsNullOrWhiteSpace(userId))
        {
            Directory.CreateDirectory(AppPaths.UserRoot(userId));
            Directory.CreateDirectory(AppPaths.Images(userId));
            Directory.CreateDirectory(AppPaths.RichData(userId));
        }
    }

    public async Task<List<ClipboardItem>> LoadClipboardAsync(int maxItems = ClipboardRetentionPolicy.DefaultLimit)
    {
        if (UserId is null) return new();
        var items = await LoadUserDataAsync<List<ClipboardItem>>(AppPaths.ClipboardFile(UserId)) ?? new();
        foreach (var item in items.OfType<ClipboardItem>())
        {
            item.Id = string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString() : item.Id;
            item.Title ??= "";
            item.Text ??= "";
            item.Source ??= "来源未知";
            item.Note ??= "";
            item.FileUrls ??= new();
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var uniqueItems = new List<ClipboardItem>();
        foreach (var item in items.OfType<ClipboardItem>().Where(item =>
                     !string.IsNullOrWhiteSpace(item.Text)
                     || !string.IsNullOrWhiteSpace(item.Html)
                     || !string.IsNullOrWhiteSpace(item.RtfPath)
                     || !string.IsNullOrWhiteSpace(item.ImagePath)
                     || item.FileUrls.Count > 0))
        {
            var key = item.Kind != ClipKind.Image && !string.IsNullOrWhiteSpace(item.RtfPath)
                ? await ClipboardContentIdentity.CreateKeyAsync(false, item.Kind.ToString(),
                    item.Text, item.Html, item.ImagePath, item.RtfPath, item.FileUrls)
                : ClipboardKey(item);
            if (seen.Add(key)) uniqueItems.Add(item);
        }
        return ClipboardRetentionPolicy.Retain(
            uniqueItems,
            maxItems, item => item.Pinned, item => item.CreatedAt);
    }

    public async Task SaveClipboardAsync(IReadOnlyCollection<ClipboardItem> items)
    {
        if (UserId is null) return;
        await SaveAsync(AppPaths.ClipboardFile(UserId), items.ToList());
    }

    public async Task<List<string>> LoadDeletedClipboardIdsAsync()
    {
        if (UserId is null) return new();
        return await _deletedClipboardIdStore.LoadAsync(
            AppPaths.DeletedClipboardIdsFile(UserId), LoadWarnings.Add);
    }

    public async Task SaveDeletedClipboardIdsAsync(IReadOnlyCollection<string> ids)
    {
        if (UserId is null) return;
        await _deletedClipboardIdStore.SaveAsync(AppPaths.DeletedClipboardIdsFile(UserId), ids);
    }

    public async Task<List<QaEntry>> LoadQaAsync()
    {
        if (UserId is null) return new();
        var entries = (await LoadUserDataAsync<List<QaEntry>>(AppPaths.QaFile(UserId)) ?? new())
            .OfType<QaEntry>()
            .ToList();
        foreach (var entry in entries)
        {
            entry.Id = string.IsNullOrWhiteSpace(entry.Id) ? Guid.NewGuid().ToString() : entry.Id;
            entry.Question ??= "";
            entry.Answer ??= "";
            entry.Aliases ??= "";
            entry.Keywords ??= "";
            entry.Category ??= "";
            entry.Note ??= "";
        }
        return entries;
    }

    public async Task SaveQaAsync(IReadOnlyCollection<QaEntry> entries)
    {
        if (UserId is null) return;
        await SaveAsync(AppPaths.QaFile(UserId), entries.ToList());
    }

    public async Task<List<string>> LoadCalculatorHistoryAsync()
    {
        if (UserId is null) return new();
        var history = await LoadUserDataAsync<List<string>>(AppPaths.CalculatorFile(UserId)) ?? new();
        return history
            .OfType<string>()
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Take(30)
            .ToList();
    }

    public async Task SaveCalculatorHistoryAsync(IReadOnlyCollection<string> history)
    {
        if (UserId is null) return;
        await SaveAsync(AppPaths.CalculatorFile(UserId), history.ToList());
    }

    public async Task<List<DownloadTaskItem>> LoadDownloadsAsync()
    {
        if (UserId is null) return new();
        var tasks = (await LoadUserDataAsync<List<DownloadTaskItem>>(AppPaths.DownloadsFile(UserId)) ?? new())
            .OfType<DownloadTaskItem>()
            .ToList();
        var changed = false;
        foreach (var task in tasks)
        {
            if (!Guid.TryParse(task.Id, out var taskId))
            {
                task.Id = Guid.NewGuid().ToString();
                changed = true;
            }
            else
            {
                task.Id = taskId.ToString();
            }
            task.SourceUrl ??= "";
            task.Title ??= "";
            task.Message ??= "";
        }
        foreach (var task in tasks.Where(x => x.State is DownloadState.Queued or DownloadState.Running))
        {
            task.State = DownloadState.Cancelled;
            task.Message = "应用关闭，下载已取消";
            changed = true;
        }
        if (changed) await SaveAsync(AppPaths.DownloadsFile(UserId), tasks);
        return tasks;
    }

    public async Task SaveDownloadsAsync(IReadOnlyCollection<DownloadTaskItem> tasks)
    {
        if (UserId is null) return;
        await SaveAsync(AppPaths.DownloadsFile(UserId), tasks.ToList());
    }

    public async Task SaveDownloadsAsync(string userId, IReadOnlyCollection<DownloadTaskItem> tasks)
    {
        if (!AuthResponsePolicy.IsSafeAccountId(userId))
            throw new InvalidDataException("账号标识格式无效，无法保存下载记录。");
        await SaveAsync(AppPaths.DownloadsFile(userId), tasks.ToList());
    }

    public static async Task<T?> LoadAsync<T>(string path)
    {
        try
        {
            if (!File.Exists(path)) return default;
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions);
        }
        catch
        {
            return default;
        }
    }

    private async Task<T?> LoadUserDataAsync<T>(string path)
    {
        return await UserDataJsonStore.LoadPreservingCorruptAsync<T>(path, JsonOptions, LoadWarnings.Add);
    }

    public static async Task SaveAsync<T>(string path, T value)
    {
        await UserDataJsonStore.SaveAtomicallyAsync(path, value, JsonOptions);
    }

    private static string ClipboardKey(ClipboardItem item)
    {
        var files = string.Join("\u001F", item.FileUrls.OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
        return string.Join("\u001E", item.Kind, item.Text, item.Html, item.ImagePath, files);
    }

}

public sealed class AuthService
{
    private const string ApiBaseUrl = "https://api.kuaijian.jingronghao.com";
    private const string VaultResource = "com.kuaijian.winui.auth";
    private const string VaultUser = "access-token";
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public AccountUser? CurrentUser { get; private set; }
    public bool IsRestoring { get; private set; } = true;
    public bool IsBusy { get; private set; }
    public string Message { get; private set; } = "";
    public int SmsCooldownRemaining { get; private set; }
    public string? Token => ReadToken();
    public event EventHandler? StateChanged;

    public async Task RestoreAsync()
    {
        IsRestoring = true;
        Notify();
        var token = ReadToken();
        if (!string.IsNullOrWhiteSpace(token))
        {
            if (!AuthResponsePolicy.IsValidAccessToken(token))
            {
                DeleteToken();
                Message = "本地登录凭据格式无效，请重新登录。";
            }
            else
            {
                try
                {
                    var result = await RequestAsync<UserResponse>(HttpMethod.Get, "/v1/auth/me", null, token);
                    if (result is null || result.User is null || !AuthResponsePolicy.IsSafeAccountId(result.User.Id))
                        throw new InvalidDataException("账号信息响应格式无效。");
                    CurrentUser = result.User;
                }
                catch (AuthApiException ex)
                {
                    CurrentUser = null;
                    if (AuthResponsePolicy.IsInvalidRestoreSessionResponse(ex.StatusCode, ex.Code))
                    {
                        DeleteToken();
                        Message = "登录状态已失效，请重新登录。";
                    }
                    else
                    {
                        Message = ex.Message;
                    }
                }
                catch (HttpRequestException)
                {
                    CurrentUser = null;
                    Message = "云端连接失败，请检查网络后重试。";
                }
                catch (TaskCanceledException)
                {
                    CurrentUser = null;
                    Message = "云端连接超时，请稍后重试。";
                }
                catch (Exception ex) when (ex is JsonException or InvalidDataException)
                {
                    CurrentUser = null;
                    Message = "登录状态恢复失败，请稍后重试。";
                }
            }
        }
        else
        {
            Message = "";
        }
        IsRestoring = false;
        Notify();
    }

    public async Task SendCodeAsync(string phone, string purpose)
    {
        Message = "";
        if (IsBusy)
        {
            Message = "请求正在处理中，请稍候。";
            Notify();
            return;
        }
        if (SmsCooldownRemaining > 0)
        {
            Message = "验证码发送过于频繁，请稍后再试。";
            Notify();
            return;
        }
        if (!IsValidPhone(phone))
        {
            Message = "请输入有效的 11 位手机号。";
            Notify();
            return;
        }
        try
        {
            IsBusy = true;
            Notify();
            var path = purpose switch
            {
                "register" => "/v1/auth/register/send-code",
                "reset-password" => "/v1/auth/reset-password/send-code",
                _ => "/v1/auth/login/send-code"
            };
            await RequestAsync<object>(HttpMethod.Post, path, new { phone = NormalizePhone(phone) });
            StartCooldown();
            Message = "验证码已发送，请查收短信。";
        }
        catch (Exception ex)
        {
            Message = UserMessage(ex);
        }
        finally
        {
            IsBusy = false;
            Notify();
        }
    }

    public async Task<bool> LoginAsync(string phone, string? password, string? smsCode)
    {
        if (!IsValidPhone(phone)) return SetError("请输入有效的 11 位手机号。");
        if (password is null && !IsValidCode(smsCode)) return SetError("请输入 6 位短信验证码。");
        if (password is not null && string.IsNullOrWhiteSpace(password)) return SetError("请输入密码。");
        if (password is not null && !AuthInputPolicy.IsPasswordLengthValid(password))
            return SetError("密码长度超出允许范围。");
        return await RunAuthAsync(new
        {
            phone = NormalizePhone(phone),
            password = string.IsNullOrWhiteSpace(password) ? null : password,
            smsCode = string.IsNullOrWhiteSpace(smsCode) ? null : smsCode
        });
    }

    public async Task<bool> RegisterAsync(string phone, string email, string code, string password, string confirmation)
    {
        if (IsBusy) return SetError("请求正在处理中，请稍候。");
        if (!IsValidPhone(phone)) return SetError("请输入有效的 11 位手机号。");
        if (!AuthInputPolicy.IsEmailLengthValid(email)
            || (!string.IsNullOrWhiteSpace(email) && !IsValidEmail(email)))
            return SetError("请输入有效的邮箱地址（最多 254 位），或留空跳过。");
        if (!AuthInputPolicy.IsPasswordLengthValid(password, 6)) return SetError("密码需要为 6–256 位。");
        if (password != confirmation) return SetError("两次输入的密码不一致。");
        if (!IsValidCode(code)) return SetError("请输入 6 位短信验证码。");
        try
        {
            IsBusy = true;
            Notify();
            await RequestAsync<AuthResponse>(HttpMethod.Post, "/v1/auth/register", new
            {
                phone = NormalizePhone(phone),
                email = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant(),
                smsCode = code,
                password,
                confirmation
            });
            Message = "注册成功，请使用手机号登录。";
            return true;
        }
        catch (Exception ex)
        {
            Message = UserMessage(ex);
            return false;
        }
        finally
        {
            IsBusy = false;
            Notify();
        }
    }

    public async Task<bool> ResetPasswordAsync(string phone, string code, string password, string confirmation)
    {
        if (IsBusy) return SetError("请求正在处理中，请稍候。");
        if (!IsValidPhone(phone)) return SetError("请输入有效的 11 位手机号。");
        if (!IsValidCode(code)) return SetError("请输入 6 位短信验证码。");
        if (!AuthInputPolicy.IsPasswordLengthValid(password, 6)) return SetError("新密码需要为 6–256 位。");
        if (password != confirmation) return SetError("两次输入的新密码不一致。");
        try
        {
            IsBusy = true;
            Notify();
            await RequestAsync<object>(HttpMethod.Post, "/v1/auth/reset-password", new
            {
                phone = NormalizePhone(phone), smsCode = code, password, confirmation
            });
            DeleteToken();
            CurrentUser = null;
            Message = "密码重置成功，请使用新密码登录。";
            return true;
        }
        catch (Exception ex)
        {
            Message = UserMessage(ex);
            return false;
        }
        finally
        {
            IsBusy = false;
            Notify();
        }
    }

    public async Task LogoutAsync()
    {
        var token = ReadToken();
        DeleteToken();
        CurrentUser = null;
        Message = "已退出登录";
        Notify();
        if (!string.IsNullOrWhiteSpace(token))
        {
            try { await RequestAsync<object>(HttpMethod.Post, "/v1/auth/logout", null, token); }
            catch { }
        }
    }

    public void ExpireSession()
    {
        DeleteToken();
        CurrentUser = null;
        Message = "登录状态已失效，请重新登录。";
        Notify();
    }

    private async Task<bool> RunAuthAsync(object payload)
    {
        if (IsBusy) return SetError("请求正在处理中，请稍候。");
        try
        {
            IsBusy = true;
            Notify();
            var result = await RequestAsync<AuthResponse>(HttpMethod.Post, "/v1/auth/login", payload);
            if (result is null || result.User is null || !AuthResponsePolicy.IsSafeAccountId(result.User.Id)
                || !AuthResponsePolicy.IsValidAccessToken(result.Token))
                throw new InvalidDataException("认证响应格式无效。");
            if (!SaveToken(result.Token))
            {
                Message = "登录成功，但无法安全保存登录状态，请检查 Windows 凭据管理器权限后重试。";
                return false;
            }
            CurrentUser = result.User;
            Message = "登录成功";
            return true;
        }
        catch (Exception ex)
        {
            Message = UserMessage(ex);
            return false;
        }
        finally
        {
            IsBusy = false;
            Notify();
        }
    }

    private async Task<T> RequestAsync<T>(HttpMethod method, string path, object? payload, string? token = null)
    {
        using var request = new HttpRequestMessage(method, ApiBaseUrl + path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrWhiteSpace(token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (payload is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        }
        using var timeout = new CancellationTokenSource(_http.Timeout);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        var bodyBytes = await BoundedHttpContentReader.ReadBytesAsync(
            response.Content, 1024 * 1024, timeout.Token);
        var body = Encoding.UTF8.GetString(bodyBytes);
        if (!response.IsSuccessStatusCode)
        {
            var code = "server_error";
            try { code = JsonSerializer.Deserialize<ApiError>(body, JsonOptions)?.Error ?? code; } catch { }
            throw new AuthApiException(code, UserMessage(code), response.StatusCode);
        }
        if (typeof(T) == typeof(object)) return (T)(object)new object();
        return JsonSerializer.Deserialize<T>(body, JsonOptions)!;
    }

    private static string UserMessage(string code) => code switch
    {
        "phone_already_registered" => "该手机号已经注册，请直接登录。",
        "email_already_registered" => "该邮箱已经绑定其他账号。",
        "invalid_credentials" => "手机号、验证码或密码错误。",
        "invalid_registration" => "请检查手机号、验证码和密码格式。",
        "invalid_password_reset" => "请检查手机号、验证码和新密码格式。",
        "password_mismatch" => "两次输入的密码不一致。",
        "invalid_phone" => "请输入有效的手机号。",
        "sms_cooldown" => "验证码发送过于频繁，请稍后再试。",
        "rate_limited" => "操作过于频繁，请稍后再试。",
        "sms_code_invalid" => "短信验证码错误。",
        "sms_code_expired" => "短信验证码已过期，请重新获取。",
        "sms_not_configured" => "短信服务尚未配置，请联系管理员。",
        "sms_send_failed" => "短信发送失败，请稍后再试。",
        "unauthorized" => "登录状态已失效，请重新登录。",
        "invalid_token" => "登录状态已失效，请重新登录。",
        "token_expired" => "登录状态已过期，请重新登录。",
        _ => "云端操作失败，请稍后重试。"
    };

    private static string UserMessage(Exception exception) => exception switch
    {
        InvalidOperationException invalid => invalid.Message,
        HttpRequestException => "云端连接失败，请检查网络后重试。",
        TaskCanceledException => "云端连接超时，请稍后重试。",
        _ => "云端操作失败，请稍后重试。"
    };

    private bool SetError(string message)
    {
        Message = message;
        Notify();
        return false;
    }

    private void StartCooldown()
    {
        SmsCooldownRemaining = 60;
        _ = Task.Run(async () =>
        {
            while (SmsCooldownRemaining > 0)
            {
                await Task.Delay(1000);
                SmsCooldownRemaining--;
                Notify();
            }
        });
    }

    private static string NormalizePhone(string input)
    {
        var value = new string(input.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());
        if (value.StartsWith("+86")) value = value[3..];
        if (value.StartsWith("86") && value.Length == 13) value = value[2..];
        return value;
    }

    private static bool IsValidPhone(string value) =>
        AuthInputPolicy.IsPhoneInputLengthValid(value)
        && System.Text.RegularExpressions.Regex.IsMatch(NormalizePhone(value), "^1[3-9][0-9]{9}$");

    private static bool IsValidCode(string? value) =>
        value is not null && System.Text.RegularExpressions.Regex.IsMatch(value, "^[0-9]{6}$");

    private static bool IsValidEmail(string value) =>
        AuthInputPolicy.IsEmailLengthValid(value)
        && System.Text.RegularExpressions.Regex.IsMatch(value.Trim(), "^[^\\s@]+@[^\\s@]+\\.[^\\s@]+$");

    private static bool SaveToken(string token)
    {
        if (!AuthResponsePolicy.IsValidAccessToken(token)) return false;
        try
        {
            var vault = new Windows.Security.Credentials.PasswordVault();
            try { vault.Remove(vault.Retrieve(VaultResource, VaultUser)); } catch { }
            vault.Add(new Windows.Security.Credentials.PasswordCredential(VaultResource, VaultUser, token));
            var saved = vault.Retrieve(VaultResource, VaultUser);
            saved.RetrievePassword();
            return string.Equals(saved.Password, token, StringComparison.Ordinal);
        }
        catch { return false; }
    }

    private static string? ReadToken()
    {
        try
        {
            var vault = new Windows.Security.Credentials.PasswordVault();
            var credential = vault.Retrieve(VaultResource, VaultUser);
            credential.RetrievePassword();
            return credential.Password;
        }
        catch { return null; }
    }

    private static void DeleteToken()
    {
        try
        {
            var vault = new Windows.Security.Credentials.PasswordVault();
            try { vault.Remove(vault.Retrieve(VaultResource, VaultUser)); } catch { }
        }
        catch { }
    }

    private void Notify() => StateChanged?.Invoke(this, EventArgs.Empty);

    private sealed class AuthApiException : InvalidOperationException
    {
        public string Code { get; }
        public System.Net.HttpStatusCode StatusCode { get; }

        public AuthApiException(string code, string message, System.Net.HttpStatusCode statusCode) : base(message)
        {
            Code = code;
            StatusCode = statusCode;
        }
    }

    private sealed class AuthResponse { public AccountUser? User { get; set; } = new(); public string Token { get; set; } = ""; }
    private sealed class UserResponse { public AccountUser? User { get; set; } = new(); }
    private sealed class ApiError { public string? Error { get; set; } }
}

public sealed class ClipboardMonitor : IDisposable
{
    private readonly LocalDataStore _data;
    private readonly Func<bool> _saveText;
    private readonly Func<bool> _saveImages;
    private readonly Func<bool> _saveUrls;
    private readonly Func<bool> _saveRichText;
    private int _ignoredClipboardSequence;
    private bool _disposed;

    public event EventHandler<ClipboardItem>? ItemCaptured;
    public event EventHandler<string>? CaptureFailed;
    public event EventHandler<ClipboardTextObservedEventArgs>? TextObserved;

    public ClipboardMonitor(LocalDataStore data, Func<bool> saveText, Func<bool> saveImages,
        Func<bool> saveUrls, Func<bool> saveRichText)
    {
        _data = data;
        _saveText = saveText;
        _saveImages = saveImages;
        _saveUrls = saveUrls;
        _saveRichText = saveRichText;
        Clipboard.ContentChanged += OnContentChanged;
    }

    public void IgnoreNextChange()
    {
        // Pair the one-shot ignore marker with the sequence number expected
        // from the imminent app-owned clipboard write. A stale marker must
        // never discard the user's next real copy operation.
        var currentSequence = GetClipboardSequenceNumber();
        Volatile.Write(ref _ignoredClipboardSequence, unchecked((int)(currentSequence + 1)));
    }

    public void CancelIgnoreNextChange()
    {
        Interlocked.Exchange(ref _ignoredClipboardSequence, 0);
    }

    private async void OnContentChanged(object? sender, object e)
    {
        var userId = _data.UserId;
        if (_disposed || string.IsNullOrWhiteSpace(userId)) return;
        string? capturedImagePath = null;
        string? capturedRtfPath = null;
        var expectedSequence = Interlocked.Exchange(ref _ignoredClipboardSequence, 0);
        var snapshotSequence = GetClipboardSequenceNumber();
        if (ClipboardIgnorePolicy.ShouldIgnore(expectedSequence, unchecked((int)snapshotSequence))) return;
        try
        {
            var view = Clipboard.GetContent();
            var source = ClipboardSourceResolver.GetCurrentSource();
            // Bind the lazily-read DataPackageView to the clipboard owner before
            // any await; otherwise a fast subsequent copy can misattribute the
            // captured text/image to the newer foreground clipboard source.
            if (!ClipboardCapturePolicy.IsSnapshotCurrent(snapshotSequence, GetClipboardSequenceNumber())) return;

            if (ClipboardCapturePolicy.ShouldCaptureImage(_saveImages())
                && view.Contains(StandardDataFormats.Bitmap) && !view.Contains(StandardDataFormats.StorageItems))
            {
                var reference = await view.GetBitmapAsync();
                if (_disposed || _data.UserId != userId) return;
                var stream = await reference.OpenReadAsync();
                if (_disposed || _data.UserId != userId) return;
                var folder = await StorageFolder.GetFolderFromPathAsync(AppPaths.Images(userId));
                var file = await folder.CreateFileAsync($"{Guid.NewGuid():N}.png", CreationCollisionOption.GenerateUniqueName);
                capturedImagePath = file.Path;
                using var output = await file.OpenAsync(FileAccessMode.ReadWrite);
                var decoder = await BitmapDecoder.CreateAsync(stream);
                var pixels = await decoder.GetPixelDataAsync();
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
                encoder.SetPixelData(decoder.BitmapPixelFormat, decoder.BitmapAlphaMode,
                    decoder.OrientedPixelWidth, decoder.OrientedPixelHeight, 96, 96, pixels.DetachPixelData());
                await encoder.FlushAsync();
                if (_disposed || _data.UserId != userId) return;
                capturedImagePath = null;
                ItemCaptured?.Invoke(this, new ClipboardItem
                {
                    Kind = ClipKind.Image, Title = "图片", ImagePath = file.Path, Source = source
                });
                return;
            }

            if (view.Contains(StandardDataFormats.Text) && !view.Contains(StandardDataFormats.StorageItems))
            {
                var text = (await view.GetTextAsync()).Trim();
                if (_disposed || _data.UserId != userId) return;
                if (string.IsNullOrWhiteSpace(text)) return;
                var isUrl = Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
                            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeFtp);
                TextObserved?.Invoke(this, new ClipboardTextObservedEventArgs(userId, text, source));
                if (!ClipboardCapturePolicy.ShouldCaptureText(isUrl, _saveText(), _saveUrls())) return;
                string? html = null;
                if (_saveRichText() && view.Contains(StandardDataFormats.Html))
                {
                    try { html = await view.GetHtmlFormatAsync(); } catch { }
                }
                string? rtfPath = null;
                if (_saveRichText() && view.Contains(StandardDataFormats.Rtf))
                {
                    try
                    {
                        var rtf = await view.GetTextAsync(StandardDataFormats.Rtf);
                        if (_disposed || _data.UserId != userId) return;
                        if (!string.IsNullOrWhiteSpace(rtf))
                        {
                            rtfPath = Path.Combine(AppPaths.RichData(userId), $"{Guid.NewGuid():N}.rtf");
                            capturedRtfPath = rtfPath;
                            await File.WriteAllTextAsync(rtfPath, rtf, Encoding.UTF8);
                        }
                    }
                    catch
                    {
                        rtfPath = null;
                    }
                }
                if (_disposed || _data.UserId != userId) return;
                var item = new ClipboardItem
                {
                    Kind = isUrl ? ClipKind.Url : ClipKind.Text,
                    Title = text,
                    Text = text,
                    Html = html,
                    RtfPath = rtfPath,
                    Source = source
                };
                capturedRtfPath = null;
                ItemCaptured?.Invoke(this, item);
                return;
            }

            if (view.Contains(StandardDataFormats.StorageItems))
            {
                if (ClipboardCapturePolicy.ShouldCaptureFilePaths(_saveText()))
                {
                    var files = await view.GetStorageItemsAsync();
                    if (_disposed || _data.UserId != userId) return;
                    var paths = files
                        .Where(item => item is StorageFile or StorageFolder)
                        .Select(item => item.Path)
                        .Where(path => !string.IsNullOrWhiteSpace(path))
                        .ToList();
                    if (paths.Count > 0)
                    {
                        ItemCaptured?.Invoke(this, new ClipboardItem
                        {
                            Kind = ClipKind.Text,
                            Title = string.Join("、", paths.Select(path => Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)))),
                            Text = string.Join(Environment.NewLine, paths),
                            FileUrls = paths,
                            Source = source
                        });
                        return;
                    }
                }
            }

        }
        catch
        {
            // 剪贴板内容可能在异步读取期间被其他程序替换；保留监听并把状态交给界面反馈。
            CaptureFailed?.Invoke(this, "读取剪贴板失败，请稍后重试。 ");
        }
        finally
        {
            ClipboardCaptureCleanup.DeleteOrphanedFile(capturedImagePath);
            ClipboardCaptureCleanup.DeleteOrphanedFile(capturedRtfPath);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        Clipboard.ContentChanged -= OnContentChanged;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();
}

public sealed class ClipboardTextObservedEventArgs(string userId, string text, string source) : EventArgs
{
    public string UserId { get; } = userId;
    public string Text { get; } = text;
    public string Source { get; } = source;
}

internal static class ClipboardSourceResolver
{
    public static string GetCurrentSource()
    {
        try
        {
            // The clipboard owner is the application that last called
            // SetClipboardData. GetOpenClipboardWindow only identifies a
            // process that currently has the clipboard open, which is often
            // already zero by the time ContentChanged is delivered.
            var hwnd = GetClipboardOwner();
            if (hwnd == IntPtr.Zero) hwnd = GetOpenClipboardWindow();
            if (hwnd == IntPtr.Zero) hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return "来源未知";
            GetWindowThreadProcessId(hwnd, out var processId);
            if (processId == 0) return "来源未知";
            using var process = System.Diagnostics.Process.GetProcessById((int)processId);
            try
            {
                var productName = process.MainModule?.FileVersionInfo.ProductName;
                if (!string.IsNullOrWhiteSpace(productName)) return productName.Trim();
            }
            catch
            {
                // Access to another process' module metadata can be denied;
                // the stable process name remains a useful fallback.
            }
            return string.IsNullOrWhiteSpace(process.ProcessName) ? "来源未知" : process.ProcessName;
        }
        catch { return "来源未知"; }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetClipboardOwner();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetOpenClipboardWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}

public sealed class CloudSyncService
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static bool IsSyncable(ClipboardItem item) =>
        ClipboardSyncPolicy.IsSyncable(
            item.Kind == ClipKind.Image,
            !string.IsNullOrWhiteSpace(item.ImagePath),
            !string.IsNullOrWhiteSpace(item.RtfPath),
            item.FileUrls?.Count ?? 0);
    private const string BaseUrl = "https://api.kuaijian.jingronghao.com";

    public async Task<List<ClipboardItem>> PullAsync(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + "/v1/clipboard");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var timeout = new CancellationTokenSource(_http.Timeout);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        await ThrowIfSessionExpiredAsync(response, timeout.Token);
        response.EnsureSuccessStatusCode();
        if (!ClipboardSyncPolicy.IsPayloadLengthAllowed(response.Content.Headers.ContentLength))
            throw new InvalidDataException("云端剪贴板响应超过大小限制。");
        var body = await BoundedHttpContentReader.ReadBytesAsync(
            response.Content, ClipboardSyncPolicy.MaxPayloadBytes, timeout.Token);
        var result = JsonSerializer.Deserialize<ClipboardEnvelope>(body, JsonOptions);
        if (result?.Items is null || result.Items.Count > ClipboardSyncPolicy.MaxRecords
            || result.Items.Any(item => item is null
                || !Enum.IsDefined(item.Kind)
                || !ClipboardSyncPolicy.IsValidRecord(item.Id, item.Title, item.Text, item.Html, item.Source, item.Note)))
            throw new InvalidDataException("云端剪贴板响应格式无效。");
        return result.Items
            .Where(IsSyncable)
            .Select(CloneForCloud)
            .ToList();
    }

    public async Task PushAsync(string token, IReadOnlyCollection<ClipboardItem> items)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, BaseUrl + "/v1/clipboard");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var records = items.Where(IsSyncable).Take(ClipboardSyncPolicy.MaxRecords).ToList();
        if (records.Any(item => !ClipboardSyncPolicy.IsValidRecord(
                item.Id, item.Title, item.Text, item.Html, item.Source, item.Note)))
            throw new InvalidDataException("本机剪贴板记录超出云同步字段限制。");
        var payload = JsonSerializer.Serialize(new ClipboardEnvelope
        {
            Items = records.Select(CloneForCloud).ToList()
        });
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        if (!ClipboardSyncPolicy.IsPayloadLengthAllowed(payloadBytes.LongLength))
            throw new InvalidDataException("本机剪贴板同步数据超过大小限制。");
        request.Content = new ByteArrayContent(payloadBytes);
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json")
        {
            CharSet = "utf-8"
        };
        using var timeout = new CancellationTokenSource(_http.Timeout);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        await ThrowIfSessionExpiredAsync(response, timeout.Token);
        response.EnsureSuccessStatusCode();
    }

    private static async Task ThrowIfSessionExpiredAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (await CloudSyncResponsePolicy.ShouldExpireSessionAsync(response, cancellationToken))
            throw new CloudSyncUnauthorizedException();
    }

    public sealed class CloudSyncUnauthorizedException : InvalidOperationException
    {
        public CloudSyncUnauthorizedException() : base("云端登录状态已失效。") { }
    }

    private static ClipboardItem CloneForCloud(ClipboardItem item) => new()
    {
        Id = string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString() : item.Id,
        Kind = item.Kind,
        Title = item.Title ?? "",
        Text = item.Text ?? "",
        Html = item.Html,
        Source = string.IsNullOrWhiteSpace(item.Source) ? "来源未知" : item.Source,
        CreatedAt = item.CreatedAt,
        Favorite = item.Favorite,
        Note = item.Note ?? "",
        Pinned = item.Pinned
    };

    private sealed class ClipboardEnvelope { public List<ClipboardItem> Items { get; set; } = new(); }
}

public sealed class UpdateInfo
{
    public string Version { get; set; } = "";
    public string Platform { get; set; } = "";
    public string DownloadURL { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string Notes { get; set; } = "";
}

public sealed class UpdateService
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static bool IsNewer(UpdateInfo? info)
    {
        return info is not null && Version.TryParse(info.Version, out var remote)
            && Version.TryParse(AppInfo.CurrentVersion, out var current)
            && remote > current;
    }

    public async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_http.Timeout);
        using var response = await _http.GetAsync(
            AppInfo.WindowsUpdateManifest, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        var body = await BoundedHttpContentReader.ReadBytesAsync(
            response.Content, UpdateManifestPolicy.MaximumManifestBytes, timeout.Token);
        var info = JsonSerializer.Deserialize<UpdateInfo>(body, JsonOptions);
        return IsValidWindowsUpdate(info) ? info : null;
    }

    public async Task<string> DownloadInstallerAsync(UpdateInfo info, CancellationToken cancellationToken = default)
    {
        if (!IsValidWindowsUpdate(info) || !IsNewer(info))
            throw new InvalidOperationException("更新清单不是适用于当前 Windows x64 版本的更新。");
        var uri = new Uri(info.DownloadURL, UriKind.Absolute);
        var file = Path.Combine(Path.GetTempPath(), $"KuaiJian-{info.Version}-{Guid.NewGuid():N}-Windows-x64-Setup.exe");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        var downloadToken = timeout.Token;
        try
        {
            using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, downloadToken);
            response.EnsureSuccessStatusCode();
            if (response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps)
                throw new InvalidOperationException("更新下载被重定向到非 HTTPS 地址，已取消更新。");
            if (response.Content.Headers.ContentLength is long length
                && (length <= 0 || length > UpdateManifestPolicy.MaximumInstallerBytes))
                throw new InvalidDataException("更新安装包大小无效或超过限制。");

            await using var input = await response.Content.ReadAsStreamAsync(downloadToken);
            await using var output = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(
                System.Security.Cryptography.HashAlgorithmName.SHA256);
            var buffer = new byte[128 * 1024];
            long totalBytes = 0;
            while (true)
            {
                var read = await input.ReadAsync(buffer.AsMemory(), downloadToken);
                if (read == 0) break;
                totalBytes += read;
                if (totalBytes > UpdateManifestPolicy.MaximumInstallerBytes)
                    throw new InvalidDataException("更新安装包超过大小限制。");
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), downloadToken);
            }
            if (totalBytes == 0 || !Convert.ToHexString(hash.GetHashAndReset())
                    .Equals(info.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("安装包校验失败，已取消更新。");
            return file;
        }
        catch
        {
            TryDelete(file);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // An antivirus scan or another process may briefly hold the file.
        }
    }

    private static bool IsValidWindowsUpdate(UpdateInfo? info)
    {
        return info is not null && UpdateManifestPolicy.IsValidWindowsX64(
            info.Platform, info.Version, info.DownloadURL, info.Sha256, info.Notes);
    }
}

public sealed class YtDlpService
{
    private static readonly System.Text.RegularExpressions.Regex ProgressPattern = new(@"\[download\]\s+(?<value>\d+(?:\.\d+)?)%", System.Text.RegularExpressions.RegexOptions.Compiled);
    public string? ExecutablePath
    {
        get
        {
            var bundled = Path.Combine(AppContext.BaseDirectory, "runtime-tools", "yt-dlp.exe");
            if (File.Exists(bundled)) return bundled;
            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = Path.Combine(directory.Trim(), "yt-dlp.exe");
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }
    }

    public bool IsAvailable => ExecutablePath is not null;

    public string? FfmpegPath
    {
        get
        {
            var bundled = Path.Combine(AppContext.BaseDirectory, "runtime-tools", "ffmpeg.exe");
            if (File.Exists(bundled)) return bundled;
            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = Path.Combine(directory.Trim(), "ffmpeg.exe");
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }
    }

    public bool IsFfmpegAvailable => FfmpegPath is not null;

    public static bool IsSupportedSite(Uri uri)
        => SupportedSitePolicy.IsSupported(uri);

    public async Task<string?> DownloadAsync(
        Uri uri,
        string outputDirectory,
        string stagingDirectory,
        string browser,
        CancellationToken cancellationToken,
        Action<double, string> progress,
        Action<string> output)
    {
        var executable = ExecutablePath ?? throw new FileNotFoundException("未找到 yt-dlp.exe。", "yt-dlp.exe");
        Directory.CreateDirectory(outputDirectory);
        Directory.CreateDirectory(stagingDirectory);
        var start = DateTimeOffset.Now;
        using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo(executable)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    WorkingDirectory = stagingDirectory
                },
                EnableRaisingEvents = true
            };
            process.StartInfo.ArgumentList.Add("--encoding");
            process.StartInfo.ArgumentList.Add("UTF-8");
            process.StartInfo.ArgumentList.Add("--newline");
            process.StartInfo.ArgumentList.Add("--no-playlist");
            process.StartInfo.ArgumentList.Add("--continue");
            process.StartInfo.ArgumentList.Add("--no-overwrites");
            process.StartInfo.ArgumentList.Add("--windows-filenames");
            process.StartInfo.ArgumentList.Add("--format");
            process.StartInfo.ArgumentList.Add(IsFfmpegAvailable ? "bestvideo*+bestaudio/best" : "best");
            if (FfmpegPath is { } ffmpegPath)
            {
                process.StartInfo.ArgumentList.Add("--ffmpeg-location");
                process.StartInfo.ArgumentList.Add(Path.GetDirectoryName(ffmpegPath)!);
                process.StartInfo.ArgumentList.Add("--merge-output-format");
                process.StartInfo.ArgumentList.Add("mp4");
            }
            process.StartInfo.ArgumentList.Add("--print");
            process.StartInfo.ArgumentList.Add("after_move:filepath");
            process.StartInfo.ArgumentList.Add("-P");
            process.StartInfo.ArgumentList.Add(stagingDirectory);
            process.StartInfo.ArgumentList.Add("-o");
            process.StartInfo.ArgumentList.Add("%(title)s.%(ext)s");
            foreach (var argument in DownloadBrowserPolicy.CookieArguments(browser))
                process.StartInfo.ArgumentList.Add(argument);
            process.StartInfo.ArgumentList.Add(uri.ToString());

            string? outputPath = null;
            string? lastError = null;
            void HandleLine(string? line)
            {
                if (string.IsNullOrWhiteSpace(line)) return;
                var match = ProgressPattern.Match(line);
                if (match.Success && double.TryParse(match.Groups["value"].Value, out var percent))
                    progress(Math.Clamp(percent / 100, 0, 1), line.Trim());
                else
                    output(line.Trim());
                var candidate = line.Trim().Trim('"');
                if (Path.IsPathRooted(candidate) && File.Exists(candidate)) outputPath = candidate;
            }
            process.OutputDataReceived += (_, args) => HandleLine(args.Data);
            process.ErrorDataReceived += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.Data)) lastError = args.Data.Trim();
                HandleLine(args.Data);
            };
            if (!process.Start()) throw new InvalidOperationException("无法启动 yt-dlp.exe。");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await CancellableProcessWait.WaitForExitAsync(process, cancellationToken);
            if (process.ExitCode != 0)
            {
                var detail = string.IsNullOrWhiteSpace(lastError) ? "请检查链接或浏览器登录状态。" : lastError;
                throw new InvalidOperationException($"yt-dlp 下载失败：{detail}");
            }
            outputPath ??= Directory.GetFiles(stagingDirectory)
                .Where(path => File.GetLastWriteTimeUtc(path) >= start.UtcDateTime && !path.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(outputPath) || !File.Exists(outputPath))
                throw new InvalidOperationException("下载完成，但未找到输出文件。");

            var finalPath = await DownloadFileStore.MoveToUniquePathAsync(
                outputPath, outputDirectory, Path.GetFileName(outputPath), cancellationToken);
            try { if (Directory.Exists(stagingDirectory)) Directory.Delete(stagingDirectory, true); } catch { }
            return finalPath;
    }

}
