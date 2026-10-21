using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Time;

namespace Fdp.Toolkit.Perception
{
    /// <summary>
    /// ⭐⭐ <c>CE-3292</c> (R-3035) — <b>perception's clock: the sim STEP, never the rendered frame.</b>
    /// 📄 <c>docs/blueprints/DESIGN_Time_Architecture.md</c> §17 · <c>docs/DESIGN_Sensors_And_Doctrine.md</c> (known-rot, CE-3292).
    ///
    /// <para>⛔ <c>ISimulationView.Tick</c> is the world's FRAME counter — <c>ModuleHostKernel</c> advances it every rendered frame,
    /// paused or not — so a sighting stamped with it, an age measured in it or a mishearing seeded by it depends on how many frames
    /// were drawn between two steps. 📐 Measured: paused 3 s ⇒ 70 senses; one HTTP step ⇒ ~6 frames.</para>
    ///
    /// <para>⭐ <see cref="GlobalTime.FrameNumber"/> is the master's step counter: it moves once per step (once per frame in live
    /// play), never on a paused stepped frame, and a load or a preview Stop snaps it back with the clock. ⇒ every stamp, age and seed in
    /// perception — <c>TargetMemory.LastSeenTick</c> and everything compared with it — reads THIS, on both sides.</para>
    ///
    /// <para>⚠ A cache keyed "rebuilt this frame?" (the broadphase, the batch mark, the child index) keeps the frame tick: it only
    /// decides when to rebuild, never what is perceived.</para>
    /// </summary>
    public static class PerceptionClock
    {
        /// <summary>The current sim step of the world behind <paramref name="view"/> (0 when it has no clock yet).</summary>
        public static uint Now(ISimulationView view) => unchecked((uint)SimClock.Of(view).FrameNumber);
    }
}
