using Aiakos.Orchestrator;
using Aiakos.Orchestrator.Link;

namespace Aiakos.Orchestrator.Tests.Link;

public sealed class NodeTokenRegistryTests
{
    private static readonly Guid DefaultTenant = Guid.Parse("00000000-0000-0000-0000-000000000001");

    [Fact]
    public void SnapshotsRegistrationAndAuthenticatesWithoutRetainingPlaintext()
    {
        var registration = new NodeRegistration { Id = "node-1", Name = "machine", Token = "bootstrap-secret" };
        var registry = new NodeTokenRegistry([registration]);

        var identity = registry.Authenticate("Bearer bootstrap-secret");

        Assert.Equal(new NodeIdentity("node-1", DefaultTenant, "machine"), identity);
        Assert.Equal(string.Empty, registration.Token);
    }

    [Fact]
    public void AuthenticatesTheDefaultNameAndConfiguredTenant()
    {
        var registration = new NodeRegistration
        {
            Id = "opaque/id",
            Token = "token-value",
            TenantId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")
        };
        var registry = new NodeTokenRegistry([registration]);

        Assert.Equal(new NodeIdentity(registration.Id, registration.TenantId, registration.Id),
            registry.Authenticate("Bearer token-value"));
    }

    [Fact]
    public void AuthenticatesTokenHashRegistrations()
    {
        var registration = new NodeRegistration
        {
            Id = "hashed",
            TokenHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes("hash-source")))
        };
        var registry = new NodeTokenRegistry([registration]);

        Assert.Equal(new NodeIdentity("hashed", DefaultTenant, "hashed"), registry.Authenticate("Bearer hash-source"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Basic token")]
    [InlineData("Bearer ")]
    [InlineData("bearer token")]
    [InlineData("Bearer token with-space")]
    [InlineData("Bearer unknown-token")]
    public void RejectsMalformedOrUnknownAuthorization(string? authorization)
    {
        var registry = new NodeTokenRegistry([new NodeRegistration { Id = "n", Token = "registered" }]);

        Assert.Null(registry.Authenticate(authorization));
    }

    [Fact]
    public void RejectsDuplicateOrMalformedRegistrationsWithTheFixedError()
    {
        AssertInvalid(new NodeRegistration { Id = "" , Token = "token" });
        AssertInvalid(new NodeRegistration { Id = "n", Token = "token", TokenHash = new string('0', 64) });
        AssertInvalid(new NodeRegistration { Id = "n", TokenHash = "not-64-hex" });
        AssertInvalid(new NodeRegistration { Id = "n" });
        AssertInvalid(new NodeRegistration { Id = "n", Token = "token", TenantId = Guid.Empty });
        AssertInvalid(
            new NodeRegistration { Id = "same", Token = "one" },
            new NodeRegistration { Id = "same", Token = "two" });
        AssertInvalid(
            new NodeRegistration { Id = "one", Token = "duplicate" },
            new NodeRegistration { Id = "two", Token = "duplicate" });
    }

    private static void AssertInvalid(params NodeRegistration[] registrations)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new NodeTokenRegistry(registrations));
        Assert.Equal("Invalid node registration.", exception.Message);
    }
}
