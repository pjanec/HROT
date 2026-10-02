using System.Text.Json.Nodes;
using ImGuiNET;
using System.Numerics;
using Fdp.Diagnostics.Contracts.Panels;
using Hrot.UI.Common.Facades;

namespace Hrot.UI.Common.Panels;

/// <summary>⭐⭐⭐ U-obs-5 — the whole of what <see cref="PreviewPanel"/> shows, this frame. ⚠ See
/// <c>ConfigPanel</c>'s remarks for the group-5 twin finding (same shape here — this is the SHIPPED
/// copy, the <c>Hrot.UI.Common</c> project's copy is dead).</summary>
public sealed record PreviewPanelViewModel(string PanelId, string PanelKind, bool IsInPreviewMode) : IPanelViewModel
{
    /// <inheritdoc/>
    public JsonNode Dump() => PanelDump.Of(this);
}

/// <summary>
/// Lightweight panel for switching between Edit and Preview modes.
///
/// <para>Reads <see cref="IPreviewController.IsInPreviewMode"/> each frame to
/// determine which button and label to show.  The panel itself has no internal
/// state.</para>
///
/// <para><b>Testing:</b> the button-click handlers are exposed as
/// <c>internal</c> methods (<see cref="HandleEnterPreview"/> and
/// <see cref="HandleExitPreview"/>) so tests can verify controller dispatch
/// without an active ImGui render frame.</para>
/// </summary>
public sealed class PreviewPanel
{
    // ── Colour constants ──────────────────────────────────────────────────────

    private static readonly Vector4 ColorEditGreen  = new(0.18f, 0.80f, 0.18f, 1.0f);
    private static readonly Vector4 ColorPreviewAmber = new(1.00f, 0.65f, 0.00f, 1.0f);

    // ── Public BUILD entry point (U-obs-5) ───────────────────────────────
    /// <summary>⭐⭐⭐ BUILD — a pure projection of the controller's mode. No ImGui.</summary>
    public PreviewPanelViewModel BuildViewModel(IPreviewController ctrl, string panelId, string panelKind) =>
        new(panelId, panelKind, ctrl.IsInPreviewMode);

    // ── Render ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Renders the preview panel.  Must be called inside an active ImGui window.
    /// </summary>
    public void DrawContent(IPreviewController ctrl)
    {
        if (!ctrl.IsInPreviewMode)
        {
            if (ImGui.Button("▶ Enter Preview"))
                HandleEnterPreview(ctrl);

            ImGui.TextColored(ColorEditGreen, "● EDIT");
        }
        else
        {
            if (ImGui.Button("■ Stop Preview"))
                HandleExitPreview(ctrl);

            ImGui.TextColored(ColorPreviewAmber, "● PREVIEW");
        }
    }

    // ── Internal logic (exposed for unit testing) ─────────────────────────────

    /// <summary>
    /// Handles the "Enter Preview" button click.
    /// Invokes <see cref="IPreviewController.EnterPreviewMode"/>.
    /// </summary>
    internal void HandleEnterPreview(IPreviewController ctrl)
    {
        ctrl.EnterPreviewMode();
    }

    /// <summary>
    /// Handles the "Stop Preview" button click.
    /// Invokes <see cref="IPreviewController.ExitPreviewMode"/>.
    /// </summary>
    internal void HandleExitPreview(IPreviewController ctrl)
    {
        ctrl.ExitPreviewMode();
    }
}
