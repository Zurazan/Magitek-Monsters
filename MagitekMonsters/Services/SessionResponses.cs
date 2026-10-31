using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace MagitekMonsters.Services;

public sealed class SessionResponses
{
    private long generation;
    public long Generation => Volatile.Read(ref generation);
    public bool IsCurrent(long expected) => Generation == expected;
    public void Invalidate() => Interlocked.Increment(ref generation);
    public Response<T> Observe<T>(Task<T> request) => new(this, Generation, request);
    public Response Observe(Task request) => new(this, Generation, request);

    private void Check(long expected)
    {
        if (!IsCurrent(expected)) throw new StaleSessionException();
    }

    public readonly struct Response<T>(SessionResponses session, long generation, Task<T> request)
    {
        public Awaiter GetAwaiter() => new(session, generation, request.GetAwaiter());
        public readonly struct Awaiter(SessionResponses session, long generation, TaskAwaiter<T> inner) : ICriticalNotifyCompletion
        {
            public bool IsCompleted => inner.IsCompleted;
            public void OnCompleted(Action continuation) => inner.OnCompleted(continuation);
            public void UnsafeOnCompleted(Action continuation) => inner.UnsafeOnCompleted(continuation);
            public T GetResult()
            {
                T result;
                try { result = inner.GetResult(); }
                catch (Exception) when (!session.IsCurrent(generation)) { throw new StaleSessionException(); }
                // Check on the caller's continuation, before it applies the result.
                session.Check(generation);
                return result;
            }
        }
    }

    public readonly struct Response(SessionResponses session, long generation, Task request)
    {
        public Awaiter GetAwaiter() => new(session, generation, request.GetAwaiter());
        public readonly struct Awaiter(SessionResponses session, long generation, TaskAwaiter inner) : ICriticalNotifyCompletion
        {
            public bool IsCompleted => inner.IsCompleted;
            public void OnCompleted(Action continuation) => inner.OnCompleted(continuation);
            public void UnsafeOnCompleted(Action continuation) => inner.UnsafeOnCompleted(continuation);
            public void GetResult()
            {
                try { inner.GetResult(); }
                catch (Exception) when (!session.IsCurrent(generation)) { throw new StaleSessionException(); }
                session.Check(generation);
            }
        }
    }
}

public sealed class StaleSessionException() : OperationCanceledException("This response belongs to an ended login session.");
