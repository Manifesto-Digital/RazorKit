using Microsoft.AspNetCore.Http;

namespace Manifesto.RazorKit.Filters;

/// <summary>
/// The origins a request without Sec-Fetch-Site may come from: the configured AllowedOrigins, or
/// when none are configured, the origin the request itself was addressed to.
/// </summary>
internal sealed class AllowedOriginList
{
    private readonly Origin[] _configured;

    private AllowedOriginList(Origin[] configured)
    {
        _configured = configured;
    }

    public bool IsEmpty => _configured.Length == 0;

    /// <summary>Parses the configured origins, throwing for any entry that is not a plain http(s) origin.</summary>
    public static AllowedOriginList Create(IEnumerable<string> configured)
    {
        var origins = new List<Origin>();
        foreach (var entry in configured)
        {
            // Includes "null" and "*": an allowlist entry that matches opaque or every origin
            // would turn the check off.
            if (!TryParse(entry, out var origin))
            {
                throw new ArgumentException(
                    $"RazorKit AllowedOrigins entry '{entry}' is not an origin. Use scheme://host[:port], for example https://www.example.com.",
                    nameof(configured));
            }

            origins.Add(origin);
        }

        return new AllowedOriginList(origins.ToArray());
    }

    public bool Allows(string originHeader, HttpRequest request)
    {
        if (!TryParse(originHeader, out var origin))
        {
            return false;
        }

        if (!IsEmpty)
        {
            return _configured.Contains(origin);
        }

        // Behind TLS offload without forwarded headers this derives http://, which then never
        // matches the browser's https:// origin. That fails closed; AllowedOrigins is the fix.
        return request.Host.HasValue
            && TryParse($"{request.Scheme}://{request.Host.Value}", out var derived)
            && derived == origin;
    }

    private static bool TryParse(string? value, out Origin origin)
    {
        origin = default;
        if (string.IsNullOrEmpty(value)
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || uri.UserInfo.Length != 0
            || uri.AbsolutePath != "/"
            || uri.Query.Length != 0
            || uri.Fragment.Length != 0
            // Uri accepts a trailing '?' or '#' and reports them as empty.
            || value.IndexOfAny(new[] { '?', '#' }) >= 0)
        {
            return false;
        }

        // IdnHost so a Unicode host and its punycode form compare equal; Port is the effective
        // port, so http://host and http://host:80 are the same origin.
        origin = new Origin(uri.Scheme, uri.IdnHost.ToLowerInvariant(), uri.Port);
        return true;
    }

    private readonly record struct Origin(string Scheme, string Host, int Port);
}
