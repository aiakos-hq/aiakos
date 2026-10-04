namespace Aiakos.Spec;

public sealed record EmbeddedFile(string Path, string Sha256, byte[] Content)
{
    public int Bytes => Content.Length;
}
