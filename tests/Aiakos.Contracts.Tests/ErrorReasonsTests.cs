using Aiakos.Contracts.Node;
using Aiakos.Contracts.Node.V1;

namespace Aiakos.Contracts.Tests;

[Trait("Category", "Contract")]
public sealed class ErrorReasonsTests
{
    [Theory]
    [InlineData("SEAT_NOT_FOUND", ErrorCode.NotFound, false)]
    [InlineData("SEAT_ALREADY_RUNNING", ErrorCode.AlreadyExists, false)]
    [InlineData("SEAT_NOT_READY", ErrorCode.FailedPrecondition, true)]
    [InlineData("SEAT_BUSY", ErrorCode.FailedPrecondition, true)]
    [InlineData("SEAT_STOPPING", ErrorCode.Aborted, false)]
    [InlineData("INVALID_SESSION_ID", ErrorCode.InvalidArgument, false)]
    [InlineData("ORPHAN_HARNESS_DETECTED", ErrorCode.FailedPrecondition, false)]
    [InlineData("UNSUPPORTED", ErrorCode.Unimplemented, false)]
    [InlineData("PATH_NOT_ALLOWED", ErrorCode.InvalidArgument, false)]
    [InlineData("PAYLOAD_TOO_LARGE", ErrorCode.ResourceExhausted, false)]
    [InlineData("SESSION_HOST_ERROR", ErrorCode.Internal, true)]
    [InlineData("INPUT_NOT_ALLOWED", ErrorCode.InvalidArgument, false)]
    [InlineData("INVALID_LAUNCH", ErrorCode.InvalidArgument, false)]
    [InlineData("SESSION_HOST_UNAVAILABLE", ErrorCode.FailedPrecondition, false)]
    public void CreatesTheCatalogueError(string reason, ErrorCode code, bool retryable)
    {
        Error result = ErrorReasons.Create(reason, "x", new Dictionary<string, string> { ["field"] = "value" });
        Assert.Equal(reason, result.Reason);
        Assert.Equal("x", result.Message);
        Assert.Equal(code, result.Code);
        Assert.Equal(retryable, result.Retryable);
        Assert.Equal("value", result.Metadata["field"]);
    }

    [Fact]
    public void UnknownReasonsAreUnspecifiedAndNotRetryable()
    {
        Error result = ErrorReasons.Create("UNKNOWN", "x");
        Assert.Equal(ErrorCode.Unspecified, result.Code);
        Assert.False(result.Retryable);
    }
}
