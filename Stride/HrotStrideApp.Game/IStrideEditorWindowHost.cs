#nullable enable

namespace HrotStrideApp;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-213</c> / ruling <c>R-S17</c> — the small optional contract that makes the raylib
/// window host usable by BOTH modes.</b>
/// </summary>
/// <remarks>
/// <para><b>📌 Why this exists.</b> <c>DESIGN_Stride_Node_Modes.md</c> §7.2 described the window host as
/// a generic <i>"raylib window + <c>WindowManager</c> + dockspace + message log"</i> and made
/// <c>CE-214</c>/<c>S6</c> depend on that. 📐 Measured, it was false: the host took an
/// <c>EditorStrideSubsystem</c> outright, so a mode-2 node — which has no editor subsystem — could not
/// construct it. 🔒 User ruling: <i>"point 1: widen"</i>.</para>
///
/// <para><b>⭐⭐ Why OPERATIONS and not the editor object.</b> The first cut of this interface exposed
/// <c>HostedEditor</c> — an <c>EditorSubsystem</c> — which decoupled the TYPE but not the CONCEPT: mode
/// 2 would still have had to produce an editor to draw anything. 📐 Then measured:
/// <c>SimHostVisualization</c> exposes <c>DrawWorld()</c> / <c>DrawUI()</c> with the <b>identical
/// shape</b> <c>EditorSubsystem</c> does. ⇒ ⭐⭐⭐ the window does not need an editor at all — it needs
/// <b>three operations</b>, and both modes already have something that provides them.</para>
///
/// <para><b>⭐ This is what makes <c>R-S15</c>'s map possible.</b> The 2-D map canvas is drawn by
/// <c>DrawWorld()</c>. While the contract exposed a null <c>HostedEditor</c>, mode 2's window drew no
/// map at all — the panels composed but the map surface stayed blank, which is precisely the half
/// 🔒 <i>"the map is part of the diagnostic suite … with entities, gizmos, context menu"</i> demands.</para>
///
/// <para>⚠ Every member is allowed to be a no-op. Mode 1 forwards to its hosted editor (itself null
/// until <c>buildEditorUi</c>); mode 2 forwards to its <c>SimHostVisualization</c>. ⛔ The window must
/// not know which it is talking to.</para>
/// </remarks>
public interface IStrideEditorWindowHost
{
    /// <summary>
    /// Registers this host's panels into the window's <c>WindowManager</c>, once, at open.
    /// ⭐ A host with no panels of its own implements this as a no-op.
    /// </summary>
    void RegisterWindows(Fdp.Presentation.WindowManager.WindowManager windowManager);

    /// <summary>
    /// ⭐⭐ Draws the 2-D world/map canvas for this frame — <b>this is the map</b> (<c>R-S15</c>).
    /// Called inside the window's <c>BeginDrawing</c>/<c>EndDrawing</c> pair, before the UI.
    /// </summary>
    void DrawWorld();

    /// <summary>Draws this host's own ImGui content for the frame, after the window manager's.</summary>
    void DrawUI();

    /// <summary>Seconds left on the transient toast overlay; ⭐ <c>0</c> means "draw nothing".</summary>
    float ToastSecondsRemaining { get; }

    /// <summary>The toast text. ⚠ Only read when <see cref="ToastSecondsRemaining"/> is positive.</summary>
    string ToastMessage { get; }
}
