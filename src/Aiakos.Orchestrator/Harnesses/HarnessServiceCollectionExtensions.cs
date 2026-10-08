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

        services.TryAddSingleton<ClaudeCodeStateProfile>();
        var profileAlias = ServiceDescriptor.Singleton<IHarnessStateProfile, ClaudeCodeStateProfile>(
            static provider => provider.GetRequiredService<ClaudeCodeStateProfile>());
        var registrationCount = services.Count;
        services.TryAddEnumerable(profileAlias);
        if (services.Count > registrationCount)
        {
            // Preserve an existing default profile while adding the Claude singleton to the set.
            services.Remove(profileAlias);
            services.Insert(0, profileAlias);
        }
        services.TryAddSingleton<ClaudeCodeSettings>();
        services.TryAddSingleton<ClaudeCodeAdapter>();
        services.TryAddSingleton<IHarnessAdapter>(static provider =>
            provider.GetRequiredService<ClaudeCodeAdapter>());

        return services;
    }

}
