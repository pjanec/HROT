using System.Collections.Generic;
using FDP.Toolkit.DER;
using ImGuiNET;

namespace Hrot.Editor.UI;

/// <summary>
/// Panel that displays and edits properties of the currently selected entity.
/// Reads from <see cref="IEditorLogic.View"/>; commits via
/// <see cref="IEditorLogic.CommitPropertyEdit"/>.
///
/// <para>
/// NED constraint: This panel must NOT introduce a transitive dependency on Hrot.NED.
/// Entity display uses only <see cref="IDerEntity"/> members (EntityId, TkbType)
/// which are available directly from FDP.Toolkit.DER.
/// </para>
/// </summary>
public sealed class EntityPropertyInspector
{
    private long _selectedNetworkId;

    // ── Testable handler ──────────────────────────────────────────────────────

    public void HandleCommitEdit(IEditorLogic logic, long networkId,
        IReadOnlyList<object> components)
    {
        logic.CommitPropertyEdit(networkId, components);
    }

    public void SetSelectedEntity(long networkId) { _selectedNetworkId = networkId; }

    // ── ImGui rendering ───────────────────────────────────────────────────────

    public void DrawContent(IEditorLogic logic)
    {
        var entity = logic.View.GetEntity((int)_selectedNetworkId);
        if (entity == null)
        {
            ImGui.Text("No entity selected.");
            return;
        }

        // Display only IDerEntity-level fields — no Hrot.NED reference.
        ImGui.Text($"Entity ID: {entity.EntityId}");
        ImGui.Text($"TKB Type: {entity.TkbType}");
        // Property editing committed via HandleCommitEdit in response to user interaction.
    }
}
