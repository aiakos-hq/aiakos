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
        var profileAlias = new ServiceDescriptor(
            typeof(IHarnessStateProfile),
            ProfileAlias,
            ServiceLifetime.Singleton);
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(IHarnessStateProfile)
            && descriptor.ImplementationFactory == profileAlias.ImplementationFactory))
        {
            // Keep a previously registered default profile as the last registration while
            // adding Claude to the enumerable profile set.
            var firstProfile = -1;
            for (var index = 0; index < services.Count; index++)
            {
                if (services[index].ServiceType == typeof(IHarnessStateProfile))
                {
                    firstProfile = index;
                    break;
                }
            }
            if (firstProfile >= 0)
            {
                services.Insert(firstProfile, profileAlias);
            }
            else
            {
                services.Add(profileAlias);
            }
        }
        services.TryAddSingleton<ClaudeCodeSettings>();
        services.TryAddSingleton<ClaudeCodeAdapter>();
        services.TryAddSingleton<IHarnessAdapter>(static provider =>
            provider.GetRequiredService<ClaudeCodeAdapter>());

        return services;
    }

    private static ClaudeCodeStateProfile ProfileAlias(IServiceProvider provider) =>
        provider.GetRequiredService<ClaudeCodeStateProfile>();
}
