using Aiakos.Contracts.Node.V1;
using Aiakos.Orchestrator.Seats;
using Aiakos.Spec;

namespace Aiakos.Orchestrator.Harnesses;

public interface IHarnessAdapter : Aiakos.Core.IHarnessAdapter
{
    string Harness { get; }
    IHarnessStateProfile Profile { get; }
    LaunchSpec BuildLaunch(ResolvedSeatParameters seat, LaunchMode mode, NativeSession session,
        IReadOnlyList<SeatFile> suppliedFiles);
    DeliverySpec BuildDelivery(string authenticatedSender, string body, Guid commandId);
}

public sealed record NativeSession(string Id);

public sealed record LaunchSpec(IReadOnlyList<string> Argv, IReadOnlyDictionary<string, string> Env,
    IReadOnlyList<SeatFile> Files, TimeSpan ReadyTimeout, TerminalSize Terminal);

public sealed record DeliverySpec(string Lead, string Body, bool ExpectConfirmation,
    TimeSpan ConfirmTimeout);
