using System.Text.Json;

using Aiakos.Spec;

namespace Aiakos.Spec.Tests;

public sealed class CanonicalJsonTests
{
    [Fact]
    public void WritesCompactNfcOrdinalJsonWithExactEscaping()
    {
        using var document = JsonDocument.Parse("{\"z\":null,\"s\":\"e\\u0301<&/\",\"b\":true,\"a\":[2,1]}");

        Assert.Equal("{\"a\":[2,1],\"b\":true,\"s\":\"é<&/\",\"z\":null}", CanonicalJson.Write(document.RootElement));

        using var controls = JsonDocument.Parse("\"\\u0000\\b\\t\\n\\f\\r\\u001f\\\"\\\\😀\"");
        Assert.Equal("\"\\u0000\\b\\t\\n\\f\\r\\u001f\\\"\\\\😀\"", CanonicalJson.Write(controls.RootElement));

        using var nested = JsonDocument.Parse("{\"outer\":{\"z\":1,\"a\":\"\\ud83d\\ude00\"}}");
        Assert.Equal("{\"outer\":{\"a\":\"😀\",\"z\":1}}", CanonicalJson.Write(nested.RootElement));
    }

    [Theory]
    [InlineData("1.5")]
    [InlineData("1.0")]
    [InlineData("1e0")]
    [InlineData("9223372036854775808")]
    [InlineData("{\"a\":1,\"a\":2}")]
    [InlineData("{\"é\":1,\"e\\u0301\":2}")]
    [InlineData("\"\\ud800\"")]
    [InlineData("{\"\\ud800\":1}")]
    public void RejectsUnsupportedJsonWithExactException(string json)
    {
        using var document = JsonDocument.Parse(json);

        var exception = Assert.Throws<ArgumentException>(() => CanonicalJson.Write(document.RootElement));

        Assert.Equal("unsupported canonical JSON value", exception.Message);
        Assert.Null(exception.ParamName);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void WritesNegativeZeroAsZeroAndRejectsUndefined()
    {
        using var negativeZero = JsonDocument.Parse("-0");

        Assert.Equal("0", CanonicalJson.Write(negativeZero.RootElement));

        var exception = Assert.Throws<ArgumentException>(() => CanonicalJson.Write(default));
        Assert.Equal("unsupported canonical JSON value", exception.Message);
        Assert.Null(exception.ParamName);
        Assert.Null(exception.InnerException);
    }
}
