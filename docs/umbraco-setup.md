# Umbraco Integration Guide

This guide covers integrating RazorKit with Umbraco CMS projects.

## Prerequisites

- Umbraco 13+ (running on .NET 9.0)
- RazorKit NuGet package installed

## Installation

### 1. Install the Package

```bash
dotnet add package Manifesto.RazorKit
```

### 2. Create a Composer

Umbraco uses the Composer pattern for dependency injection. Create a new composer to register RazorKit services:

```csharp
using Manifesto.RazorKit.Extensions;
using Umbraco.Cms.Core.Composing;

namespace YourProject.Composers;

public class RazorKitComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddRazorKit(options =>
        {
            options.ComponentLibraryName = "YourProject.UmbracoComponents";
        });
    }
}
```

### 3. Configure Program.cs

Modify your `Program.cs` to add the required middleware and routing:

```csharp
// ... existing Umbraco builder code ...

builder.CreateUmbracoBuilder()
    .AddBackOffice()
    .AddWebsite()
    .AddDeliveryApi()
    .AddComposers()
    .Build();

// Add Razor Pages for RazorKit (required)
builder.Services.AddRazorPages();

var app = builder.Build();

await app.BootUmbracoAsync();

// ... other middleware ...

app.UseUmbraco()
    .WithMiddleware(u =>
    {
        u.UseBackOffice();
        u.UseWebsite();
    })
    .WithEndpoints(u =>
    {
        u.UseBackOfficeEndpoints();
        u.UseWebsiteEndpoints();
    });

// Map RazorKit routes AFTER Umbraco endpoints
app.MapControllers();
app.MapRazorPages();

await app.RunAsync();
```

## Component Organization

We recommend organizing your Umbraco components following Atomic Design principles:

```
YourProject/
├── Components/
│   ├── Atoms/
│   │   ├── Button/
│   │   │   ├── Button.cshtml
│   │   │   ├── ButtonProps.cs
│   │   │   └── ButtonStories.cs
│   │   └── Icon/
│   │       └── ...
│   ├── Molecules/
│   │   └── Card/
│   │       └── ...
│   ├── Organisms/
│   │   └── Header/
│   │       └── ...
│   └── Templates/
│       └── ...
```

## Creating Components for Umbraco

### Props Class

```csharp
namespace YourProject.Components.Atoms.Button;

public class ButtonProps
{
    public string Text { get; set; } = "Click me";
    public string Variant { get; set; } = "primary";
    public string? Href { get; set; }
    public bool Disabled { get; set; } = false;
}
```

### Razor Component

```html
@model YourProject.Components.Atoms.Button.ButtonProps

@if (!string.IsNullOrEmpty(Model.Href))
{
    <a href="@Model.Href" class="btn btn-@Model.Variant">
        @Model.Text
    </a>
}
else
{
    <button type="button" 
            class="btn btn-@Model.Variant" 
            disabled="@Model.Disabled">
        @Model.Text
    </button>
}
```

### Stories

```csharp
using Manifesto.RazorKit.Models;

namespace YourProject.Components.Atoms.Button;

public class ButtonStories : ComponentStoriesBase<ButtonProps>
{
    public override string ComponentName => "Button";

    public override List<ComponentStory> GetStories()
    {
        return new List<ComponentStory>
        {
            CreateStory("primary", "Primary Button", "Default primary button style", new ButtonProps
            {
                Text = "Primary Button",
                Variant = "primary"
            }),
            CreateStory("secondary", "Secondary Button", "Secondary button variant", new ButtonProps
            {
                Text = "Secondary Button",
                Variant = "secondary"
            }),
            CreateStory("link", "Link Button", "Button rendered as a link", new ButtonProps
            {
                Text = "Learn More",
                Variant = "primary",
                Href = "/about"
            }),
            CreateStory("disabled", "Disabled State", "Button in disabled state", new ButtonProps
            {
                Text = "Disabled",
                Disabled = true
            })
        };
    }
}
```

## Using Components in Umbraco Views

Once your components are set up, you can use them in your Umbraco templates:

```html
@inherits Umbraco.Cms.Web.Common.Views.UmbracoViewPage

@{
    Layout = "_Layout.cshtml";
}

<div class="page-content">
    @await Component.InvokeAsync("Button", new ButtonProps 
    { 
        Text = "Contact Us", 
        Variant = "primary",
        Href = "/contact"
    })
</div>
```

## Accessing RazorKit

After setup, navigate to `/razorkit` to access the component preview UI.

> **Tip:** You may want to restrict access to the RazorKit UI in production. Consider using environment checks or authentication middleware.

```csharp
// Example: Only enable RazorKit in development
if (app.Environment.IsDevelopment())
{
    app.MapRazorPages();
}
```

## Troubleshooting

### Components Not Discovered

Ensure your components follow the naming convention:
- Props class: `{ComponentName}Props.cs`
- Stories class: `{ComponentName}Stories.cs`
- Component: `{ComponentName}.cshtml`

### Static Assets Not Loading

Verify your `ComponentLibraryName` matches your project's assembly name:

```csharp
options.ComponentLibraryName = "YourProject.UmbracoComponents";
```

This generates paths like:
- `/_content/YourProject.UmbracoComponents/css/main.css`

### Route Conflicts

If RazorKit routes conflict with Umbraco routes, ensure `MapRazorPages()` is called after Umbraco's `WithEndpoints()`.

## Support

For issues specific to Umbraco integration, please [open an issue](https://github.com/manifesto-digital/razorkit/issues) with the `umbraco` label.
