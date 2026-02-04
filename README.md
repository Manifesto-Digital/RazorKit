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

Start your application and navigate to `/razorkit` to view your components.

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
