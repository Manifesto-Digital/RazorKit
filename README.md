# Manifesto.RazorKit

A component preview and development tool for ASP.NET Core Razor components, inspired by Storybook.

## Features

- **Interactive Component Preview**: View and interact with your Razor components in isolation
- **Property Editor**: Dynamically modify component props in real-time
- **Story Support**: Define multiple states/variants for each component
- **Accessibility Testing**: Built-in axe-core integration for a11y validation
- **Responsive Testing**: Preview components at different viewport sizes
- **Component Discovery**: Automatically discovers components in your project

## Installation

Install via NuGet:

```bash
dotnet add package Manifesto.RazorKit
```

## Quick Start

### 1. Add RazorKit to your ASP.NET Core application

In your `Program.cs`:

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
app.MapControllers();  // For the preview controller
app.MapRazorPages();   // For the /razorkit page

app.Run();
```

#### For Umbraco Projects

If using with Umbraco, add after `builder.CreateUmbracoBuilder()...Build()` and before `app.UseUmbraco()`:

```csharp
builder.CreateUmbracoBuilder()
    .AddBackOffice()
    .AddWebsite()
    .AddDeliveryApi()
    .AddComposers()
    .Build();

// Add Razor Pages for RazorKit
builder.Services.AddRazorPages();

// RazorKit services will be registered via Composer (see below)

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

// Map RazorKit routes
app.MapControllers();
app.MapRazorPages();

await app.RunAsync();
```

**Create a Composer** in your Umbraco project to register RazorKit services:

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

After setup, navigate to `/razorkit` to access the component preview UI.

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

### 3. Create Stories (Optional)

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

### 4. Navigate to RazorKit

Start your application and navigate to `/razorkit` to see your components.

## Component Organization

RazorKit supports Atomic Design principles. Organize your components like:

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

## Configuration

### Component Library Name

Configure the component library name to specify which static asset paths to use in the preview HTML. This is required when your components use static files (CSS/JS) from a Razor Class Library:

```csharp
builder.Services.AddRazorKit(options =>
{
    options.ComponentLibraryName = "YourProject.Components";
});
```

This will generate preview HTML with paths like:
- `/_content/YourProject.Components/css/main.css`
- `/_content/YourProject.Components/js/main.js`

If not specified, no static file references will be included in the preview.

### Custom Component Discovery

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

## Requirements

- .NET 9.0 or later
- ASP.NET Core

## Publishing to GitHub Packages

### Prerequisites

1. **Create a GitHub Personal Access Token (PAT)**:
   - Go to GitHub → Settings → Developer settings → Personal access tokens → Tokens (classic)
   - Click "Generate new token (classic)"
   - Select scopes: `write:packages`, `read:packages`, `delete:packages` (optional)
   - Copy the token

2. **Add GitHub Packages as a NuGet source**:
   ```bash
   dotnet nuget add source \
     --username YOUR_GITHUB_USERNAME \
     --password YOUR_GITHUB_PAT \
     --store-password-in-clear-text \
     --name github \
     "https://nuget.pkg.github.com/OWNER/index.json"
   ```

### Publishing a New Version

1. **Update the version** in `Manifesto.RazorKit.csproj`:
   ```xml
   <Version>1.0.1</Version>
   ```

2. **Build and pack the project**:
   ```bash
   cd src/Manifesto.RazorKit
   dotnet pack -c Release -o ../../packages
   ```

3. **Push to GitHub Packages**:
   ```bash
   cd ../..
   dotnet nuget push packages/Manifesto.RazorKit.1.0.1.nupkg \
     --api-key YOUR_GITHUB_PAT \
     --source "github"
   ```

### Using in a Project

1. **Add NuGet.config** to your project root (optional, for team sharing):
   ```xml
   <?xml version="1.0" encoding="utf-8"?>
   <configuration>
     <packageSources>
       <clear />
       <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
       <add key="github" value="https://nuget.pkg.github.com/OWNER/index.json" />
     </packageSources>
   </configuration>
   ```
   
   **Note**: Store credentials separately or use environment variables. Don't commit PATs to git!

2. **Install the package**:
   ```bash
   dotnet add package Manifesto.RazorKit --version 1.0.0
   ```

   Or add to your `.csproj`:
   ```xml
   <PackageReference Include="Manifesto.RazorKit" Version="1.0.0" />
   ```

3. **Restore packages**:
   ```bash
   dotnet restore
   ```

### Automated Publishing with GitHub Actions

Create `.github/workflows/publish-nuget.yml`:

```yaml
name: Publish NuGet Package

on:
  push:
    tags:
      - 'v*'

jobs:
  publish:
    runs-on: ubuntu-latest
    permissions:
      packages: write
      contents: read
    
    steps:
      - uses: actions/checkout@v4
      
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.0.x'
      
      - name: Extract version from tag
        id: version
        run: echo "VERSION=${GITHUB_REF#refs/tags/v}" >> $GITHUB_OUTPUT
      
      - name: Update version in csproj
        run: |
          sed -i "s/<Version>.*<\/Version>/<Version>${{ steps.version.outputs.VERSION }}<\/Version>/" \
            src/Manifesto.RazorKit/Manifesto.RazorKit.csproj
      
      - name: Build and Pack
        run: |
          cd src/Manifesto.RazorKit
          dotnet pack -c Release -o ../../packages
      
      - name: Publish to GitHub Packages
        run: |
          dotnet nuget push packages/*.nupkg \
            --api-key ${{ secrets.GITHUB_TOKEN }} \
            --source "https://nuget.pkg.github.com/${{ github.repository_owner }}/index.json" \
            --skip-duplicate
```

Then publish by creating and pushing a git tag:
```bash
git tag v1.0.1
git push origin v1.0.1
```

## License

MIT
