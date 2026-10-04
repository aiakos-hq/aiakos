using System.Security.Cryptography;
using System.Text;

namespace Aiakos.Orchestrator.Link;

public sealed record NodeIdentity(string NodeId, Guid TenantId, string NodeName);

public sealed class NodeTokenRegistry
{
    private const string InvalidRegistrationMessage = "Invalid node registration.";
    private readonly Credential[] _credentials;

    public NodeTokenRegistry(IEnumerable<Aiakos.Orchestrator.NodeRegistration> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        var registrations = nodes.ToArray();
        var identities = new HashSet<string>(StringComparer.Ordinal);
        var digests = new HashSet<string>(StringComparer.Ordinal);
        var credentials = new Credential[registrations.Length];

        for (var index = 0; index < registrations.Length; index++)
        {
            var registration = registrations[index];
            if (registration is null || string.IsNullOrEmpty(registration.Id) || registration.TenantId == Guid.Empty ||
                !identities.Add(registration.Id))
                throw InvalidRegistration();

            var hasToken = !string.IsNullOrEmpty(registration.Token);
            var hasTokenHash = !string.IsNullOrEmpty(registration.TokenHash);
            if (hasToken == hasTokenHash)
                throw InvalidRegistration();

            byte[] digest;
            if (hasTokenHash)
            {
                if (registration.TokenHash.Length != 64 || !IsHex(registration.TokenHash))
                    throw InvalidRegistration();
                digest = Convert.FromHexString(registration.TokenHash);
            }
            else
            {
                digest = SHA256.HashData(Encoding.UTF8.GetBytes(registration.Token));
            }

            if (!digests.Add(Convert.ToHexString(digest)))
                throw InvalidRegistration();

            var name = string.IsNullOrEmpty(registration.Name) ? registration.Id : registration.Name;
            credentials[index] = new Credential(new NodeIdentity(registration.Id, registration.TenantId, name), digest);
        }

        _credentials = credentials;
        foreach (var registration in registrations)
            registration.Token = string.Empty;
    }

    public NodeIdentity? Authenticate(string? authorization)
    {
        const string prefix = "Bearer ";
        if (authorization is null || !authorization.StartsWith(prefix, StringComparison.Ordinal))
            return null;

        var token = authorization[prefix.Length..];
        if (token.Length == 0 || token.Any(char.IsWhiteSpace))
            return null;

        var suppliedDigest = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        foreach (var credential in _credentials)
        {
            if (CryptographicOperations.FixedTimeEquals(suppliedDigest, credential.Digest))
                return credential.Identity;
        }

        return null;
    }

    private static bool IsHex(string value)
    {
        foreach (var character in value)
        {
            if (!Uri.IsHexDigit(character))
                return false;
        }

        return true;
    }

    private static InvalidOperationException InvalidRegistration() =>
        new(InvalidRegistrationMessage);

    private sealed record Credential(NodeIdentity Identity, byte[] Digest);
}
