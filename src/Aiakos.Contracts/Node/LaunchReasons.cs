namespace Aiakos.Contracts.Node;

public static class LaunchReasons
{
    public const string ReadyTimeout = "READY_TIMEOUT";
    public const string ResumeSessionNotFound = "RESUME_SESSION_NOT_FOUND";
    public const string HarnessExited = "HARNESS_EXITED";
    public const string SessionIdMismatch = "SESSION_ID_MISMATCH";
    public const string Stopped = "STOPPED";
}
