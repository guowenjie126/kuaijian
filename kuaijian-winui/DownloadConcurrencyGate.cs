namespace KuaiJian.WinUI;

public sealed class DownloadConcurrencyGate
{
    private readonly object _sync = new();
    private readonly LinkedList<Waiter> _waiters = new();
    private int _limit;
    private int _active;

    public DownloadConcurrencyGate(int limit = 2) => _limit = Normalize(limit);

    public int Limit { get { lock (_sync) return _limit; } }
    public int ActiveCount { get { lock (_sync) return _active; } }
    public int PendingCount { get { lock (_sync) return _waiters.Count; } }

    public void SetLimit(int limit)
    {
        lock (_sync)
        {
            _limit = Normalize(limit);
            DrainWaiters();
        }
    }

    public Task<IDisposable> AcquireAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<IDisposable>(cancellationToken);

        lock (_sync)
        {
            if (_active < _limit)
            {
                _active++;
                return Task.FromResult<IDisposable>(new Lease(this));
            }

            var waiter = new Waiter();
            _waiters.AddLast(waiter.Node = new LinkedListNode<Waiter>(waiter));
            return AwaitWaiterAsync(waiter, cancellationToken);
        }
    }

    private async Task<IDisposable> AwaitWaiterAsync(Waiter waiter, CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(() => CancelWaiter(waiter, cancellationToken));
        return await waiter.Completion.Task.ConfigureAwait(false);
    }

    private void CancelWaiter(Waiter waiter, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (waiter.Node is { List: not null } node)
            {
                _waiters.Remove(node);
                waiter.Node = null;
            }
            waiter.Completion.TrySetCanceled(cancellationToken);
        }
    }

    private void DrainWaiters()
    {
        while (_active < _limit && _waiters.First is { } first)
        {
            _waiters.RemoveFirst();
            var waiter = first.Value;
            waiter.Node = null;
            if (!waiter.Completion.TrySetResult(new Lease(this))) continue;
            _active++;
        }
    }

    private void Release()
    {
        lock (_sync)
        {
            if (_active > 0) _active--;
            DrainWaiters();
        }
    }

    private static int Normalize(int limit) => Math.Clamp(limit, 1, 5);

    private sealed class Waiter
    {
        public readonly TaskCompletionSource<IDisposable> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public LinkedListNode<Waiter>? Node;
    }

    private sealed class Lease(DownloadConcurrencyGate owner) : IDisposable
    {
        private DownloadConcurrencyGate? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release();
    }
}
