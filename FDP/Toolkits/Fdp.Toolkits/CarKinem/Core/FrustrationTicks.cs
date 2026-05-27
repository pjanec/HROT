using System.Runtime.InteropServices;
using Fdp.Core;

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
    /// reset to zero whenever a new <see cref="Fdp.Toolkit.Navigation.NavigationIntent"/> is
    /// detected (intent-ID mismatch).  Consumer code must not write this component.</para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.FrustrationTicks)]
    [DataPolicy(DataPolicy.NoSave)]
    public struct FrustrationTicks
    {
        /// <summary>
        /// Number of consecutive simulation ticks during which the entity's speed was below
        /// <c>NavigationExecutionSystem.FrustrationSpeedThreshold</c>.
        /// Reset to zero when the vehicle accelerates above the threshold or a new intent begins.
        /// </summary>
        public int Ticks;

        /// <summary>
        /// 1 once <see cref="Fdp.Toolkit.Navigation.PathfindingEvents.MoveStartedEvent"/> has been
        /// fired for the current intent, 0 before. Reset to 0 with the rest of the struct when a
        /// new intent begins.
        /// </summary>
        public byte MoveStartedFired;

        /// <summary>
        /// 1 once <see cref="Fdp.Toolkit.Navigation.PathfindingEvents.MoveBlockedEvent"/> has been
        /// fired for the current frustration episode, 0 before.  Reset to 0 when frustration
        /// resets (new intent or post-replan).
        /// </summary>
        public byte BlockedEventFired;

        // 2 bytes explicit padding to keep the struct size at 8 bytes (int + 4 x byte).
        private byte _pad0;
        private byte _pad1;
    }
}
