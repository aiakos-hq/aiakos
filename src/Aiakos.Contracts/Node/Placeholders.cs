using Google.Protobuf;
using System.Text;

namespace Aiakos.Contracts.Node;

public static class Placeholders
{
    public const string SeatHome = "${AIAKOS_SEAT_HOME}";
    public const string Workspace = "${AIAKOS_WORKSPACE}";

    public static string Expand(string text, string seatHome, string workspace)
    {
        return Replace(text, SeatHome, seatHome, Workspace, workspace);
    }

    public static ByteString Expand(ByteString content, string seatHome, string workspace)
    {
        byte[] input = content.ToByteArray();
        return ByteString.CopyFrom(Replace(
            input,
            Encoding.UTF8.GetBytes(SeatHome), Encoding.UTF8.GetBytes(seatHome),
            Encoding.UTF8.GetBytes(Workspace), Encoding.UTF8.GetBytes(workspace)));
    }

    private static string Replace(string input, string firstToken, string firstReplacement, string secondToken, string secondReplacement)
    {
        var output = new StringBuilder(input.Length);
        for (int i = 0; i < input.Length;)
        {
            if (input.AsSpan(i).StartsWith(firstToken, StringComparison.Ordinal))
            {
                output.Append(firstReplacement);
                i += firstToken.Length;
            }
            else if (input.AsSpan(i).StartsWith(secondToken, StringComparison.Ordinal))
            {
                output.Append(secondReplacement);
                i += secondToken.Length;
            }
            else
            {
                output.Append(input[i++]);
            }
        }

        return output.ToString();
    }

    private static byte[] Replace(byte[] input, byte[] firstToken, byte[] firstReplacement, byte[] secondToken, byte[] secondReplacement)
    {
        using var output = new MemoryStream(input.Length);
        for (int i = 0; i < input.Length;)
        {
            if (i + firstToken.Length <= input.Length && input.AsSpan(i, firstToken.Length).SequenceEqual(firstToken))
            {
                output.Write(firstReplacement);
                i += firstToken.Length;
            }
            else if (i + secondToken.Length <= input.Length && input.AsSpan(i, secondToken.Length).SequenceEqual(secondToken))
            {
                output.Write(secondReplacement);
                i += secondToken.Length;
            }
            else
            {
                output.WriteByte(input[i++]);
            }
        }

        return output.ToArray();
    }
}
