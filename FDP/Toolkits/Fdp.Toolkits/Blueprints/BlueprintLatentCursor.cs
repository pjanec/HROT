using System.Runtime.InteropServices;

namespace Fdp.Toolkit.Blueprints;

/// <summary>
/// 16-byte cursor tracking the current latent execution point in a Blueprint graph.
/// Stored inline inside the entity's blackboard slot.
/// Per Runtime DD §3.x and Compiler DD Q-18.1; must remain unmanaged.
/// Layout: ResumeAt(4) + WaitUntilTime(4) + InstanceVersion(4) + padding(4) = 16 bytes.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 16)]
public struct BlueprintLatentCursor
{
    /// <summary>Encoded resume-point index within the latent coroutine state machine.</summary>
    public uint  ResumeAt;

    /// <summary>Absolute simulation time at which the latent wait expires.</summary>
    public float WaitUntilTime;

    /// <summary>
    /// Instance version captured at suspend time (per Compiler DD Q-18.1).
    /// Staleness check: if this differs from the current slot InstanceVersion the cursor is stale.
    /// </summary>
    public uint  InstanceVersion;

    // 4 bytes reserved padding to maintain total struct size = 16.
}
