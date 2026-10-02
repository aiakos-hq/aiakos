using Aiakos.Contracts.Node.V1;

namespace Aiakos.Contracts.Node;

public static class ErrorReasons
{
    public const string SeatNotFound = "SEAT_NOT_FOUND";
    public const string SeatAlreadyRunning = "SEAT_ALREADY_RUNNING";
    public const string SeatNotReady = "SEAT_NOT_READY";
    public const string SeatBusy = "SEAT_BUSY";
    public const string SeatStopping = "SEAT_STOPPING";
    public const string InvalidSessionId = "INVALID_SESSION_ID";
    public const string OrphanHarnessDetected = "ORPHAN_HARNESS_DETECTED";
    public const string Unsupported = "UNSUPPORTED";
    public const string PathNotAllowed = "PATH_NOT_ALLOWED";
    public const string PayloadTooLarge = "PAYLOAD_TOO_LARGE";
    public const string SessionHostError = "SESSION_HOST_ERROR";
    public const string InputNotAllowed = "INPUT_NOT_ALLOWED";
    public const string InvalidLaunch = "INVALID_LAUNCH";
    public const string SessionHostUnavailable = "SESSION_HOST_UNAVAILABLE";

    public static Error Create(string reason, string message, IReadOnlyDictionary<string, string>? metadata = null)
    {
        ErrorCode code = ErrorCode.Unspecified;
        bool retryable = false;
        switch (reason)
        {
            case SeatNotFound: code = ErrorCode.NotFound; break;
            case SeatAlreadyRunning: code = ErrorCode.AlreadyExists; break;
            case SeatNotReady:
            case SeatBusy: code = ErrorCode.FailedPrecondition; retryable = true; break;
            case SeatStopping: code = ErrorCode.Aborted; break;
            case InvalidSessionId:
            case PathNotAllowed:
            case InputNotAllowed:
            case InvalidLaunch: code = ErrorCode.InvalidArgument; break;
            case OrphanHarnessDetected: code = ErrorCode.FailedPrecondition; break;
            case Unsupported: code = ErrorCode.Unimplemented; break;
            case PayloadTooLarge: code = ErrorCode.ResourceExhausted; break;
            case SessionHostError: code = ErrorCode.Internal; retryable = true; break;
            case SessionHostUnavailable: code = ErrorCode.FailedPrecondition; break;
        }

        var error = new Error { Reason = reason, Message = message, Code = code, Retryable = retryable };
        if (metadata is not null)
        {
            foreach (KeyValuePair<string, string> item in metadata)
            {
                error.Metadata[item.Key] = item.Value;
            }
        }

        return error;
    }
}
