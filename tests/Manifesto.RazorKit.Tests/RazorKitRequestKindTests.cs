using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Manifesto.RazorKit.Filters;

namespace Manifesto.RazorKit.Tests;

public class RazorKitRequestKindTests
{
    private static ControllerActionDescriptor Action(params string[] methods) =>
        new()
        {
            ActionConstraints = new List<IActionConstraintMetadata> { new HttpMethodActionConstraint(methods) }
        };

    private static HttpRequest RequestWith(string method)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        return context.Request;
    }

    // A method-override middleware can leave any value here after routing chose the action.
    public static TheoryData<string> Methods => new() { "GET", "HEAD", "POST", "PUT", "PATCH", "DELETE" };

    [Theory]
    [MemberData(nameof(Methods))]
    public void A_GET_only_controller_action_takes_a_render_slot_whatever_the_request_method(string method) =>
        Assert.True(RazorKitRequestKind.TakesRenderSlot(RequestWith(method), Action("GET")));

    [Theory]
    [MemberData(nameof(Methods))]
    public void A_GET_only_controller_action_stays_in_the_page_bucket_for_a_GET_or_HEAD(string method) =>
        Assert.Equal(method is not ("GET" or "HEAD"), RazorKitRequestKind.IsRender(RequestWith(method), Action("GET")));

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    public void A_page_takes_no_render_slot_for_a_GET_or_HEAD(string method)
    {
        Assert.False(RazorKitRequestKind.TakesRenderSlot(RequestWith(method), new PageActionDescriptor()));
        Assert.False(RazorKitRequestKind.TakesRenderSlot(RequestWith(method), new CompiledPageActionDescriptor()));
    }

    [Fact]
    public void A_POST_controller_action_takes_a_render_slot() =>
        Assert.True(RazorKitRequestKind.TakesRenderSlot(RequestWith("GET"), Action("POST")));
}
