namespace KuaiJian.WinUI;

/// <summary>
/// Merges a cloud snapshot with local records without discarding records that
/// have not reached the cloud yet. The caller decides how same-ID metadata and
/// device-local resource references are reconciled.
/// </summary>
public static class ClipboardSyncMerge
{
    public static List<T> ExcludeDeleted<T>(
        IEnumerable<T> remoteItems,
        IEnumerable<string> deletedIds,
        Func<T, string?> getId)
    {
        ArgumentNullException.ThrowIfNull(remoteItems);
        ArgumentNullException.ThrowIfNull(deletedIds);
        ArgumentNullException.ThrowIfNull(getId);
        var deleted = deletedIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);
        return remoteItems
            .Where(item => !deleted.Contains(getId(item) ?? ""))
            .ToList();
    }

    public static List<T> Merge<T>(
        IEnumerable<T> localItems,
        IEnumerable<T> remoteItems,
        Func<T, string?> getId,
        Func<T, T, T> mergeSameId)
    {
        ArgumentNullException.ThrowIfNull(localItems);
        ArgumentNullException.ThrowIfNull(remoteItems);
        ArgumentNullException.ThrowIfNull(getId);
        ArgumentNullException.ThrowIfNull(mergeSameId);

        var local = localItems.ToList();
        var localById = local
            .Where(item => !string.IsNullOrWhiteSpace(getId(item)))
            .GroupBy(item => getId(item)!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var merged = new List<T>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var remote in remoteItems)
        {
            var id = getId(remote);
            if (string.IsNullOrWhiteSpace(id))
            {
                merged.Add(remote);
                continue;
            }

            if (!seen.Add(id)) continue;
            merged.Add(localById.TryGetValue(id, out var matchingLocal)
                ? mergeSameId(remote, matchingLocal)
                : remote);
        }

        foreach (var localItem in local)
        {
            var id = getId(localItem);
            if (string.IsNullOrWhiteSpace(id) || seen.Add(id))
                merged.Add(localItem);
        }

        return merged;
    }

    /// <summary>
    /// Merges a cloud snapshot with local records while treating identical
    /// content as one clipboard entry even when clients assigned different IDs.
    /// Remote order/IDs remain canonical; the callbacks preserve device-local
    /// resources and resolve fields that can be combined safely.
    /// </summary>
    public static List<T> MergeByContent<T>(
        IEnumerable<T> localItems,
        IEnumerable<T> remoteItems,
        Func<T, string?> getId,
        Func<T, string?> getContentKey,
        Func<T, T, T> mergeSameId,
        Func<T, T, T> mergeSameContent)
    {
        ArgumentNullException.ThrowIfNull(localItems);
        ArgumentNullException.ThrowIfNull(remoteItems);
        ArgumentNullException.ThrowIfNull(getId);
        ArgumentNullException.ThrowIfNull(getContentKey);
        ArgumentNullException.ThrowIfNull(mergeSameId);
        ArgumentNullException.ThrowIfNull(mergeSameContent);

        var merged = new List<T>();
        var indexesById = new Dictionary<string, int>(StringComparer.Ordinal);
        var indexesByContent = new Dictionary<string, int>(StringComparer.Ordinal);

        void AddOrMerge(T candidate)
        {
            var id = getId(candidate);
            var contentKey = getContentKey(candidate);
            int index;
            if (!string.IsNullOrWhiteSpace(id) && indexesById.TryGetValue(id, out index))
            {
                merged[index] = mergeSameId(merged[index], candidate);
            }
            else if (!string.IsNullOrWhiteSpace(contentKey)
                && indexesByContent.TryGetValue(contentKey, out index))
            {
                merged[index] = mergeSameContent(merged[index], candidate);
            }
            else
            {
                index = merged.Count;
                merged.Add(candidate);
            }

            var result = merged[index];
            var resultId = getId(result);
            var resultContentKey = getContentKey(result);
            if (!string.IsNullOrWhiteSpace(resultId)) indexesById[resultId] = index;
            if (!string.IsNullOrWhiteSpace(resultContentKey)) indexesByContent[resultContentKey] = index;
        }

        foreach (var remote in remoteItems) AddOrMerge(remote);
        foreach (var local in localItems) AddOrMerge(local);
        return merged;
    }
}
