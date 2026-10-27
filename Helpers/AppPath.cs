using Microsoft.AspNetCore.Http;

namespace Manifesto.RazorKit.Helpers;

/// <summary>
/// App-relative paths: the one place RazorKit turns "/x" or "~/x" into a URL under the host's
/// path base, and the one rule for which configured paths are acceptable.
/// </summary>
internal static class AppPath
{
    private static readonly char[] AttributeBreakers = { '"', '\'', '<', '>' };
    private static readonly char[] PathEnd = { '?', '#' };

    /// <summary>
    /// "{PathBase}/x" for "/x" or "~/x". Both forms mean the same thing: a path inside the
    /// application, so a site mounted under a sub-path finds it. Only call with a path that
    /// passed <see cref="Problem"/> or a fixed RazorKit route.
    /// </summary>
    public static string Resolve(PathString pathBase, string appRelativePath)
    {
        var path = appRelativePath.StartsWith("~/", StringComparison.Ordinal) ? appRelativePath[1..] : appRelativePath;

        // ToUriComponent escapes the base as a URL path; the configured path is written as given.
        // The base can come from a forwarded header the host copied in unchecked, and "//evil.com"
        // would make every URL below it scheme-relative (off-origin), so leading slashes collapse
        // to one. The base is still best taken from trusted configuration; this is the backstop.
        var escapedBase = pathBase.ToUriComponent();
        if (escapedBase.StartsWith("//", StringComparison.Ordinal))
        {
            escapedBase = "/" + escapedBase.TrimStart('/');
        }

        // A base of only slashes is no base at all.
        return (escapedBase == "/" ? string.Empty : escapedBase) + path;
    }

    /// <summary>
    /// Why <paramref name="path"/> is not a usable app-relative path, or null when it is. Only
    /// "/x" and "~/x" are accepted. Each other rule closes a way a path written into an href or src
    /// could leave the application or the attribute.
    /// </summary>
    public static string? Problem(string? path)
    {
        if (path is null)
        {
            return "is null.";
        }

        var rooted = path.StartsWith("~/", StringComparison.Ordinal) ? path[1..] : path;
        if (!rooted.StartsWith('/'))
        {
            return "must be an app-relative path starting with / or ~/, such as /css/main.css.";
        }

        // Browsers strip tabs and newlines from URLs, so "/\t/host" would become "//host".
        if (path.Any(char.IsControl))
        {
            return "must not contain control characters.";
        }

        // Browsers read a backslash as a slash, so "/\host" is scheme-relative too.
        if (path.Contains('\\'))
        {
            return "must not contain a backslash.";
        }

        // "//host/x" (or "~//host/x") is a URL on another host, not a path in this application.
        if (rooted.StartsWith("//", StringComparison.Ordinal))
        {
            return "must not start with // (a scheme-relative URL on another host).";
        }

        // Encoding on output already keeps these inside the attribute; rejecting them as well
        // means a path never depends on that alone.
        if (path.IndexOfAny(AttributeBreakers) >= 0)
        {
            return "must not contain quotes, < or >.";
        }

        // A ".." segment, plain or percent-encoded (browsers normalise %2e%2e to ..), would climb
        // out of the path base.
        var end = rooted.IndexOfAny(PathEnd);
        var pathPart = end < 0 ? rooted : rooted[..end];
        foreach (var segment in pathPart.Split('/'))
        {
            if (segment.Replace("%2e", ".", StringComparison.OrdinalIgnoreCase) == "..")
            {
                return "must not contain a .. segment.";
            }
        }

        return null;
    }
}
