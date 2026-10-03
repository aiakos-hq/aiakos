using Aiakos.Core;

namespace Aiakos.Node.Tests.Sessions;

public sealed class InputValidatorTests
{
    [Fact]
    public void AllowsAnEmptyLead()
    {
        Assert.Equal(new LeadCheck(InputProblem.None, null), InputValidator.CheckLead(string.Empty));
    }

    [Fact]
    public void ChecksLeadLengthInUtf8Bytes()
    {
        Assert.Equal(InputProblem.None, InputValidator.CheckLead(new string('x', 1024)).Problem);
        Assert.Equal(InputProblem.NotAllowed, InputValidator.CheckLead(new string('x', 1025)).Problem);
    }

    [Theory]
    [InlineData("\0")]
    [InlineData("\t")]
    [InlineData("\n")]
    [InlineData("\u007f")]
    [InlineData("\u0085")]
    public void RejectsLeadControlCharacters(string lead)
    {
        Assert.Equal(InputProblem.NotAllowed, InputValidator.CheckLead(lead).Problem);
    }

    [Fact]
    public void RejectsALeadWithAnUnpairedSurrogate()
    {
        Assert.Equal(InputProblem.NotAllowed, InputValidator.CheckLead(new string('\ud800', 1)).Problem);
    }

    [Fact]
    public void AllowsALeadEndingInSemicolon()
    {
        Assert.Equal(InputProblem.None, InputValidator.CheckLead("run;").Problem);
    }

    [Fact]
    public void RejectsALeadEndingInEscapedSemicolon()
    {
        var check = InputValidator.CheckLead("run\\;");

        Assert.Equal(InputProblem.NotAllowed, check.Problem);
        Assert.NotNull(check.Reason);
        Assert.DoesNotContain("run", check.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\u001b")]
    [InlineData("\u007f")]
    [InlineData("\u0085")]
    public void RejectsInvalidBodyCharacters(string body)
    {
        var check = InputValidator.CheckBody(body);

        Assert.Equal(InputProblem.NotAllowed, check.Problem);
        Assert.NotNull(check.Reason);
        Assert.DoesNotContain(body, check.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsABodyWithAnUnpairedSurrogate()
    {
        var check = InputValidator.CheckBody(new string('\ud800', 1));

        Assert.Equal(InputProblem.NotAllowed, check.Problem);
        Assert.Contains("unpaired surrogate", check.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("a\r\nb", "a\nb")]
    [InlineData("a\rb", "a\nb")]
    [InlineData("a\r\nb\rc", "a\nb\nc")]
    public void NormalizesBodyLineEndings(string body, string expected)
    {
        var check = InputValidator.CheckBody(body);

        Assert.Equal(InputProblem.None, check.Problem);
        Assert.Equal(expected, check.Normalized);
        Assert.True(check.LineEndingsNormalized);
    }

    [Fact]
    public void LeavesUnchangedLineEndingsUnmarked()
    {
        var check = InputValidator.CheckBody("a\nb");

        Assert.False(check.LineEndingsNormalized);
        Assert.Equal(3, check.Bytes);
    }

    [Fact]
    public void ChecksBodySizeInNormalizedUtf8Bytes()
    {
        Assert.Equal(InputProblem.None, InputValidator.CheckBody(new string('x', InputValidator.MaxBodyBytes)).Problem);
        Assert.Equal(InputProblem.TooLarge, InputValidator.CheckBody(new string('x', InputValidator.MaxBodyBytes + 1)).Problem);
    }

    [Fact]
    public void CountsMultibyteBodyCharactersAsUtf8Bytes()
    {
        var body = new string('é', InputValidator.MaxBodyBytes / 2 + 1);
        var check = InputValidator.CheckBody(body);

        Assert.Equal(InputProblem.TooLarge, check.Problem);
        Assert.Equal(InputValidator.MaxBodyBytes + 2, check.Bytes);
    }
}
