using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;

namespace Manifesto.RazorKit.Tests.Support;

/// <summary>
/// Reads the explorer page's own stylesheet, script and data block the way a browser finds them:
/// from the URLs the page references, not from the files on disk, so a test also covers the reference.
/// </summary>
public static partial class PageAssets
{
    public const string ScriptPath = "/_content/Manifesto.RazorKit/js/razorkit.js";
    public const string StylesheetPath = "/_content/Manifesto.RazorKit/css/razorkit.css";

    /// <summary>
    /// Static web assets from a referenced Razor Class Library are only mapped in Development, and
    /// the host has to serve static files, as a consumer site does for the bundled axe.
    /// </summary>
    public static TestHostOptions WithStaticFiles() => new()
    {
        EnvironmentName = Environments.Development,
        ConfigurePipeline = app => app.UseStaticFiles()
    };

    [GeneratedRegex("<script src=\"([^\"]*/js/razorkit\\.js[^\"]*)\"></script>")]
    private static partial Regex ScriptReference();

    [GeneratedRegex("<link rel=\"stylesheet\" href=\"([^\"]*/css/razorkit\\.css[^\"]*)\" />")]
    private static partial Regex StylesheetReference();

    [GeneratedRegex("<script type=\"application/json\" id=\"razorkit-data\">(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex DataBlock();

    public static async Task<string> LoadPageAsync(HttpClient client, string path = RazorKitRequests.ShellPath)
    {
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>The script URL as the page writes it, HTML-decoded the way the browser reads the attribute.</summary>
    public static string ScriptUrl(string html)
    {
        var match = ScriptReference().Match(html);
        Assert.True(match.Success, "The page does not reference razorkit.js.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    public static string StylesheetUrl(string html)
    {
        var match = StylesheetReference().Match(html);
        Assert.True(match.Success, "The page does not reference razorkit.css.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    /// <summary>The data block's text exactly as served, before any parsing.</summary>
    public static string DataBlockSource(string html)
    {
        var matches = DataBlock().Matches(html);
        Assert.True(matches.Count == 1, $"Expected one razorkit-data block, found {matches.Count}.");
        return matches[0].Groups[1].Value;
    }

    /// <summary>Parses the data block as the script does: the element's text, read as JSON.</summary>
    public static JsonElement Data(string html) => JsonDocument.Parse(DataBlockSource(html)).RootElement.Clone();

    /// <summary>The page script, fetched from the URL the page references.</summary>
    public static async Task<string> LoadScriptAsync()
    {
        await using var host = await TestHost.StartAsync(WithStaticFiles());

        var html = await LoadPageAsync(host.Client);
        var response = await host.Client.GetAsync(ScriptUrl(html));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }
}
