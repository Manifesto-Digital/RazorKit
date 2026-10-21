using Manifesto.RazorKit.Filters;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Manifesto.RazorKit.Tests;

public class DeclaredMethodsTests
{
    private static ControllerActionDescriptor Action(params string[]? methods) => new()
    {
        ActionConstraints = methods is null
            ? null
            : new List<IActionConstraintMetadata> { new HttpMethodActionConstraint(methods) }
    };

    [Fact]
    public void A_controller_action_with_no_method_constraint_counts_as_unsafe()
    {
        Assert.True(DeclaredMethods.DeclaresUnsafeMethod(Action(null)));
        Assert.True(DeclaredMethods.DeclaresUnsafeMethod(new ControllerActionDescriptor { ActionConstraints = new List<IActionConstraintMetadata>() }));
    }

    [Fact]
    public void A_controller_action_declaring_post_is_unsafe()
    {
        Assert.True(DeclaredMethods.DeclaresUnsafeMethod(Action("POST")));
        Assert.True(DeclaredMethods.DeclaresUnsafeMethod(Action("GET", "POST")));
    }

    [Fact]
    public void A_controller_action_declaring_only_get_or_head_is_safe()
    {
        Assert.False(DeclaredMethods.DeclaresUnsafeMethod(Action("GET")));
        Assert.False(DeclaredMethods.DeclaresUnsafeMethod(Action("GET", "HEAD")));
    }

    [Fact]
    public void A_razor_page_falls_back_to_the_request_method()
    {
        Assert.False(DeclaredMethods.DeclaresUnsafeMethod(new PageActionDescriptor()));
        Assert.False(DeclaredMethods.DeclaresUnsafeMethod(new CompiledPageActionDescriptor()));
    }

    [Fact]
    public void Any_other_unconstrained_descriptor_counts_as_unsafe()
    {
        Assert.True(DeclaredMethods.DeclaresUnsafeMethod(new ActionDescriptor()));
    }
}
