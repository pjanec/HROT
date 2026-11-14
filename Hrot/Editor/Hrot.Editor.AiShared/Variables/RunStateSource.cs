using System;

namespace Hrot.Editor.AiShared.Variables;

/// <summary>
/// ⭐⭐⭐ <b>THE one place that answers <i>"is the sim up, and is it frozen?"</i></b> for the variable
/// surfaces.
///
/// <para>📌 <b><c>Q32</c> ruling 3</b> switches the Value column's meaning on run state, and
/// <c>VariableChangeMonitor</c> already observes against <see cref="VariableRunState"/>. ⛔ <b>A second
/// notion of "running" would let the highlight and the value disagree about whether the sim is up</b>
/// — which is the one thing a monitor must not do.</para>
///
/// <para>🔴🔴 <b><c>R-66</c> — the premise this type shipped with was FALSE.</b> Batch 83 derived run
/// state from <c>IDebugSessionRegistry.ActiveSession</c>, on the reasoning that <i>"a live session is
/// what running means to this editor."</i> 📐 <b>Measured <c>2026-08-18</c>:</b>
/// <c>EditorSubsystem.SyncActiveDebugSession</c> sets that session from
/// <c>_aiDocumentManager.Active.Kind</c> ⇒ ⛔ <b>it means "a blueprint DOCUMENT IS OPEN", not "the sim
/// is up."</b> ⇒ opening any blueprint made every surface read <c>Running</c>, so <c>ModeFor</c> chose
/// <c>Current</c> and every row the run had not written rendered <c>(pending)</c> forever:
/// ⛔⛔ <b>the INITIAL arm was unreachable in production.</b></para>
///
/// <para>⭐⭐ <b>The fix is an input that means what it says, not a different registry.</b>
/// ⛔ <c>SetActiveSession</c> was NOT made conditional — other consumers legitimately ask <i>"which
/// document's session is active?"</i>, and that is a different question. ⇒ two delegates, both about
/// TIME:
/// <list type="bullet">
///   <item>⭐ <paramref name="isSimUp"/> — the editor's preview/run mode
///   (<c>IPreviewController.IsInPreviewMode</c>). ⛔ Nothing is live before it.</item>
///   <item>⭐ <paramref name="isFrozen"/> — <c>IDataBreakpointManager.IsPaused</c> <b>or</b>
///   <c>IEngineDebugTimeController.IsPausedByDebugger</c>. 📌 Ruling 15 names both arms:
///   <i>"paused on breakpoint <b>or</b> deterministic time step."</i></item>
/// </list>
/// ⚠ <b>Delegates rather than references</b> because both signals live in assemblies ABOVE this one —
/// the same reason the value DECODER is injected.</para>
///
/// <para>⛔⛔ <b>Do not re-derive the frozen arm from <c>IsPausedByDebugger</c> ALONE.</b> 📐 The editor
/// boots in <c>TimeMode.Deterministic</c> and stays there until preview starts
/// (<c>EditorSubsystem:614</c>, <c>EditorPreviewController.EnterPreviewMode</c>) ⇒ <b>"frozen" is true
/// while planning too.</b> ⭐ It is only meaningful once <paramref name="isSimUp"/> holds — which is
/// exactly the order this method evaluates them in.</para>
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
    /// ⭐ <b>Sim down ⇒ <c>Planning</c>; sim up ⇒ <c>Paused</c> when the debugger holds time, else
    /// <c>Running</c>.</b>
    /// </summary>
    /// <param name="isSimUp">
    /// ⭐ Whether the simulation is running at all. ⛔ <b>Null ⇒ <c>Planning</c></b>, because a surface
    /// with no way to observe the sim must not claim the sim is up. ⚠ That default is what makes an
    /// un-wired host SAFE rather than wrong — 📌 it is also why <c>R-66</c> was invisible: the old
    /// default fired on a signal that was always present.
    /// </param>
    /// <param name="isFrozen">
    /// ⭐ Whether time is held by the debugger — a breakpoint pause or deterministic stepping.
    /// ⛔ Only consulted once <paramref name="isSimUp"/> holds; see the type remarks.
    /// </param>
    public static VariableRunState Resolve(Func<bool>? isSimUp, Func<bool>? isFrozen = null)
    {
        if (isSimUp is null || !isSimUp()) return VariableRunState.Planning;
        return isFrozen != null && isFrozen() ? VariableRunState.Paused : VariableRunState.Running;
    }

    /// <summary>⭐ The same rule as a delegate, for surfaces that re-read it every frame.</summary>
    public static Func<VariableRunState> For(Func<bool>? isSimUp, Func<bool>? isFrozen = null)
        => () => Resolve(isSimUp, isFrozen);

    /// <summary>
    /// ⭐⭐⭐ <b>The same two predicates, as a SENTENCE — so a refusal can report its INPUTS.</b>
    ///
    /// <para>🔴🔴 <b>Why this exists</b> *(user, twice, <c>2026-08-21</c>)*: the edit dialog said
    /// <i>"the simulation is running, pause it"</i> <b>while the simulation was paused</b>. 📐 The editor
    /// has FIVE independent notions of "stopped" — a data breakpoint, deterministic stepping, the clock's
    /// <c>TimeScale</c>, preview mode, and the cluster state *(<c>M-38</c>, <c>M-40</c>)* — ⛔ and the
    /// message named none of them, so each occurrence cost a measurement session.</para>
    ///
    /// <para>⭐⭐ <b>Built HERE because this is the one place that holds both predicates.</b> ⛔ A surface
    /// downstream sees only the verdict, which is exactly what was not enough.</para>
    /// </summary>
    public static Func<string> Describe(Func<bool>? isSimUp, Func<bool>? isFrozen = null)
        => () =>
        {
            bool up     = isSimUp  is not null && isSimUp();
            bool frozen = isFrozen is not null && isFrozen();
            return $"simUp={up}, frozen={frozen} => {Resolve(isSimUp, isFrozen)}";
        };
}
