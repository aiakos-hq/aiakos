using Aiakos.Contracts.Node.V1;
using Google.Protobuf;
using System.Text;

namespace Aiakos.Contracts.Node;

public static class HarnessEventLimits
{
    public static void Apply(HarnessEvent harnessEvent)
    {
        harnessEvent.RawSize = (uint)harnessEvent.Raw.Length;
        if (harnessEvent.Raw.Length > ContractLimits.MaxHarnessRawBytes)
        {
            harnessEvent.Raw = ByteString.CopyFrom(harnessEvent.Raw.Span[..ContractLimits.MaxHarnessRawBytes]);
            harnessEvent.RawTruncated = true;
        }

        foreach (string key in harnessEvent.Attributes.Keys.ToArray())
        {
            string value = harnessEvent.Attributes[key];
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            if (bytes.Length > ContractLimits.MaxAttributeValueBytes)
            {
                int length = ContractLimits.MaxAttributeValueBytes;
                var encoding = new UTF8Encoding(false, true);
                while (length > 0)
                {
                    try
                    {
                        harnessEvent.Attributes[key] = encoding.GetString(bytes, 0, length);
                        break;
                    }
                    catch (DecoderFallbackException)
                    {
                        length--;
                    }
                }
            }
        }
    }
}
