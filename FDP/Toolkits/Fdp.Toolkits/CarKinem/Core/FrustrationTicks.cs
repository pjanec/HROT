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
    [DataPolicy(DataPolicy.NoScenario)]
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

        // 2 bytes explicit padding to keep the float field naturally aligned.
        /// <summary>⭐ <c>CE-3457</c> — 1 while the vehicle slows or stands for an ANIMAL in its way (written by <c>CarKinematicsSystem</c>, the hull's own entity):
        /// a deliberate hold, so the watchdog neither counts it nor fails the move (was the first padding byte — the layout is unchanged).</summary>
        public byte YieldingToAnimal;
        private byte _pad1;

        /// <summary>
        /// Accumulated seconds the entity has been frustrated (speed below threshold) for
        /// the current intent.  Used by <c>NavigationExecutionSystem</c> to enforce
        /// <see cref="Fdp.Toolkit.Navigation.NavigationIntent.ReplanTimeBudget"/>.
        /// Reset to 0 when a new intent begins.
        /// </summary>
        public float ElapsedSinceFirstReplan;

        /// <summary>
        /// ⭐ <c>CE-2304</c> — the furthest path progress (<c>NavState.ProgressS</c>, metres) this move has reached in the current
        /// no-progress window. Appended.
        /// </summary>
        public float BestProgressS;

        /// <summary>
        /// ⭐ <c>CE-2304</c> — seconds the move has gone without gaining <c>NavigationExecutionSystem.NoProgressMinGainM</c> on
        /// <see cref="BestProgressS"/> while it should be walking. Appended.
        /// </summary>
        public SimDuration NoProgressSeconds;

        // ⭐ CE-3286 (R-3025; docs/designs/motion-models/DESIGN_Motion_Domains.md §5f) — the held hull's recovery (HullRecovery), APPENDED.
        //   The watchdog's remedy, so it lives in the watchdog's never-sent state; a new move (a new IntentId) clears it with the rest.

        /// <summary>How long the wall guard has held this hull (s).</summary>
        public SimDuration WallHeldSeconds;

        /// <summary>How far the current recovery try has driven (m).</summary>
        public float RecoverMetres;

        /// <summary>Where the try last ran its exit test (m, of <see cref="RecoverMetres"/>).</summary>
        public float RecoverCheckedMetres;

        /// <summary>The move whose standing start was checked (<c>HullRecovery.MoveKey</c>); 0 = none yet.</summary>
        public int CheckedMoveKey;

        /// <summary>0 = not recovering · −1 backing · +1 driving forward (out of a wall it backed into).</summary>
        public sbyte RecoverDir;

        /// <summary>The try's full lock: +1 left, −1 right.</summary>
        public sbyte RecoverSteer;

        /// <summary>Tries this move (≤ <c>HullRecovery.MaxRecoveries</c>).</summary>
        public byte Recoveries;

        private byte _pad2;

        /// <summary>⭐ <c>CE-11057</c> — the SIM SECONDS the current frustration episode has spent below the speed rule (beside <see cref="Ticks"/>, which
        /// counts frames): the watchdog decides on this (<c>NavigationExecutionSystem.FrustrationLimitSeconds</c>), so a live step and a fixed one agree.</summary>
        public float SlowSeconds;

        /// <summary>⭐ <c>CE-11076</c> — the SIM SECONDS a point move has waited for its path with none yet (no trajectory, standing): not frustration
        /// (nothing has blocked it), so the watchdog re-asks every <c>NavigationExecutionSystem.PathRetrySeconds</c> and gives up only after
        /// <c>PathWaitLimitSeconds</c>. Appended.</summary>
        public float PathWaitSeconds;

        /// <summary>⭐ <c>CE-11076</c> — seconds before a move that FAILED unreachable may re-plan again on a navmesh change (CE-4674): a mesh that
        /// changes every frame (a live overlay bake) re-planned it every solver run. Appended.</summary>
        public float UnreachableReplanCooldown;
    }
}
