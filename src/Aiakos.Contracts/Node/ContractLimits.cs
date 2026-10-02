namespace Aiakos.Contracts.Node;

public static class ContractLimits
{
    public const int MaxMessageBytes = 4 * 1024 * 1024;
    public const int MaxDeliverBodyBytes = 1024 * 1024;
    public const int MaxStartSeatFilesBytes = 2 * 1024 * 1024;
    public const int MaxPaneCaptureBytes = 1024 * 1024;
    public const int MaxHarnessRawBytes = 256 * 1024;
    public const int MaxAttributeValueBytes = 1024;
}
