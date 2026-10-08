using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Noto.Sync;

public sealed record ServerDevice(Guid Id, string Name, string Platform);

public sealed record ServerSession(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt
);

// `Code` is the server's error code (INVALID_CREDENTIALS, EMAIL_TAKEN, ...), or NETWORK when the server is unreachable.
public sealed class ServerAuthException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

// What the desktop app needs from a sync server's /v1/auth endpoints.
public interface IServerAuth
{
    Task<ServerSession> SignUpAsync(
        Uri server,
        string email,
        string password,
        string? inviteCode,
        ServerDevice device,
        CancellationToken ct
    );
    Task<ServerSession> SignInAsync(
        Uri server,
        string email,
        string password,
        ServerDevice device,
        CancellationToken ct
    );
    Task<ServerSession> RefreshAsync(Uri server, string refreshToken, CancellationToken ct);
    Task SignOutAsync(Uri server, string accessToken, CancellationToken ct);
}

// Stateless: the caller passes the server URL and keeps the tokens (docs/06 › Auth).
public sealed class ServerAuthClient(HttpClient http) : IServerAuth
{
    public async Task<ServerSession> SignUpAsync(
        Uri server,
        string email,
        string password,
        string? inviteCode,
        ServerDevice device,
        CancellationToken ct
    ) =>
        Session(
            await PostAsync<TokensWire>(
                server,
                "/v1/auth/register",
                new
                {
                    email,
                    password,
                    device,
                    invite_code = string.IsNullOrWhiteSpace(inviteCode) ? null : inviteCode,
                },
                ct
            )
        );

    public async Task<ServerSession> SignInAsync(
        Uri server,
        string email,
        string password,
        ServerDevice device,
        CancellationToken ct
    ) =>
        Session(
            await PostAsync<TokensWire>(
                server,
                "/v1/auth/login",
                new
                {
                    email,
                    password,
                    device,
                },
                ct
            )
        );

    public async Task<ServerSession> RefreshAsync(
        Uri server,
        string refreshToken,
        CancellationToken ct
    ) =>
        Session(
            await PostAsync<TokensWire>(
                server,
                "/v1/auth/refresh",
                new { refresh_token = refreshToken },
                ct
            )
        );

    public async Task SignOutAsync(Uri server, string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(server, "/v1/auth/logout")
        );
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await Send(request, ct);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.Unauthorized)
            throw await ErrorAsync(response);
    }

    async Task<T> PostAsync<T>(Uri server, string path, object body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(server, path))
        {
            Content = JsonContent.Create(body),
        };
        using var response = await Send(request, ct);
        if (!response.IsSuccessStatusCode)
            throw await ErrorAsync(response);
        return await response.Content.ReadFromJsonAsync<T>(ct)
            ?? throw new ServerAuthException("BAD_RESPONSE", "The server sent an empty response");
    }

    async Task<HttpResponseMessage> Send(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            return await http.SendAsync(request, ct);
        }
        catch (HttpRequestException)
        {
            throw new ServerAuthException("NETWORK", "Can't reach the server");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ServerAuthException("NETWORK", "The server didn't respond in time");
        }
    }

    // Problem details carry the code as an extension member: { "code": "EMAIL_TAKEN", "title": "...", ... }.
    static async Task<ServerAuthException> ErrorAsync(HttpResponseMessage response)
    {
        string code = $"HTTP_{(int)response.StatusCode}";
        string message = response.ReasonPhrase ?? "Request failed";
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            if (problem.TryGetProperty("code", out var c) && c.GetString() is { } s)
                code = s;
            if (problem.TryGetProperty("title", out var t) && t.GetString() is { } title)
                message = title;
        }
        catch (JsonException) { }
        return new ServerAuthException(code, message);
    }

    static ServerSession Session(TokensWire w) => new(w.AccessToken, w.RefreshToken, w.ExpiresAt);

    sealed record TokensWire(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string RefreshToken,
        [property: JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt
    );
}
