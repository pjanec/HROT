#nullable enable
using System;
using System.Numerics;
using Hrot.Editor;
using Hrot.Editor.UI;
using ImGuiNET;

namespace HrotStrideApp;

/// <summary>
/// Stage-4.1 plumbing proof: renders the real editor's pure-ImGui panels —
/// <see cref="EditorToolbarPanel"/> and <see cref="EditorOrbatPanel"/> — inside the
/// second (raylib/rlImGui) inspector window, bound to the live hosted
/// <see cref="IEditorLogic"/>.
///
/// <para>
/// <b>Design:</b>
/// <c>EditorToolbarWindow</c> and <c>EditorOrbatWindow</c> are <c>internal sealed</c>
/// and cannot be constructed from this assembly.  Instead, we call the panels'
/// <c>DrawContent(IEditorLogic)</c> methods directly — this is exactly what the
/// window wrappers do in their <c>DrawClientArea()</c> overrides. No <c>WindowManager</c>
/// is needed for the 4.1 proof; the menu-bar / perspective machinery is deferred to 4.2.
/// </para>
///
/// <para>
/// <b>Lifecycle:</b>
/// Construct after <see cref="EditorStrideSubsystem.Initialize"/> has been called
/// (so <see cref="EditorStrideSubsystem.HostedEditorLogic"/> is non-null).
/// Call <see cref="DrawEditorPanels"/> inside an active rlImGui frame
/// (between <c>rlImGui.Begin()</c> and <c>rlImGui.End()</c>).
/// </para>
///
/// <para>
/// <b>Active only when both flags are on:</b>
/// Instantiated and called only when <c>STRIDE_HOST_REAL_EDITOR=1</c>
/// AND <c>STRIDE_EDITOR_WINDOW=1</c>.
/// With either flag off the existing simple inspector is used unchanged.
/// </para>
/// </summary>
public sealed class StrideEditorUiHost
{
    private static readonly NLog.Logger Log = NLog.LogManager.GetCurrentClassLogger();

    private readonly EditorToolbarPanel _toolbarPanel;
    private readonly EditorOrbatPanel   _orbatPanel;
    private readonly IEditorLogic       _editorLogic;

    /// <summary>
    /// Constructs the host, creating fresh panel instances bound to
    /// <paramref name="editorLogic"/>.
    /// </summary>
    /// <param name="editorLogic">
    /// The live editor logic facade (from
    /// <see cref="EditorStrideSubsystem.HostedEditorLogic"/>).
    /// Must not be null.
    /// </param>
    public StrideEditorUiHost(IEditorLogic editorLogic)
    {
        _editorLogic  = editorLogic ?? throw new ArgumentNullException(nameof(editorLogic));
        _toolbarPanel = new EditorToolbarPanel();
        _orbatPanel   = new EditorOrbatPanel();

        Log.Info("[StrideEditorUiHost] Constructed — EditorToolbarPanel + EditorOrbatPanel bound to hosted IEditorLogic.");
    }

    /// <summary>
    /// Draws the editor toolbar and orbat panels as ImGui child windows.
    /// Must be called inside an active ImGui frame (between <c>rlImGui.Begin()</c>
    /// and <c>rlImGui.End()</c>).
    ///
    /// <para>
    /// Layout: toolbar at the top, orbat below it, filling the remaining height.
    /// Both are framed child windows so their scroll/resize behaviour is independent
    /// of the host window.
    /// </para>
    /// </summary>
    public void DrawEditorPanels()
    {
        // ── Toolbar section ──────────────────────────────────────────────
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.10f, 0.12f, 0.20f, 1f));
        if (ImGui.BeginChild("##editor_toolbar_host", new Vector2(0, 48), ImGuiChildFlags.Borders))
        {
            _toolbarPanel.DrawContent(_editorLogic);
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();

        ImGui.Spacing();

        // ── Orbat section ────────────────────────────────────────────────
        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1f), "Editor ORBAT");
        ImGui.Separator();

        if (ImGui.BeginChild("##editor_orbat_host", new Vector2(0, 0), ImGuiChildFlags.Borders))
        {
            _orbatPanel.DrawContent(_editorLogic);
        }
        ImGui.EndChild();
    }
}
