using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Noto.Sync;

// ISyncTransport over the REST API (docs/06). The caller supplies the access token; on a 401 it is asked
// to refresh once (`refreshAsync` returns false when the session is gone) and the request is retried.
public sealed class HttpSyncTransport(
    HttpClient http, Func<CancellationToken, Task<string?>> accessToken, Func<CancellationToken, Task<bool>>? refreshAsync = null)
    : ISyncTransport
{
    public Task<SyncResponse> SyncAsync(SyncRequest request, CancellationToken ct = default) =>
        SendAsync<SyncResponse>(() => Json(HttpMethod.Post, "/v1/sync", request, ProtocolJson.Default.SyncRequest), ProtocolJson.Default.SyncResponse, ct);

    public Task<SnapshotPage> SnapshotAsync(Guid workspaceId, string entityType, string? after, int limit, CancellationToken ct = default)
    {
        var url = $"/v1/sync/snapshot?workspace_id={workspaceId}&entity_type={Uri.EscapeDataString(entityType)}&limit={limit}"
                  + (after is null ? "" : $"&after={Uri.EscapeDataString(after)}");
        return SnapshotOrEmptyAsync(() => SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), ProtocolJson.Default.SnapshotPage, ct), entityType);
    }

    // A workspace the server has never seen has an empty snapshot (first enable on the first device).
    static async Task<SnapshotPage> SnapshotOrEmptyAsync(Func<Task<SnapshotPage>> fetch, string entityType)
    {
        try { return await fetch(); }
        catch (NotFoundException) { return new SnapshotPage(0, entityType, [], null); }
    }

    public async Task<IReadOnlyList<Guid>> ListWorkspacesAsync(CancellationToken ct = default) =>
        (await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, "/v1/sync/workspaces"), ProtocolJson.Default.ServerWorkspaces, ct)).Workspaces;

    public async Task DeleteWorkspaceAsync(Guid workspaceId, CancellationToken ct = default) =>
        await SendAsync<object?>(() => new HttpRequestMessage(HttpMethod.Delete, $"/v1/sync/workspaces/{workspaceId}"), null, ct);

    sealed class NotFoundException : Exception;

    static HttpRequestMessage Json<T>(HttpMethod method, string url, T body, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info) =>
        new(method, url) { Content = JsonContent.Create(body, info) };

    async Task<T> SendAsync<T>(Func<HttpRequestMessage> build, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>? info, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = build();
            if (await accessToken(ct) is { } token) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            HttpResponseMessage response;
            try { response = await http.SendAsync(request, ct); }
            catch (HttpRequestException e) { throw new SyncTransportException("Network error", e); }
            catch (TaskCanceledException e) when (!ct.IsCancellationRequested) { throw new SyncTransportException("Timed out", e); }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0 && refreshAsync is not null && await refreshAsync(ct))
                    continue;
                if (response.StatusCode == HttpStatusCode.Conflict) throw new CursorAheadException();
                if (response.StatusCode == HttpStatusCode.NotFound) throw new NotFoundException();
                if (!response.IsSuccessStatusCode) throw new SyncTransportException($"Server returned {(int)response.StatusCode}");
                if (info is null) return default!;
                return (await response.Content.ReadFromJsonAsync(info, ct))!;
            }
        }
    }
}
