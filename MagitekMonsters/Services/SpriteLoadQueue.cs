using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MagitekMonsters.Services;

public sealed class SpriteLoadQueue(int concurrency = 2) : IDisposable
{
    private readonly object gate = new();
    private readonly SemaphoreSlim slots = new(concurrency);
    private readonly CancellationTokenSource shutdown = new();
    private readonly HashSet<Task> pending = [];
    private bool disposed;

    public Task<T> Run<T>(Func<CancellationToken, Task<T>> action)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var token = shutdown.Token;
            var work = Task.Run(async () =>
            {
                await slots.WaitAsync(token).ConfigureAwait(false);
                try { return await action(token).ConfigureAwait(false); }
                finally { slots.Release(); }
            }, token);
            pending.Add(work);
            _ = Forget(work);
            return work;
        }
    }
    private async Task Forget(Task work)
    {
        try { await work.ConfigureAwait(false); }
        catch { }
        finally { lock (gate) pending.Remove(work); }
    }
    public void Dispose()
    {
        Task[] work;
        lock (gate)
        {
            if (disposed) return;
            disposed = true; work = pending.ToArray();
        }
        shutdown.Cancel();
        _ = Release(work);
    }
    private async Task Release(Task[] work)
    {
        try { await Task.WhenAll(work).ConfigureAwait(false); }
        catch { }
        finally { slots.Dispose(); shutdown.Dispose(); }
    }
}
