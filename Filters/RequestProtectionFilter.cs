using System.Text;
using Manifesto.RazorKit.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Manifesto.RazorKit.Filters;

/// <summary>
/// Protects every RazorKit request except GET and HEAD, before model binding runs:
/// 1. the request must come from this site (Sec-Fetch-Site, or Origin when that is absent);
/// 2. the body must be a URL-encoded form within the size limits, read here under those limits;
/// 3. the RazorKit token cookie and form field must match.
/// Rejections carry a static body and never echo any part of the request.
/// </summary>
internal sealed partial class RequestProtectionFilter : IAsyncResourceFilter
{
    internal const string RejectHeader = "X-RazorKit-Reject";
    internal const string PropsJsonField = "propsJson";

    private const string RejectedBody = "Request rejected.";
    private const string TooLargeBody = "Request too large.";
    private const string UnsupportedMediaTypeBody = "Unsupported media type.";

    // RazorKit's client posts two fields. The rest is headroom, not an invitation.
    private const int MaxFormValues = 8;
    private const int MaxFormKeyLength = 64;

    // Room for the token field, the field names and separators on top of the encoded props.
    private const int FormOverheadBytes = 1024;

    // Percent-encoding at most triples a byte, and browsers encode most JSON punctuation.
    private const int PercentEncodingFactor = 3;

    private readonly AllowedOriginList _allowedOrigins;
    private readonly ILogger<RequestProtectionFilter> _logger;
    private readonly int _maxPropsJsonBytes;
    private readonly int _maxEncodedValueLength;
    private readonly long _maxBodyBytes;

    public RequestProtectionFilter(RazorKitOptions options, AllowedOriginList allowedOrigins, ILogger<RequestProtectionFilter> logger)
    {
        _allowedOrigins = allowedOrigins;
        _logger = logger;
        _maxPropsJsonBytes = options.MaxPropsJsonBytes;
        _maxEncodedValueLength = options.MaxPropsJsonBytes * PercentEncodingFactor;
        _maxBodyBytes = (long)_maxEncodedValueLength + FormOverheadBytes;
    }

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var request = context.HttpContext.Request;

        // The request method alone is not trusted: a host's method-override middleware can turn a
        // POST into a GET after routing chose the POST action, which still binds the form.
        if ((HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
            && !DeclaredMethods.DeclaresUnsafeMethod(context.ActionDescriptor))
        {
            await next();
            return;
        }

        var provenanceFailure = CheckProvenance(request);
        if (provenanceFailure is not null)
        {
            Reject(context, provenanceFailure);
            return;
        }

        if (request.ContentLength > _maxBodyBytes)
        {
            RejectBody(context, StatusCodes.Status413PayloadTooLarge, TooLargeBody);
            return;
        }

        if (!IsUrlEncodedForm(request.ContentType))
        {
            RejectBody(context, StatusCodes.Status415UnsupportedMediaType, UnsupportedMediaTypeBody);
            return;
        }

        var form = await ReadFormWithinLimitsAsync(context.HttpContext);
        if (form is null)
        {
            RejectBody(context, StatusCodes.Status413PayloadTooLarge, TooLargeBody);
            return;
        }

        var tokenFailure = CheckToken(request, form);
        if (tokenFailure is not null)
        {
            Reject(context, tokenFailure);
            return;
        }

        await next();
    }

    private string? CheckProvenance(HttpRequest request)
    {
        // Sec-Fetch-Site is set by the browser and cannot be changed by page script, so when it
        // is present it decides alone. same-site is refused: a sibling subdomain is not trusted.
        var fetchSite = request.Headers["Sec-Fetch-Site"];
        if (fetchSite.Count > 0)
        {
            return fetchSite.Count == 1 && string.Equals(fetchSite[0], "same-origin", StringComparison.Ordinal)
                ? null
                : RejectCodes.FetchSite;
        }

        var origin = request.Headers.Origin;
        if (origin.Count == 0)
        {
            return RejectCodes.NoProvenance;
        }

        if (origin.Count > 1)
        {
            return RejectCodes.OriginMismatch;
        }

        // Browsers send "null" for sandboxed frames and other opaque origins; it identifies no site.
        if (string.Equals(origin[0], "null", StringComparison.Ordinal))
        {
            return RejectCodes.OriginNull;
        }

        return _allowedOrigins.Allows(origin[0]!, request) ? null : RejectCodes.OriginMismatch;
    }

