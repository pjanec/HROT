using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Replay
{
    /// <summary>
    /// ⭐⭐⭐ <b>CE-3329 P2 — "is this world a replay?", one flag IN the world.</b> 📄 docs/DESIGN_Replay_Time_Axis_And_Review.md §12l.
    /// </summary>
    /// <remarks>
    /// <para>⭐ Every system that WRITES world state — that would simulate, spawn, destroy or move what the recording already holds —
    /// opens with <c>if (ReplayState.IsActive(view)) return;</c>. Egress, playback, the recorder, presentation and diagnostics never
    /// read it. 🔒 User, `2026-10-26`: <i>"Cannot every system simply check if there is a replay or not and behave according to that?"</i>
    /// — approved: <i>"P2 using the replay flag approved."</i></para>
    /// <para>⭐ Set by whatever installs a replay (<c>EcsRecordReplayController</c> on every ECS host, the Replay Browser for its own
    /// world), so no host wires a predicate into a system. ⛔ SUPERSEDED: an injected <c>Func&lt;bool&gt;? IsReplayActive</c> per
    /// system (six of them, wired in five host roots) — a host that forgot the wiring left the system writing during a replay, silently.</para>
    /// <para>⭐ A Transient world singleton (the <c>EgressHold</c> / <c>WorldEpoch</c> pattern): a keyframe never carries it, so a
    /// seek or a review's return cannot flip it. Created once by <see cref="Ensure"/> on the frame thread; afterwards only its
    /// volatile flag changes, so the async prepare that installs a replay never writes a singleton off the frame thread.</para>
    /// </remarks>
    [ComponentId(GlobalComponentIds.ReplayState)]
    [DataPolicy(DataPolicy.Transient)]
    public sealed class ReplayState
    {
        private volatile bool _active;

        /// <summary>Creates the world's flag (off) if it has none. Call where the world is built or the controller is composed.</summary>
        public static ReplayState Ensure(EntityRepository world)
        {
            var s = world.HasSingletonManaged<ReplayState>() ? world.GetSingletonManaged<ReplayState>() : null;
            if (s == null) { s = new ReplayState(); world.SetSingletonManaged(s); }
            return s;
        }

        /// <summary>The world shows a recording from now on.</summary>
        public static void Enter(EntityRepository world) => Ensure(world)._active = true;

        /// <summary>The world is live again (no-op when it never replayed).</summary>
        public static void Leave(EntityRepository world)
        {
            if (world.HasSingletonManaged<ReplayState>() && world.GetSingletonManaged<ReplayState>() is { } s) s._active = false;
        }

        /// <summary>⭐ Is this world showing a recording? A world with no flag is live.</summary>
        public static bool IsActive(ISimulationView view)
            => view is EntityRepository repo && repo.HasSingletonManaged<ReplayState>() && repo.GetSingletonManaged<ReplayState>()?._active == true;
    }
}
