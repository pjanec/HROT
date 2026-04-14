namespace Hrot.Core.Network;

/// <summary>
/// Numeric ordinals for the NED descriptor types used as keys in the JSON/binary
/// attribute compilation pipeline. Values match <c>Hrot.NED.Descriptors.EDescriptorType</c>
/// so existing compiled data does not need migration.
/// These constants allow <c>Hrot.SimHost</c> to build attribute compilers without a
/// direct reference to <c>Hrot.Network.NED</c>.
/// </summary>
public static class DescriptorTypeOrdinals
{
    public const long EntityMaster         = 0L;
    public const long EntityInfo           = 1L;
    public const long WorldPos             = 2L;
    public const long MapVisualOverlay     = 3L;
    public const long MapRoute             = 4L;
    public const long EntityMission        = 51L;
    public const long NavigationIntent     = 52L;
    public const long NavigationStatus     = 53L;
    public const long DeferredTakeOwnership = 54L;
    public const long OwnershipUpdate      = 55L;
}
