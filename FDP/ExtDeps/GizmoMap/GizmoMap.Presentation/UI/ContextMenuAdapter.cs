using System;
using System.Numerics;
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
        /// Optional resolver that maps a menu item's <c>"icon"</c> key to a colored atlas sprite,
        /// drawn in an aligned left gutter. Null (default) renders text-only.
        /// </summary>
        public MenuIconResolver? IconResolver { get; set; }

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

        private void DrawMenuItems(string json, long anchorId, Action<long, int>? onAction)
        {
            JsonDocument doc;
            try { doc = JsonDocument.Parse(json); }
            catch { ImGui.TextDisabled("[invalid menu json]"); return; }

            using (doc)
            {
                bool reserve = IconResolver != null && LevelHasIcon(doc.RootElement);
                foreach (var item in doc.RootElement.EnumerateArray())
                    DrawItem(item, anchorId, onAction, reserve);
            }
        }

        // True when any sibling at this array level carries a non-empty "icon" property.
        private static bool LevelHasIcon(JsonElement array)
        {
            if (array.ValueKind != JsonValueKind.Array) return false;
            foreach (var el in array.EnumerateArray())
                if (el.TryGetProperty("icon", out var ic)
                    && ic.ValueKind == JsonValueKind.String
                    && !string.IsNullOrEmpty(ic.GetString()))
                    return true;
            return false;
        }

        private void DrawItem(JsonElement item, long anchorId, Action<long, int>? onAction, bool reserve)
        {
            // Separator
            if (item.TryGetProperty("separator", out var sep) && sep.ValueKind == JsonValueKind.True)
            {
                ImGui.Separator();
                return;
            }

            string rawLabel = item.TryGetProperty("label",   out var lbl) ? lbl.GetString() ?? "" : "?";
            string shortcut = item.TryGetProperty("shortcut", out var sc) ? sc.GetString() ?? "" : "";
            bool enabled   = !item.TryGetProperty("enabled", out var en) || en.GetBoolean();
            string? iconKey = item.TryGetProperty("icon", out var ik) && ik.ValueKind == JsonValueKind.String
                ? ik.GetString() : null;

            // Reserve the aligned gutter (pad the label) so icon and non-icon rows line up.
            var p0 = ImGui.GetCursorScreenPos();
            float gutter = 0f;
            string label = reserve ? MenuIconRenderer.Pad(rawLabel, out gutter) : rawLabel;

            // Submenu
            if (item.TryGetProperty("children", out var children)
                && children.ValueKind == JsonValueKind.Array)
            {
                bool open = ImGui.BeginMenu(label, enabled);
                MenuIconRenderer.DrawIcon(IconResolver, iconKey, p0, gutter);
                if (open)
                {
                    bool childReserve = IconResolver != null && LevelHasIcon(children);
                    foreach (var child in children.EnumerateArray())
                        DrawItem(child, anchorId, onAction, childReserve);
                    ImGui.EndMenu();
                }
                return;
            }

            // Leaf item
            if (!enabled) ImGui.BeginDisabled();

            bool clicked = ImGui.MenuItem(label, shortcut);
            MenuIconRenderer.DrawIcon(IconResolver, iconKey, p0, gutter);

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
