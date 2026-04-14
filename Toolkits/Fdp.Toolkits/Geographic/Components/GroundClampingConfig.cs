using Fdp.Core;
using Fdp.Toolkit.Geographic;

namespace Fdp.Modules.Geographic.Components
{
    /// <summary>
    /// Per-entity configuration component written by the
    /// <c>GroundClampingOverrideTranslator</c> when a DDS override arrives.
    /// Read by <c>TerrainQuerySubmitSystem</c> to determine whether to issue a
    /// terrain query for this entity each frame.
    /// </summary>
    [ComponentId(GeographicComponentIds.GroundClampingConfig)]
    public struct GroundClampingConfig
    {
        /// <summary>Network-controlled clamping mode for this entity.</summary>
        public EClampingMode Mode;

        /// <summary>
        /// Seeded from the TKB blueprint: <c>1</c> = grounded vehicle (default clamped),
        /// <c>0</c> = aircraft or floating entity (default unclamped).
        /// Evaluated only when <see cref="Mode"/> is <see cref="EClampingMode.Default"/>.
        /// </summary>
        public byte BaseRequiresClamping;

        /// <summary>
        /// Returns <c>true</c> when terrain clamping should be active for this entity:
        /// <list type="bullet">
        ///   <item><see cref="EClampingMode.ForceOn"/> — always clamped.</item>
        ///   <item><see cref="EClampingMode.Default"/> + <see cref="BaseRequiresClamping"/> == 1 — blueprint default clamped.</item>
        ///   <item>All other combinations — unclamped.</item>
        /// </list>
        /// </summary>
        public readonly bool IsClampingActive =>
            Mode == EClampingMode.ForceOn ||
            (Mode == EClampingMode.Default && BaseRequiresClamping == 1);
    }
}
