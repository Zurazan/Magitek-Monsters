using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MagitekMonsters.Services;

public enum ServerConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    Syncing,
    Unauthorized,
    Incompatible
}

public sealed partial class ServerConnection : IDisposable
{
    private readonly Func<string?> readToken;
    private readonly ClientUpdateState update;
    private readonly Uri eventsUri;
    private readonly HttpClient http;
    private readonly TimeProvider time;
    private readonly Action<Exception>? interrupted;
    private readonly object sync = new();
    private CancellationTokenSource? cancellation;
    private volatile ServerConnectionState state = ServerConnectionState.Disconnected;
    private long generation;
    private bool disposed;

    public ServerConnection(Func<string?> readToken, ClientUpdateState update, Uri eventsUri, Action<Exception>? interrupted = null)
        : this(readToken, update, eventsUri, new HttpClient { Timeout = Timeout.InfiniteTimeSpan }, TimeProvider.System, interrupted) { liveEnabled = true; }

    internal ServerConnection(Func<string?> readToken, ClientUpdateState update, Uri eventsUri, HttpClient http, TimeProvider time,
        Action<Exception>? interrupted = null)
    {
        this.readToken = readToken;
        this.update = update;
        this.eventsUri = eventsUri;
        this.http = http;
        this.time = time;
        this.interrupted = interrupted;
    }

    public ServerConnectionState State => state;
    public long Generation { get { lock (sync) return generation; } }
    public event Action<string, string>? EventReceived;

    public void Start()
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (cancellation is not null) return;
            generation++;
            cancellation = new CancellationTokenSource();
            _ = Run(cancellation);
        }
    }

    public void Stop()
    {
        lock (sync)
        {
            var current = cancellation;
            cancellation = null;
            generation++;
            state = ServerConnectionState.Disconnected;
            current?.Cancel();
        }
    }

    public bool CompleteSync(long expected)
    {
        lock (sync)
        {
            if (generation != expected || state != ServerConnectionState.Syncing) return false;
            state = ServerConnectionState.Connected;
            return true;
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            Stop();
            http.Dispose();
        }
    }

    private bool SetState(CancellationTokenSource owner, ServerConnectionState next)
    {
        lock (sync)
        {
            if (!ReferenceEquals(cancellation, owner) || owner.IsCancellationRequested) return false;
            state = next;
            return true;
        }
    }

    private async Task Run(CancellationTokenSource owner)
    {
        var failures = 0;
        try
        {
            while (!owner.IsCancellationRequested)
            {
                try
                {
                    if (!SetState(owner, failures == 0 ? ServerConnectionState.Connecting : ServerConnectionState.Reconnecting)) break;
                    if (!await Connect(owner, () => failures = 0).ConfigureAwait(false)) break;
                }
                catch (OperationCanceledException) when (owner.IsCancellationRequested) { break; }
                catch (Exception exception)
                {
                    if (owner.IsCancellationRequested) break;
                    interrupted?.Invoke(exception);
                }
                if (!SetState(owner, ServerConnectionState.Reconnecting)) break;
                failures = Math.Min(failures + 1, 5);
                await Task.Delay(RetryDelay(failures, Random.Shared.NextDouble()), time, owner.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (owner.IsCancellationRequested) { }
        finally
        {
            lock (sync)
            {
                if (ReferenceEquals(cancellation, owner))
                {
                    cancellation = null;
                    if (state is not (ServerConnectionState.Unauthorized or ServerConnectionState.Incompatible)) state = ServerConnectionState.Disconnected;
                }
                owner.Dispose();
            }
        }
    }

    internal static TimeSpan RetryDelay(int failures, double jitter) =>
        TimeSpan.FromSeconds(Math.Min(15, (1 << Math.Clamp(failures - 1, 0, 4)) * (0.8 + 0.4 * jitter)));

    private async Task<bool> Connect(CancellationTokenSource owner, Action healthy)
    {
        if (liveEnabled) return await ConnectLive(owner, healthy).ConfigureAwait(false);
        if (update.Required) { SetState(owner, ServerConnectionState.Incompatible); return false; }
        var token = readToken();
        if (string.IsNullOrWhiteSpace(token)) { SetState(owner, ServerConnectionState.Unauthorized); return false; }

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10), time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(owner.Token, deadline.Token);
        var cancellationToken = linked.Token;
        using var request = new HttpRequestMessage(HttpMethod.Get, eventsUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation(ProtocolInfo.ProtocolHeader, ProtocolInfo.Version);
        request.Headers.TryAddWithoutValidation(ProtocolInfo.ClientHeader, ClientBuildVersion.Current);

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized) { SetState(owner, ServerConnectionState.Unauthorized); return false; }
        if (await update.Observe(response, cancellationToken).ConfigureAwait(false)) { SetState(owner, ServerConnectionState.Incompatible); return false; }
        response.EnsureSuccessStatusCode();
        if (!string.Equals(response.Content.Headers.ContentType?.MediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The server did not return an event stream.");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var abort = cancellationToken.Register(() => stream.Dispose());
        using var reader = new StreamReader(stream);
        string? eventName = null;
        var data = new StringBuilder();
        var welcomed = false;
        var stable = false;
        var started = time.GetTimestamp();
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null) return true;
            if (welcomed)
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(60));
                if (!stable && time.GetElapsedTime(started) >= TimeSpan.FromSeconds(20)) { stable = true; healthy(); }
            }
            if (line.StartsWith("event:", StringComparison.Ordinal)) eventName = line[6..].Trim();
            else if (line.StartsWith("data:", StringComparison.Ordinal)) data.Append(line[5..].TrimStart()).Append('\n');
            else if (line.Length == 0)
            {
                if (!string.IsNullOrWhiteSpace(eventName))
                {
                    var payload = data.ToString().TrimEnd('\n');
                    if (!welcomed)
                    {
                        if (eventName != "hello") throw new InvalidDataException("The event stream did not send a greeting.");
                        using var hello = JsonDocument.Parse(payload);
                        if (!hello.RootElement.TryGetProperty("protocolVersion", out var version) || version.ToString() != ProtocolInfo.Version)
                        {
                            update.Require();
                            SetState(owner, ServerConnectionState.Incompatible);
                            return false;
                        }
                        welcomed = true;
                        started = time.GetTimestamp();
                        deadline.CancelAfter(TimeSpan.FromSeconds(60));
                    }
                    lock (sync)
                    {
                        if (!ReferenceEquals(cancellation, owner) || cancellationToken.IsCancellationRequested) return false;
                        if (eventName == "hello") { generation++; state = ServerConnectionState.Syncing; }
                        EventReceived?.Invoke(eventName, payload);
                    }
                }
                eventName = null;
                data.Clear();
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return false;
    }
}
