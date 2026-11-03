using System.Diagnostics;

namespace KuaiJian.WinUI;

public static class CancellableProcessWait
{
    public static async Task WaitForExitAsync(Process process, CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(() => TryKillProcessTree(process));
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryKillProcessTree(process);
            await process.WaitForExitAsync(CancellationToken.None);
            process.WaitForExit();
            throw;
        }

        // Drain asynchronous stdout/stderr callbacks before callers inspect the
        // final exit code or delete files the child process may still hold open.
        process.WaitForExit();
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }
}
