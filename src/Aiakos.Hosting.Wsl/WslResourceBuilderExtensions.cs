using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Aiakos.Hosting.Wsl;

/// <summary>AppHost API for Linux processes in WSL (spec 0001 R22–R26).</summary>
public static partial class WslResourceBuilderExtensions
{
    /// <summary>The prefixes <see cref="WithWslEnvironment"/> forwards when none are given.</summary>
    public static readonly IReadOnlyList<string> DefaultPrefixes = ["OTEL_", "AIAKOS_", "DOTNET_"];

    /// <summary>The host WSL processes use to reach Windows listeners (mirrored networking only).</summary>
    public const string WindowsLoopback = "127.0.0.1";

    private const string OtlpEndpointVariable = "OTEL_EXPORTER_OTLP_ENDPOINT";

    /// <summary>
    /// Adds a Linux process started as <c>wsl.exe -d &lt;distro&gt; --cd ~ --exec &lt;linuxPath&gt; &lt;args…&gt;</c>
    /// (R23). No shell sits in between: <paramref name="linuxPath"/> is absolute or relative to the
    /// WSL user's home, and <c>~</c> is not expanded. Before it starts, a preflight checks that the
    /// distro exists and uses mirrored networking (R26); a failure fails the resource.
    /// </summary>
    public static IResourceBuilder<WslExecutableResource> AddWslExecutable(
        this IDistributedApplicationBuilder builder,
        string name,
        string distro,
        string linuxPath,
        params string[] args)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(distro);
        ArgumentException.ThrowIfNullOrWhiteSpace(linuxPath);
        ArgumentNullException.ThrowIfNull(args);

        builder.Services.TryAddSingleton<IWslProcessRunner, WslProcessRunner>();
        builder.Services.TryAddSingleton<IWslConfigFile, UserProfileWslConfigFile>();
        builder.Services.TryAddSingleton<WslPreflight>();

        var resource = new WslExecutableResource(name, distro, linuxPath, builder.AppHostDirectory);
        return builder.AddResource(resource)
            .WithArgs(["-d", distro, "--cd", "~", "--exec", linuxPath, .. args])
            .OnBeforeResourceStarted(RunPreflightAsync);
    }

    /// <summary>
    /// Sets <c>WSLENV</c> so that every environment variable of the resource whose name starts
    /// with one of <paramref name="prefixes"/> (default <c>OTEL_</c>, <c>AIAKOS_</c>,
    /// <c>DOTNET_</c>) crosses into WSL with <c>/u</c> (R24). An existing <c>WSLENV</c> (set on the
    /// resource, else inherited from the AppHost) is kept and appended to. Variables added after
    /// this call are included too.
    /// </summary>
    public static IResourceBuilder<WslExecutableResource> WithWslEnvironment(
        this IResourceBuilder<WslExecutableResource> builder,
        params string[] prefixes)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(prefixes);

        var annotation = builder.Resource.Annotations.OfType<WslEnvironmentAnnotation>().SingleOrDefault();
        var isNew = annotation is null;
        annotation ??= new WslEnvironmentAnnotation();
        foreach (var prefix in prefixes.Length == 0 ? DefaultPrefixes : prefixes)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(prefix, nameof(prefixes));
            annotation.Prefixes.Add(prefix);
        }

        if (!isNew)
        {
            return builder;
        }

        builder.WithAnnotation(annotation);
        return builder.WithEnvironment(context =>
        {
            var environment = context.EnvironmentVariables;
            environment.TryGetValue(WslEnvironmentValue.VariableName, out var existing);
            if (existing is null)
            {
                var inherited = Environment.GetEnvironmentVariable(WslEnvironmentValue.VariableName);
                existing = string.IsNullOrEmpty(inherited) ? null : inherited;
            }

            environment[WslEnvironmentValue.VariableName] =
                new WslEnvironmentValue(environment, annotation.Prefixes, existing);
        });
    }

    /// <summary>
    /// Calls <c>WithOtlpExporter()</c>, rewrites the host of the injected
    /// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> to <c>127.0.0.1</c> (keeping scheme and port), and
    /// forwards <c>OTEL_*</c> through <c>WSLENV</c> (R25). <c>127.0.0.1</c> rather than
    /// <c>localhost</c>, so the Linux resolver cannot pick <c>::1</c>.
    /// </summary>
    public static IResourceBuilder<WslExecutableResource> WithWslOtlpExporter(
        this IResourceBuilder<WslExecutableResource> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder
            .WithOtlpExporter()
            .WithEnvironment(context =>
            {
                var environment = context.EnvironmentVariables;
                if (environment.TryGetValue(OtlpEndpointVariable, out var value)
                    && RewriteToLoopback(value) is { } rewritten)
                {
                    environment[OtlpEndpointVariable] = rewritten;
                }
            })
            .WithWslEnvironment("OTEL_");
    }

    private static object? RewriteToLoopback(object value) => value switch
    {
        // Aspire 13.x injects the dashboard's OTLP endpoint as an EndpointReference (spike 0003
        // pitfall 2); keep its scheme and port, replace the host.
        EndpointReference endpoint => ReferenceExpression.Create(
            $"{endpoint.Property(EndpointProperty.Scheme)}://{WindowsLoopback}:{endpoint.Property(EndpointProperty.Port)}"),
        HostUrl hostUrl => new HostUrl(ReplaceHost(hostUrl.Url)),
        string url => ReplaceHost(url),
        _ => null,
    };

    private static string ReplaceHost(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? new UriBuilder(uri) { Host = WindowsLoopback }.Uri.ToString().TrimEnd('/')
            : url;

    private static async Task RunPreflightAsync(
        WslExecutableResource resource,
        BeforeResourceStartedEvent @event,
        CancellationToken cancellationToken)
    {
        var logger = @event.Services.GetRequiredService<ResourceLoggerService>().GetLogger(resource);
        var preflight = @event.Services.GetRequiredService<WslPreflight>();
        var result = await preflight.CheckAsync(resource.Distro, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            // Rule 3: the failure is visible on the resource, and the resource does not start.
            LogPreflightFailed(logger, result.Message);
            throw new WslPreflightException(result.Message);
        }

        LogPreflightPassed(logger, result.Message);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "WSL preflight failed: {Message}")]
    private static partial void LogPreflightFailed(ILogger logger, string message);

    [LoggerMessage(Level = LogLevel.Information, Message = "WSL preflight passed: {Message}")]
    private static partial void LogPreflightPassed(ILogger logger, string message);
}
