using Aiakos.Orchestrator.Harnesses;
using Aiakos.Orchestrator.Harnesses.ClaudeCode;
using Aiakos.Orchestrator.Seats;
using Aiakos.Orchestrator.Tests.Seats;

using Microsoft.Extensions.DependencyInjection;

namespace Aiakos.Orchestrator.Tests.Harnesses.ClaudeCode;

public sealed class ClaudeCodeRegistrationTests
{
    [Fact]
    public void RegistersClaudeServicesAsSharedSingletonsAndPreservesOtherProfiles()
    {
        var services = new ServiceCollection();
        services.AddClaudeCodeHarness();
        services.AddSingleton<IHarnessStateProfile>(new OpenCodeLike());

        using var provider = services.BuildServiceProvider();

        var profile = provider.GetRequiredService<ClaudeCodeStateProfile>();
        var settings = provider.GetRequiredService<ClaudeCodeSettings>();
        var adapter = provider.GetRequiredService<ClaudeCodeAdapter>();

        Assert.Same(adapter, provider.GetRequiredService<IHarnessAdapter>());
        Assert.Same(profile, adapter.Profile);
        Assert.Same(settings, provider.GetRequiredService<ClaudeCodeSettings>());
        Assert.Same(profile, Assert.Single(provider.GetServices<IHarnessStateProfile>().OfType<ClaudeCodeStateProfile>()));
        Assert.Contains(provider.GetServices<IHarnessStateProfile>(), item => item is OpenCodeLike);
    }

    [Fact]
    public void DefaultProfileServiceResolvesRegisteredConcreteInstance()
    {
        var services = new ServiceCollection();
        services.AddClaudeCodeHarness();

        using var provider = services.BuildServiceProvider();

        Assert.Same(provider.GetRequiredService<ClaudeCodeStateProfile>(),
            provider.GetRequiredService<IHarnessStateProfile>());
    }

    [Fact]
    public void PreservesExplicitConcreteAndServiceOverrides()
    {
        var overrideProfile = new ClaudeCodeStateProfile();
        var overrideSettings = new ClaudeCodeSettings();
        var overrideAdapter = new ClaudeCodeAdapter(overrideProfile, overrideSettings);
        var services = new ServiceCollection();
        services.AddSingleton(overrideProfile);
        services.AddSingleton<IHarnessStateProfile>(overrideProfile);
        services.AddSingleton(overrideSettings);
        services.AddSingleton(overrideAdapter);
        services.AddSingleton<IHarnessAdapter>(overrideAdapter);
        services.AddClaudeCodeHarness();

        using var provider = services.BuildServiceProvider();

        Assert.Same(overrideProfile, provider.GetRequiredService<ClaudeCodeStateProfile>());
        Assert.Same(overrideProfile, provider.GetRequiredService<IHarnessStateProfile>());
        Assert.Same(overrideSettings, provider.GetRequiredService<ClaudeCodeSettings>());
        Assert.Same(overrideAdapter, provider.GetRequiredService<ClaudeCodeAdapter>());
        Assert.Same(overrideAdapter, provider.GetRequiredService<IHarnessAdapter>());
    }

    [Fact]
    public void RepeatedRegistrationDoesNotDuplicateClaudeProfile()
    {
        var services = new ServiceCollection();
        services.AddClaudeCodeHarness();
        services.AddClaudeCodeHarness();

        using var provider = services.BuildServiceProvider();

        Assert.Single(provider.GetServices<IHarnessStateProfile>().OfType<ClaudeCodeStateProfile>());
        Assert.Single(provider.GetServices<IHarnessAdapter>().OfType<ClaudeCodeAdapter>());
    }
}
