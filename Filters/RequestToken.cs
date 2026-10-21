using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;

namespace Manifesto.RazorKit.Filters;

/// <summary>
/// RazorKit's own double-submit token. The explorer page issues it as a cookie and as a value the
/// page script posts back; <see cref="RequestProtectionFilter"/> requires the two to match.
/// The endpoints are anonymous, so the token only shows that this browser loaded a RazorKit page
/// from this origin. It is deliberately not tied to the host's antiforgery or data protection,
/// so RazorKit works whatever the host has configured for those.
/// </summary>
internal static class RequestToken
{
    // The __Host- prefix makes browsers refuse the cookie unless it is Secure, host-only and
    // Path=/, so a sibling subdomain or a plain-HTTP response cannot plant a value.
    public const string CookieName = "__Host-RazorKit-AF";

    public const string FormField = "__RazorKitToken";

    // Long enough that a page left open all day keeps working; the lifetime slides on every page load.
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(8);

    private const int ByteLength = 32;

    // Unpadded base64url length of 32 bytes.
    private const int EncodedLength = 43;

    /// <summary>
    /// Reuses a well-formed cookie value, so a second tab does not invalidate the first, or mints
    /// a new one; re-sends the cookie either way to refresh its lifetime. Returns the value for the page.
    /// </summary>
    public static string Issue(HttpContext context)
    {
        var value = FindCookie(context.Request, out var existing) == 1 && TryDecode(existing, out _)
            ? existing
            : WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(ByteLength));

        context.Response.Cookies.Append(CookieName, value, new CookieOptions
        {
            // Always Secure, whatever Request.IsHttps says: behind TLS offload the origin hop is
            // plain HTTP while the browser is on HTTPS, and the browser is what judges Secure.
            Secure = true,
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            MaxAge = Lifetime,
            // Without this a host's consent-based cookie policy would drop the cookie, and every
            // render POST would then be rejected.
            IsEssential = true
        });

        return value;
    }

    /// <summary>
    /// Counts the cookies named exactly <see cref="CookieName"/> in the raw Cookie header(s), and
    /// returns the value when there is exactly one. Request.Cookies is not used because it matches
    /// names case-insensitively and keeps only the last duplicate, so a cookie with a differently
    /// cased name, which the browser does not hold to the __Host- rules, could stand in for this one.
    /// </summary>
    public static int FindCookie(HttpRequest request, out string? value)
    {
        value = null;
        var count = 0;
        foreach (var header in request.Headers.Cookie)
        {
            if (header is null)
            {
                continue;
            }

            foreach (var part in header.Split(';'))
            {
                var separator = part.IndexOf('=');
                if (separator < 0 || !part.AsSpan(0, separator).Trim().SequenceEqual(CookieName))
                {
                    continue;
                }

                count++;
                value = part[(separator + 1)..].Trim();
            }
        }

        if (count != 1)
        {
            value = null;
        }

        return count;
    }

    /// <summary>True when both values are well formed and equal, compared in constant time.</summary>
    public static bool Matches(string? cookieValue, string? formValue) =>
        TryDecode(cookieValue, out var cookieBytes) &&
        TryDecode(formValue, out var formBytes) &&
        CryptographicOperations.FixedTimeEquals(cookieBytes, formBytes);

    private static bool TryDecode(string? value, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();

        // Checked by hand first because the decoder also accepts '+', '/' and whitespace.
        if (value is null || value.Length != EncodedLength || !value.All(IsBase64UrlChar))
        {
            return false;
        }

        try
        {
            bytes = WebEncoders.Base64UrlDecode(value);
        }
        catch (FormatException)
        {
            return false;
        }

        return bytes.Length == ByteLength;
    }

    private static bool IsBase64UrlChar(char c) =>
        c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_';
}
