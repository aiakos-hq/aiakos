using System.Net;

namespace Aiakos.Orchestrator.Link;

public static class NodeLinkEndpointPolicy
{
    private const string PolicyError = "NodeLink requires an explicit loopback HTTP endpoint.";

    public static bool IsAllowed(string? address)
    {
        const string scheme = "http://";
        if (string.IsNullOrEmpty(address) || address.Length <= scheme.Length ||
            address.Any(static character => character > 0x7f || char.IsWhiteSpace(character)) ||
            !address.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
            return false;

        var authority = address.AsSpan(scheme.Length);
        ReadOnlySpan<char> host;
        ReadOnlySpan<char> port = default;
        var hasPort = false;

        if (authority[0] == '[')
        {
            var closeBracket = authority.IndexOf(']');
            if (closeBracket < 0)
                return false;

            host = authority[..(closeBracket + 1)];
            var remainder = authority[(closeBracket + 1)..];
            if (!remainder.IsEmpty)
            {
                if (remainder[0] != ':')
                    return false;
                hasPort = true;
                port = remainder[1..];
            }

            if (!host.SequenceEqual("[::1]"))
                return false;
        }
        else
        {
            var colon = authority.IndexOf(':');
            if (colon >= 0)
            {
                if (authority[(colon + 1)..].IndexOf(':') >= 0)
                    return false;
                host = authority[..colon];
                port = authority[(colon + 1)..];
                hasPort = true;
            }
            else
            {
                host = authority;
            }

            if (!host.Equals("localhost", StringComparison.OrdinalIgnoreCase) && !IsLoopbackIpv4(host))
                return false;
        }

        return !hasPort || IsValidPort(port);
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

    private static bool IsLoopbackIpv4(ReadOnlySpan<char> host)
    {
        var octetIndex = 0;
        var start = 0;
        var first = -1;
        for (var index = 0; index <= host.Length; index++)
        {
            if (index < host.Length && host[index] != '.')
                continue;

            var octet = host[start..index];
            if (!TryParseCanonicalNumber(octet, 255, out var value))
                return false;
            if (octetIndex == 0)
                first = value;
            octetIndex++;
            start = index + 1;
        }

        return octetIndex == 4 && first == 127;
    }

    private static bool IsValidPort(ReadOnlySpan<char> port) =>
        TryParseCanonicalNumber(port, 65535, out var value) && value > 0;

    private static bool TryParseCanonicalNumber(ReadOnlySpan<char> text, int maximum, out int value)
    {
        value = 0;
        if (text.IsEmpty || (text.Length > 1 && text[0] == '0'))
            return false;

        foreach (var character in text)
        {
            if (character is < '0' or > '9')
                return false;
            var digit = character - '0';
            if (value > (maximum - digit) / 10)
                return false;
            value = value * 10 + digit;
        }

        return true;
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
