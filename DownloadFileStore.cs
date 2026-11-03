namespace KuaiJian.WinUI;

public sealed record DownloadFileReservation(string DestinationPath, string PartPath, FileStream Stream);

public static class DownloadFileStore
{
    public static DownloadFileReservation CreateUniquePartFile(string directory, string fileName)
    {
        Directory.CreateDirectory(directory);
        var safeName = Sanitize(fileName);
        var (stem, extension) = GetNameParts(safeName);

        for (var index = 0; ; index++)
        {
            var destination = GetCandidatePath(directory, stem, extension, index);
            var part = destination + ".part";
            if (File.Exists(destination) || Directory.Exists(destination) || File.Exists(part)) continue;

            try
            {
                var stream = new FileStream(part, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    128 * 1024, FileOptions.Asynchronous);
                return new DownloadFileReservation(destination, part, stream);
            }
            catch (IOException exception) when (IsNameCollision(exception)
                || File.Exists(part) || File.Exists(destination) || Directory.Exists(destination))
            {
                // Another download reserved or committed this candidate after our check.
            }
        }
    }

    public static string MoveToUniquePath(string sourcePath, string directory, string fileName)
    {
        Directory.CreateDirectory(directory);
        var safeName = Sanitize(fileName);
        var (stem, extension) = GetNameParts(safeName);

        for (var index = 0; ; index++)
        {
            var destination = GetCandidatePath(directory, stem, extension, index);
            if (File.Exists(destination) || Directory.Exists(destination) || File.Exists(destination + ".part")) continue;
            try
            {
                File.Move(sourcePath, destination);
                return destination;
            }
            catch (IOException exception) when (IsNameCollision(exception)
                || File.Exists(destination) || Directory.Exists(destination))
            {
                // Another download won the final-name race; retry with the next suffix.
            }
        }
    }

    public static Task<string> MoveToUniquePathAsync(string sourcePath, string directory, string fileName,
        CancellationToken cancellationToken = default)
    {
        var sourceRoot = Path.GetPathRoot(Path.GetFullPath(sourcePath));
        var destinationRoot = Path.GetPathRoot(Path.GetFullPath(directory));
        if (string.Equals(sourceRoot, destinationRoot, StringComparison.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(MoveToUniquePath(sourcePath, directory, fileName));
        }

        return CopyToUniquePathAsync(sourcePath, directory, fileName, cancellationToken);
    }

    public static async Task<string> CopyToUniquePathAsync(string sourcePath, string directory, string fileName,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var reservation = CreateUniquePartFile(directory, fileName);
            try
            {
                await using (reservation.Stream)
                await using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await source.CopyToAsync(reservation.Stream, 128 * 1024, cancellationToken);
                    await reservation.Stream.FlushAsync(cancellationToken);
                    reservation.Stream.Flush(flushToDisk: true);
                }

                try
                {
                    File.Move(reservation.PartPath, reservation.DestinationPath);
                }
                catch (IOException exception) when (IsNameCollision(exception)
                    || File.Exists(reservation.DestinationPath) || Directory.Exists(reservation.DestinationPath))
                {
                    TryDelete(reservation.PartPath);
                    continue;
                }

                TryDelete(sourcePath);
                return reservation.DestinationPath;
            }
            catch
            {
                TryDelete(reservation.PartPath);
                throw;
            }
        }
    }

    private static string GetCandidatePath(string directory, string stem, string extension, int index)
    {
        var name = index == 0 ? stem + extension : $"{stem} ({index}){extension}";
        return Path.Combine(directory, name);
    }

    private static (string Stem, string Extension) GetNameParts(string name)
    {
        var extension = Path.GetExtension(name);
        var stem = Path.GetFileNameWithoutExtension(name);
        if (string.IsNullOrWhiteSpace(stem)) stem = $"KuaiJian-{DateTime.Now:yyyyMMdd-HHmmss}";
        return (stem, extension);
    }

    private static bool IsNameCollision(IOException exception)
    {
        // The competing writer may already have moved/deleted its reservation
        // by the time File.Exists runs in the catch filter. Preserve the native
        // CreateNew/File.Move collision codes so that transient races retry.
        var windowsError = exception.HResult & 0xFFFF;
        return windowsError is 80 or 183; // ERROR_FILE_EXISTS / ERROR_ALREADY_EXISTS
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static string Sanitize(string name)
    {
        name = Path.GetFileName(name);
        foreach (var character in Path.GetInvalidFileNameChars()) name = name.Replace(character, '_');
        return string.IsNullOrWhiteSpace(name) ? $"KuaiJian-{DateTime.Now:yyyyMMdd-HHmmss}.bin" : name;
    }
}
