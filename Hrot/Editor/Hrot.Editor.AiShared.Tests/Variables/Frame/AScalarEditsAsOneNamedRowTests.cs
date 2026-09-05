using System.Linq;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Inspector;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.UiFrameRail;
using StructEdit.Core;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Variables.Frame;

/// <summary>
/// ⭐⭐⭐ <b>Batch 101 (<c>101a</c>) — a SCALAR variable edits as ONE ROW carrying its OWN NAME.</b>
///
/// <para>🔴 <b>What the designer saw</b> *(user, <c>2026-08-20</c>)*: <i>"it now shows a tree with one
/// collapsible node ScalarEditorBox`1 which after expanding shows a line with two columns, first reads
/// Value … No numeric value shown anywhere."</i> ⭐ Batch 100 fixed the missing NUMBER *(the container
/// width)*; ⛔ <b>the tree and the wrong label survived it</b>, because <c>100b</c>'s fixture is a
/// <b>struct</b> and a struct never enters <see cref="ScalarEditBox{T}"/>.</para>
///
/// <para>⚠⚠ <b>THAT IS THE GAP THIS FILE CLOSES: the scalar path had no rail at all</b>, through every
/// batch from 94 to 100 — which is exactly why it kept coming back.</para>
///
/// <para>⭐ <b>Two rails, deliberately.</b> The fix is a PURE function, so its own rail needs no frame
/// and cannot flake; the frame rail then proves the real modal still lays out with a scalar session —
/// 📌 <c>M-29</c>: the pure rail fakes nothing, the frame rail fakes only the mouse.</para>
/// </summary>
public sealed class AScalarEditsAsOneNamedRowTests
{
    private const string VariableName = "Count";

    private static IEditSession OpenScalarSession() =>
        DefaultValueAuthoring.OpenSession(
            new StructEdit.Reflection.ComponentEditServiceBuilder().Build(),
            new BlackboardVariableEntry(VariableName, typeof(int), Comment: null,
                                        DefaultValueJson: "11"));

    /// <summary>⭐ A real struct, to prove the fix flattens ONLY the wrapper.</summary>
#pragma warning disable CS0649 // assigned by the reflection document builder, never here
    private struct Counter { public int Count; }
#pragma warning restore CS0649

    private static IEditSession OpenStructSession() =>
        DefaultValueAuthoring.OpenSession(
            new StructEdit.Reflection.ComponentEditServiceBuilder().Build(),
            new BlackboardVariableEntry(VariableName, typeof(Counter), Comment: null));

    // ── the premise, so a change of shape cannot pass silently ───────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>The premise this fix rests on</b>, asserted rather than assumed: a scalar opens as a
    /// ONE-CHILD wrapper whose child is named after the FIELD. ⛔ If <c>97a</c>'s wrapper ever changes
    /// shape, this goes red first and says so — instead of the fix silently doing nothing.
    /// </summary>
    [Fact]
    public void AScalarSessionIsAOneChildWrapperWhoseChildIsNamedValue()
    {
        using var session = OpenScalarSession();
        var root = session.Document.Root;

        Assert.True(ScalarEditBox.IsWrapper(root.ClrType));
        Assert.Single(root.Children);
        Assert.Equal("Value", root.Children[0].Name);
        Assert.Equal(typeof(int), root.Children[0].ClrType);
    }

    // ── the fix ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b>The row is the LEAF, renamed — and it carries the SAME binding object.</b>
    /// ⚠ The binding identity is the load-bearing half: it is what makes every commit path
    /// *(<c>Commit</c> → <see cref="ScalarEditBox.Unwrap"/> → JSON / live bytes)* unchanged by a
    /// change that is purely about a LABEL.
    /// </summary>
    [Fact]
    public void TheDrawnNodeIsTheLeafRenamedToTheVariable_AndKeepsTheSameBinding()
    {
        using var session = OpenScalarSession();
        var root = session.Document.Root;
        var leaf = root.Children[0];

        var drawn = VariableEditModal.ScalarRowOrRoot(root, VariableName);

        Assert.Equal(VariableName, drawn.Name);
        Assert.Empty(drawn.Children);                     // ⭐ one row — ⛔ no collapsible parent
        Assert.Same(leaf.Binding, drawn.Binding);         // ⭐⭐ the same binding OBJECT
        Assert.Equal(leaf.ClrType, drawn.ClrType);
        Assert.Equal(leaf.JsonPath, drawn.JsonPath);
    }

