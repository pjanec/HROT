using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using ImGuiNET;

namespace GizmoMap.Presentation
{
    /// <summary>
    /// Stateless utility that projects an ordered sequence of <see cref="ContextMenuItemDto"/>
    /// items to the current ImGui menu context.
    ///
    /// Usage (inside ImGui frame):
    /// <code>
    /// var items = layer.ConsumeMainMenu();
    /// ImGuiMenuRenderer.DrawMenus(items, id => OnMenuAction(id), iconResolver);
    /// </code>
    /// </summary>
    public static class ImGuiMenuRenderer
    {
        /// <summary>
        /// Draws menu items into the current ImGui menu scope.
        /// Caller owns <c>ImGui.BeginMainMenuBar()</c>/<c>ImGui.EndMainMenuBar()</c>.
        /// </summary>
        public static void DrawMenus(IEnumerable<ContextMenuItemDto> items, Action<int>? onAction)
            => DrawMenus(items, onAction, null);

        /// <summary>
        /// Draws menu items, resolving each item's <see cref="ContextMenuItemDto.Icon"/> through
        /// <paramref name="icons"/> and rendering it in an aligned left gutter. Pass
        /// <c>null</c> for <paramref name="icons"/> to render text-only (original behavior).
        /// </summary>
        public static void DrawMenus(
            IEnumerable<ContextMenuItemDto> items,
            Action<int>? onAction,
            MenuIconResolver? icons)
        {
            if (items == null) return;
            var list = items as IReadOnlyList<ContextMenuItemDto> ?? new List<ContextMenuItemDto>(items);
            bool reserve = icons != null && MenuIconRenderer.AnyHasIcon(list);
            foreach (var item in list)
                DrawItem(item, onAction, icons, reserve);
        }

        // Recursively renders a menu item: separator, submenu, checkable item, or plain item.
        private static void DrawItem(
            ContextMenuItemDto item, Action<int>? onAction, MenuIconResolver? icons, bool reserve)
        {
            // A null/empty label is treated as a separator.
            if (string.IsNullOrEmpty(item.Label) || item.IsSeparator == true)
            {
                ImGui.Separator();
                return;
            }

            bool hasChildren = item.Children != null && item.Children.Length > 0;
            bool enabled     = item.Enabled != false;

            // Reserve the gutter (pad the label) so every row at this level aligns.
            Vector2 p0 = ImGui.GetCursorScreenPos();
            float gutter = 0f;
            string label = reserve ? MenuIconRenderer.Pad(item.Label!, out gutter) : item.Label!;

            if (hasChildren)
            {
                bool open = ImGui.BeginMenu(label, enabled);
                MenuIconRenderer.DrawIcon(icons, item.Icon, p0, gutter);
                if (open)
                {
                    var children = item.Children!;
                    bool childReserve = icons != null && MenuIconRenderer.AnyHasIcon(children);
                    foreach (var child in children)
                        DrawItem(child, onAction, icons, childReserve);
                    ImGui.EndMenu();
                }
            }
            else if (item.IsChecked.HasValue)
            {
                bool ticked = item.IsChecked.Value;
                bool clicked = ImGui.MenuItem(label, string.Empty, ticked, enabled);
                MenuIconRenderer.DrawIcon(icons, item.Icon, p0, gutter);
                if (clicked) onAction?.Invoke(item.Id);
            }
            else
            {
                bool clicked = ImGui.MenuItem(label, string.Empty, false, enabled);
                MenuIconRenderer.DrawIcon(icons, item.Icon, p0, gutter);
                if (clicked) onAction?.Invoke(item.Id);
            }
        }
    }
}
