using System.Globalization;
using System.Text;

namespace Manifesto.RazorKit.Models;

/// <summary>
/// Same-origin placeholder images for stories. A site with a strict <c>img-src</c> policy blocks
/// third-party placeholder services, but allows <c>'self'</c>, so the SVG RazorKit generates
/// itself is always loadable. Use <see cref="Url"/> wherever a story needs an image address.
/// </summary>
public static class RazorKitPlaceholder
{
    /// <summary>The smallest width or height the endpoint serves. Smaller values are raised to this.</summary>
    public const int MinSize = 16;

    /// <summary>The largest width or height the endpoint serves. Larger values are lowered to this.</summary>
    public const int MaxSize = 4000;

    /// <summary>The longest label the endpoint accepts, in characters.</summary>
    public const int MaxLabelLength = 40;

    // Lives under the controller prefix rather than "razorkit/...": that segment belongs to the
    // explorer page's catch-all route, where a component called "placeholder" would shadow it.
    internal const string RoutePrefix = "razorkit-preview/placeholder";

    internal const string LabelQueryKey = "label";
    internal const string BackgroundQueryKey = "bg";
    internal const string ForegroundQueryKey = "fg";

    private const string DefaultBackground = "d9d9d9";
    private const string DarkText = "1f1f1f";
    private const string LightText = "ffffff";

    /// <summary>
    /// Builds the root-relative URL of a placeholder image. The path carries no path base: a host
    /// mounted under a sub-path must prefix it with <c>Request.PathBase</c> (or its own
    /// <c>Url.Content</c>) before using it in markup.
    /// </summary>
    /// <param name="width">Width in pixels, clamped to <see cref="MinSize"/> to <see cref="MaxSize"/>.</param>
    /// <param name="height">Height in pixels, clamped to <see cref="MinSize"/> to <see cref="MaxSize"/>.</param>
    /// <param name="label">
    /// Text shown on the image. Letters, digits, spaces and <c>- _ . , : ( )</c> are kept; anything
    /// else is removed and the result is cut to <see cref="MaxLabelLength"/> characters. Left out,
    /// the image shows its size.
    /// </param>
    /// <param name="background">A 3 or 6 digit hex colour, with or without a leading <c>#</c>. An invalid value is ignored.</param>
    /// <param name="foreground">The text colour, in the same form. Left out, black or white is chosen for contrast.</param>
    /// <returns>A root-relative URL with every query value escaped.</returns>
    public static string Url(int width, int height, string? label = null, string? background = null, string? foreground = null)
    {
        var builder = new StringBuilder("/")
            .Append(RoutePrefix).Append('/')
            .Append(ClampSize(width).ToString(CultureInfo.InvariantCulture)).Append('x')
            .Append(ClampSize(height).ToString(CultureInfo.InvariantCulture));

        var separator = '?';
        void Add(string key, string? value)
        {
            if (value is null)
            {
                return;
            }

            builder.Append(separator).Append(key).Append('=').Append(Uri.EscapeDataString(value));
            separator = '&';
        }

        Add(LabelQueryKey, Strip(label));
        Add(BackgroundQueryKey, NormaliseColour(background));
        Add(ForegroundQueryKey, NormaliseColour(foreground));
        return builder.ToString();
    }

    internal static int ClampSize(int value) => Math.Clamp(value, MinSize, MaxSize);

    // Digits are matched by hand rather than int.TryParse: that accepts signs, whitespace and
    // culture-specific digits, and a 20 digit value would overflow instead of clamping.
    internal static bool TryParseSize(string? route, out int width, out int height)
    {
        width = height = 0;
        if (route is null)
        {
            return false;
        }

        var parts = route.Split('x', 2);
        return parts.Length == 2 && TryParseDimension(parts[0], out width) && TryParseDimension(parts[1], out height);
    }

