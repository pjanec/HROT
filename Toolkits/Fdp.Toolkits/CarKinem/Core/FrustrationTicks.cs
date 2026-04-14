using System.Runtime.InteropServices;
using Fdp.Kernel;

namespace CarKinem.Core
{
    /// <summary>
    /// Tracks consecutive ticks below the frustration speed threshold for a navigating entity.
    ///
    /// <para>
    /// Replaces the <c>Dictionary&lt;int, int&gt; _frustrationTicks</c> that lived inside
    /// <c>NavigationExecutionSystem</c>.  Storing the counter directly on the entity gives:
    /// <list type="bullet">
    ///   <item>Automatic memory reclamation when the entity is destroyed (no leak).</item>
    ///   <item>O(1) access via component slot — no dictionary lookup per tick.</item>
    ///   <item>Stale-state isolation: multiple entities never share a counter bucket.</item>
    /// </list>
    /// </para>
    ///
    /// <para>The component is written exclusively by <c>NavigationExecutionSystem</c> and is
    /// reset to zero whenever a new <see cref="FDP.Toolkit.Navigation.NavigationIntent"/> is
    /// detected (intent-ID mismatch).  Consumer code must not write this component.</para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.FrustrationTicks)]
    public struct FrustrationTicks
    {
        /// <summary>
        /// Number of consecutive simulation ticks during which the entity's speed was below
        /// <c>NavigationExecutionSystem.FrustrationSpeedThreshold</c>.
        /// Reset to zero when the vehicle accelerates above the threshold or a new intent begins.
        /// </summary>
        public int Ticks;
    }
}
