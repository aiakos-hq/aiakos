using System.Reflection;

using Aiakos.ServiceDefaults;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Aiakos.Orchestrator.Tests;

public sealed class TelemetryShutdownServiceTests
{
    [Fact]
    public void InternalControlConstructorIsAvailableAndProductionConstructorUsesDefaults()
    {
        var serviceType = typeof(ServiceDefaultsExtensions).Assembly.GetType(
            "Aiakos.ServiceDefaults.TelemetryShutdownService",
            throwOnError: true)!;
        var productionConstructor = serviceType.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            [typeof(IServiceProvider), typeof(IOptions<TelemetryShutdownOptions>)],
            modifiers: null);
        var constructor = serviceType.GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(IServiceProvider), typeof(IOptions<TelemetryShutdownOptions>), typeof(TimeProvider), typeof(TaskScheduler)],
            modifiers: null);

        Assert.NotNull(productionConstructor);
        Assert.NotNull(constructor);
        Assert.True(constructor!.IsAssembly);

        using var services = new ServiceCollection().BuildServiceProvider();
        var options = Options.Create(new TelemetryShutdownOptions());
        var service = Activator.CreateInstance(
            serviceType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            [services, options],
            culture: null)!;

        var timeProvider = serviceType.GetField("timeProvider", BindingFlags.Instance | BindingFlags.NonPublic);
        var taskScheduler = serviceType.GetField("taskScheduler", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(timeProvider);
        Assert.NotNull(taskScheduler);
        Assert.Same(TimeProvider.System, timeProvider!.GetValue(service));
        Assert.Same(TaskScheduler.Default, taskScheduler!.GetValue(service));
    }
}
