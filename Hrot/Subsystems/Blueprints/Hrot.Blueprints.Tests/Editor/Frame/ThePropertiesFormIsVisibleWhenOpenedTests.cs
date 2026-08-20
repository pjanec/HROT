using System;
using Hrot.Blueprints.Editor.NodeDrawers;
using Hrot.Blueprints.Editor.Windows;
using System.Reflection;
using AiSelectionStore = Hrot.Editor.AiShared.Selection.EditorSelectionStore;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.UiFrameRail;
using ImGuiNET;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor.Frame;

/// <summary>
/// ⭐⭐⭐ <b>Batch 100 (<c>100d</c>) — the Properties form APPEARS.</b>
///
/// <para>🔴🔴 <b>Batch 99 built this form and the designer never saw it.</b> The field, the
/// constructor call, the <c>Open</c>, the commit and the test accessor were all correct and all
/// railed — ⛔ <b>and no line called <c>Draw()</c>.</b> 📌 <c>BP-327</c>, third occurrence.</para>
///
/// <para>⭐⭐ <b>This rail is the pair to <c>EveryModalAWindowOwnsIsDrawnTests</c>, and the pairing is
/// deliberate.</b> That one reads IL and proves <b>the call exists</b>; ⛔ it cannot see that the call
/// sits after an early <c>return</c>. ⭐ This one renders the real window and asks ImGui whether the
/// popup is actually on screen — <b>the only question the designer was asking.</b></para>
///
/// <para>⚠ <b>WHICH LAYER IS FAKED</b> *(📌 <c>M-29</c>)*: the row is built here rather than by an
/// outline click, and the window is constructed directly rather than by <c>EditorSubsystem</c> —
/// ⭐ the composition-root half is covered from the other side by
/// <c>TheDialogOpensOnEveryHostTests.OnlyBlueprintHasAPropertiesFormHost</c>. ⛔ Nothing else is faked:
/// this is the production window, its production modal, and a real frame.</para>
/// </summary>
[Collection(UiFrameCollection.Name)]
public sealed class ThePropertiesFormIsVisibleWhenOpenedTests
{
    /// <summary>
    /// ⭐⭐ <b>Draws the window's CLIENT AREA — ⛔ not <c>ManagedWindow.Render</c>, and the reason is a
    /// finding.</b>
    ///
    /// <para>🔴 <b>Measured:</b> calling <c>Render(perspective, new IconAtlas(IntPtr.Zero, …))</c>
    /// <b>CRASHED THE TEST HOST</b> under a real GL context — the title-bar pin button hands ImGui a
    /// zero texture handle, which is harmless with no renderer attached and fatal with one.
    /// ⚠⚠ <b>A crashed host truncates the run and makes the counts differ between runs</b> — 📌 exactly
    /// the shape of <c>BP-337</c> and <c>DEBT-AIB-030</c>, which have cost this programme whole batches
    /// of confusion. ⛔ Shipping a rail that can do that would be worse than shipping no rail.</para>
    ///
    /// <para>⭐ <c>DrawClientArea</c> is <b>the method the defect was in</b> — it is where
    /// <c>_propertiesModal.Draw()</c> belongs and where it was missing. ⇒ the window CHROME is the
    /// faked layer *(📌 <c>M-29</c>)*, and nothing else is.</para>
    /// </summary>
    private static void DrawClientArea(BlueprintDetailsWindow w)
        => typeof(BlueprintDetailsWindow)
            .GetMethod("DrawClientArea", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(w, null);

    /// <summary>
    /// ⭐⭐⭐ <b>Open it through the window's own host interface, render, and assert ImGui shows it.</b>
    /// ⭐ Driven through <see cref="IVariablePropertiesFormHost.OpenVariableProperties"/> — the method
    /// the gesture actually calls — ⛔ not by reaching for the modal field, which would skip the seam
    /// that was broken.
    /// </summary>
    [SkippableFact]
    public void OpeningPropertiesPutsAVisiblePopupOnScreen()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);

        var window = new BlueprintDetailsWindow(
            selectionStore: new AiSelectionStore(),
            drawerRegistry: new BlueprintNodeDrawerRegistry());

        window.IsOpen = true;

        var rig = ThePropertiesFormIsCustomTests.SceneForFrameRail();

        // ⭐ The gesture's own entry point, not the field.
        Assert.True(((IVariablePropertiesFormHost)window)
            .OpenVariableProperties(rig.Row, editable: true));

        bool visible = false;
        using (var f = UiFrameHarness.Begin())
        {
            f.StepN(4, () => DrawClientArea(window));
            f.Step(() =>
            {
                DrawClientArea(window);
                visible = ImGui.IsPopupOpen(VariablePropertiesModal.PopupIdForTest);
            });
        }

        Assert.True(visible,
            "\"Properties…\" was opened and no popup appeared. The window owns the modal but does " +
            "not draw it — BP-327, and this is the third time.");
    }

    /// <summary>
    /// ⭐⭐ <b>ANTI-VACUITY, and it is the assertion that gives the one above its meaning.</b>
    /// ⛔ If <c>IsPopupOpen</c> were true for a form nobody opened, the rail above would pass on a
    /// window that draws a stray popup every frame. ⭐ Rendering the same window with nothing opened
    /// must show nothing.
    /// </summary>
    [SkippableFact]
    public void WithNothingOpened_NoPropertiesPopupIsDrawn()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);

        var window = new BlueprintDetailsWindow(
            selectionStore: new AiSelectionStore(),
            drawerRegistry: new BlueprintNodeDrawerRegistry());

        window.IsOpen = true;

        bool visible = true;
        using (var f = UiFrameHarness.Begin())
        {
            f.StepN(4, () => DrawClientArea(window));
            f.Step(() =>
            {
                DrawClientArea(window);
                visible = ImGui.IsPopupOpen(VariablePropertiesModal.PopupIdForTest);
            });
        }

        Assert.False(visible);
    }
}
