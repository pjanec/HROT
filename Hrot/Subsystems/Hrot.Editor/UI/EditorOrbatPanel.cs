using System.Linq;
using Fdp.Toolkit.DER;
using ImGuiNET;

namespace Hrot.Editor.UI;

/// <summary>
/// Panel that displays the entity hierarchy for the current scenario.
/// Reads from <see cref="IEditorLogic.View"/> exclusively.
/// </summary>
public sealed class EditorOrbatPanel
{
    // ── ImGui rendering ───────────────────────────────────────────────────────

    public void DrawContent(IEditorLogic logic)
    {
        var entities = logic.View.GetAllEntities().ToList();

        ImGui.Text($"Entities ({entities.Count})");
        ImGui.Separator();

        foreach (var entity in entities)
        {
            ImGui.Text($"• [{entity.EntityId}]");
        }
    }
}
