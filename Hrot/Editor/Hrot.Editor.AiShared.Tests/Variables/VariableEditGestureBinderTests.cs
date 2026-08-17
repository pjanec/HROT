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
    /// ⭐⭐⭐ <b>The VALUE cell opens the VALUE scope; the NAME cell opens the WHOLE object.</b>
    ///
    /// <para>
    /// ⚠ Asserted on the SESSION's scope, not on the action the binder recorded: the action is what
    /// this class chose, the scope is what the user gets. ⛔ Checking the former would let the binder
    /// agree with itself while the launcher mapped it the other way round.
    /// </para>
    /// </summary>
    [Fact]
    public void TheValueCellOpensTheFieldScope_AndTheNameCellTheWholeObject()
    {
        var (binder, _) = Make(VariableRunState.Planning);

        binder.OnEditValue(Row());
        Assert.NotNull(binder.ActiveSession);
        var fieldRoot = binder.ActiveSession!.Document.Root;

        binder.OnProperties(Row());
        Assert.NotNull(binder.ActiveSession);
        var wholeRoot = binder.ActiveSession!.Document.Root;

        // ⭐⭐ The VALUE gesture scopes to the field: the builder retains exactly that node and
        //    ApplyScope returns it AS THE ROOT, so the document IS the one field.
        Assert.Equal("$.Count", fieldRoot.JsonPath);

        // ⭐ The NAME gesture keeps the whole component — both of DemoVar's fields.
        Assert.Equal("$", wholeRoot.JsonPath);
        Assert.Equal(2, wholeRoot.Children.Count);

        // ⛔⛔ THE DEFECT THIS WIRING FOUND. Before Batch 75, ScopeFor passed the bare variable name
        //    while FilterNode matches node.JsonPath ("$.Count") ⇒ nothing matched, ApplyScope fell
        //    through to an EMPTY "$" SelectionRoot, and the value dialog opened blank. ⚠ Both cases
        //    have zero CHILDREN, which is why a child-count assertion could not tell them apart —
        //    the JsonPath is what distinguishes "the field" from "nothing".
        Assert.NotEqual(fieldRoot.JsonPath, wholeRoot.JsonPath);
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
    /// </summary>
    [Fact]
    public void ANodeOwnedRow_StillOpens_BecauseReadOnlyIsNotAbsent()
    {
        var (binder, _) = Make(VariableRunState.Running);

        binder.OnProperties(Row(kind: VariableRowKind.NodeOwned));

        Assert.NotNull(binder.ActiveSession);
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
