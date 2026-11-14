using System.Threading;

namespace Fdp.Core;

/// <summary>
/// ⭐⭐⭐ <b>The behaviour-frame pulse — "has a NON-FROZEN brain tick run since I last looked?"</b>
///
/// <para>📄 <b>Design basis:</b> <c>Architect_Question_46_What_A_VariableRow_Means.md</c> §2 rule 2b —
/// the user's own specification, verbatim: <i>"the brain (cgf) does not tick ANY behavior when dt=0 so
/// the tick source is not dependent on behavior type."</i> 📐 Measured true: <c>BlueprintTickSystem</c>,
/// <c>BTreeTickSystem</c> and <c>HsmTickSystem</c> all open <c>if (deltaTime &lt;= 0f) return;</c>
/// ⇒ ⭐⭐ <b>"a non-frozen behaviour frame" is ONE event, not three</b>, and there is no per-host and no
/// per-<c>(asset, entity)</c> clock.</para>
///
/// <para>⛔⛔ <b>This is NOT the world tick, and the difference is the whole point.</b>
/// <c>ModuleHostKernel.UpdateInternal</c> calls <c>_liveWorld.Tick()</c> <b>unconditionally</b>, before
/// any <c>dt</c> check ⇒ <c>SimulationTick</c> advances <b>while paused</b>. ⭐ Sampling a debugger
/// watch on that would clear the change highlight under a breakpoint — 📌 exactly what Batch 68
/// refused. ⇒ <b>this counter only ever moves inside a <c>dt</c>-gated region.</b></para>
///
/// <para>⭐⭐ <b>It is an EDGE DETECTOR, which is why its bump site is not load-bearing.</b> Readers ask
/// <i>"has it moved since I last sampled?"</i> and then read whatever the sim currently holds — the
/// sampling happens at DRAW time on the UI thread. ⇒ ⛔ where in the behaviour phase the bump lands
/// does not change which value is read, so <c>BlueprintTickSystem</c> living in a different module is
/// not a problem. ⭐ Bump anywhere below a <c>dt</c> gate.</para>
///
/// <para>⚠ <b>Threading.</b> The sim thread advances it; the UI thread reads it. <see cref="Current"/>
/// is a volatile read and <see cref="Advance"/> is interlocked, so a reader never observes a torn or
/// stale-forever value. ⛔ <b>No lock and no callback into the UI</b> — 📌 the design's <i>"the pulse is
/// read, not pushed"</i>.</para>
///
/// <para>⚠ <b>Wraparound is a non-issue and deliberately unguarded:</b> readers compare for
/// <em>inequality</em> with their own last-seen value, so the single instant the counter wraps to a
/// value a reader happens to hold is at worst one skipped sample. ⛔ A guard would be more code than
/// the failure it prevents.</para>
///
/// <para>⛔ <b>Deliberately global, and that is the ruling.</b> <c>Q46</c> §4b: <i>"one global
/// BehaviorFrame counter … one <c>uint++</c> per frame costs nothing ⇒ no Enabled flag, no
/// Attach/Detach refcount, no per-instance dictionary write."</i> ⭐ It replaces
/// <c>BlueprintAssetTickSource</c>'s per-<c>(asset, entity)</c> table, which had <b>zero</b> production
/// callers.</para>
/// </summary>
public static class BehaviorFrame
{
    private static uint _counter;

    /// <summary>
    /// The current pulse. ⭐ Compare for <b>inequality</b> against your own last-seen value —
    /// ⛔ never for ordering, and ⛔ never as a wall clock.
    /// </summary>
    public static uint Current => Volatile.Read(ref _counter);

    /// <summary>
    /// ⭐ Advances the pulse. ⛔ <b>Only ever called from inside a <c>dt</c>-gated region</b> — in
    /// production, from <c>BehaviorFrameSystem</c> in the Simulation phase.
    /// </summary>
    public static void Advance() => Interlocked.Increment(ref _counter);
}
