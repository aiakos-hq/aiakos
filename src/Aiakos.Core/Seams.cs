namespace Aiakos.Core;

// Rule-5 seams (CLAUDE.md): host-specific concerns stay behind these interfaces. They are empty
// placeholders in the skeleton (spec 0001 R43); the issue named on each one designs its members,
// and later specs may move them.

#pragma warning disable CA1040 // Empty interfaces are intentional placeholders until their specs land.

/// <summary>Sandbox around a seat (none, Docker, brig). Designed in M6.</summary>
public interface ISandbox;

/// <summary>Harness adapter on the orchestrator (ADR 0018). Designed by issue #12 (spec 0005).</summary>
public interface IHarnessAdapter;

/// <summary>Channel that delivers input to a seat. Designed by issue #13 (spec 0006).</summary>
public interface ISeatChannel;

/// <summary>Chat connector (Telegram, Slack). Designed in M4.</summary>
public interface IChatConnector;

#pragma warning restore CA1040
