using Microsoft.AspNetCore.WebUtilities;
using Noto.Server.Middleware;

namespace Noto.Server.Gateway;

// Where a desktop sign-in sends the browser back to. Only a loopback callback is accepted, so the one-time code
// can reach the app that started the flow and nothing else. Any other address is a redirect to a third party.
public static class LoopbackReturn
{
    public static string? Validate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        if (
            !Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttp
            || uri.Host is not ("127.0.0.1" or "localhost")
            || uri.IsDefaultPort // the app always names its port, so an implicit 80 is a mistake
            || uri.AbsolutePath != "/callback"
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !string.IsNullOrEmpty(uri.UserInfo)
        )
            throw ApiException.BadRequest(
                "INVALID_RETURN_TO",
                "return_to must be a http://127.0.0.1:<port>/callback address"
            );
        return uri.GetLeftPart(UriPartial.Path);
    }

    public static string Build(OAuthCompletion result) =>
        result.OneTimeCode is { } code
            ? QueryHelpers.AddQueryString(
                result.ReturnTo!,
                new Dictionary<string, string?> { ["code"] = code, ["state"] = result.State }
            )
            : QueryHelpers.AddQueryString(
                result.ReturnTo!,
                new Dictionary<string, string?>
                {
                    ["error"] = result.ErrorCode,
                    ["state"] = result.State,
                }
            );
}
