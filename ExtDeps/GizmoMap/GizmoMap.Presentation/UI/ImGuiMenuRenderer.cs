using System;
using System.Collections.Generic;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using ImGuiNET;

namespace GizmoMap.Presentation
{
    /// <summary>
    /// Stateless utility that projects an ordered sequence of <see cref="ContextMenuItemDto"/>
    /// items to an ImGui main menu bar.
    ///
    /// Usage (inside ImGui frame):
    /// <code>
    /// var items = layer.ConsumeMainMenu();
    /// ImGuiMenuRenderer.DrawMenuBar(items, id => OnMenuAction(id));
    /// </code>
    /// </summary>
    public static class ImGuiMenuRenderer
    {
        /// <summary>
        /// Opens <c>ImGui.BeginMainMenuBar()</c> and renders all top-level menus.
        /// Does nothing when <paramref name="items"/> is empty to avoid an empty bar.
        /// </summary>
        public static void DrawMenuBar(IReadOnlyList<ContextMenuItemDto> items, Action<int>? onAction)
        {
            if (items == null || items.Count == 0) return;

            if (ImGui.BeginMainMenuBar())
            {
                foreach (var item in items)
                    DrawTopLevelMenu(item, onAction);

                ImGui.EndMainMenuBar();
            }
        }

        // Renders a single top-level <c>BeginMenu</c> entry and its children.
        private static void DrawTopLevelMenu(ContextMenuItemDto item, Action<int>? onAction)
        {
            bool enabled = item.Enabled != false;
            if (ImGui.BeginMenu(item.Label ?? string.Empty, enabled))
            {
                if (item.Children != null)
                {
                    foreach (var child in item.Children)
                        DrawItem(child, onAction);
                }
                ImGui.EndMenu();
            }
        }

        // Recursively renders a menu item: separator, submenu, checkable item, or plain item.
        private static void DrawItem(ContextMenuItemDto item, Action<int>? onAction)
        {
            // A null/empty label is treated as a separator.
            if (string.IsNullOrEmpty(item.Label) || item.IsSeparator == true)
            {
                ImGui.Separator();
                return;
            }

            bool hasChildren = item.Children != null && item.Children.Length > 0;
            bool enabled     = item.Enabled != false;

            if (hasChildren)
            {
                if (ImGui.BeginMenu(item.Label, enabled))
                {
                    foreach (var child in item.Children!)
                        DrawItem(child, onAction);
                    ImGui.EndMenu();
                }
            }
            else if (item.IsChecked.HasValue)
            {
                // Checkable item.
                bool ticked = item.IsChecked.Value;
                if (ImGui.MenuItem(item.Label, string.Empty, ticked, enabled))
                    onAction?.Invoke(item.Id);
            }
            else
            {
                if (ImGui.MenuItem(item.Label, string.Empty, false, enabled))
                    onAction?.Invoke(item.Id);
            }
        }
    }
}
