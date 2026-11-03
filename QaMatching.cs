namespace KuaiJian.WinUI;

public sealed class QaEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Question { get; set; } = "";
    public string Answer { get; set; } = "";
    public string Aliases { get; set; } = "";
    public string Keywords { get; set; } = "";
    public string Category { get; set; } = "";
    public string Note { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

public enum QaMatchKind
{
    ExactQuestion,
    Alias,
    Keywords,
    Candidate
}

public sealed record QaMatch(QaEntry Entry, QaMatchKind Kind)
{
    public bool IsSafeForAutomaticCopy => Kind != QaMatchKind.Candidate;
}

public static class QaMatcher
{
    public static QaMatch? Find(string? input, IEnumerable<QaEntry> entries)
    {
        var query = Normalize(input);
        if (query.Length == 0) return null;
        var enabled = entries.Where(entry => entry.Enabled).ToList();

        var exact = enabled.FirstOrDefault(entry => Normalize(entry.Question) == query);
        if (exact is not null) return new QaMatch(exact, QaMatchKind.ExactQuestion);

        var alias = enabled.FirstOrDefault(entry => SplitTerms(entry.Aliases)
            .Any(term => Normalize(term) == query));
        if (alias is not null) return new QaMatch(alias, QaMatchKind.Alias);

        var keywordMatch = enabled.FirstOrDefault(entry =>
        {
            var keywords = SplitTerms(entry.Keywords).Select(Normalize).Where(term => term.Length > 0).ToArray();
            return keywords.Any(query.Contains);
        });
        if (keywordMatch is not null) return new QaMatch(keywordMatch, QaMatchKind.Keywords);

        var candidate = enabled.FirstOrDefault(entry =>
        {
            var question = Normalize(entry.Question);
            return question.Length > 0 && (query.Contains(question) || question.Contains(query));
        });
        return candidate is null ? null : new QaMatch(candidate, QaMatchKind.Candidate);
    }

    public static string Normalize(string? value) => new((value ?? "").ToLowerInvariant()
        .Where(character => !char.IsWhiteSpace(character)
            && character is not '?' and not '？' and not '。')
        .ToArray());

    private static IEnumerable<string> SplitTerms(string? value) => (value ?? "")
        .Split(new[] { '|', ';', '；', ',', '，', '\r', '\n' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}

public static class QaSourceFilter
{
    public static bool IsAllowed(string? configuredSources, string? source)
    {
        var allowedSources = (configuredSources ?? "").Split(
            new[] { '|', ';', '；', ',', '，', '\r', '\n' },
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return allowedSources.Length == 0 || allowedSources.Any(allowed =>
            string.Equals(allowed, source, StringComparison.OrdinalIgnoreCase));
    }
}
