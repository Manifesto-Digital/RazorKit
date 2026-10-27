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

These are only the defaults; the next section sets the files explicitly.

#### Stylesheets and Scripts

Three options choose the site files RazorKit loads. Each is a list of app-relative paths, loaded in order:

| Option | Loaded into | When left unset (`null`) | Empty list |
| --- | --- | --- | --- |
| `ExplorerStylesheets` | the explorer page, before RazorKit's own stylesheet | `/css/main.css` | none |
| `PreviewStylesheets` | the preview's `<head>` | `/_content/{ComponentLibraryName}/css/main.css`, or none when `ComponentLibraryName` is empty | none |
| `PreviewScripts` | the end of the preview's `<body>` | `/_content/{ComponentLibraryName}/js/main.js`, or none when `ComponentLibraryName` is empty | none |

Leaving all three unset keeps RazorKit's behaviour from earlier versions. For example, a site that builds its assets into `wwwroot/dist`:

```csharp
builder.Services.AddRazorKit(options =>
{
    options.ExplorerStylesheets = new List<string> { "~/dist/site.css" };
    options.PreviewStylesheets = new List<string> { "~/dist/site.css", "~/dist/components.css" };
    options.PreviewScripts = new List<string> { "~/dist/components.js" };
});
```

Or from `appsettings.json`, binding the whole section with `builder.Configuration.GetSection("RazorKit").Bind(options)`:

```json
{
  "RazorKit": {
    "ExplorerStylesheets": [ "~/dist/site.css" ],
    "PreviewStylesheets": [ "~/dist/site.css", "~/dist/components.css" ],
    "PreviewScripts": [ "~/dist/components.js" ]
  }
}
```

- A path is `/x` or `~/x`; both mean the same file in your application and are written under the request's path base, so `~/dist/site.css` becomes `/site/dist/site.css` when the application is mounted at `/site`. A query string such as `?v=2` is kept.
- Absolute URLs (`https://...`), scheme-relative URLs (`//host/...`), backslashes, control characters, `..` segments, quotes, `<` and `>` are rejected, and the application stops at startup with a message naming the option and the entry. Load files from another host through your own layout instead.
- An empty list means "load nothing", and can only be set in code: configuration binding skips an empty JSON array, so `"PreviewScripts": []` leaves the default in place.
- The lists are read once at startup; changing them afterwards has no effect.

#### Path Base

RazorKit works under a path base, such as `app.UsePathBase("/site")` or a virtual directory. Its pages, redirects, story links, the preview frame and its own files are all served under that base, so `/site/razorkit` opens the explorer.

Take the path base from trusted configuration, not from a raw forwarded header such as `X-Forwarded-Prefix`. RazorKit collapses a leading `//` in the base to a single `/` so it can never write an off-origin URL, but a header the client controls can still point the explorer's links at a path you did not intend.

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
