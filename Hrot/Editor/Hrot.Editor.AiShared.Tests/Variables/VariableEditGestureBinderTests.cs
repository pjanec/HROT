using System;
using Fdp.Core;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Variables;
using StructEdit.Core;
using StructEdit.Reflection;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Variables;

/// <summary>
/// ⭐⭐⭐ <b>Track C's dialog now has an entry point — and the MEANING of each gesture is asserted.</b>
///
/// <para>
/// 🔴🔴 <b>The Batch-74 measurement this closes:</b> <c>VariableTableControl</c> already raised both
/// gestures, <c>VariableEditLauncher</c> already turned an action into the right <c>EditScope</c>,
/// ⛔ <b>and nothing connected them</b> — the launcher was constructed by nobody, so the
/// <c>InspectorWindow</c> panel was the only live way to edit a variable's default.
/// </para>
///
/// <para>
/// ⚠⚠ <b>The pixels stay unverifiable; the DECISIONS do not.</b> 📄 <c>DESIGN_Variable_Details_And_
/// Editing.md</c> §3 rules two things — <i>which gesture opens which scope</i> and <i>run state
/// decides WRITABILITY, not which dialog</i>. ⭐ Both are testable headlessly, and both are below.
/// </para>
/// </summary>
public sealed class VariableEditGestureBinderTests
{
#pragma warning disable CS0649   // fields exist for their LAYOUT; StructEdit reflects them
    private struct DemoVar { public int Count; public float Speed; }
#pragma warning restore CS0649

    private static VariableRow Row(
        VariableRowKind kind = VariableRowKind.Normal, bool stale = false)
        => new(
            // ⚠ The VariablePath must name a field OF the edited object: ScopeFor turns it into
            //   EditScope.ForField(EditPath.Parse(path)), and the object is the entry's FieldType.
            //   A path naming nothing yields an EMPTY document, which is how this fixture first
            //   read "1 field" as 0 — the scope was applied and matched no member.
            Origin:    new VariableRowOrigin(Guid.NewGuid(), new Entity(1, 1), "Variables", "Count", "Alpha"),
            ShortName: "Count", TypeText: "DemoVar", ClrType: typeof(DemoVar),
            ReadValue: () => Array.Empty<byte>(),
            RowKind:   kind, IsStale: stale);

    private static BlackboardVariableEntry Entry()
        => new("Count", typeof(DemoVar), Comment: null);

    private static (VariableEditGestureBinder Binder, VariableTableControl Table) Make(
        VariableRunState runState, BlackboardVariableEntry? entry = null)
    {
        var launcher = new VariableEditLauncher(new ComponentEditServiceBuilder().Build());
        var binder   = new VariableEditGestureBinder(
            launcher,
            entryResolver: _ => entry ?? Entry(),
            runState:      () => runState);
        var table = new VariableTableControl(new VariableValueFormatter(decode: (_, _) => null));
        binder.Attach(table);
        return (binder, table);
    }

