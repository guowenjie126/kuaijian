namespace KuaiJian.WinUI;

public static class QaEntrySearchPolicy
{
    public static bool Matches(QaEntry entry, string? query)
    {
        var needle = (query ?? "").Trim();
        return needle.Length == 0
            || Contains(entry.Question, needle)
            || Contains(entry.Answer, needle)
            || Contains(entry.Aliases, needle)
            || Contains(entry.Keywords, needle);
    }

    private static bool Contains(string? value, string query) =>
        value?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;
}
