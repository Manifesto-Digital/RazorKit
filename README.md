<div align="center">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="razorkit-logo-black.png">
  <source media="(prefers-color-scheme: light)" srcset="razorkit-logo-white.png">
  <img src="razorkit-logo-white.png" alt="RazorKit logo" width="240">
</picture>

**A component preview and development tool for ASP.NET Core Razor components**

*Inspired by Storybook, built for .NET developers*

[![Build Status](https://img.shields.io/github/actions/workflow/status/manifesto-digital/razorkit/nuget-release.yml?style=flat-square&logo=github&label=build)](https://github.com/manifesto-digital/razorkit/actions/workflows/nuget-release.yml)
[![NuGet Version](https://img.shields.io/nuget/v/Manifesto.RazorKit?style=flat-square&logo=nuget&color=004880)](https://www.nuget.org/packages/Manifesto.RazorKit)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Manifesto.RazorKit?style=flat-square&logo=nuget&color=004880)](https://www.nuget.org/packages/Manifesto.RazorKit)
[![License](https://img.shields.io/github/license/manifesto-digital/razorkit?style=flat-square)](LICENSE.md)
[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)

[![GitHub Stars](https://img.shields.io/github/stars/manifesto-digital/razorkit?style=flat-square&logo=github)](https://github.com/manifesto-digital/razorkit/stargazers)
[![GitHub Forks](https://img.shields.io/github/forks/manifesto-digital/razorkit?style=flat-square&logo=github)](https://github.com/manifesto-digital/razorkit/network/members)
[![GitHub Issues](https://img.shields.io/github/issues/manifesto-digital/razorkit?style=flat-square&logo=github)](https://github.com/manifesto-digital/razorkit/issues)
[![GitHub Pull Requests](https://img.shields.io/github/issues-pr/manifesto-digital/razorkit?style=flat-square&logo=github)](https://github.com/manifesto-digital/razorkit/pulls)
[![Contributors](https://img.shields.io/github/contributors/manifesto-digital/razorkit?style=flat-square&logo=github)](https://github.com/manifesto-digital/razorkit/graphs/contributors)
[![Last Commit](https://img.shields.io/github/last-commit/manifesto-digital/razorkit?style=flat-square&logo=github)](https://github.com/manifesto-digital/razorkit/commits/main)

[Features](#features) •
[Installation](#installation) •
[Quick Start](#quick-start) •
[Documentation](#documentation) •
[Contributing](#contributing) •
[License](#license)

</div>

---

## Overview

RazorKit brings the component-driven development experience to ASP.NET Core Razor applications. Develop, test, and document your UI components in isolation within your running application using an interactive preview environment—without navigating through the full application flow.

## Features

- 🎨 **Interactive Component Preview** — View and interact with Razor components in isolation
- ⚡ **Real-time Property Editor** — Dynamically modify component props and see changes instantly
- 📚 **Story Support** — Define multiple states and variants for each component
- ♿ **Accessibility Testing** — Built-in axe-core integration for automated a11y validation
- 📱 **Responsive Testing** — Preview components at different viewport sizes
- 🔍 **Component Discovery** — Automatically discovers components in your project
- 🏗️ **Atomic Design Support** — Organize components using Atomic Design methodology

## Installation

### Requirements

- [.NET 9.0](https://dotnet.microsoft.com/download/dotnet/9.0) or later
- ASP.NET Core

### Package Installation

```bash
dotnet add package Manifesto.RazorKit
```

Or add to your `.csproj`:

```xml
<PackageReference Include="Manifesto.RazorKit" Version="0.0.1" />
```

## Quick Start

### 1. Register Services

Add RazorKit to your ASP.NET Core application in `Program.cs`:

```csharp
using Manifesto.RazorKit.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add Razor Pages (required for RazorKit UI)
builder.Services.AddRazorPages();

// Add RazorKit services
builder.Services.AddRazorKit(options =>
{
    options.ComponentLibraryName = "YourProject.Components";
});

var app = builder.Build();

// Map RazorKit routes
app.MapControllers();
app.MapRazorPages();

app.Run();
```

### 2. Define Component Props

Create a props class for your component:

```csharp
public class ButtonProps
{
    public string Text { get; set; } = "Click me";
    public string Variant { get; set; } = "primary";
    public bool Disabled { get; set; } = false;
}
```

Keep each component a pure function of its props: render only from the props it's given, and don't look up content, call services or read the request from a component's view. RazorKit renders components with whatever values are entered in its Controls panel.

### 3. Create Stories

Define stories to showcase different component states:

```csharp
using Manifesto.RazorKit.Models;

public class ButtonStories : ComponentStoriesBase<ButtonProps>
{
    public override string ComponentName => "Button";

    public override List<ComponentStory> GetStories()
    {
        return new List<ComponentStory>
        {
            CreateStory("primary", "Primary Button", "Default primary button", new ButtonProps
            {
                Text = "Primary",
                Variant = "primary"
            }),
            CreateStory("secondary", "Secondary Button", "Secondary variant", new ButtonProps
            {
                Text = "Secondary",
                Variant = "secondary"
            }),
            CreateStory("disabled", "Disabled State", "Disabled button", new ButtonProps
            {
                Text = "Disabled",
                Disabled = true
            })
        };
    }
}
```

Story values are serialised into the explorer page and its Controls panel, so don't put live services, connections or secrets in stories.

### 4. Launch RazorKit

Start your application and navigate to `/razorkit` to view your components. Live editing in the Controls panel needs the site to be served over HTTPS, or from `localhost`.

## Documentation

### Component Organization

RazorKit supports [Atomic Design](https://bradfrost.com/blog/post/atomic-web-design/) principles:

```
Components/
├── Atoms/
│   └── Button/
│       ├── Button.cshtml
│       ├── ButtonProps.cs
│       └── ButtonStories.cs
├── Molecules/
├── Organisms/
└── Templates/
```

### Configuration Options

#### Component Library Name

Configure the component library name to specify static asset paths in the preview HTML:

```csharp
builder.Services.AddRazorKit(options =>
{
    options.ComponentLibraryName = "YourProject.Components";
});
```

This generates preview HTML with paths like:
- `/_content/YourProject.Components/css/main.css`
- `/_content/YourProject.Components/js/main.js`

#### Themes

If your component library styles itself from a `data-theme` attribute, list the themes and RazorKit sets the attribute on the preview's `<html>` element:

```csharp
builder.Services.AddRazorKit(options =>
{
    options.Themes = new List<RazorKitTheme>
    {
        new() { Name = "Light", DataTheme = "light" },
        new() { Name = "Dark", DataTheme = "dark" }
    };
});
```

- The first theme is the default. With one theme it is always applied; with two or more, a theme dropdown appears in the toolbar. With none, the preview sets no `data-theme`.
- The chosen theme travels as `?theme=<Name>` on RazorKit's links, so a shared link opens in the same theme. A `?theme=` that exactly matches a configured `Name` always wins.
- The browser remembers the last theme chosen. When the URL's `?theme=` is missing, or does not exactly match a configured `Name`, the remembered theme is used. With no theme remembered, the first configured theme is the default.
- `Name` must be unique (ignoring case). `DataTheme` may only contain letters, digits, hyphens and underscores. An invalid entry stops the application at startup.

The list binds from configuration too, for example `builder.Configuration.GetSection("RazorKit:Themes").Bind(options.Themes)` with entries of `Name` and `DataTheme`.

#### Where RazorKit is served

RazorKit is public by default. Every environment that maps it serves `/razorkit` and `/razorkit-preview` to anyone who can reach the site. That is deliberate, so a component library can be shown on a live site, and the pages are kept out of search indexes by an `X-Robots-Tag: noindex` header and a robots `noindex` meta tag.

To serve it only in some environments, list them in `EnabledEnvironments`:

```csharp
builder.Services.AddRazorKit(options =>
{
    options.ComponentLibraryName = "YourProject.Components";
    options.EnabledEnvironments = ["Development", "Staging"];
    // On net8 without C# 12 collection expressions:
    // options.EnabledEnvironments = new List<string> { "Development", "Staging" };
});
```

- Names are matched ignoring case against `IHostEnvironment.EnvironmentName`.
- Unset (`null`, the default) means every environment, so existing sites are unchanged.
- An empty list set in code (`options.EnabledEnvironments = []`) means no environment: RazorKit is disabled everywhere.
- In configuration it is different. `"EnabledEnvironments": []` in `appsettings.json` binds as unset, because the configuration binder cannot tell an empty array from a missing key, so RazorKit stays enabled everywhere. To turn RazorKit off in an environment, list only the environments where it should be served.
- Route aliases or subclasses of RazorKit's controller that your host adds itself (for example `AddPageRoute("/RazorKit", ...)`) are not covered and are not hidden.
- A blank or null entry stops the application at startup.
- In an environment that is not listed, RazorKit's routes are not registered, so `/razorkit`, `/razorkit-preview` and everything beneath them (any method, any casing) get the same 404 as any unknown URL on your site. No RazorKit header, cookie or text is sent, and no rendering, props reading or rate limiting happens. RazorKit logs one Information line at startup saying so, and does not log the `AllowedOrigins` warning.
- Static assets under `/_content/Manifesto.RazorKit` are still served by the host's static files middleware. They are RazorKit's own `css/razorkit.css`, `js/razorkit.js` and the bundled axe-core accessibility checker in `lib/axe-core` (script, licence and readme files). They contain no component, story or site data.

Umbraco's built-in basic authentication (`Umbraco:CMS:BasicAuth`) cannot do this job: it protects the whole site or the backoffice and has no way to scope itself to a path, so it cannot cover `/razorkit` alone. `EnabledEnvironments` replaces the custom middleware that returns 404 for those paths.

An `Authorize` hook (to require a signed-in user instead of hiding RazorKit) may be added in a later release. It is not available today.

#### Custom Component Discovery

Implement `IComponentDiscovery` to customize how components are discovered:

```csharp
public class CustomComponentDiscovery : IComponentDiscovery
{
    public List<ComponentInfo> DiscoverComponents()
    {
        // Your custom discovery logic
    }
}

// Register in Program.cs
builder.Services.AddRazorKit(options =>
{
    options.UseCustomDiscovery<CustomComponentDiscovery>();
});
```

### Placeholder images

Use RazorKit's own placeholder images in stories instead of third-party placeholder sites such as placehold.co, via.placeholder.com or picsum.photos. A site with a strict `Content-Security-Policy` blocks those hosts unless its `img-src` lists them, whereas RazorKit serves the placeholder from your own origin, so `img-src 'self'` already covers it.

```csharp
using Manifesto.RazorKit.Models;

CreateStory("default", "Default", "Card with an image", new CardProps
{
    ImageUrl = RazorKitPlaceholder.Url(640, 360, "Card image", background: "336699")
})
```

`RazorKitPlaceholder.Url(width, height, label, background, foreground)` returns a root-relative URL such as `/razorkit-preview/placeholder/640x360?label=Card%20image&bg=336699`. Only the size is required.

- **Size**: whole pixels, clamped to 16 to 4000 in each direction.
- **Label**: shown on the image and used as its accessible name. It defaults to the size, for example `640×360`. The helper keeps letters, digits, spaces and `- _ . , : ( )`, removes anything else and cuts the result to 40 characters.
- **Colours**: `background` and `foreground` take 3 or 6 hex digits, with or without a leading `#`. An invalid value is ignored. Without a foreground, black or white is chosen to contrast with the background.
- **Path base**: the helper returns a root-relative path without any path base. If your site is mounted under a sub-path, prefix it with `Request.PathBase` (or your own `Url.Content`) wherever you render it.

You can also request the endpoint directly. A malformed size gives a 404, and a label outside the safe characters or longer than 40 characters gives a 400; the helper never produces either. Only GET and HEAD are served. Images are cached by the browser for a year (`private, max-age=31536000, immutable`) because the image depends only on its URL. The header is `private` so that a shared cache or CDN never stores it. Requests are subject to RazorKit's rate limit like the explorer page. Only the image itself gets that header: rate-limited or rejected responses, and any page your host renders in their place, keep `no-store`.

### Umbraco Integration

For Umbraco CMS projects, see [Umbraco Setup Guide](docs/umbraco-setup.md) for detailed integration instructions.

<details>
<summary>Quick Umbraco Setup</summary>

Create a Composer to register RazorKit services:

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

</details>

## Contributing

We welcome contributions! Please see our [Contributing Guide](CONTRIBUTING.md) for details on:

- Setting up the development environment
- Submitting pull requests
- Coding standards and guidelines
- Publishing new versions

## Support

- 🐛 **Bug Reports**: [Open an issue](https://github.com/manifesto-digital/razorkit/issues/new?template=bug_report.md)
- 💡 **Feature Requests**: [Open an issue](https://github.com/manifesto-digital/razorkit/issues/new?template=feature_request.md)
- 💬 **Questions**: [Start a discussion](https://github.com/manifesto-digital/razorkit/discussions)

## Roadmap

- [ ] Visual regression testing integration
- [ ] Dark mode support
- [ ] Component documentation generation
- [ ] Plugin system for custom addons

## License

This project is licensed under the MIT License - see the [LICENSE.md](LICENSE.md) file for details.

## Acknowledgments

- Inspired by [Storybook](https://storybook.js.org/)
- Built with ❤️ by [Manifesto Digital](https://www.manifesto.co.uk/)

---

<div align="center">

**[⬆ Back to Top](#-razorkit)**

</div>
