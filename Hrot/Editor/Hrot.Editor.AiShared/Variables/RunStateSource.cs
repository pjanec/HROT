using System;
using Hrot.Editor.AiShared.Debug;

namespace Hrot.Editor.AiShared.Variables;

/// <summary>
/// ⭐⭐⭐ <b>THE one place that answers <i>"is the sim up, and is it paused?"</i></b> for the variable
/// surfaces.
///
/// <para>📌 <b><c>Q32</c> ruling 3</b> switches the Value column's meaning on run state, and
/// <c>VariableChangeMonitor</c> already observes against <see cref="VariableRunState"/>. ⛔ <b>A second
/// notion of "running" would let the highlight and the value disagree about whether the sim is up</b>
/// — which is the one thing a monitor must not do.</para>
///
/// <para>⭐ <b>Derived from what the registrar ALREADY holds.</b> Every perspective's registrar takes
/// an <see cref="IDebugSessionRegistry"/>; a live session is what <i>running</i> means to this editor,
/// and <c>IEngineDebugTimeController.IsPausedByDebugger</c> is what <i>paused</i> means. ⛔ Neither is
/// a new argument for the composition root to remember — 📌 batches 79–82 each lost a surface to a seam
/// of exactly that shape.</para>
///
/// <para>⚠ <b><c>Replay</c> is not resolvable from these two</b> and is therefore never returned here.
/// ⭐ Stated rather than guessed: a host that knows it is replaying supplies its own resolver, and
/// <see cref="VariableValue.ModeFor"/> already treats <c>Replay</c> as the CURRENT arm, so the cell is
/// right either way — ⛔ only the monitor's highlight would differ, and inventing a replay signal here
/// would be coining the second notion this type exists to prevent.</para>
/// </summary>
public static class RunStateSource
{
    /// <summary>
    /// ⭐ <b>No session ⇒ <c>Planning</c>; a session ⇒ <c>Paused</c> when the debugger holds time,
    /// else <c>Running</c>.</b>
    /// </summary>
    /// <param name="sessions">
    /// The shared debug-session registry. ⛔ Null ⇒ <c>Planning</c>, because a surface with no way to
    /// observe the sim must not claim the sim is up.
    /// </param>
    /// <param name="isPaused">
    /// Reads <c>IEngineDebugTimeController.IsPausedByDebugger</c>. ⚠ Optional: the interface lives in
    /// <c>Hrot.Blueprints.Core</c>, <b>above</b> this assembly, so it arrives as a delegate rather than
    /// as a reference — the same reason the value DECODER is injected.
    /// </param>
    public static VariableRunState Resolve(IDebugSessionRegistry? sessions, Func<bool>? isPaused = null)
    {
        if (sessions?.ActiveSession is null) return VariableRunState.Planning;
        return isPaused != null && isPaused() ? VariableRunState.Paused : VariableRunState.Running;
    }

    /// <summary>⭐ The same rule as a delegate, for surfaces that re-read it every frame.</summary>
    public static Func<VariableRunState> For(IDebugSessionRegistry? sessions, Func<bool>? isPaused = null)
        => () => Resolve(sessions, isPaused);
}
