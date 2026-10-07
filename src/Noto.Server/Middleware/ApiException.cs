namespace Noto.Server.Middleware;

// Surfaces as an RFC 9457 problem document with a stable machine-readable `code`.
public sealed class ApiException(int status, string code, string title, string? detail = null)
    : Exception(title)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public string Title { get; } = title;
    public string? Detail { get; } = detail;

    public static ApiException BadRequest(string code, string title, string? detail = null) =>
        new(400, code, title, detail);

    public static ApiException Unauthorized(string code, string title) => new(401, code, title);

    public static ApiException Forbidden(string code, string title, string? detail = null) =>
        new(403, code, title, detail);

    public static ApiException NotFound(string code, string title) => new(404, code, title);
}
