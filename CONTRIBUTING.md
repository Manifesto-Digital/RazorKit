# Contributing to RazorKit

First off, thank you for considering contributing to RazorKit! It's people like you that make RazorKit such a great tool.

## Table of Contents

- [Code of Conduct](#code-of-conduct)
- [Getting Started](#getting-started)
- [Development Setup](#development-setup)
- [Making Changes](#making-changes)
- [Pull Request Process](#pull-request-process)
- [Coding Standards](#coding-standards)
- [Publishing Releases](#publishing-releases)

## Code of Conduct

This project and everyone participating in it is governed by our commitment to providing a welcoming and inclusive environment. By participating, you are expected to uphold this standard. Please report unacceptable behavior to the project maintainers.

## Getting Started

### Prerequisites

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) or later
- A code editor (we recommend [Visual Studio](https://visualstudio.microsoft.com/), [VS Code](https://code.visualstudio.com/), or [Rider](https://www.jetbrains.com/rider/))
- Git

### Types of Contributions

We welcome many types of contributions:

- 🐛 **Bug fixes** - Found a bug? We'd love a fix!
- ✨ **New features** - Have an idea? Let's discuss it first in an issue
- 📖 **Documentation** - Improvements to docs are always welcome
- 🧪 **Tests** - Help us improve test coverage
- 🎨 **UI/UX improvements** - Make RazorKit more beautiful and usable

## Development Setup

### 1. Fork and Clone

```bash
# Fork the repository on GitHub, then clone your fork
git clone https://github.com/YOUR_USERNAME/razorkit.git
cd razorkit
```

### 2. Create a Branch

```bash
git checkout -b feature/your-feature-name
# or
git checkout -b fix/your-bug-fix
```

### 3. Build the Project

```bash
dotnet restore
dotnet build
```

### 4. Run Tests

```bash
dotnet test
```

## Making Changes

### Before You Start

1. **Check existing issues** - Your idea might already be discussed
2. **Open an issue first** for significant changes - This helps avoid duplicate work
3. **Keep changes focused** - One feature/fix per pull request

### Development Workflow

1. Make your changes in your feature branch
2. Add or update tests as needed
3. Ensure all tests pass
4. Update documentation if needed
5. Commit your changes with a clear message

### Commit Messages

We follow conventional commit messages:

```
type(scope): description

[optional body]

[optional footer]
```

**Types:**
- `feat`: A new feature
- `fix`: A bug fix
- `docs`: Documentation only changes
- `style`: Code style changes (formatting, etc.)
- `refactor`: Code changes that neither fix bugs nor add features
- `test`: Adding or updating tests
- `chore`: Maintenance tasks

**Examples:**
```
feat(discovery): add support for nested component folders
fix(preview): resolve viewport resize issue on Safari
docs(readme): update installation instructions
```

## Pull Request Process

### 1. Prepare Your PR

- Ensure your code builds without errors
- Run all tests and ensure they pass
- Update the README.md if needed
- Add any relevant documentation

### 2. Submit Your PR

1. Push your branch to your fork
2. Open a pull request against the `main` branch
3. Fill out the PR template completely
4. Link any related issues

### 3. Code Review

- A maintainer will review your PR
- Address any feedback or requested changes
- Once approved, a maintainer will merge your PR

### PR Checklist

- [ ] Code compiles without warnings
- [ ] All tests pass
- [ ] Documentation updated (if applicable)
- [ ] Commit messages follow conventions
- [ ] PR description clearly explains the changes

## Coding Standards

### C# Style Guide

We follow the [Microsoft C# Coding Conventions](https://docs.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions) with these specifics:

- Use `var` when the type is obvious
- Use meaningful, descriptive names
- Prefer expression-bodied members for simple operations
- Use nullable reference types (`#nullable enable`)
- Add XML documentation for public APIs

```csharp
/// <summary>
/// Discovers components in the specified assembly.
/// </summary>
/// <param name="assembly">The assembly to scan.</param>
/// <returns>A list of discovered components.</returns>
public List<ComponentInfo> DiscoverComponents(Assembly assembly)
{
    // Implementation
}
```

### Project Structure

```
RazorKit/
├── Controllers/     # API controllers
├── Converters/      # JSON converters
├── Extensions/      # Extension methods
├── Helpers/         # Utility classes
├── Models/          # Data models
├── Pages/           # Razor Pages
├── Services/        # Business logic services
└── wwwroot/         # Static assets
```

## Publishing Releases

> **Note:** This section is for maintainers with publish access.

### Versioning

We use [Semantic Versioning](https://semver.org/):

- **MAJOR** - Breaking changes
- **MINOR** - New features (backward compatible)
- **PATCH** - Bug fixes (backward compatible)

### Publishing to NuGet

Releases are automated via GitHub Actions when a version tag is pushed.

#### Manual Release Process

1. **Update the version** in `Manifesto.RazorKit.csproj`:
   ```xml
   <Version>1.0.1</Version>
   ```

2. **Build and pack**:
   ```bash
   dotnet pack -c Release -o ./artifacts
   ```

3. **Create and push a version tag**:
   ```bash
   git tag 1.0.1
   git push origin 1.0.1
   ```

The GitHub Action will automatically:
- Build the project
- Create the NuGet package
- Publish to NuGet.org

### Publishing to GitHub Packages (Alternative)

For pre-release or testing:

1. **Create a GitHub Personal Access Token (PAT)**:
   - Go to GitHub → Settings → Developer settings → Personal access tokens
   - Select scopes: `write:packages`, `read:packages`

2. **Add GitHub Packages as a NuGet source**:
   ```bash
   dotnet nuget add source \
     --username YOUR_GITHUB_USERNAME \
     --password YOUR_GITHUB_PAT \
     --store-password-in-clear-text \
     --name github \
     "https://nuget.pkg.github.com/manifesto-digital/index.json"
   ```

3. **Push the package**:
   ```bash
   dotnet nuget push ./artifacts/Manifesto.RazorKit.1.0.1.nupkg \
     --api-key YOUR_GITHUB_PAT \
     --source "github"
   ```

## Questions?

Feel free to:
- Open an issue for bugs or feature requests
- Start a discussion for questions
- Reach out to the maintainers

Thank you for contributing! 🎉
