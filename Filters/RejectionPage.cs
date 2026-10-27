using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Mvc;

namespace Manifesto.RazorKit.Filters;

/// <summary>
/// The bodies of the rejections RazorKit answers itself (403, 405, 413, 415, 429 and 503): a small
/// HTML page, so a blocked preview reads as a message inside the iframe instead of raw text.
/// </summary>
/// <remarks>
/// Every page is built once from static strings. The only variable is the reject code, and a page
/// exists only for the fixed codes in <see cref="RequestProtectionFilter.RejectCodes"/>: nothing a
/// client sent can reach a body. The pages carry no script, so a host CSP never has to allow one.
/// Status codes and headers are set by the filters and do not depend on this class.
/// </remarks>
internal static class RejectionPage
{
    internal const string ContentType = "text/html; charset=utf-8";

    internal static readonly string MethodNotAllowed =
        Render("Method not allowed", "This address only answers GET and HEAD requests.");

    internal static readonly string TooLarge =
        Render("Request too large", "The props are larger than this site accepts. Shorten them and the preview will update.");

    internal static readonly string UnsupportedMediaType =
        Render("Unsupported request format", "Previews are only rendered from RazorKit's own Controls panel. Reload the page.");

    internal static readonly string TooManyRequests =
        Render("Too many requests", "Try again in a moment.");

    internal static readonly string Busy =
        Render("RazorKit is busy", "Too many previews are rendering right now. Try again shortly.");

    private static readonly string ForbiddenGeneric =
        Render("Preview blocked", "Reload the page, then try again.");

    private static readonly Dictionary<string, string> ForbiddenByCode = new[]
    {
        RequestProtectionFilter.RejectCodes.FetchSite,
        RequestProtectionFilter.RejectCodes.NoProvenance,
        RequestProtectionFilter.RejectCodes.OriginNull,
        RequestProtectionFilter.RejectCodes.OriginMismatch,
        RequestProtectionFilter.RejectCodes.TokenCookieMissing,
        RequestProtectionFilter.RejectCodes.TokenMismatch
    }.ToDictionary(code => code, code => Render($"Preview blocked ({code})", "Reload the page, then try again."), StringComparer.Ordinal);

    /// <summary>The 403 page for a reject code. A code RazorKit does not define gets the generic page, never an echo.</summary>
    internal static string Forbidden(string rejectCode) =>
        ForbiddenByCode.TryGetValue(rejectCode, out var page) ? page : ForbiddenGeneric;

    internal static ContentResult Result(int statusCode, string page) => new()
    {
        StatusCode = statusCode,
        Content = page,
        ContentType = ContentType
    };

    // No script, so a host CSP never needs an exception for it; if a host CSP blocks the style
    // element, the page is still readable unstyled.
    private static string Render(string headline, string detail)
    {
        var encodedHeadline = HtmlEncoder.Default.Encode(headline);
        var encodedDetail = HtmlEncoder.Default.Encode(detail);
        return $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{{encodedHeadline}}</title>
            <style>body{margin:0;padding:1.5rem;font:1rem/1.5 system-ui,-apple-system,"Segoe UI",Roboto,sans-serif;color:#1f2937;background:#fff}h1{margin:0 0 .5rem;font-size:1.125rem}p{margin:0;color:#4b5563}</style>
            </head>
            <body>
            <main>
            <h1>{{encodedHeadline}}</h1>
            <p>{{encodedDetail}}</p>
            </main>
            </body>
            </html>
            """;
    }
}
