namespace Hrot.Editor.AiShared.Variables;

/// <summary>
/// ⭐ What the "Watch this variable" entry should look like right now.
/// </summary>
/// <param name="Enabled">⛔ False ⇒ the entry is drawn GREYED, never hidden.</param>
/// <param name="Label">⭐ A toggle: the entry says what the click will DO.</param>
/// <param name="DisabledReason">
/// ⭐⭐ Why it is refused, in the designer's terms — 📌 the user's own rule: <i>"same information
/// value, no false expectations."</i> ⛔ Null exactly when <paramref name="Enabled"/> is true.
/// </param>
public readonly record struct WatchGestureState(bool Enabled, string Label, string? DisabledReason);

/// <summary>
/// ⭐⭐⭐ <b>Batch 94 (<c>94f</c>) — the variable-watch gesture, as a DECISION.</b>
///
/// <para>⭐⭐ <b>Pure on purpose.</b> 📌 <c>R-21</c>/<c>R-62</c>: the drawing needs an ImGui context and
/// no headless rail can drive it, so the RULE lives here where it can be asserted and the control only
/// renders what this returns. ⛔ Two surfaces spelling the rule themselves is how one of them drifts.</para>
///
/// <para>⭐⭐⭐ <b>ONE command, TWO entry points</b> — the My Blueprint row menu and the Details table
/// row menu. ⛔ A one-surface gesture re-creates the split <c>U-6</c> removed.</para>
/// </summary>
public static class VariableWatchGesture
{
    /// <summary>
    /// ⭐⭐⭐ <b>The command id — DISTINCT, and that is <c>BP-346</c>.</b>
    ///
    /// <para>📐 <c>CommandCatalog.ToggleWatch = "editor.toggle-watch"</c> <b>already exists</b>
    /// *(<c>NodeEditor.Core/CommandCatalog.cs:75</c>)* with a real implementation
    /// *(<c>IDebugSession.ToggleWatch(PinId)</c>, <c>BlueprintDebugToNodeEditAdapter:140</c>)* — ⛔ but
    /// it is <b>PIN-scoped</b>: a canvas pin on one graph, not a variable row on an asset.</para>
    ///
    /// <para>⚠ <b>The trap Batch 93 named:</b> reaching for the existing constant would silently bind
    /// the variable gesture to the pin-watch command. ⇒ ⭐ a distinct id, until someone rules that the
    /// two watches are one concept — which is a <c>Q38</c>/<c>Q44</c> question that <c>R-27</c> gates.</para>
    /// </summary>
    public const string CommandId = "editor.toggle-variable-watch";

    /// <summary>⭐ Shown when the row is not being watched.</summary>
    public const string WatchLabel = "Watch this variable";

    /// <summary>⭐ …and when it is. ⛔ A toggle, not two commands.</summary>
    public const string UnwatchLabel = "Stop watching";

    /// <summary>
    /// ⭐⭐ <b><c>AQ55</c> — watch this variable on an entity the designer POINTS AT</b>, rather than on
    /// the one that happens to be selected. ⛔ The ellipsis is load-bearing: it opens a map-pick mode.
    /// </summary>
    public const string PinOnEntityLabel = "Watch this variable on entity…";

    /// <summary>
    /// ⭐⭐⭐ <b><c>AQ55</c>'s gesture rule — separate from <see cref="Decide"/>, and that is the point.</b>
    ///
    /// <para>⛔ It is <b>not a toggle</b>: pinning on a picked entity always creates a NEW concrete pin,
    /// so "already pinned" is not a refusal — a designer may watch the same variable on two entities,
    /// which is most of why the gesture exists. ⚠ It is also not offered when the row is already pinned
    /// TO THAT ENTITY, but that is only knowable after the pick, so the store decides it, not this.</para>
    ///
    /// <para>⭐ The run-state rule is <see cref="Decide"/>'s, unchanged and deliberately shared in
    /// SUBSTANCE: a baseline sampled while the world moves was stale when it was read. ⛔ Re-deciding it
    /// here in different words is how two menu entries on one row come to disagree.</para>
    /// </summary>
    /// <param name="hasPicker">
    /// ⚠ <c>false</c> in a host with no map. ⛔ Then the entry is <b>ABSENT</b>, not greyed — 📌 the same
    /// distinction Batch 100 drew for "Properties…": greying says <i>"not right now"</i>, and a host that
    /// will never have a map teaches nothing by showing a permanently dead item.
    /// </param>
    public static WatchGestureState DecidePinOnEntity(VariableRow row, VariableRunState runState, bool hasPicker)
    {
        if (!hasPicker)
            return new WatchGestureState(false, PinOnEntityLabel, "this host has no map to pick on");

        if (row.IsStale)
            return new WatchGestureState(false, PinOnEntityLabel, "this variable's asset or entity is gone");

        return runState switch
        {
            VariableRunState.Running =>
                new WatchGestureState(false, PinOnEntityLabel,
                    "pause the simulation first — a value read while it is running is already stale"),
            VariableRunState.Replay =>
                new WatchGestureState(false, PinOnEntityLabel,
                    "replay is a recording — there is no live value to watch"),
            _ => new WatchGestureState(true, PinOnEntityLabel, null),
        };
    }

    /// <summary>
    /// ⭐⭐ <b>When the gesture may be used</b> *(spec §7)*: <b>Planning</b> ✅ · <b>Paused/stepping</b>
    /// ✅ · ⛔ <b>free-running FORBIDDEN</b> · ⛔ <b>replay FORBIDDEN</b>.
    ///
    /// <para>⭐ <b>Why free-running is refused</b> and paused is not: pinning takes a baseline sample,
    /// and a baseline taken while the world is moving is a value that was already stale when it was
    /// read. ⭐ Paused is the case rule 4a exists for.</para>
    ///
    /// <para>⛔⛔ <b>It is refused by GREYING WITH A REASON, never by a click that dead-ends</b> —
    /// 📌 the user's rule, and the shape <c>BP-12e</c> already established for the outline's "+" items.</para>
    /// </summary>
    public static WatchGestureState Decide(VariableRow row, VariableRunState runState, bool isPinned)
    {
        string label = isPinned ? UnwatchLabel : WatchLabel;

        // ⭐ Unpinning is ALWAYS allowed. ⛔ Otherwise a row pinned before a run started could not be
        //   removed until the run stopped, which is a trap rather than a safeguard.
        if (isPinned) return new WatchGestureState(true, label, null);

        if (row.IsStale)
            return new WatchGestureState(false, label,
                "this variable's asset or entity is gone");

        return runState switch
        {
            VariableRunState.Running =>
                new WatchGestureState(false, label,
                    "pause the simulation first — a value read while it is running is already stale"),
            VariableRunState.Replay =>
                new WatchGestureState(false, label,
                    "replay is a recording — there is no live value to watch"),
            _ => new WatchGestureState(true, label, null),
        };
    }
}
