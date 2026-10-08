using Aiakos.Orchestrator.Harnesses.ClaudeCode;
using Aiakos.Orchestrator.Seats;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aiakos.Orchestrator.Harnesses;

public static class HarnessServiceCollectionExtensions
{
    public static IServiceCollection AddClaudeCodeHarness(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IHarnessStateProfile, ClaudeCodeStateProfile>();
        services.TryAddSingleton<ClaudeCodeStateProfile>(static provider =>
            provider.GetServices<IHarnessStateProfile>().OfType<ClaudeCodeStateProfile>().First());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHarnessStateProfile, ClaudeCodeStateProfile>());
        services.TryAddSingleton<ClaudeCodeSettings>();
        services.TryAddSingleton<ClaudeCodeAdapter>();
        services.TryAddSingleton<IHarnessAdapter>(static provider =>
            provider.GetRequiredService<ClaudeCodeAdapter>());

        return services;
    }
}
