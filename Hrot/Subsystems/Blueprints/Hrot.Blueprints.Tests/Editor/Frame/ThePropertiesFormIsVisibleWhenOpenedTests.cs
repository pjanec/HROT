using System;
using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Hrot.Blueprints.Editor.NodeDrawers;
using Hrot.Blueprints.Editor.Windows;
using AiSelectionStore = Hrot.Editor.AiShared.Selection.EditorSelectionStore;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.AiShared.Windows;
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
/// outline click, and the registrar is constructed directly rather than by <c>EditorSubsystem</c> —
/// ⭐ the composition-root half is covered from the other side by
/// <c>TheDialogOpensOnEveryHostTests.OnlyBlueprintHasAPropertiesFormHost</c>. ⛔ Nothing else is faked:
/// this is the production shell, the production installer, its production modal, and a real frame.</para>
///
/// <para>⛔⛔ <b><c>S1</c> (<c>BP-399</c>, <c>2026-08-22</c>) — RE-EXPRESSED, and the seam it guards
/// MOVED.</b> 📐 The form used to be drawn from <c>BlueprintDetailsWindow.DrawClientArea</c>, so this
/// rail reflected into that private method. 📄 §7.3 ① retires that class; ⭐ the form is now registered
/// as a <b>frame overlay</b> by <see cref="BlueprintDetailsContribution"/>, which is where a modal
/// belongs — <c>ManagedWindow.Render</c> returns early when the window is closed or belongs to another
/// perspective, and a dialog drawn there vanishes with the panel. ⇒ ⭐ <b>the rail now draws what the
/// WINDOW MANAGER holds</b>, which is strictly closer to production than a private method ever was:
/// ⛔ an installer that forgot <c>RegisterFrameOverlay</c> leaves the overlay list empty and this
/// reddens.</para>
/// </summary>
[Collection(UiFrameCollection.Name)]
public sealed class ThePropertiesFormIsVisibleWhenOpenedTests
{
    /// <summary>
    /// ⭐⭐ <b>Draws the manager's FRAME OVERLAYS — ⛔ not <c>WindowManager.Render</c>, and the reason is
    /// a finding.</b>
    ///
    /// <para>🔴 <b>Measured:</b> rendering the real window chrome under a real GL context
    /// <b>CRASHED THE TEST HOST</b> — the title-bar pin button hands ImGui a zero texture handle, which
    /// is harmless with no renderer attached and fatal with one. ⚠⚠ <b>A crashed host truncates the run
    /// and makes the counts differ between runs</b> — 📌 exactly the shape of <c>BP-337</c> and
    /// <c>DEBT-AIB-030</c>. ⛔ Shipping a rail that can do that would be worse than shipping no rail.</para>
    ///
    /// <para>⭐ The overlay LIST is what production draws *(<c>WindowManager.Render</c> invokes exactly
    /// these, after every window)*, so the window CHROME is the faked layer *(📌 <c>M-29</c>)* and
    /// nothing else is.</para>
    /// </summary>
    private static void DrawOverlays(WindowManager wm)
    {
        foreach (var overlay in wm.FrameOverlays) overlay();
    }

    /// <summary>
    /// ⭐⭐ <b>The production composition, as far as this assembly can build it:</b> a real registrar
    /// *(which CONSTRUCTS the shell — §7.3 ①)*, a real manager, and the real installer.
    /// ⛔ Nothing is wired by hand — <see cref="BlueprintDetailsContribution.InstallInto"/> is the ONE
    /// call <c>EditorSubsystem</c> makes.
    /// </summary>
    private static (DetailsWindow Shell, WindowManager Windows) Production()
    {
        var registrar = new PerspectiveWorkspaceRegistrar(
            perspectiveName: "Blueprint",
            selectionStore:  new AiSelectionStore(),
            catalog:         new Hrot.Editor.AiShared.Catalog.AssetCatalog(),
            refactorService: new TheOutlineWatchEntryIsLiveTests.NoRefactorForWatch(),
            debugRegistry:   new Hrot.Editor.AiShared.Debug.DebugSessionRegistry());

        var windows = new WindowManager(new IconAtlas(IntPtr.Zero, 16f, 16f));

        BlueprintDetailsContribution.InstallInto(
            registrar:       registrar,
            windowManager:   windows,
            asset:           () => null,
            drawerRegistry:  new BlueprintNodeDrawerRegistry(),
            refactorService: new TheOutlineWatchEntryIsLiveTests.NoRefactorForWatch());

        return (registrar.Details!, windows);
    }

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

        var (shell, windows) = Production();

        var rig = ThePropertiesFormIsCustomTests.SceneForFrameRail();

        // ⭐ The gesture's own entry point, not the field.
        Assert.True(((IVariablePropertiesFormHost)shell)
            .OpenVariableProperties(rig.Row, editable: true));

        bool visible = false;
        using (var f = UiFrameHarness.Begin())
        {
            f.StepN(4, () => DrawOverlays(windows));
            f.Step(() =>
            {
                DrawOverlays(windows);
                visible = ImGui.IsPopupOpen(VariablePropertiesModal.PopupIdForTest);
            });
        }

        Assert.True(visible,
            "\"Properties…\" was opened and no popup appeared. The installer registered the form on " +
            "the shell but its Draw is not in the frame — BP-327, and this is the third time.");
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

        var (_, windows) = Production();

        bool visible = true;
        using (var f = UiFrameHarness.Begin())
        {
            f.StepN(4, () => DrawOverlays(windows));
            f.Step(() =>
            {
                DrawOverlays(windows);
                visible = ImGui.IsPopupOpen(VariablePropertiesModal.PopupIdForTest);
            });
        }

        Assert.False(visible);
    }
}
