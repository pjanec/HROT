using System.Runtime.InteropServices;
using Fdp.Kernel;

namespace Fdp.Toolkit.Perception.Components
{
    /// <summary>
    /// Per-entity optical sensor configuration.
    /// Attach to any entity that performs visual (optical) scanning.
    /// Complements <see cref="PerceptionReceptor"/> for modality-specific queries.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.VisualReceptor)]
    public struct VisualReceptor
    {
        /// <summary>Maximum detection range (metres) for optical sensors.</summary>
        public float VisionRange;

        /// <summary>
        /// Precomputed cosine of the half field-of-view angle used for fast cone tests.
        /// Example: 60° FOV → cos(30°) ≈ 0.866f.
        /// </summary>
        public float FovCos;
    }
}
