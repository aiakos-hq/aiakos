using System.Globalization;
using Akka.TestKit;
using Xunit;

namespace Aiakos.Orchestrator.Tests.Seats;

internal sealed class XunitV3TestKitAssertions : ITestKitAssertions
{
    public void Fail(string format, params object[] args) => Assert.Fail(Format(format, args));

    public void AssertTrue(bool condition, string format, params object[] args) =>
        Assert.True(condition, Format(format, args));

    public void AssertFalse(bool condition, string format, params object[] args) =>
        Assert.False(condition, Format(format, args));

    public void AssertEqual<T>(T expected, T actual, string format, params object[] args) =>
        Assert.Equal(expected, actual);

    public void AssertEqual<T>(T expected, T actual, Func<T, T, bool> comparer, string format, params object[] args) =>
        Assert.True(comparer(expected, actual), Format(format, args));

    public Exception AssertThrows(Action action) => Assert.ThrowsAny<Exception>(action);

    public TException AssertThrows<TException>(Action action) where TException : Exception =>
        Assert.Throws<TException>(action);

    public Task<Exception> AssertThrowsAsync(Func<Task> action) => Assert.ThrowsAnyAsync<Exception>(action);

    public Task<TException> AssertThrowsAsync<TException>(Func<Task> action) where TException : Exception =>
        Assert.ThrowsAsync<TException>(action);

    private static string Format(string format, object[] args) =>
        args.Length == 0 ? format : string.Format(CultureInfo.InvariantCulture, format, args);
}
