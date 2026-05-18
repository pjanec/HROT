using System;
using System.Text.Json;
using ImGuiNET;

namespace GizmoMap.Presentation
{
    /// <summary>
    /// Schedules and renders a JSON-defined context menu using ImGui popups.
    ///
    /// Usage pattern:
    ///   1. Call <see cref="Schedule"/> from input handling code when a right-click is detected.
    ///   2. Call <see cref="DrawScheduled"/> inside an rlImGui Begin/End block each frame.
    ///
    /// The menu JSON format is an array of <c>ContextMenuItem</c> objects:
    /// <code>
    /// [
    ///   { "id": 1, "label": "Do Something", "shortcut": "S", "enabled": true },
    ///   { "separator": true },
    ///   { "label": "Submenu", "children": [ { "id": 2, "label": "Sub-item" } ] }
    /// ]
    /// </code>
    /// </summary>
    public sealed class ContextMenuAdapter
    {
        private const string PopupId = "##GizmoCtxMenu";

        private bool   _requestOpen;
        private long   _pendingAnchorId;
        private string? _pendingMenuJson;

        /// <summary>
        /// Records a right-click event on the entity with the given anchor ID.
        /// Must be called before <see cref="DrawScheduled"/> in the same frame.
        /// </summary>
        public void Schedule(long anchorId, string menuJson)
        {
            _requestOpen    = true;
            _pendingAnchorId = anchorId;
            _pendingMenuJson = menuJson;
        }

        /// <summary>
        /// Must be called inside an rlImGui Begin/End block each frame.
        /// Opens or continues the popup and fires <paramref name="onAction"/> when an item is clicked.
        /// </summary>
        public void DrawScheduled(Action<long, int>? onAction = null)
        {
            if (_requestOpen)
            {
                Console.WriteLine($"[Debug] Calling ImGui.OpenPopup({PopupId})");
                ImGui.OpenPopup(PopupId);
                _requestOpen = false;
            }

            if (_pendingMenuJson == null) return;

            bool isOpen = ImGui.BeginPopup(PopupId);
            Console.WriteLine($"[Debug] ImGui.BeginPopup returned: {isOpen}");
            if (isOpen)
            {
                DrawMenuItems(_pendingMenuJson, _pendingAnchorId, onAction);
                ImGui.EndPopup();
            }
            else
            {
                // Popup was closed (click-outside or item selected).
                _pendingMenuJson = null;
            }
        }

        // ---- Private rendering helpers --------------------------------------

        private static void DrawMenuItems(string json, long anchorId, Action<long, int>? onAction)
        {
            JsonDocument doc;
            try { doc = JsonDocument.Parse(json); }
            catch { ImGui.TextDisabled("[invalid menu json]"); return; }

            using (doc)
            {
                foreach (var item in doc.RootElement.EnumerateArray())
                    DrawItem(item, anchorId, onAction);
            }
        }

        private static void DrawItem(JsonElement item, long anchorId, Action<long, int>? onAction)
        {
            // Separator
            if (item.TryGetProperty("separator", out var sep) && sep.ValueKind == JsonValueKind.True)
            {
                ImGui.Separator();
                return;
            }

            string label   = item.TryGetProperty("label",   out var lbl) ? lbl.GetString() ?? "" : "?";
            string shortcut = item.TryGetProperty("shortcut", out var sc) ? sc.GetString() ?? "" : "";
            bool enabled   = !item.TryGetProperty("enabled", out var en) || en.GetBoolean();

            // Submenu
            if (item.TryGetProperty("children", out var children)
                && children.ValueKind == JsonValueKind.Array)
            {
                if (ImGui.BeginMenu(label, enabled))
                {
                    foreach (var child in children.EnumerateArray())
                        DrawItem(child, anchorId, onAction);
                    ImGui.EndMenu();
                }
                return;
            }

            // Leaf item
            if (!enabled) ImGui.BeginDisabled();

            bool clicked = ImGui.MenuItem(label, shortcut);

            if (!enabled) ImGui.EndDisabled();

            if (clicked && item.TryGetProperty("id", out var idProp))
            {
                int actionId = idProp.GetInt32();
                onAction?.Invoke(anchorId, actionId);
                ImGui.CloseCurrentPopup();
            }
        }
    }
}
