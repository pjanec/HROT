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
            // ⭐ CE-1243 MB-1 — the rows are MenuRows', the same the shell's menu tree draws.
            MenuRows.Dtos(items as IReadOnlyList<ContextMenuItemDto> ?? new List<ContextMenuItemDto>(items), onAction, icons);
        }
    }
}
