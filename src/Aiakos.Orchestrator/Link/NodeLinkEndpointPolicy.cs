using System.Net;
using System.Net.Sockets;

namespace Aiakos.Orchestrator.Link;

public static class NodeLinkEndpointPolicy
{
    private const string PolicyError = "NodeLink requires an explicit loopback HTTP endpoint.";

    public static bool IsAllowed(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
            return true;

        var host = uri.Host.Trim('[', ']');
        if (!IPAddress.TryParse(host, out var ipAddress))
            return false;

        if (ipAddress.AddressFamily == AddressFamily.InterNetwork)
            return ipAddress.GetAddressBytes()[0] == 127;

        return ipAddress.AddressFamily == AddressFamily.InterNetworkV6 && IPAddress.IPv6Loopback.Equals(ipAddress);
    }

    public static void Validate(IEnumerable<string> addresses)
    {
        if (addresses is null)
            throw new InvalidOperationException(PolicyError);

        var any = false;
        foreach (var address in addresses)
        {
            any = true;
            if (!IsAllowed(address))
                throw new InvalidOperationException(PolicyError);
        }

        if (!any)
            throw new InvalidOperationException(PolicyError);
    }

    public static void ValidateConfigured(IEnumerable<string> addresses, int? grpcPort)
    {
        var eligible = addresses
            .Where(address => grpcPort is null ||
                Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Port == grpcPort.Value)
            .ToArray();
        Validate(eligible);
    }

    public static void ValidateResolved(EndPoint? endpoint, int? grpcPort)
    {
        if (endpoint is IPEndPoint ipEndpoint && grpcPort is { } port && ipEndpoint.Port != port)
            return;
        if (endpoint is not IPEndPoint resolved || !IPAddress.IsLoopback(resolved.Address))
            throw new InvalidOperationException(PolicyError);
    }
}
