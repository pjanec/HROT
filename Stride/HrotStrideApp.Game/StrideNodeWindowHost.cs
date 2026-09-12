#nullable enable
using Hrot.SimHost;

namespace HrotStrideApp;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-214</c> / <c>R-S15</c> — mode 2's half of <see cref="IStrideEditorWindowHost"/>:
/// it makes the operator window draw <b>SimHost's map</b>.</b>
/// </summary>
/// <remarks>
/// <para><b>📐 Why this adapter is three forwarding lines and not a map implementation.</b>
/// <c>SimHostVisualization</c> already exposes <c>DrawWorld()</c> / <c>DrawUI()</c> with the identical
/// shape <c>EditorSubsystem</c> does — measured, which is what let <c>IStrideEditorWindowHost</c> become
/// three OPERATIONS instead of an editor object. ⇒ ⭐ mode 2's map is SimHost's map, drawn by SimHost's
/// own code, exactly as §7.2 says (<i>"the Stride node reuses it — same role, same components, no new
/// map code"</i>).</para>
///
/// <para>🔒 <b><c>R-S18</c></b>, <i>"the more unified, the better"</i>: ⛔ nothing here re-implements a
/// canvas, a picker or a gizmo layer. ⭐ Everything the map needs — <c>MapCanvas</c>,
/// <c>SelectionInteractionSystem</c>, <c>GlobalGizmoManager</c>, <c>DebugGizmoLayer</c>,
/// <c>MapPickServiceBridge</c>, the entity context menu — is built by the visualization this forwards
/// to.</para>
///
/// <para>⛔ <b>No toast.</b> The toast is mode 1's paused-navigation overlay; a node has no such
/// affordance, so <see cref="ToastSecondsRemaining"/> is always <c>0</c> and the window draws nothing.
/// ⚠ Returning 0 rather than throwing is the contract: every member is allowed to be a no-op.</para>
///
/// <para>⚠ <c>RegisterWindows</c> is a no-op HERE on purpose — mode 2's panels are composed by
/// <c>StrideNodeShell.StartOperatorWindow</c> through <c>UiBundleHost.Compose</c>, which is the same
/// call the other four hosts make. ⛔ Registering them twice would publish each panel under two
/// addresses.</para>
/// </remarks>
internal sealed class StrideNodeWindowHost : IStrideEditorWindowHost
{
    private readonly SimHostVisualization _visualization;

    internal StrideNodeWindowHost(SimHostVisualization visualization)
        => _visualization = visualization;

    /// <inheritdoc/>
    public void RegisterWindows(Fdp.Presentation.WindowManager.WindowManager windowManager) { }

    /// <inheritdoc/>
    public void DrawWorld() => _visualization.DrawWorld();

    /// <inheritdoc/>
    public void DrawUI() => _visualization.DrawUI();

    /// <inheritdoc/>
    public float ToastSecondsRemaining => 0f;

    /// <inheritdoc/>
    public string ToastMessage => string.Empty;
}
