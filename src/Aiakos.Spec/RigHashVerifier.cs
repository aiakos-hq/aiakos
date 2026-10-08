using System.Text.Json;

namespace Aiakos.Spec;

public sealed record RigHashVerification(bool Valid, string SpecHash, string BindingHash);

public static class RigHashVerifier
{
    public static RigHashVerification Verify(string resolvedJson, string specHash, string bindingHash)
    {
        try
        {
            using var document = JsonDocument.Parse(resolvedJson);
            var hashes = RigCanonicalizer.HashCanonicalJson(document.RootElement);
            return new RigHashVerification(
                StringComparer.Ordinal.Equals(specHash, hashes.SpecHash) &&
                StringComparer.Ordinal.Equals(bindingHash, hashes.BindingHash),
                hashes.SpecHash,
                hashes.BindingHash);
        }
        catch (FormatException exception) when (exception.Message == "Resolved JSON is invalid.")
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            throw new FormatException("Resolved JSON is invalid.", exception);
        }
    }
}
