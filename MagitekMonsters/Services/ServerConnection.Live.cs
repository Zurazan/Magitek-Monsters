using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MagitekMonsters.Contracts;
using MagitekMonsters.Models;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace MagitekMonsters.Services;

public sealed partial class ServerConnection
{
    private bool liveEnabled;
    private HubConnection? activeHub;
    public event Action<long, LiveEvent>? StateReceived;
    public bool CanInvoke { get { lock (sync) return activeHub?.State == HubConnectionState.Connected && state is ServerConnectionState.Connected or ServerConnectionState.Syncing; } }
    private async Task<bool> ConnectLive(CancellationTokenSource owner, Action healthy)
    {
        if (update.Required) { SetState(owner, ServerConnectionState.Incompatible); return false; }
        if (string.IsNullOrWhiteSpace(readToken())) { SetState(owner, ServerConnectionState.Unauthorized); return false; }
        var greeted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var hub = new HubConnectionBuilder().WithUrl(new Uri(eventsUri, LiveProtocol.Path), options =>
        {
            options.Transports = HttpTransportType.WebSockets;
            options.AccessTokenProvider = () => Task.FromResult(readToken());
            options.Headers[ProtocolInfo.ProtocolHeader] = ProtocolInfo.Version;
            options.Headers[ProtocolInfo.ClientHeader] = ClientBuildVersion.Current;
            options.HttpMessageHandlerFactory = handler =>
            { if (handler is HttpClientHandler settings) { settings.AllowAutoRedirect = false; settings.UseCookies = false; } return handler; };
        }).Build();
        hub.ServerTimeout = TimeSpan.FromSeconds(45); hub.KeepAliveInterval = TimeSpan.FromSeconds(15);
        using var subscription = hub.On<LiveEvent>("Event", message =>
        {
            lock (sync)
            {
                if (!ReferenceEquals(cancellation, owner) || owner.IsCancellationRequested) return;
                if (message.Name == "hello")
                {
                    var hello = message.Data.Deserialize<LiveHello>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
                    if (hello?.Version != LiveProtocol.Version || hello.ProtocolVersion != ProtocolInfo.Version)
                    { update.Require(); state = ServerConnectionState.Incompatible; greeted.TrySetResult(); return; }
                    generation++; state = ServerConnectionState.Syncing; greeted.TrySetResult();
                }
                if (message.Battle is not null || message.Raid is not null) StateReceived?.Invoke(generation, message);
                else EventReceived?.Invoke(message.Name, message.Data.GetRawText());
            }
        });
        hub.Closed += _ => { closed.TrySetResult(); return Task.CompletedTask; };
        lock (sync) { if (!ReferenceEquals(cancellation, owner)) return false; activeHub = hub; }
        try
        {
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(owner.Token))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(10));
                await hub.StartAsync(deadline.Token).ConfigureAwait(false);
                await greeted.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            }
            if (update.Required) return false;
            if (await Task.WhenAny(closed.Task, Task.Delay(TimeSpan.FromSeconds(20), time, owner.Token)).ConfigureAwait(false) != closed.Task) healthy();
            await closed.Task.WaitAsync(owner.Token).ConfigureAwait(false);
            return true;
        }
        catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.Unauthorized)
        { SetState(owner, ServerConnectionState.Unauthorized); return false; }
        catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.UpgradeRequired)
        { update.Require(); SetState(owner, ServerConnectionState.Incompatible); return false; }
        catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.NotFound)
        { liveEnabled = false; return true; }
        finally { lock (sync) { if (ReferenceEquals(activeHub, hub)) activeHub = null; } }
    }
    public async Task<LiveReply> Invoke(LiveCommand command, CancellationToken token)
    {
        HubConnection hub;
        lock (sync) hub = cancellation is { IsCancellationRequested: false } && state is ServerConnectionState.Connected or ServerConnectionState.Syncing
            && activeHub is { State: HubConnectionState.Connected } current ? current : throw new HttpRequestException("The live connection is unavailable.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(15));
        LiveReply reply;
        try { reply = await hub.InvokeCoreAsync<LiveReply>("Execute", [command], deadline.Token).ConfigureAwait(false); }
        catch (Exception error) when (error is HubException or InvalidOperationException or System.IO.IOException)
        { throw new HttpRequestException("The live action could not be confirmed.", error); }
        if (reply.Error is { } failure)
        {
            if (failure.Status == 426) update.Require(failure.Message);
            throw new ApiException(failure.Status, failure.Message, failure.Code, failure.RetryAfterSeconds);
        }
        return reply;
    }
    internal static LiveCommand? Command(HttpMethod method, string path, IClientRequest? body)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (method == HttpMethod.Get && path == "/api/pvp/status") return new("pvp.status");
        if (parts.Length >= 3 && parts[0] == "api")
        {
            var pvp = parts[1] == "pvp" && parts.Length >= 4 && parts[2] == "battles";
            var battle = parts[1] == "battles";
            var raid = parts[1] == "raids";
            var at = pvp ? 3 : 2;
            if (!(pvp || battle || raid) || !Guid.TryParseExact(parts[at], "D", out _)) return null;
            var kind = pvp ? "pvp" : raid ? "raid" : "battle";
            if (method == HttpMethod.Get && parts.Length == at + 1) return new(kind + ".get", parts[at]);
            if (method == HttpMethod.Post && parts.Length == at + 2)
            {
                if (parts[at + 1] == "choose" && body is BattleChoiceRequest choice && !raid) return new(kind + ".choose", parts[at], choice);
                if (parts[at + 1] == "choose" && body is RaidChooseRequest turn && raid) return new("raid.choose", parts[at], RaidChoice: turn);
                if (parts[at + 1] == "forfeit" && pvp && body is EmptyRequest) return new("pvp.forfeit", parts[at]);
            }
        }
        return null;
    }
}
