using System;
using Hrot.Editor.AiShared.Variables;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Variables;

/// <summary>
/// ⭐⭐⭐ <b>Batch 97 (<c>97b</c>) — "Edit value…" is greyed when the policy DENIES it, and only then.</b>
///
/// <para>🔴 <b>The gap.</b> <c>DrawRowMenu</c> enabled both entries on <c>row.CanEverBeWritten</c> —
/// <b>the ROW KIND alone</b> — while <see cref="VariableEditPolicy"/> also knows <b>Replay ⇒
/// Denied</b>. ⇒ ⛔ during replay the entry was live and clicking it opened <b>nothing at all</b>, with
/// no explanation.</para>
///
/// <para>⭐⭐ <b>Shaped exactly like <see cref="VariableWatchGesture"/></b>, which sits two lines below
/// it in the same method — 📌 the handoff: <i>"mirror it exactly"</i>. ⛔ And it <b>calls</b>
/// <c>VariableEditPolicy.Resolve</c> rather than restating the matrix *(ruling 9)*.</para>
///
/// <para>⛔⛔ <b>WHAT THIS CANNOT PROVE, plainly</b> *(<c>R-21</c>/<c>R-62</c>, <c>M-29</c>)*: that
/// ImGui actually greys the item or shows the tooltip. ⭐ It pins WHAT the menu is told; the RENDERING
/// is unrailed, and that is stated in the report too.</para>
/// </summary>
public sealed class TheEditGestureTests
{
    private static VariableRow Row(
        VariableRowKind kind = VariableRowKind.Normal, bool stale = false) => new(
        Origin:    new VariableRowOrigin(Guid.NewGuid(), default, "s", "Health", "Alpha"),
        ShortName: "Health", TypeText: "int", ClrType: typeof(int),
        ReadValue: () => Array.Empty<byte>(),
        RowKind:   kind, IsStale: stale);

    public static TheoryData<VariableEditAction> BothActions => new()
    {
        VariableEditAction.EditValue,
        VariableEditAction.Properties,
    };

    // ══ it agrees with the policy, by construction ═══════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>THE invariant: enabled ⟺ the policy did not deny.</b> ⛔ Asserted across the whole
    /// matrix rather than case by case, so a new run state or row kind cannot be added to the policy
    /// and silently disagree with the menu.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryCombination))]
    public void EnabledMeansExactlyThatThePolicyDidNotDeny(
        VariableEditAction action, VariableRunState runState, VariableRowKind kind, bool stale)
    {
        var row = Row(kind, stale);

        var gesture = VariableEditGesture.Decide(row, action, runState);
        var policy  = VariableEditPolicy.Resolve(action, runState, row);

        Assert.Equal(policy != VariableEditAvailability.Denied, gesture.Enabled);
        Assert.Equal(policy == VariableEditAvailability.ReadOnly, gesture.OpensReadOnly);
    }

    /// <summary>⭐⭐ Every refusal carries a reason and every allowance carries none — 📌 the user's
    /// rule reduced to one assertion. ⛔ A greyed entry with no explanation is the defect.</summary>
    [Theory]
    [MemberData(nameof(EveryCombination))]
    public void ARefusalAlwaysCarriesAReasonAndAnAllowanceNeverDoes(
        VariableEditAction action, VariableRunState runState, VariableRowKind kind, bool stale)
    {
        var g = VariableEditGesture.Decide(Row(kind, stale), action, runState);

        Assert.Equal(g.Enabled, g.DisabledReason is null);
        if (!g.Enabled) Assert.False(string.IsNullOrWhiteSpace(g.DisabledReason));
    }

    public static TheoryData<VariableEditAction, VariableRunState, VariableRowKind, bool>
        EveryCombination()
    {
        var data = new TheoryData<VariableEditAction, VariableRunState, VariableRowKind, bool>();
        foreach (VariableEditAction a in Enum.GetValues<VariableEditAction>())
        foreach (VariableRunState  r in Enum.GetValues<VariableRunState>())
        foreach (VariableRowKind   k in Enum.GetValues<VariableRowKind>())
        foreach (bool stale in new[] { false, true })
            data.Add(a, r, k, stale);
        return data;
    }

    // ══ the cases the row kind alone got wrong ═══════════════════════════════

