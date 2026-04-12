namespace Hrot.Common.Infrastructure;

/// <summary>
/// Minimal configuration required by <see cref="HrotNodeBuilder"/> to construct a
/// <see cref="HrotNodeContext"/>.
/// </summary>
public sealed class HrotNodeConfig
{
    /// <summary>CycloneDDS domain ID.</summary>
    public int DomainId { get; set; }

    /// <summary>Logical node identifier used in DDS heartbeats, entity IDs, and recording file names.</summary>
    public int NodeId { get; set; }

    /// <summary>Human-readable subsystem name published in heartbeats (e.g. "EyesAndMuscle").</summary>
    public string SubsystemName { get; set; } = string.Empty;

    /// <summary>
    /// Root directory for scenario staging and checkpoint storage.
    /// Defaults to <c>C:\FDP_Temp</c>.
    /// </summary>
    public string LocalTempRoot { get; set; } = @"C:\FDP_Temp";

    /// <summary>
    /// When <c>true</c>, all DDS-related initialization steps are skipped
    /// (no participant, no ID allocator, no DDS slave translator).
    /// Intended for unit tests and headless environments without a running DDS stack.
    /// </summary>
    public bool Headless { get; set; }

    /// <summary>
    /// When <c>true</c>, the <see cref="DdsIdAllocatorHelper.EnsureRouting"/> wait is
    /// skipped even when <see cref="Headless"/> is <c>false</c>.
    /// Use for subsystems (e.g. IG) that create their own allocator and do not depend
    /// on the builder-owned <see cref="HrotNodeContext.IdAllocator"/>.
    /// </summary>
    public bool SkipAllocatorRouting { get; set; }
}
