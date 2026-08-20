namespace Hrot.Editor.AiShared.Variables;

/// <summary>
/// ⭐ What an edit menu entry — <i>"Edit value…"</i> or <i>"Properties…"</i> — should look like right now.
/// </summary>
/// <param name="Enabled">⛔ False ⇒ the entry is drawn GREYED, never hidden.</param>
/// <param name="DisabledReason">
/// ⭐⭐ Why it is refused, in the designer's terms — 📌 the user's own rule: <i>"same information value,
/// no false expectations."</i> ⛔ Null exactly when <paramref name="Enabled"/> is true.
/// </param>
/// <param name="OpensReadOnly">
/// ⭐⭐⭐ <b>Enabled, but it will open a VIEW rather than an editor.</b> ⚠ Distinct from a refusal on
/// purpose: 📌 <c>VariableEditLauncher.Open</c>'s own doc-comment — <i>"<c>ReadOnly</c> still OPENS —
/// the design says properties are read-only mid-run, not absent; refusing to open would hide the values
/// a designer wants to read."</i> ⇒ ⛔ <b>greying this would hide information the designer asked for.</b>
/// </param>
public readonly record struct EditGestureState(bool Enabled, string? DisabledReason, bool OpensReadOnly);

/// <summary>
/// ⭐⭐⭐ <b>Batch 97 (<c>97b</c>) — the edit gestures, as a DECISION.</b>
///
/// <para>🔴🔴 <b>The gap.</b> <c>VariableTableControl.DrawRowMenu</c> enabled both entries on
/// <c>row.CanEverBeWritten</c> — <b>the ROW KIND alone</b>. ⛔ The real policy is
/// <see cref="VariableEditPolicy.Resolve"/>, which also knows <b>Replay ⇒ Denied</b> ⇒ the entry was
/// live in a state where clicking it opens <b>nothing at all</b>, with no explanation.</para>
///
/// <para>⭐⭐ <b>It does NOT re-implement the matrix — it CALLS it</b> *(ruling 9)*. ⛔ A second spelling
/// of "when may this be edited" is exactly how the menu and the dialog would come to disagree, and the
/// menu is the half no rail can see.</para>
///
/// <para>⭐ <b>Shaped exactly like <see cref="VariableWatchGesture"/></b>, two lines below it in the same
/// method — 📌 the handoff: <i>"mirror it exactly."</i></para>
///
/// <para>⛔⛔ <b>What this class CANNOT prove</b> *(<c>R-21</c>/<c>R-62</c>)*: that ImGui greys the item
/// or shows the tooltip. ⭐ It pins WHAT the menu is told; the RENDERING of it is unrailed.</para>
/// </summary>
public static class VariableEditGesture
{
    /// <summary>⭐ <i>"Edit value…"</i> — the label the row menu draws.</summary>
    public const string EditValueLabel = "Edit value…";

    /// <summary>⭐ <i>"Properties…"</i>.</summary>
    public const string PropertiesLabel = "Properties…";

    /// <summary>
    /// ⭐⭐⭐ <b>Whether the entry is clickable, and why not.</b>
    ///
    /// <para>⭐⭐ <b>Only <c>Denied</c> greys.</b> The three states mean three different things:
    /// <list type="bullet">
    ///   <item><c>Editable</c> ⇒ ✅ enabled, an editor.</item>
    ///   <item><c>ReadOnly</c> ⇒ ✅ <b>still enabled</b>, and it opens SHAPED AS A VIEW *(Batch 96)*.
    ///   ⛔ Greying it would hide values the designer asked to read.</item>
    ///   <item><c>Denied</c> ⇒ ⛔ greyed, with the reason.</item>
    /// </list></para>
    ///
    /// <para>⭐ <b>The reason names the ACTUAL cause</b> — ⛔ never the three-way <i>"node-owned, a
    /// passthrough, or stale"</i> guess when the row itself says which.</para>
    /// </summary>
    public static EditGestureState Decide(
        VariableRow row, VariableEditAction action, VariableRunState runState)
    {
        var availability = VariableEditPolicy.Resolve(action, runState, row);

        if (availability != VariableEditAvailability.Denied)
            return new EditGestureState(
                Enabled: true,
                DisabledReason: null,
                OpensReadOnly: availability == VariableEditAvailability.ReadOnly);

        // ⭐ Denied has exactly two causes in the policy, and they are told apart HERE rather than
        //   guessed at the surface — ⚠ the policy checks staleness first, so this order matches it.
        string reason = row.IsStale
            ? "this variable's asset or entity is gone"
            : "replay is a recording — nothing in it can be changed";

        return new EditGestureState(Enabled: false, DisabledReason: reason, OpensReadOnly: false);
    }
}