    /// <summary>
    /// 🔴🔴 <b>RED before this batch.</b> A perfectly ordinary row during REPLAY: <c>CanEverBeWritten</c>
    /// is <c>true</c>, so the old menu enabled the entry — ⛔ and <c>VariableEditLauncher.Open</c>
    /// returns <c>null</c> for <c>Denied</c>, so the click opened nothing and said nothing.
    /// </summary>
    [Theory]
    [MemberData(nameof(BothActions))]
    public void ReplayIsGreyedWithAReason(VariableEditAction action)
    {
        var row = Row();
        Assert.True(row.CanEverBeWritten, "the premise: the ROW KIND says writable");

        var g = VariableEditGesture.Decide(row, action, VariableRunState.Replay);

        Assert.False(g.Enabled);
        Assert.Contains("replay", g.DisabledReason!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>⚠ A STALE row is denied in every run state — its asset or entity is gone — ⭐ and the
    /// reason says THAT, ⛔ not the three-way "node-owned, a passthrough, or stale" guess.</summary>
    [Theory]
    [InlineData(VariableRunState.Planning)]
    [InlineData(VariableRunState.Running)]
    [InlineData(VariableRunState.Paused)]
    public void AStaleRowIsGreyedAndTheReasonNamesTheActualCause(VariableRunState runState)
    {
        var g = VariableEditGesture.Decide(Row(stale: true), VariableEditAction.EditValue, runState);

        Assert.False(g.Enabled);
        Assert.Contains("gone", g.DisabledReason!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("node-owned", g.DisabledReason!, StringComparison.OrdinalIgnoreCase);
    }

    // ══ ReadOnly is NOT Denied ═══════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>A node-owned or passthrough row STAYS ENABLED.</b> 📌 <c>VariableEditLauncher.Open</c>'s
    /// own doc-comment: <i>"<c>ReadOnly</c> still OPENS — the design says properties are read-only
    /// mid-run, not absent; refusing to open would hide the values a designer wants to read."</i>
    /// ⇒ ⛔ greying it would hide information the designer asked for; ⭐ Batch 96 already shapes the
    /// dialog as a VIEW for exactly these rows.
    /// </summary>
    [Theory]
    [InlineData(VariableRowKind.NodeOwned)]
    [InlineData(VariableRowKind.ReadOnlyPassthrough)]
    public void AReadOnlyRowIsEnabledAndFlaggedAsAView(VariableRowKind kind)
    {
        var g = VariableEditGesture.Decide(
            Row(kind), VariableEditAction.EditValue, VariableRunState.Planning);

        Assert.True(g.Enabled);
        Assert.Null(g.DisabledReason);
        Assert.True(g.OpensReadOnly);
    }

    /// <summary>⭐ …and an ordinary planning-state row is a plain editor — ⛔ the greying must not
    /// swallow the case the menu exists for.</summary>
    [Theory]
    [MemberData(nameof(BothActions))]
    public void AnOrdinaryRowIsAPlainEditor(VariableEditAction action)
    {
        var g = VariableEditGesture.Decide(Row(), action, VariableRunState.Planning);

        Assert.True(g.Enabled);
        Assert.False(g.OpensReadOnly);
        Assert.Null(g.DisabledReason);
    }
    // ══ the menu actually ASKS ═══════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>The control CALLS <c>Decide</c>, and no longer decides for itself.</b>
    ///
    /// <para>📌 <c>M-22</c>'s lesson: a perfect decision nothing consults is the shape this programme
    /// keeps filing. ⛔ No headless rail can drive the menu *(<c>R-21</c>/<c>R-62</c>)*, so the wiring
    /// is asserted over the SOURCE — weaker than a behavioural rail and honest about it, ⭐ but it is
    /// the difference between "the rule exists" and "the rule is used".</para>
    ///
    /// <para>🔴 The line it replaces was <c>bool writable = row.CanEverBeWritten;</c>, consulted by
    /// both menu items.</para>
    /// </summary>
    [Fact]
    public void TheRowMenuAsksTheGestureRatherThanTheRowKind()
    {
        var src = RepoFiles.Read(
            "Hrot/Editor/Hrot.Editor.AiShared/Variables/VariableTableControl.cs");

        int menuAt = src.IndexOf("private void DrawRowMenu", StringComparison.Ordinal);
        Assert.True(menuAt >= 0, "DrawRowMenu moved — re-measure before trusting 97b's wiring.");
        var menu = src[menuAt..src.IndexOf("private void DrawEditItem", StringComparison.Ordinal)];

        Assert.Contains("DrawEditItem(", menu, StringComparison.Ordinal);
        Assert.DoesNotContain("row.CanEverBeWritten", menu, StringComparison.Ordinal);
        Assert.Contains("VariableEditGesture.Decide", src, StringComparison.Ordinal);
    }

}
