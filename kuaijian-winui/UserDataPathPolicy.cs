namespace KuaiJian.WinUI;

public static class UserDataPathPolicy
{
    public static string GetUserRoot(string dataRoot, string userId)
    {
        if (!AuthResponsePolicy.IsSafeAccountId(userId))
            throw new InvalidDataException("账号标识格式无效，无法创建本地数据目录。");
        return Path.Combine(dataRoot, "users", userId);
    }
}
