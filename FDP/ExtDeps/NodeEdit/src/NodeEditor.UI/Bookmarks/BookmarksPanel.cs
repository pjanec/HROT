using ImGuiNET;
using NodeEditor.Core.Bookmarks;
using System.Numerics;

namespace NodeEditor.UI.Bookmarks;

/// <summary>
/// Side-panel listing all bookmarks. V1 implementation: read-only list with slot numbers.
/// </summary>
public sealed class BookmarksPanel
{
    private readonly BookmarkStore _store;

    public BookmarksPanel(BookmarkStore store) => _store = store;

    /// <summary>Draw the panel contents. Call inside an <c>ImGui.Begin</c>/<c>End</c> pair.</summary>
    public void Draw()
    {
        if (!_store.All.Any())
        {
            ImGui.TextDisabled("No bookmarks yet. Press Ctrl+Shift+1..9 to set one.");
            return;
        }

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(4, 6));
        foreach (var b in _store.All.OrderBy(x => x.SlotNumber == 0 ? 10 : x.SlotNumber))
        {
            var slot = b.SlotNumber is >= 1 and <= 9 ? $"[{b.SlotNumber}]" : "[  ]";
            ImGui.Text(slot);
            ImGui.SameLine();
            ImGui.TextUnformatted(b.Label);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"Pan: ({b.ViewportPan.X:F0}, {b.ViewportPan.Y:F0})  Zoom: {b.ViewportZoom:F2}×");
        }
        ImGui.PopStyleVar();
    }
}
