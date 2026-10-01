using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aiakos.Hosting.Wsl.Tests;

internal static class TestApp
{
    public const string DashboardOtlpUrl = "http://localhost:19180";

    public static IDistributedApplicationBuilder CreateBuilder()
    {
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { Args = [] });
        builder.Configuration["ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL"] = DashboardOtlpUrl;
        builder.Configuration["ASPIRE_ALLOW_UNSECURED_TRANSPORT"] = "true";
        return builder;
    }

    /// <summary>Resolves arguments and environment the way the run-mode executor does (Aspire 13.5).</summary>
    public static async Task<IExecutionConfigurationResult> ResolveAsync(IDistributedApplicationBuilder builder, IResource resource)
    {
        var result = await ExecutionConfigurationBuilder.Create(resource)
            .WithArgumentsConfig()
            .WithEnvironmentVariablesConfig()
            .BuildAsync(builder.ExecutionContext, NullLogger.Instance, TestContext.Current.CancellationToken);
        Assert.Null(result.Exception);
        return result;
    }

    public static async Task<Dictionary<string, string>> EnvironmentAsync(IDistributedApplicationBuilder builder, IResource resource)
    {
        var result = await ResolveAsync(builder, resource);
        return result.EnvironmentVariables.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
    }

    /// <summary>The WSLENV entries that this library added, without any inherited from the test process.</summary>
    public static string[] AddedWslEnvEntries(string wslEnv)
    {
        var inherited = Environment.GetEnvironmentVariable("WSLENV");
        var entries = wslEnv.Split(':', StringSplitOptions.RemoveEmptyEntries);
        return string.IsNullOrEmpty(inherited)
            ? entries
            : [.. entries.Skip(inherited.Split(':', StringSplitOptions.RemoveEmptyEntries).Length)];
    }
}
