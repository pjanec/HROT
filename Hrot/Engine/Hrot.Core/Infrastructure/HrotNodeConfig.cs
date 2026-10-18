using CycloneDDS.Runtime;
using Fdp.Toolkit.Orchestration;

namespace Hrot.Common.Infrastructure;

/// <summary>
/// Minimal configuration required by <see cref="HrotNodeBuilder"/> to construct a
/// <see cref="HrotNodeContext"/>.
/// </summary>
public sealed class HrotNodeConfig
{
    /// <summary>
    /// ⭐ CE-4013 D13 — THE node config every networked host starts from: its domain, node id, role token and DDS participant, its staging
    /// under <c>&lt;staging&gt;/nodes/node-&lt;id&gt;</c> (<see cref="OrchestrationConstants.GetNodeStagingRoot(string, int)"/> — where the
    /// orchestrator writes each node's scenarios and TKB, <c>docs/DESIGN_Artifact_Staging.md</c> V1) and its logs beside the binaries.
    /// 📐 Five hosts built it inline and differed: IG and the Stride node took the BARE staging root (in a co-located run they read
    /// where nothing is written for them). A host sets only what is genuinely its own afterwards (a geo transform, a skipped routing wait).
    /// </summary>
    /// <param name="stagingRoot">The staging root, when the host was given one; null/empty ⇒ <see cref="OrchestrationConstants.ResolveStagingRoot"/>.</param>
    public static HrotNodeConfig ForNode(int domainId, int nodeId, string subsystemName, DdsParticipant? participant, string? stagingRoot = null)
        => new()
        {
            DomainId            = domainId,
            NodeId              = nodeId,
            SubsystemName       = subsystemName,
            Headless            = false,   // a networked host always has its participant; Headless here is not the window
            ExternalParticipant = participant,
            LocalTempRoot       = OrchestrationConstants.GetNodeStagingRoot(
                                      string.IsNullOrEmpty(stagingRoot) ? OrchestrationConstants.ResolveStagingRoot() : stagingRoot, nodeId),
            LogDirectory        = System.IO.Path.Combine(System.AppContext.BaseDirectory, "logs"),
        };

    /// <summary>CycloneDDS domain ID.</summary>
    public int DomainId { get; set; }

    /// <summary>Logical node identifier used in DDS heartbeats, entity IDs, and recording file names.</summary>
    public int NodeId { get; set; }

    /// <summary>Human-readable subsystem name published in heartbeats (e.g. "SimHost").</summary>
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
    /// ⭐ <c>CE-3126</c> — the node's ONE geo transform, when the composition root created it before the builder (SimHost
    /// closes over it while composing). <see cref="HrotNodeBuilder"/> then uses it as <c>HrotNodeContext.GeoTransform</c>
    /// instead of making another; otherwise the network factory's, otherwise a fresh one. 📄 docs/DESIGN_Geo_Origin.md §2 C.
    /// </summary>
    public Fdp.Modules.Geographic.IGeographicTransform? ExternalGeoTransform { get; set; }

    /// <summary>
    /// Directory where this node writes its log files.
    /// Used by <c>LogArchiveExtractionService</c> to locate and archive matching logs.
    /// Defaults to an empty string (feature disabled when empty).
    /// </summary>
    public string LogDirectory { get; set; } = string.Empty;
}
