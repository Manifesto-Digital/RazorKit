using Manifesto.RazorKit.Models;

namespace Manifesto.RazorKit.Tests;

public class PlaceholderHelperTests
{
    private const string Prefix = "/razorkit-preview/placeholder";

    [Fact]
    public void Builds_a_root_relative_url_for_a_size()
    {
        Assert.Equal($"{Prefix}/640x360", RazorKitPlaceholder.Url(640, 360));
    }

    [Theory]
    [InlineData(0, 0, "16x16")]
    [InlineData(-5, 99999, "16x4000")]
    [InlineData(4001, 15, "4000x16")]
    public void Clamps_sizes_the_way_the_endpoint_does(int width, int height, string expected)
    {
        Assert.Equal($"{Prefix}/{expected}", RazorKitPlaceholder.Url(width, height));
    }

    [Fact]
    public void Escapes_the_label_in_the_query_string()
    {
        var url = RazorKitPlaceholder.Url(100, 100, "Hero image (1): a,b");

        Assert.Equal($"{Prefix}/100x100?label=Hero%20image%20%281%29%3A%20a%2Cb", url);
    }

    [Fact]
    public void Strips_characters_the_endpoint_would_reject_so_the_url_never_400s()
    {
        var url = RazorKitPlaceholder.Url(100, 100, "<b>Hi</b> & \"you\"");

        Assert.Equal($"{Prefix}/100x100?label=bHib%20%20you", url);
    }

    [Fact]
    public void Truncates_a_long_label_to_the_limit()
    {
        var url = RazorKitPlaceholder.Url(100, 100, new string('a', 100));

        Assert.EndsWith("label=" + new string('a', RazorKitPlaceholder.MaxLabelLength), url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<>")]
    public void Omits_the_label_when_nothing_usable_is_left(string? label)
    {
        Assert.Equal($"{Prefix}/100x100", RazorKitPlaceholder.Url(100, 100, label));
    }

    [Theory]
    [InlineData("336699", "336699")]
    [InlineData("#336699", "336699")]
    [InlineData("ABC", "aabbcc")]
    public void Adds_a_valid_background_without_the_hash(string background, string expected)
    {
        Assert.Equal($"{Prefix}/100x100?bg={expected}", RazorKitPlaceholder.Url(100, 100, background: background));
    }

    [Theory]
    [InlineData("red")]
    [InlineData("12345")]
    [InlineData("zzzzzz")]
    [InlineData("333&label=x")]
    public void Drops_an_invalid_background(string background)
    {
        Assert.Equal($"{Prefix}/100x100", RazorKitPlaceholder.Url(100, 100, background: background));
    }

    [Fact]
    public void Combines_label_background_and_foreground()
    {
        Assert.Equal($"{Prefix}/200x100?label=Hero&bg=eeeeee&fg=111111", RazorKitPlaceholder.Url(200, 100, "Hero", "eee", "111"));
    }

    [Fact]
    public void Svg_encodes_markup_characters_even_though_the_charset_excludes_them()
    {
        var svg = RazorKitPlaceholder.RenderSvg(100, 100, "<script>\"'&]]>", null, null);

        Assert.DoesNotContain("<script", svg);
        Assert.Contains("&lt;script&gt;&quot;&apos;&amp;]]&gt;", svg);
    }
}
