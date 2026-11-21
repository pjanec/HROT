using Fdp.Core;
using Hrot.Editor.AiShared.Windows;

namespace Hrot.Editor.AiComposition;

/// <summary>
/// ⭐⭐ <b>What driving an AI debug session needs: a world, and the entity to follow.</b>
/// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §32.22.
/// </summary>
public sealed record AiDebugSessionPumpServices
{
    /// <summary>⚠ A PROVIDER: both hosts assign their world during initialisation, after the canvas
    /// hooks are built. ⛔ Capturing it would pin <see langword="null"/> — the `CE-343` lesson.</summary>
    public required Func<EntityRepository?> World { get; init; }

    /// <summary>
    /// ⭐⭐⭐ <b>THE ANCHOR — option (1), approved by the user (`2026-09-26`): the graph follows the
    /// ENTITY-INSPECTOR SELECTION.</b>
    ///
    /// <para>📐 This is the DESIGNED anchor, not a new concept. <c>SharedEntitySelection</c> (`CE-301`,
    /// 📄 <c>DESIGN_Editor_Entity_Selection_Source.md</c>) is *"the AI editors' view of THE ONE
    /// selection"*, written solely by <c>SelectionNotificationSystem</c> from
    /// <c>SelectionChangedNotification.Primary</c> ⇒ every cause moves it — a map click, the entity
    /// inspector, the orbat, a context-menu <i>Select</i>, a remote <c>CMD_SET_SELECTION</c>.</para>
    ///
    /// <para>⭐ So the canvas overlay and the entity inspector show the SAME entity by construction,
    /// which is exactly why this option was chosen over inventing a per-tab pin. 🔒 <b>Pinning is
    /// deliberately NOT here</b> — user, `2026-09-26`: *"pinning to concrete entity might come later
    /// when necessity comes"* — and <c>SharedEntitySelection</c>'s own remarks already say pinning
    /// does not go through it.</para>
    /// </summary>
    public required Func<Entity?> SelectedEntity { get; init; }
}

/// <summary>
/// ⭐⭐⭐ <b><c>CE-348</c> — THE KERNEL ADAPTER THE DESIGN DEFERRED AT "SLICE 3+".</b>
/// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §32.22.
///
/// <para>🔒 <b>Not a new idea — a named, scheduled, never-built one.</b> 📄
/// <c>docs/projects/Hrot/AI/Hrot.BTree.Editor.md</c>: *"When a simulation is running, the editor
/// connects a <c>BTreeDebugSession</c> to the <b>kernel adapter</b>"* (`:58`); the session exposes its
/// <c>Record*</c> methods ***"for the FUTURE kernel adapter"*** with ***"step controls … no-ops until
/// kernel wiring (Slice 3+)"*** (`:294`). ⇒ 🔒 **under-adoption of a working seam** — the user's
/// reading, and the corpus agrees.</para>
///
/// <para>⭐⭐ <b>What one line of pumping lights up.</b> 📐 The consumers are ALREADY constructed and
/// registered by <c>BTreeDocumentFactory</c> and were sitting dark: <c>BTreeRuntimeOverlayRenderer</c>
/// (`:202`), <c>HeatmapOverlayRenderer</c> (`:216`), the breakpoint gutter
/// (<c>BTreeEditorHostServices:90</c>) and <c>SubtreeBoundaryRenderer</c>.</para>
///
/// <para>⭐⭐⭐ <b>And the SNAPSHOT half needs no arming</b> — 📐 <c>BTreeDebugSession.Update</c> reads the
/// running node and call stack from <c>RootStateAccess.TryGetState</c>, i.e. <b>the occurrence root
/// slot this programme built in <c>CE-319</c></b>. ⇒ the executing-node outline and the live snapshot
/// work the moment this pumps. ⚠ Only the HISTORY/heatmap half needs
/// <c>BTreeTraceWorkingMemory1024</c>, which is armed separately and already has its own path
/// (<c>EditorAiTracerCoordinator.ArmEntity</c>) — ⛔ <b>not claimed here.</b></para>
///
/// <para>⚠ <b>Pumped from the canvas's per-frame hook, deliberately.</b> ⭐ A graph that is not drawn
/// costs nothing, and the hook already exists on both hosts.</para>
/// </summary>
public static class AiDebugSessionPump
{
    /// <summary>
    /// ⭐ An <c>AfterDraw</c> action that advances <paramref name="session"/> for the selected entity.
    /// ⚠ Returns a no-op when the host has no session, so a caller need not branch.
    /// </summary>
    public static Action<AiCanvasContext> ForBTree(
        Hrot.BTree.Editor.Debug.BTreeDebugSession? session,
        AiDebugSessionPumpServices services)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));
        if (session is null) return static _ => { };

        return _ =>
        {
            var world = services.World();
            var e     = services.SelectedEntity();
            // ⚠ No selection, or no world yet, is an ORDINARY state — the session simply does not
            //   advance. ⛔ Not an error, and nothing is logged: this runs every frame.
            if (world is null || e is null) return;
            session.Update(world, e.Value);
        };
    }

    /// <summary>⭐ The HSM twin. ⚠ Same contract, same anchor.</summary>
    public static Action<AiCanvasContext> ForHsm(
        Hrot.Hsm.Editor.Debug.HsmDebugSession? session,
        AiDebugSessionPumpServices services)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));
        if (session is null) return static _ => { };

        return _ =>
        {
            var world = services.World();
            var e     = services.SelectedEntity();
            if (world is null || e is null) return;
            session.Update(world, e.Value);
        };
    }

    /// <summary>
    /// ⭐ Run <paramref name="first"/> then <paramref name="second"/> — so a host can add the pump to a
    /// canvas hook that already carries a selection bridge without either owning the other.
    /// </summary>
    public static Action<AiCanvasContext> Then(
        Action<AiCanvasContext>? first, Action<AiCanvasContext>? second)
        => ctx => { first?.Invoke(ctx); second?.Invoke(ctx); };
}