    // ══ §3 — which gesture opens which scope ═════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>INVERTED, Batch 96 (<c>96b</c>) — BOTH gestures open the WHOLE VALUE.</b>
    ///
    /// <para>⛔⛔⛔ <b>THIS RAIL IS WHY THE DEFECT SURVIVED, and the reason is worth keeping.</b> It was
    /// the ONLY test that asserted the resulting DOCUMENT rather than the scope object — 📌 exactly
    /// what the findings said was missing — ⚠ <b>and its fixture is the one shape in which the bug is
    /// INVISIBLE</b>: a variable named <c>Count</c> whose type is <c>DemoVar { int Count; float Speed;
    /// }</c>. ⇒ the old <c>ForField("$.Count")</c> DID match a node — <b>the DTO's own <c>Count</c>
    /// member</b> — so the rail saw <i>"the field"</i> and called it correct.</para>
    ///
    /// <para>🔴 <b>It was the wrong node.</b> The session is opened over the variable's VALUE, so
    /// <c>$</c> IS the value; <c>$.Count</c> is <i>a member inside it</i>. For every variable NOT named
    /// after one of its own fields — including the user's plain <c>int</c> — it matched nothing and the
    /// dialog drew an empty body.</para>
    ///
    /// <para>⭐ Asserted on the SESSION's document, not on the action the binder recorded: the action is
    /// what this class chose, the document is what the designer gets.</para>
    ///
    /// <para>⭐⭐⭐ <b>NARROWED, Batch 99 (<c>99a</c>) — and the narrowing is the POINT, not a
    /// concession.</b> 📌 <c>R-109</c>: <i>"Properties CANNOT be a StructEdit document"</i>, because
    /// <c>Name</c> is a <b>RENAME</b> and <c>Type</c> is a <b>RETYPE MIGRATION</b> — operations, not
    /// fields. ⇒ ⛔ <b>the "both" in this rail's old name was the DEFECT</b>: Batch 96 corrected
    /// <i>which</i> document Properties opened while leaving intact the premise that it opens one at
    /// all, and that premise is what <c>BP-359</c> actually was.</para>
    ///
    /// <para>⚠ <b>What the rail keeps is the half <c>R-109</c> explicitly preserves</b> —
    /// <i>"'Edit value…' stays StructEdit, unchanged"</i> — so the <c>$</c>-rooted whole-value document
    /// is still asserted here, on the gesture that still opens one. ⭐ The Properties half moves to
    /// <see cref="ThePropertiesGesture_OpensNoStructEditSession"/>, which asserts its ABSENCE.</para>
    /// </summary>
    [Fact]
    public void TheValueGestureOpensTheWholeValueDocument()
    {
        var (binder, _) = Make(VariableRunState.Planning);

        binder.OnEditValue(Row());
        Assert.NotNull(binder.ActiveSession);
        var fieldRoot = binder.ActiveSession!.Document.Root;

        // ⭐⭐⭐ Batch 96 — the whole value, with both of DemoVar's fields.
        // 🔴 This used to read Assert.Equal("$.Count", fieldRoot.JsonPath) — see above: that node is
        //    the DTO's own Count member, matched only because this fixture's variable happens to be
        //    named after one of its own fields.
        Assert.Equal("$", fieldRoot.JsonPath);
        Assert.Equal(2, fieldRoot.Children.Count);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Batch 99 (<c>99a</c>) — the OTHER half of the rail above, asserting an ABSENCE.</b>
    ///
    /// <para>📌 <c>R-108</c>: <i>"the two menu items are TWO OBJECTS, not two SCOPES"</i> · 📌
    /// <c>R-109</c>: the declaration object is <b>not a struct StructEdit can document</b>. ⇒ ⭐ the
    /// binder RAISES <see cref="VariableEditGestureBinder.PropertiesRequestedForRow"/> and opens
    /// <b>no session at all</b>.</para>
    ///
    /// <para>⭐ <b>Why an absence is worth a rail:</b> the defect it fences is a FALLTHROUGH — deleting
    /// the early return in <c>Open</c> silently restores <c>BP-359</c>, and every other rail here would
    /// stay green because they all drive the VALUE gesture.</para>
    ///
    /// <para>⚠ <b>Deliberately duplicated</b> by <c>ThePropertiesFormIsCustomTests</c> in
    /// <c>Hrot.Blueprints.Tests</c>. ⭐ That one asserts it over a whole Blueprint scene <i>with the
    /// form attached</i>; this one is the binder's OWN rail, in the binder's own file, so a change to
    /// <c>Open</c> reddens something a reader of that file can see. ⛔ Ruling 9 is about
    /// IMPLEMENTATIONS — there is still exactly one early return.</para>
    /// </summary>
    [Fact]
    public void ThePropertiesGesture_OpensNoStructEditSession()
    {
        var (binder, _) = Make(VariableRunState.Planning);

        VariableRow? raised   = null;
        bool         editable = false;
        binder.PropertiesRequestedForRow += (r, e) => { raised = r; editable = e; };

        binder.OnProperties(Row());

        Assert.Null(binder.ActiveSession);                                // ⛔ no StructEdit document
        Assert.Equal(VariableEditAction.Properties, binder.LastAction);   // ⭐ the gesture still landed
        Assert.NotNull(raised);
        Assert.True(editable);                                            // ⭐ planning ⇒ editable
    }

    /// <summary>
    /// ⭐⭐ <b>The binder does not re-implement the policy — it CONSULTS it.</b> A replayed row is
    /// refused, so no session opens. ⛔ A second copy of §5's matrix here is how the two would drift.
    /// </summary>
    [Fact]
    public void Replay_OpensNoSession()
    {
        var (binder, _) = Make(VariableRunState.Replay);

        binder.OnEditValue(Row());

        Assert.Null(binder.ActiveSession);
        Assert.Equal(VariableEditAction.EditValue, binder.LastAction);   // ⭐ the gesture still landed
    }

    /// <summary>
    /// ⚠ <b>A READ-ONLY row still OPENS</b> — §5: <i>properties are read-only mid-run, not absent</i>.
    /// ⛔ Refusing to open would hide values a designer wants to read.
    ///
    /// <para>⭐⭐ <b>RE-EVIDENCED, Batch 99 (<c>99a</c>) — the PROPERTY is unchanged, only what proves
    /// it.</b> 📌 <c>R-109</c> made Properties a custom form, so <i>"it opened"</i> is no longer
    /// <c>ActiveSession != null</c>; it is the form event being RAISED. ⛔ <b>And the assertion got
    /// STRONGER, not weaker</b>: the old one could not tell <i>read-only</i> from <i>editable</i> at all
    /// — a session opened either way — whereas the <c>bool</c> carries the distinction this rail's own
    /// name is about. ⚠ It would have stayed green if a running row had been offered an EDITABLE
    /// dialog.</para>
    /// </summary>
    [Fact]
    public void ANodeOwnedRow_StillOpens_BecauseReadOnlyIsNotAbsent()
    {
        var (binder, _) = Make(VariableRunState.Running);

        bool raised = false, editable = true;
        binder.PropertiesRequestedForRow += (_, e) => { raised = true; editable = e; };

        binder.OnProperties(Row(kind: VariableRowKind.NodeOwned));

        Assert.True(raised);       // ⭐ NOT absent — the form is offered
        Assert.False(editable);    // ⭐ but READ-ONLY, which the old session-shaped rail could not see
    }

    /// <summary>⛔ A STALE row's asset or entity is gone: denied outright, in every run state.</summary>
    [Fact]
    public void AStaleRow_IsDenied()
    {
        var (binder, _) = Make(VariableRunState.Planning);

        binder.OnEditValue(Row(stale: true));

        Assert.Null(binder.ActiveSession);
    }

    /// <summary>
    /// ⭐ <b>Fails CLOSED when the row's variable cannot be resolved</b> — no session, no guess. ⚠ The
    /// alternative (open on a fabricated entry) would edit something the designer did not point at.
    /// </summary>
    [Fact]
    public void AnUnresolvableRow_OpensNothing()
    {
        var launcher = new VariableEditLauncher(new ComponentEditServiceBuilder().Build());
        var binder   = new VariableEditGestureBinder(
            launcher, entryResolver: _ => null, runState: () => VariableRunState.Planning);

        binder.OnEditValue(Row());

        Assert.Null(binder.ActiveSession);
    }

    /// <summary>
    /// ⚠ <b>The run state is read PER GESTURE, not captured once.</b> 📌 Writability changes when the
    /// sim starts or pauses; a snapshot taken at construction would offer an editable dialog during a
    /// replay that began afterwards.
    /// </summary>
    [Fact]
    public void TheRunStateIsReadAtEachGesture()
    {
        var state    = VariableRunState.Planning;
        var launcher = new VariableEditLauncher(new ComponentEditServiceBuilder().Build());
        var binder   = new VariableEditGestureBinder(
            launcher, entryResolver: _ => Entry(), runState: () => state);

        binder.OnEditValue(Row());
        Assert.NotNull(binder.ActiveSession);

        state = VariableRunState.Replay;          // ⭐ changes AFTER construction
        binder.OnEditValue(Row());
        Assert.Null(binder.ActiveSession);
    }

    // ══ the seam itself ══════════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>The table's gestures really reach the binder</b> — <c>Attach</c> subscribes and
    /// <c>Detach</c> unsubscribes. ⚠ Asserted by RAISING the events on the control, so a renamed or
    /// unsubscribed event fails here rather than silently doing nothing in the editor.
    /// </summary>
    [Fact]
    public void AttachSubscribesTheGestures_AndDetachStopsThem()
    {
        var (binder, table) = Make(VariableRunState.Planning);

        RaiseEditValue(table, Row());
        Assert.Equal(VariableEditAction.EditValue, binder.LastAction);

        RaiseProperties(table, Row());
        Assert.Equal(VariableEditAction.Properties, binder.LastAction);

        binder.Detach(table);
        RaiseEditValue(table, Row());
        Assert.Equal(VariableEditAction.Properties, binder.LastAction);   // ⭐ unchanged ⇒ detached
    }

    private static void RaiseEditValue(VariableTableControl table, VariableRow row)
        => Raise(table, "EditValueRequested", row);

    private static void RaiseProperties(VariableTableControl table, VariableRow row)
        => Raise(table, "PropertiesRequested", row);

    /// <summary>⭐ Raises a control event by its backing field — the control's own raise sites need an
    /// ImGui context, and the subscription is what this asserts, not the hit-testing.</summary>
    private static void Raise(VariableTableControl table, string eventName, VariableRow row)
    {
        var field = typeof(VariableTableControl).GetField(eventName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);
        ((Action<VariableRow>?)field!.GetValue(table))?.Invoke(row);
    }
}
