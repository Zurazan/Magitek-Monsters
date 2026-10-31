using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MagitekMonsters.Models;

namespace MagitekMonsters.Services;

public sealed class ClientUpdateState
{
    public const string DefaultMessage = "Please update Magitek Monsters to the latest version in Dalamud's Plugin Installer, then reload the plugin.";
    private string? message;
    public bool Required => Volatile.Read(ref message) is not null;
    public string Message => Volatile.Read(ref message) ?? DefaultMessage;
    public void Require(string? detail = null) => Interlocked.CompareExchange(ref message, string.IsNullOrWhiteSpace(detail) ? DefaultMessage : detail, null);
    public async Task<bool> Observe(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var mismatch = response.Headers.TryGetValues(ProtocolInfo.ProtocolHeader, out var protocols)
            && string.Join(string.Empty, protocols) != ProtocolInfo.Version;
        if (response.StatusCode != HttpStatusCode.UpgradeRequired && !mismatch) return false;
        Require();
        if (response.StatusCode == HttpStatusCode.UpgradeRequired)
        {
            try
            {
                var error = await response.Content.ReadFromJsonAsync<ApiErrorDto>(cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(error?.Message)) Interlocked.CompareExchange(ref message, error.Message, DefaultMessage);
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException or HttpRequestException) { }
        }
        return true;
    }
}
