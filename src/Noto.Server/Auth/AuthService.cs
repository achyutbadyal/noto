using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Noto.Server.Data;
using Noto.Server.Middleware;

namespace Noto.Server.Auth;

public sealed record DeviceInfo(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("platform")] string? Platform
);

public sealed record AuthRequest(
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("password")] string? Password,
    [property: JsonPropertyName("device")] DeviceInfo? Device
);

public sealed record TokenPair(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt
);

public sealed record RegisterResponse(
    [property: JsonPropertyName("user_id")] Guid UserId,
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt
);

public sealed class AuthService(
    ServerDbContext db,
    Argon2PasswordHasher hasher,
    JwtService jwt,
    TimeProvider time
)
{
    static readonly TimeSpan RefreshLifetime = TimeSpan.FromDays(90);
    const int MinPasswordLength = 10;

    public async Task<RegisterResponse> RegisterAsync(AuthRequest req, CancellationToken ct)
    {
        var (email, password, device) = Validate(req);
        if (password.Length < MinPasswordLength)
            throw ApiException.BadRequest(
                "WEAK_PASSWORD",
                "Password too short",
                $"Use at least {MinPasswordLength} characters"
            );
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            throw new ApiException(409, "EMAIL_TAKEN", "Email already registered");

        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Email = email,
            PasswordHash = hasher.Hash(password),
            CreatedAt = Now(),
        };
        db.Users.Add(user);
        await UpsertDeviceAsync(user.Id, device, ct);
        var pair = await IssueAsync(user.Id, device.Id, ct);
        return new RegisterResponse(user.Id, pair.AccessToken, pair.RefreshToken, pair.ExpiresAt);
    }

    public async Task<TokenPair> LoginAsync(AuthRequest req, CancellationToken ct)
    {
        var (email, password, device) = Validate(req);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is null)
        {
            hasher.DummyVerify(password);
            throw ApiException.Unauthorized("INVALID_CREDENTIALS", "Invalid email or password");
        }
        if (!hasher.Verify(password, user.PasswordHash))
            throw ApiException.Unauthorized("INVALID_CREDENTIALS", "Invalid email or password");

        await UpsertDeviceAsync(user.Id, device, ct);
        return await IssueAsync(user.Id, device.Id, ct);
    }

    // Rotation: each refresh token is single use. Presenting a used one means it leaked, so the device is revoked.
    public async Task<TokenPair> RefreshAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(refreshToken))
            throw ApiException.Unauthorized("INVALID_REFRESH_TOKEN", "Invalid refresh token");
        var token =
            await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == Hash(refreshToken), ct)
            ?? throw ApiException.Unauthorized("INVALID_REFRESH_TOKEN", "Invalid refresh token");

        var now = Now();
        if (token.UsedAt is not null)
        {
            await RevokeDeviceAsync(token.UserId, token.DeviceId, ct);
            throw ApiException.Unauthorized(
                "REFRESH_TOKEN_REUSED",
                "Refresh token reuse detected; device revoked"
            );
        }
        if (token.RevokedAt is not null || token.ExpiresAt <= now)
            throw ApiException.Unauthorized("INVALID_REFRESH_TOKEN", "Invalid refresh token");

        var device = await db.Devices.FirstOrDefaultAsync(d => d.Id == token.DeviceId, ct);
        if (device is null || device.RevokedAt is not null)
            throw ApiException.Forbidden("DEVICE_REVOKED", "Device revoked");

        token.UsedAt = now;
        return await IssueAsync(token.UserId, token.DeviceId, ct);
    }

    public async Task LogoutAsync(Guid userId, Guid deviceId, CancellationToken ct)
    {
        var now = Now();
        await db
            .RefreshTokens.Where(t =>
                t.UserId == userId && t.DeviceId == deviceId && t.RevokedAt == null
            )
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
    }

    public async Task RevokeDeviceAsync(Guid userId, Guid deviceId, CancellationToken ct)
    {
        var now = Now();
        var device =
            await db.Devices.FirstOrDefaultAsync(d => d.Id == deviceId && d.UserId == userId, ct)
            ?? throw ApiException.NotFound("DEVICE_NOT_FOUND", "Device not found");
        device.RevokedAt ??= now;
        await db
            .RefreshTokens.Where(t => t.DeviceId == deviceId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
        await db.SaveChangesAsync(ct);
    }

    async Task UpsertDeviceAsync(Guid userId, DeviceInfo device, CancellationToken ct)
    {
        var existing = await db.Devices.FirstOrDefaultAsync(d => d.Id == device.Id, ct);
        if (existing is null)
        {
            db.Devices.Add(
                new Device
                {
                    Id = device.Id,
                    UserId = userId,
                    Name = device.Name ?? "Unnamed device",
                    Platform = device.Platform ?? "unknown",
                    CreatedAt = Now(),
                }
            );
            return;
        }
        if (existing.UserId != userId)
            throw new ApiException(409, "DEVICE_CONFLICT", "Device id belongs to another account");
        if (existing.RevokedAt is not null)
            throw ApiException.Forbidden("DEVICE_REVOKED", "Device revoked");
        existing.Name = device.Name ?? existing.Name;
        existing.Platform = device.Platform ?? existing.Platform;
    }

    async Task<TokenPair> IssueAsync(Guid userId, Guid deviceId, CancellationToken ct)
    {
        var (access, expires) = jwt.CreateAccessToken(userId, deviceId);
        var refresh = Base64Url(RandomNumberGenerator.GetBytes(32));
        db.RefreshTokens.Add(
            new RefreshToken
            {
                Id = Guid.CreateVersion7(),
                TokenHash = Hash(refresh),
                UserId = userId,
                DeviceId = deviceId,
                ExpiresAt = Now() + RefreshLifetime,
            }
        );
        await db.SaveChangesAsync(ct);
        return new TokenPair(access, refresh, expires);
    }

    static (string Email, string Password, DeviceInfo Device) Validate(AuthRequest req)
    {
        var email = req.Email?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(email) || !MailAddress.TryCreate(email, out _))
            throw ApiException.BadRequest("INVALID_EMAIL", "Invalid email");
        if (string.IsNullOrEmpty(req.Password))
            throw ApiException.BadRequest("INVALID_PASSWORD", "Password required");
        if (req.Device is null || req.Device.Id == Guid.Empty)
            throw ApiException.BadRequest("INVALID_DEVICE", "Device id required");
        return (email, req.Password, req.Device);
    }

    DateTime Now() => time.GetUtcNow().UtcDateTime;

    static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    static string Base64Url(byte[] b) =>
        Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
