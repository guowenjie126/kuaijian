using System.Globalization;

namespace KuaiJian.WinUI;

public static class RelativeTimePolicy
{
    public static string Format(DateTimeOffset value, DateTimeOffset now)
    {
        var seconds = Math.Max(0, (int)(now - value).TotalSeconds);
        if (seconds < 60) return $"{Math.Max(1, seconds)} 秒前";
        if (seconds < 3600) return $"{seconds / 60} 分钟前";
        if (seconds < 86400) return $"{seconds / 3600} 小时前";
        if (now.LocalDateTime.Date - value.LocalDateTime.Date == TimeSpan.FromDays(1)) return "昨天";
        return value.LocalDateTime.ToString("M月d日", CultureInfo.GetCultureInfo("zh-CN"));
    }
}
