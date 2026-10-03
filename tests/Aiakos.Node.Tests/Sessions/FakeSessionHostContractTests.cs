using Aiakos.Node.Sessions;
using Aiakos.Node.Testing;

namespace Aiakos.Node.Tests.Sessions;

public sealed class FakeSessionHostContractTests : SessionHostContractTests
{
    protected override Task<ISessionHostRig> CreateRigAsync() =>
        Task.FromResult<ISessionHostRig>(new FakeSessionHostRig());
}
