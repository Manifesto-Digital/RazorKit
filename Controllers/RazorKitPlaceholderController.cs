using System.Text;
using Manifesto.RazorKit.Filters;
using Manifesto.RazorKit.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Manifesto.RazorKit.Controllers;

/// <summary>
/// Serves generated SVG placeholder images from the host's own origin, so a strict
/// <c>img-src 'self'</c> policy covers them. Kept apart from <see cref="RazorKitController"/>
/// because that controller's class-level filters would send no-store and take a render slot,
/// and an image is neither a render nor uncacheable.
/// It carries the concurrency filter only as a backstop; see <c>RazorKitRequestKind.TakesRenderSlot</c>.
/// </summary>
[Route(RazorKitPlaceholder.RoutePrefix)]
[IgnoreAntiforgeryToken]
[ResponseHeaders(ImmutableOnSuccess = true)]
[RateLimit]
[RequestProtection]
[ConcurrencyLimit]
public sealed class RazorKitPlaceholderController : Controller
{
    private const string SvgContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'";

    // Declared explicitly: HttpGet alone does not match HEAD, and a probe or link checker sends it.
    // Any other method gets the routing 405 before a filter runs, so it has no RazorKit headers.
    [AcceptVerbs("GET", "HEAD")]
    [Route("{size}")]
    public IActionResult Image(
        string size,
        [FromQuery(Name = RazorKitPlaceholder.LabelQueryKey)] string? label,
        [FromQuery(Name = RazorKitPlaceholder.BackgroundQueryKey)] string? background,
        [FromQuery(Name = RazorKitPlaceholder.ForegroundQueryKey)] string? foreground)
    {
        if (!RazorKitPlaceholder.TryParseSize(size, out var width, out var height))
        {
            return NotFound();
        }

        // Rejected rather than repaired: a hand-typed URL with a stray character should say so
        // instead of silently showing something else. The helper strips, so it never triggers this.
        var checkedLabel = RazorKitPlaceholder.ValidateLabel(label, out var labelIsValid);
        if (!labelIsValid)
        {
            return BadRequest();
        }

        var svg = RazorKitPlaceholder.RenderSvg(width, height, checkedLabel, background, foreground);

        // Only this path opts in to caching, so a host page that re-executes an error as a 200
        // still gets no-store.
        HttpContext.Items[ResponseHeadersFilter.CacheableItemKey] = true;
        Response.Headers.XContentTypeOptions = "nosniff";
        // A second policy beside RazorKit's: the image opened directly can run nothing.
        Response.Headers.Append("Content-Security-Policy", SvgContentSecurityPolicy);
        return Content(svg, "image/svg+xml", Encoding.UTF8);
    }
}
