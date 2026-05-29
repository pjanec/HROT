namespace Hrot.Utility.Editor.Model;

// Editor-side representation of per-consideration sensor parameters.
public sealed class InputParamsModel
{
    // FNV-1a of asset GUID -- EQS sensor readers.
    public uint  BlueprintId;
    // Maximum range in metres -- DistanceToContext readers.
    public float MaxRange;
    // Zero-based weapon mount index -- per-mount weapon readers.
    public int   MountIndex;
}
