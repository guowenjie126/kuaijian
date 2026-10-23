namespace KuaiJian.WinUI;

public sealed record ClipboardImportMergeResult<T>(IReadOnlyList<T> Unique, IReadOnlyList<T> Skipped);

public static class ClipboardImportMergePolicy
{
    private sealed class ContentGroup<T>
    {
        public List<T> Items { get; } = new();
        public HashSet<string>? Identities { get; set; }
    }

    public static async Task<ClipboardImportMergeResult<T>> SelectUniqueAsync<T, TShape>(
        IEnumerable<T> existing,
        IEnumerable<T> imported,
        Func<T, bool> isImportable,
        Func<T, TShape> createShape,
        Func<T, Task<string>> createIdentity)
        where TShape : notnull
    {
        var groups = new Dictionary<TShape, ContentGroup<T>>();
        foreach (var item in existing)
        {
            var shape = createShape(item);
            if (!groups.TryGetValue(shape, out var group)) groups[shape] = group = new ContentGroup<T>();
            group.Items.Add(item);
        }

        var unique = new List<T>();
        var skipped = new List<T>();

        foreach (var candidate in imported)
        {
            if (!isImportable(candidate))
            {
                skipped.Add(candidate);
                continue;
            }

            var candidateShape = createShape(candidate);
            if (!groups.TryGetValue(candidateShape, out var matchingGroup))
            {
                matchingGroup = new ContentGroup<T>();
                groups[candidateShape] = matchingGroup;
                matchingGroup.Items.Add(candidate);
                unique.Add(candidate);
                continue;
            }

            var candidateIdentity = await createIdentity(candidate);
            if (matchingGroup.Identities is null)
            {
                matchingGroup.Identities = new HashSet<string>(StringComparer.Ordinal);
                foreach (var prior in matchingGroup.Items)
                    matchingGroup.Identities.Add(await createIdentity(prior));
            }

            if (matchingGroup.Identities.Add(candidateIdentity))
            {
                unique.Add(candidate);
                matchingGroup.Items.Add(candidate);
            }
            else
                skipped.Add(candidate);
        }

        return new ClipboardImportMergeResult<T>(unique, skipped);
    }
}
