using CycloneDDS.Runtime;
using Fdp.Toolkit.Orchestration;

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
    /// Defaults to <see cref="OrchestrationConstants.ResolveStagingRoot"/>.
    /// </summary>
    public string LocalTempRoot { get; set; } = OrchestrationConstants.ResolveStagingRoot();

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

    /// <summary>
    /// Optional DDS participant provided by the composition root.
    /// When non-null, <see cref="HrotNodeBuilder"/> uses this participant instead of
    /// calling <c>HrotEnvironment.CreateParticipant</c>.
    /// </summary>
    public CycloneDDS.Runtime.DdsParticipant? ExternalParticipant { get; set; }

    /// <summary>
    /// Directory where this node writes its log files.
    /// Used by <c>LogArchiveExtractionService</c> to locate and archive matching logs.
    /// Defaults to an empty string (feature disabled when empty).
    /// </summary>
    public string LogDirectory { get; set; } = string.Empty;
}
