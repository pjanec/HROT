using System;
using Fdp.Presentation.WindowManager;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.NodeDrawers;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.Windows;

namespace Hrot.Blueprints.Editor.Windows;

/// <summary>
/// ⭐⭐⭐ <b><c>S1</c> — EVERYTHING <c>BlueprintDetailsWindow</c> USED TO BE, INSTALLED ONTO THE ONE SHELL.</b>
/// 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §7.3 ① *("<c>DetailsWindow</c> is THE shell on all
/// four perspectives; <c>BlueprintDetailsWindow</c> DISSOLVES")* · §7.6 ①.
///
/// <para>⭐⭐⭐ <b>ONE call at the composition root, not three</b> — and that is the whole reason this
/// type exists. 📐 The retired window bundled three things that are now separate concerns: the node
/// arm *(a view)*, the Properties form *(a delegate)*, and drawing that form *(a frame overlay)*.
/// ⛔ Three loose lines in <c>EditorSubsystem</c> would be three things to forget, and 📌 this
/// programme has filed <b>nine</b> silent defaults of exactly that shape. ⭐ Bundling them means a rail
/// on the CONSTRUCTED editor covers all three at once *(the <c>2026-08-16</c> control)</para>
///
/// <para>⛔⛔ <b>Why the root has to call ANYTHING at all — a reference-wall fact, not laziness.</b>
/// 📐 <c>VariablePropertiesModal</c> and <c>BlueprintNodeDrawerRegistry</c> live in THIS assembly,
/// which sits <b>above</b> <c>Hrot.Editor.AiShared</c> where the shell and the registrar live ⇒ the
/// registrar cannot build either. ⭐ Same shape as <c>L6.3</c>/<c>L6.4</c>'s Scenario view adapters,
/// which the root adds for the same reason *(§3's reference wall)</para>
///
/// <para>⚠ <b>What is NOT here:</b> the variables list, the run-state source, the edit gestures and the
/// live projection. 📐 Those were forwarded by the retired window and are wired by the registrar for
/// EVERY shell *(<c>R-67</c>)* ⇒ Blueprint gets them by being a <c>DetailsWindow</c>, with nothing to
/// install.</para>
/// </summary>
public static class BlueprintDetailsContribution
{
    /// <summary>
    /// ⭐⭐ Install Blueprint's Details contribution onto <paramref name="registrar"/>'s shell.
    /// </summary>
    /// <param name="registrar">⭐ The Blueprint perspective's registrar — its <c>Details</c> shell is the
    /// target. ⛔ Throws when it has none: 📌 §7.3 ① makes a shell-less perspective impossible, so a
    /// <c>null</c> here is a wiring regression and must fail loudly rather than silently install nothing.</param>
    /// <param name="windowManager">⭐ Where the Properties form joins the frame — see the overlay note below.</param>
    /// <param name="asset">⭐ The active Blueprint asset, re-asked every frame *(<c>R-126</c>'s pull)</param>
    /// <param name="drawerRegistry">⭐ The node-drawer registry the node view resolves through.</param>
    /// <param name="refactorService">
    /// ⭐⭐ <b>The service a RENAME must run.</b> 📌 The silent-default ruling: <i>"a production caller
    /// that HAS a dependency must PASS it."</i> ⚠⚠ The first draft of <c>99a</c> defaulted this to
    /// <c>null</c> while <c>EditorSubsystem</c> held one and handed it to a neighbouring window seven
    /// lines away — ⛔ it is REQUIRED here so that mistake is unrepresentable.
    /// </param>
    public static void InstallInto(
        PerspectiveWorkspaceRegistrar registrar,
        WindowManager windowManager,
        Func<BlueprintAsset?> asset,
        BlueprintNodeDrawerRegistry drawerRegistry,
        IRefactorService refactorService)
    {
        ArgumentNullException.ThrowIfNull(registrar);
        ArgumentNullException.ThrowIfNull(windowManager);
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(drawerRegistry);
        ArgumentNullException.ThrowIfNull(refactorService);

        var shell = registrar.Details
            ?? throw new InvalidOperationException(
                "The Blueprint perspective has no Details shell. §7.3 ① builds one for EVERY "
              + "perspective, so this means PerspectiveWorkspaceRegistrar stopped doing that.");

        // ⭐⭐⭐ ① THE NODE ARM, as a view. 📄 §7.4: content EXTRACTED, not wrapped.
        registrar.DetailsViews.Add(
            BlueprintNodeDetailsViewDescriptor.For(asset, drawerRegistry));

        // ⭐⭐⭐ ② THE PROPERTIES FORM. 📌 R-109 — a CUSTOM form, so the gesture binder cannot open one
        //    and the host that HAS one must. ⚠ The schema is null exactly as it was on the retired
        //    window (measured, not lazy: neither it nor the shell holds an IVariablesSchemaSource), so
        //    the form draws Name DISABLED with its reason rather than renaming without the refactor
        //    service — 📌 M-15: that would dangle the binding.
        var propertiesModal = new VariablePropertiesModal(refactorService);
        shell.SetPropertiesForm(
            (row, editable) => propertiesModal.Open(row, schema: null, row.Origin.AssetId, editable));

        // ⭐⭐⭐ ③ AND IT IS DRAWN. 🔴🔴 BP-327, filed THREE times: Batch 87 shipped "the modal draws",
        //    Batch 89 fixed "Draw had no caller", Batch 99 built this form with every path but the one
        //    that renders it. ⛔ NOT from a window's client area — ManagedWindow.Render returns early
        //    when the window is closed or belongs to another perspective, and the dialog would vanish.
        //    ⭐ A METHOD GROUP, not a lambda, so EveryModalAWindowOwnsIsDrawnTests can find it.
        windowManager.RegisterFrameOverlay(propertiesModal.Draw);
    }
}
