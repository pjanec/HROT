using System;
using System.Collections.Generic;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using ImGuiNET;

namespace GizmoMap.Presentation
{
    /// <summary>
    /// ⭐⭐ <c>CE-1243</c> MB-1 (docs/UX/UX_Feature_Menu_Follows_Focus.md §MB) — ONE menu row: label, icon gutter, check mark, enabled
    /// state, shortcut, submenu. 🔒 User, 2026-10-22: <i>"shouldnt the manin menu bar rendering be unified and shared?"</i> — the shell's
    /// menu tree (<c>WindowManager</c>) and the per-frame DTO menus both draw their rows here, so a row fix lands once (CE-1027 was one
    /// such fix, made three times before this).
    /// </summary>
    public static class MenuRows
    {
        /// <summary>A submenu row. Returns whether it is open (the caller then draws its children and calls <c>ImGui.EndMenu</c>).</summary>
        public static bool Submenu(string label, string? icon, bool enabled, MenuIconResolver? icons, bool reserve)
        {
            var p0 = ImGui.GetCursorScreenPos();
            float gutter = 0f;
            string text = reserve ? MenuIconRenderer.Pad(label, out gutter) : label;
            return MenuIconRenderer.BeginMenu(text, enabled, icons, icon, p0, gutter);
        }

        /// <summary>A leaf row; <paramref name="isChecked"/> null = not checkable. Returns whether it was clicked this frame.</summary>
        public static bool Item(string label, string? icon, string? shortcut, bool? isChecked, bool enabled, MenuIconResolver? icons, bool reserve)
        {
            var p0 = ImGui.GetCursorScreenPos();
            float gutter = 0f;
            string text = reserve ? MenuIconRenderer.Pad(label, out gutter) : label;
            bool clicked = ImGui.MenuItem(text, shortcut ?? string.Empty, isChecked ?? false, enabled);
            MenuIconRenderer.DrawIcon(icons, icon, p0, gutter);
            return clicked;
        }

        /// <summary>One DTO menu item and its children — a separator, a submenu or a leaf; a click calls <paramref name="onAction"/>.</summary>
        public static void Dto(ContextMenuItemDto item, Action<int>? onAction, MenuIconResolver? icons, bool reserve)
        {
            if (string.IsNullOrEmpty(item.Label) || item.IsSeparator == true)
            {
                ImGui.Separator();
                return;
            }
            bool enabled = item.Enabled != false;
            if (item.Children is { Length: > 0 } children)
            {
                if (Submenu(item.Label!, item.Icon, enabled, icons, reserve))
                {
                    bool childReserve = icons != null && MenuIconRenderer.AnyHasIcon(children);
                    foreach (var child in children) Dto(child, onAction, icons, childReserve);
                    ImGui.EndMenu();
                }
                return;
            }
            if (Item(item.Label!, item.Icon, null, item.IsChecked, enabled, icons, reserve))
                onAction?.Invoke(item.Id);
        }

        /// <summary>A level of DTO items, sharing one icon gutter.</summary>
        public static void Dtos(IReadOnlyList<ContextMenuItemDto> items, Action<int>? onAction, MenuIconResolver? icons)
        {
            bool reserve = icons != null && MenuIconRenderer.AnyHasIcon(items);
            foreach (var item in items) Dto(item, onAction, icons, reserve);
        }
    }
}
