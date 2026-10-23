using System.Text.RegularExpressions;
using static Manifesto.RazorKit.Tests.Support.RazorKitRequests;

namespace Manifesto.RazorKit.Tests;

public class A11yResultsEscapeTests
{
    private static async Task<string> DisplayA11yResultsBodyAsync()
    {
        await using var host = await TestHost.StartAsync();

        var html = await (await host.Client.GetAsync(ShellPath)).Content.ReadAsStringAsync();

        var start = html.IndexOf("function displayA11yResults(", StringComparison.Ordinal);
        var end = html.IndexOf("function toggleA11yIssue(", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "displayA11yResults was not found in the page source.");
        return html[start..end];
    }

    [Fact]
    public async Task Axe_fields_are_never_assigned_to_innerHTML()
    {
        var body = await DisplayA11yResultsBodyAsync();

        // The only innerHTML left is the summary badges, built from counts and never from axe strings.
        foreach (Match assignment in Regex.Matches(body, @"innerHTML\s*[+]?=[^;]*;"))
        {
            Assert.DoesNotMatch(@"\b(violation|node|results)\.(?!violations\.length|passes\.length|incomplete\.length)", assignment.Value);
        }

        Assert.DoesNotContain("${violation.", body);
        Assert.DoesNotContain("${node.", body);
        Assert.DoesNotContain("${target}", body);
        Assert.DoesNotContain("${failureSummary}", body);
        Assert.DoesNotContain("issues.innerHTML", body);
    }

    [Fact]
    public async Task Axe_fields_are_written_as_text()
    {
        var body = await DisplayA11yResultsBodyAsync();

        Assert.Contains("textContent", body);
        Assert.Contains("createTextNode(' ' + String(violation.help))", body);
        Assert.Contains("String(violation.description)", body);
    }

    [Fact]
    public async Task The_help_link_requires_an_https_url_and_has_no_inline_handlers()
    {
        var body = await DisplayA11yResultsBodyAsync();

        Assert.Contains("new URL(", body);
        Assert.Contains("protocol === 'https:'", body);
        Assert.Contains("link.href = helpHref", body);
        Assert.Contains("noopener noreferrer", body);
        Assert.DoesNotContain("onclick", body);
        Assert.Contains("addEventListener('click'", body);
    }
}
