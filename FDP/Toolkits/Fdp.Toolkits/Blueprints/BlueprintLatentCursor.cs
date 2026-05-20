using System.Runtime.InteropServices;

namespace Fdp.Toolkit.Blueprints;

/// <summary>
/// 16-byte cursor tracking the current latent execution point in a Blueprint graph.
/// Stored inline inside the entity's blackboard slot.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 16)]
public struct BlueprintLatentCursor
{
    public Guid GraphId;
    // Reserved bytes for frame counter / sub-state are part of the 16-byte budget.
}
