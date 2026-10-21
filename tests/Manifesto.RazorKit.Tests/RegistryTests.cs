using System.Text.Json;
using Manifesto.RazorKit.Converters;
using Manifesto.RazorKit.Registry;
using Manifesto.RazorKit.Services;
using Manifesto.RazorKit.Tests.Components.Atoms.Button;
using Manifesto.RazorKit.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Manifesto.RazorKit.Tests;

public class RegistryTests
{
    private static RazorKitRegistry Scanning() => new(NullLogger<RazorKitRegistry>.Instance);

    [Fact]
    public async Task Concurrent_first_requests_run_the_scan_once()
    {
        var scans = 0;
        var started = new ManualResetEventSlim();
        var registry = new RazorKitRegistry(() =>
        {
            Interlocked.Increment(ref scans);
            Thread.Sleep(100); // Keeps the other callers waiting inside the lazy while it builds.
            return new RegistrySnapshot();
        });

        var callers = Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
        {
            started.Wait();
            return registry.Components;
        })).ToArray();
        started.Set();
        var results = await Task.WhenAll(callers);

        Assert.Equal(1, scans);
        Assert.All(results, r => Assert.Same(results[0], r));
    }

    [Fact]
    public void Nothing_is_scanned_until_the_first_lookup()
    {
        var scans = 0;
        var registry = new RazorKitRegistry(() =>
        {
            scans++;
            return new RegistrySnapshot();
        });

        Assert.Equal(0, scans);
        _ = registry.FindPropsType("anything");
        _ = registry.FindPropsType("anything-else");
        Assert.Equal(1, scans);
    }

    [Fact]
    public void Props_types_are_found_by_component_name_ignoring_case()
    {
        var registry = Scanning();

        Assert.Equal(typeof(ButtonProps), registry.FindPropsType("Button"));
        Assert.Equal(typeof(ButtonProps), registry.FindPropsType("button"));
        Assert.Null(registry.FindPropsType("NoSuchComponent"));
    }

    [Fact]
    public void Duplicate_props_types_in_one_assembly_keep_the_first_and_log_a_warning()
    {
        var log = new CapturingLogger();

        var registry = new RazorKitRegistry(log);
        var found = registry.FindPropsType("Widget");

        Assert.NotNull(found);
        Assert.Equal("WidgetProps", found!.Name);
        Assert.Contains(log.Warnings, m => m.Contains("props type") && m.Contains("Widget"));
    }

    [Fact]
    public void Stories_are_registered_as_types_not_instances()
    {
        var registry = Scanning();

        Assert.Equal(typeof(ButtonStories), registry.FindStoriesType("button"));
        Assert.Null(registry.FindStoriesType("NoSuchComponent"));
    }

    [Fact]
    public void Components_come_back_in_explorer_order_with_their_model_type()
    {
        var components = Scanning().Components;

        var button = Assert.Single(components, c => c.Name == "Button");
        Assert.Equal("Atoms", button.AtomicLevel);
        Assert.Equal("~/Components/Atoms/Button/Button.cshtml", button.Path);
        Assert.Equal(typeof(ButtonProps), button.ModelType);
        Assert.Equal(components.OrderBy(c => c.Order).ThenBy(c => c.Name).Select(c => c.Name), components.Select(c => c.Name));
    }

    [Fact]
    public async Task Discovery_returns_new_component_objects_each_call()
    {
        await using var host = await TestHost.StartAsync();
        using var scope = host.App.Services.CreateScope();
        var discovery = scope.ServiceProvider.GetRequiredService<ComponentDiscoveryService>();

        var first = discovery.DiscoverComponents();
        first.First(c => c.Name == "Button").Name = "Changed";
        var second = discovery.DiscoverComponents();

        Assert.Contains(second, c => c.Name == "Button");
    }

    [Fact]
    public async Task Story_service_creates_a_new_story_instance_for_every_call()
    {
        await using var host = await TestHost.StartAsync();
        using var scope = host.App.Services.CreateScope();
        var stories = scope.ServiceProvider.GetRequiredService<ComponentStoryService>();

        var first = stories.GetStoriesForComponent("mutating").Single().Properties["Items"];
        ((List<string>)first).Add("changed");
        var second = stories.GetStoriesForComponent("mutating").Single().Properties["Items"];

        Assert.Equal(new[] { "seed" }, (List<string>)second);
    }

    [Fact]
    public async Task One_registry_serves_every_request_and_scope()
    {
        await using var host = await TestHost.StartAsync();

        using var a = host.App.Services.CreateScope();
        using var b = host.App.Services.CreateScope();

        Assert.Same(
            a.ServiceProvider.GetRequiredService<RazorKitRegistry>(),
            b.ServiceProvider.GetRequiredService<RazorKitRegistry>());
    }

    [Fact]
    public void An_interface_resolves_to_the_concrete_type_named_without_the_I_prefix()
    {
        var registry = Scanning();

        Assert.Equal(typeof(FixtureThing), registry.FindConcreteType(typeof(IFixtureThing)));
        Assert.Equal(typeof(Microsoft.AspNetCore.Html.HtmlString), registry.FindConcreteType(typeof(Microsoft.AspNetCore.Html.IHtmlContent)));
        Assert.Null(registry.FindConcreteType(typeof(IDisposable)));
        Assert.Null(registry.FindConcreteType(typeof(FixtureThing)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Converter_maps_an_interface_member_to_its_concrete_type(bool withRegistry)
    {
        var converter = withRegistry ? new InterfaceToConcreteConverter(Scanning()) : new InterfaceToConcreteConverter();
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { converter } };

        var holder = JsonSerializer.Deserialize<FixtureThingHolder>("""{"thing":{"title":"hello"}}""", options);

        var thing = Assert.IsType<FixtureThing>(holder!.Thing);
        Assert.Equal("hello", thing.Title);
    }

    private sealed class CapturingLogger : ILogger<RazorKitRegistry>
    {
        public List<string> Warnings { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }
}