    private static bool TryParseDimension(string text, out int value)
    {
        value = 0;
        if (text.Length is 0 or > 5 || !text.All(char.IsAsciiDigit))
        {
            return false;
        }

        value = ClampSize(int.Parse(text, CultureInfo.InvariantCulture));
        return true;
    }

    internal static bool IsAllowedLabelCharacter(char c) =>
        char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' or '.' or ',' or ':' or '(' or ')';

    /// <summary>Null means the label is absent or blank. A label that breaks the rules is reported through <paramref name="valid"/>.</summary>
    internal static string? ValidateLabel(string? label, out bool valid)
    {
        valid = true;
        var trimmed = label?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        valid = trimmed.Length <= MaxLabelLength && trimmed.All(IsAllowedLabelCharacter);
        return valid ? trimmed : null;
    }

    // The helper repairs a label where the endpoint rejects it: the helper's caller is a story
    // author who should get a picture, while a hand-typed URL should get told it is wrong.
    private static string? Strip(string? label)
    {
        if (label is null)
        {
            return null;
        }

        var kept = new string(label.Where(IsAllowedLabelCharacter).ToArray()).Trim();
        if (kept.Length > MaxLabelLength)
        {
            kept = kept[..MaxLabelLength].TrimEnd();
        }

        return kept.Length == 0 ? null : kept;
    }

    /// <summary>Returns six lower-case hex digits, or null when the value is not 3 or 6 hex digits.</summary>
    internal static string? NormaliseColour(string? value)
    {
        var hex = value?.Trim();
        if (hex is { Length: > 0 } && hex[0] == '#')
        {
            hex = hex[1..];
        }

        if (hex is null || hex.Length is not (3 or 6) || !hex.All(char.IsAsciiHexDigit))
        {
            return null;
        }

        hex = hex.ToLowerInvariant();
        return hex.Length == 3 ? string.Concat(hex.Select(c => new string(c, 2))) : hex;
    }

    /// <summary>
    /// Renders the image. Every value is encoded here as well as validated by the caller, so the
    /// output stays well-formed whatever reaches it. The result depends only on its arguments,
    /// which is what lets the endpoint send a year-long cache header.
    /// </summary>
    internal static string RenderSvg(int width, int height, string? label, string? background, string? foreground)
    {
        width = ClampSize(width);
        height = ClampSize(height);
        var raw = string.IsNullOrWhiteSpace(label)
            ? FormattableString.Invariant($"{width}×{height}")
            : label;
        var text = Escape(raw);
        var fill = NormaliseColour(background) ?? DefaultBackground;
        var textFill = NormaliseColour(foreground) ?? ReadableTextOn(fill);

        // Roughly half an em per character, so a long label shrinks to fit instead of overflowing.
        var length = Math.Max(1, raw.Length);
        var fontSize = Math.Clamp(Math.Min(height / 5.0, width * 1.6 / length), 6.0, 64.0);
        var size = fontSize.ToString("0.#", CultureInfo.InvariantCulture);

        return FormattableString.Invariant(
            $"""<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}" role="img" aria-label="{text}"><title>{text}</title><rect width="100%" height="100%" fill="#{fill}"/><text x="50%" y="50%" fill="#{textFill}" font-family="system-ui, sans-serif" font-size="{size}" text-anchor="middle" dominant-baseline="central">{text}</text></svg>""");
    }

    // WCAG relative luminance, so the default text stays readable on whichever background is chosen.
    private static string ReadableTextOn(string hex)
    {
        double Channel(int offset)
        {
            var c = int.Parse(hex.AsSpan(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        var luminance = 0.2126 * Channel(0) + 0.7152 * Channel(2) + 0.0722 * Channel(4);
        return luminance > 0.179 ? DarkText : LightText;
    }

    private static string Escape(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            builder.Append(c switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '"' => "&quot;",
                '\'' => "&apos;",
                _ when char.IsControl(c) => " ",
                _ => c.ToString()
            });
        }

        return builder.ToString();
    }
}
