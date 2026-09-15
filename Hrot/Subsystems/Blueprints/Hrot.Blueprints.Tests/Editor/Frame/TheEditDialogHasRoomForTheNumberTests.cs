using System;
using Fdp.Core;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.UiFrameRail;
using ImGuiNET;
using StructEdit.Reflection;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor.Frame;

/// <summary>
/// ⭐⭐⭐ <b>Batch 100 (<c>100a</c> acceptance + <c>100b</c>) — THE FIRST RAIL THAT RENDERS.</b>
///
/// <para>📌 <b><c>R-124</c></b>. 🔴🔴 <b>Five batches shipped <c>3852 / 0</c> green while this dialog
/// drew a clipped number</b>, because every rail asserted <b>state</b> and the defect lived in
/// <b>layout</b>. ⇒ ⛔ <b>this rail exists to fail where those could not.</b></para>
///
/// <para>⭐⭐⭐ <b>WHICH LAYER IS FAKED</b> *(📌 <c>M-29</c>)* — <b>and this is the first batch where the
/// honest answer is "almost none":</b> the modal is the production <see cref="VariableEditModal"/>, the
/// session is a real StructEdit session over a real declaration, the container flags are production's,
/// and <b>ImGui really lays it out</b>. ⛔ What is NOT covered: a human's mouse. ⭐ It does not need to
/// be — the defect is <b>state → draw</b>.</para>
///
/// <para>⚠⚠ <b>ONE HONEST LIMIT, stated rather than buried.</b> The number the DESIGNER loses is the
/// avail width <b>inside the value column</b>, and that column is drawn by
/// <c>ComponentEditDrawer</c> — 📌 <b>`Fdp.Presentation` infrastructure with five other working
/// callers, which this batch must not touch.</b> ⇒ ⭐ this rail measures the <b>CONTAINER</b>, which is
/// the CAUSE: <c>BeginPopupModal(AlwaysAutoResize)</c> with no <c>SetNextWindowSize</c> makes a
/// <c>WidthStretch</c> column circular, and the drawer clamps to 60 px. ⭐⭐ <b>The symptom is measured
/// separately, in the drawer's exact shape</b>, by
/// <see cref="TheValueColumnIsCircularWithoutAnExplicitWidthTests"/> — ⛔ and that one is labelled a
/// REPLICA, because it is one.</para>
/// </summary>
[Collection(UiFrameCollection.Name)]
public sealed class TheEditDialogHasRoomForTheNumberTests
{
#pragma warning disable CS0649   // the field exists for its LAYOUT; StructEdit reflects it
    private struct Counter { public int Count; }
#pragma warning restore CS0649

    /// <summary>
    /// ⭐⭐ <b>A sane floor, not the exact number.</b> ⛔ Asserting <c>=== 504</c> would be a golden in
    /// disguise — it moves with the font, the padding and the theme. ⭐ <b>320 px is chosen from the
    /// measurement</b>: the defect renders <b>259.0</b> and the fix <b>504.0</b>, so the floor sits
    /// clear of both — ⚠ it cannot pass by accident, and it will not flake on a padding change.
    /// </summary>
    private const float SaneFloor = 320f;

    private static VariableRow Row() => new(
        Origin:    new VariableRowOrigin(Guid.NewGuid(), new Entity(1, 1), "Variables", "Count", "Count4"),
        ShortName: "Count", TypeText: "Counter", ClrType: typeof(Counter),
        ReadValue: () => Array.Empty<byte>(),
        RowKind:   VariableRowKind.Normal, IsStale: false);

    /// <summary>⭐ The production modal, over a real session on a real declaration.</summary>
    private static (VariableEditModal Modal, VariableEditGestureBinder Binder) Make()
    {
        var binder = new VariableEditGestureBinder(
            new VariableEditLauncher(new ComponentEditServiceBuilder().Build()),
            entryResolver: _ => new BlackboardVariableEntry("Count", typeof(Counter), Comment: null),
            runState:      () => VariableRunState.Planning);
        return (new VariableEditModal(binder, () => VariableRunState.Planning), binder);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>THE ACCEPTANCE RAIL for <c>100a</c>, and the gate for <c>100b</c>.</b>
    ///
    /// <para>⭐ Renders the real modal for a handful of frames — ⚠ ImGui needs two or three before an
    /// auto-resized popup settles — then <b>appends to the popup window</b> to read the content width
    /// the table is laid out into.</para>
    ///
    /// <para>🔴 <b>MEASURED against <c>f4ec0209c</c>: 259.0 px</b> — under the floor, ⭐ <b>so this rail
    /// was RED before <c>100b</c>.</b> ✅ <b>After the fix: 504.0 px.</b> ⛔ Not the coordinator's
    /// synthetic 60→305; that probe measured the value column of a REPLICA. ⚠ <b>Different seam,
    /// different numbers, same defect</b> — and reporting mine rather than copying theirs is the point
    /// of the handoff's instruction.</para>
    /// </summary>
    [SkippableFact]
    public void TheEditDialogsContentIsWideEnoughToDrawAValue()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);

        var (modal, binder) = Make();
        binder.OnEditValue(Row());
        Assert.NotNull(binder.ActiveSession);   // ⛔ anti-vacuity: a closed modal draws nothing

        float avail = -1f;
        using (var frame = UiFrameHarness.Begin())
        {
            frame.StepN(6, () =>
            {
                modal.Draw();

                // ⭐ Appending to a window by name is an ordinary ImGui idiom — it re-enters the SAME
                //   window ImGui just laid out, so this is the popup's REAL content width, not an
                //   estimate. ⛔ It does not draw anything into it.
                if (ImGui.Begin(modal.PopupId)) avail = UiProbe.AvailWidth();
                ImGui.End();
            });
        }

        Assert.True(avail > SaneFloor,
            $"The edit dialog's content width is {avail:F1} px (floor {SaneFloor:F1}). " +
            "An AlwaysAutoResize popup with a WidthStretch column is circular: the value column " +
            "collapses to ComponentEditDrawer's 60 px clamp and InputInt's -/+ step buttons consume " +
            "it, so the number has nowhere to draw. Give the popup an explicit SetNextWindowSize.");
    }

    /// <summary>
    /// ⭐⭐ <b>The Properties form gets the same room</b> — 📌 the handoff: <i>"Both modals."</i>
    /// ⚠ It is a different class with the same container mistake, so a fix to one is not a fix to both.
    /// </summary>
    [SkippableFact]
    public void ThePropertiesFormsContentIsWideEnoughToDrawItsFields()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);

        var rig   = ThePropertiesFormIsCustomTests.SceneForFrameRail();
        var modal = rig.Modal;
        Assert.True(modal.Open(rig.Row, rig.Schema, Guid.NewGuid(), editable: true));

        float avail = -1f;
        using (var frame = UiFrameHarness.Begin())
        {
            frame.StepN(6, () =>
            {
                modal.Draw();
                if (ImGui.Begin(Hrot.Blueprints.Editor.Windows.VariablePropertiesModal.PopupIdForTest))
                    avail = UiProbe.AvailWidth();
                ImGui.End();
            });
        }

        Assert.True(avail > SaneFloor,
            $"The Properties form's content width is {avail:F1} px (floor {SaneFloor:F1}).");
    }
}