    private static bool IsUrlEncodedForm(string? contentType) =>
        MediaTypeHeaderValue.TryParse(contentType, out var mediaType)
        && mediaType.MediaType.Equals("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads the form under RazorKit's own limits, as the framework's request-size and form-limit
    /// filters would, which cannot be used here because their limits must be attribute constants.
    /// Returns null when any limit is exceeded.
    /// </summary>
    private async Task<IFormCollection?> ReadFormWithinLimitsAsync(HttpContext httpContext)
    {
        var request = httpContext.Request;

        var sizeFeature = httpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is { IsReadOnly: false })
        {
            sizeFeature.MaxRequestBodySize = _maxBodyBytes;
        }

        if (httpContext.Features.Get<IFormFeature>()?.Form is null)
        {
            // Servers without a body-size feature, and chunked bodies, are capped here as well.
            request.Body = new LengthLimitedStream(request.Body, _maxBodyBytes);
            httpContext.Features.Set<IFormFeature>(new FormFeature(request, new FormOptions
            {
                ValueCountLimit = MaxFormValues,
                KeyLengthLimit = MaxFormKeyLength,
                ValueLengthLimit = _maxEncodedValueLength
            }));
        }

        IFormCollection form;
        try
        {
            form = await request.ReadFormAsync(httpContext.RequestAborted);
        }
        catch (InvalidDataException)
        {
            // The form reader's count and length limits.
            return null;
        }
        catch (LengthLimitedStream.LimitExceededException)
        {
            return null;
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return null;
        }

        // Checked again on the result: a host middleware may have read the form first under
        // its own, looser limits.
        var valueCount = 0;
        foreach (var field in form)
        {
            valueCount += field.Value.Count;
        }

        if (valueCount > MaxFormValues)
        {
            return null;
        }

        // The configured cap is in UTF-8 bytes of the decoded JSON, which is what gets parsed.
        foreach (var props in form[PropsJsonField])
        {
            if (props is not null && Encoding.UTF8.GetByteCount(props) > _maxPropsJsonBytes)
            {
                return null;
            }
        }

        return form;
    }

    private static string? CheckToken(HttpRequest request, IFormCollection form)
    {
        var cookieCount = RequestToken.FindCookie(request, out var cookie);
        if (cookieCount == 0)
        {
            return RejectCodes.TokenCookieMissing;
        }

        // More than one cookie with the exact name means something other than RazorKit set one,
        // and there is no way to tell which is genuine.
        if (cookieCount > 1)
        {
            return RejectCodes.TokenMismatch;
        }

        StringValues field = form[RequestToken.FormField];
        return field.Count == 1 && RequestToken.Matches(cookie, field[0])
            ? null
            : RejectCodes.TokenMismatch;
    }

    private void Reject(ResourceExecutingContext context, string code)
    {
        // A route template, never the request path: nothing the client sent reaches the log.
        var route = context.ActionDescriptor.AttributeRouteInfo?.Template ?? context.ActionDescriptor.DisplayName;
        if (code == RejectCodes.TokenCookieMissing)
        {
            LogTokenCookieMissing(_logger, route);
        }
        else
        {
            LogRejected(_logger, code, route);
        }

        context.HttpContext.Response.Headers[RejectHeader] = code;
        context.Result = StaticResult(StatusCodes.Status403Forbidden, RejectedBody);
    }

    private void RejectBody(ResourceExecutingContext context, int statusCode, string body)
    {
        var route = context.ActionDescriptor.AttributeRouteInfo?.Template ?? context.ActionDescriptor.DisplayName;
        LogBodyRejected(_logger, statusCode, route);
        context.Result = StaticResult(statusCode, body);
    }

    private static ContentResult StaticResult(int statusCode, string body) => new()
    {
        StatusCode = statusCode,
        Content = body,
        ContentType = "text/plain; charset=utf-8"
    };

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "RazorKit rejected a request ({RejectCode}) on {Route}.")]
    private static partial void LogRejected(ILogger logger, string rejectCode, string? route);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
        Message = "RazorKit rejected a request (token-cookie-missing) on {Route}. The RazorKit token cookie was not sent. " +
                  "Browsers only store it from a page served over HTTPS or from localhost, and only RazorKit's own explorer page issues it: " +
                  "check the site is not served over plain HTTP and that no host page replaces Pages/RazorKit.cshtml.")]
    private static partial void LogTokenCookieMissing(ILogger logger, string? route);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning,
        Message = "RazorKit rejected a request body ({StatusCode}) on {Route}.")]
    private static partial void LogBodyRejected(ILogger logger, int statusCode, string? route);

    /// <summary>The reason codes sent in <see cref="RejectHeader"/>. Coarse on purpose: enough to diagnose, nothing to probe with.</summary>
    internal static class RejectCodes
    {
        public const string FetchSite = "fetch-site";
        public const string NoProvenance = "no-provenance";
        public const string OriginNull = "origin-null";
        public const string OriginMismatch = "origin-mismatch";
        public const string TokenCookieMissing = "token-cookie-missing";
        public const string TokenMismatch = "token-mismatch";
    }
}
