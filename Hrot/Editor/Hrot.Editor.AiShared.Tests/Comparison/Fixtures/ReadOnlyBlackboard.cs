// HROT_EDITOR_GENERATED - managed by AI editor; manual edits to this file will be overwritten on next save.
// OwningAssetId: dddddddd-face-0001-0000-000000000001
// OwningAssetName: ReadOnlyAsset_BT

using System.Runtime.InteropServices;

namespace Hrot.AI.Behaviors.Trees;

[StructLayout(LayoutKind.Sequential)]
public partial struct ReadOnlyAsset_BT_Blackboard
{
    /// <summary>External target position, injected from the parent context. Read-only.</summary>
    [ReadOnly]
    public float TargetX;

    /// <summary>External target entity identifier. Read-only.</summary>
    [ReadOnly]
    public int TargetEntityId;

    /// <summary>Elapsed simulation time, provided by the runtime. Read-only.</summary>
    [ReadOnly]
    public double ElapsedTime;
}
