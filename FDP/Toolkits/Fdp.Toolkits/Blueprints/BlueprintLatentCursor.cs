using System.Runtime.InteropServices;

namespace Fdp.Toolkit.Blueprints;

/// <summary>
/// 16-byte cursor tracking the current latent execution point in a Blueprint graph.
/// Stored inline inside the entity's blackboard slot.
/// 8 bytes used (ResumeAt + WaitUntilTime) + 8 bytes reserved padding.
/// Per Runtime DD §3.x; must remain unmanaged.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 16)]
public struct BlueprintLatentCursor
{
    /// <summary>Encoded resume-point index within the latent coroutine state machine.</summary>
    public uint  ResumeAt;

    /// <summary>Absolute simulation time at which the latent wait expires.</summary>
    public float WaitUntilTime;

    // Remaining 8 bytes are reserved padding (enforced by StructLayout Size = 16).
}