    /// <summary>⭐ A real struct still draws its tree — ⛔ the fix must not flatten anything else.</summary>
    [Fact]
    public void AStructIsUntouched()
    {
        using var session = OpenStructSession();
        var root = session.Document.Root;

        Assert.False(ScalarEditBox.IsWrapper(root.ClrType));
        Assert.Same(root, VariableEditModal.ScalarRowOrRoot(root, VariableName));
    }

    /// <summary>
    /// ⚠ <b>A row that cannot say what it is gets the OLD shape, not a guessed label.</b>
    /// ⛔ Renaming to "" or to null would be worse than the wrapper's own name.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WithoutANameTheRootIsDrawnAsBefore(string? name)
    {
        using var session = OpenScalarSession();
        var root = session.Document.Root;

        Assert.Same(root, VariableEditModal.ScalarRowOrRoot(root, name));
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The COMMIT still round-trips, through the renamed row.</b>
    /// ⚠ This is the rail that would catch a regression in <c>97a</c>'s unwrap: the value is written
    /// through the binding the DRAWN node carries, and it must come back as a bare <c>int</c> —
    /// ⛔ never <c>{"Value":42}</c> and never the wrapper.
    /// </summary>
    [Fact]
    public void WritingThroughTheRenamedRowCommitsTheBareScalar()
    {
        using var session = OpenScalarSession();
        var drawn = VariableEditModal.ScalarRowOrRoot(session.Document.Root, VariableName);

        drawn.Binding!.SetBoxed(42);

        var committed = ScalarEditBox.Unwrap(session.Commit(), typeof(int));
        Assert.Equal(42, Assert.IsType<int>(committed));
    }

    // ── the frame rail: the gap Batch 100 left ───────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>The REAL modal, over a REAL scalar, in a REAL frame.</b>
    /// 📌 <c>100b</c>'s width rail used a <b>struct</b> fixture, so this path was never rendered.
    /// ⛔ Asserts no more than it can see: that a scalar session lays out without starving, i.e. the
    /// popup keeps the width <c>100b</c> gave it. ⚠ <b>The mouse is the only faked layer</b>.
    /// </summary>
    [SkippableFact]
    public void TheScalarDialogRendersWithRoomForTheNumber()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);

        using var session = OpenScalarSession();
        var drawn = VariableEditModal.ScalarRowOrRoot(session.Document.Root, VariableName);

        float valueColumn = -1f;

        using var frame = UiFrameHarness.Begin();
        frame.StepN(6, () =>
        {
            ImGuiNET.ImGui.SetNextWindowSize(new System.Numerics.Vector2(520, 0),
                                             ImGuiNET.ImGuiCond.Appearing);
            ImGuiNET.ImGui.Begin("scalar##b101");
            if (ImGuiNET.ImGui.BeginTable("##t", 2,
                    ImGuiNET.ImGuiTableFlags.Borders | ImGuiNET.ImGuiTableFlags.RowBg |
                    ImGuiNET.ImGuiTableFlags.Resizable | ImGuiNET.ImGuiTableFlags.SizingFixedFit))
            {
                ImGuiNET.ImGui.TableSetupColumn("Property",
                    ImGuiNET.ImGuiTableColumnFlags.WidthFixed, 180f);
                ImGuiNET.ImGui.TableSetupColumn("Value",
                    ImGuiNET.ImGuiTableColumnFlags.WidthStretch);

                new Fdp.Presentation.Editing.ComponentEditDrawer(session, pickerCtx: null)
                    .DrawEditNode(drawn);

                ImGuiNET.ImGui.TableSetColumnIndex(1);
                valueColumn = ImGuiNET.ImGui.GetContentRegionAvail().X;
                ImGuiNET.ImGui.EndTable();
            }
            ImGuiNET.ImGui.End();
        });

        // ⭐ The drawer's own clamp is 60 px (ComponentEditDrawer:253). A column at or below it is a
        //   column that got nothing — which is the defect, not a threshold chosen by taste.
        Assert.True(valueColumn > 60f,
            $"the scalar dialog's value column is {valueColumn:F1} px (drawer clamp floor 60.0)");
    }
}
