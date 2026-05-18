using System;
using System.Collections.Generic;
using System.Text.Json;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;

namespace GizmoMap.Presentation
{
    /// <summary>
    /// Aggregates <see cref="DebugPrimitiveShape.MainMenuBinding"/> primitives emitted during
    /// a frame, merges entries by top-level label, sorts by priority, and exposes the merged
    /// list for rendering via <see cref="ImGuiMenuRenderer"/>.
    ///
    /// Each backend gizmo that wants a main-menu entry emits a JSON array such as:
    /// <code>[{"label":"View","priority":30,"children":[{"id":250,"label":"Tactical Map Layers..."}]}]</code>
    /// This adapter combines multiple bindings with the same top-level label into a single
    /// submenu, picking the minimum priority across all contributors.
    /// </summary>
    public sealed class MainMenuAdapter
    {
        // Keyed on top-level menu label.
        private readonly Dictionary<string, ContextMenuItemDto> _merged = new();

        /// <summary>
        /// Parses <paramref name="menuJson"/> (a JSON array of <see cref="ContextMenuItemDto"/>)
        /// and merges it into the in-progress aggregated state.
        /// </summary>
        public void Schedule(string menuJson)
        {
            ContextMenuItemDto[]? items;
            try
            {
                items = JsonSerializer.Deserialize<ContextMenuItemDto[]>(menuJson, _jsonOptions);
            }
            catch (JsonException)
            {
                // Malformed JSON from backend: silently ignore.
                return;
            }

            if (items == null) return;

            foreach (var item in items)
            {
                if (string.IsNullOrEmpty(item.Label)) continue;

                if (!_merged.TryGetValue(item.Label, out var existing))
                {
                    _merged[item.Label] = item;
                }
                else
                {
                    // Merge: combine children arrays; keep minimum priority.
                    int mergedPriority = Math.Min(existing.Priority ?? int.MaxValue, item.Priority ?? int.MaxValue);
                    if (mergedPriority == int.MaxValue) mergedPriority = 0;

                    var mergedChildren = MergeChildren(existing.Children, item.Children);

                    _merged[item.Label] = new ContextMenuItemDto
                    {
                        Label    = existing.Label,
                        Id       = existing.Id,
                        Enabled  = (existing.Enabled == false || item.Enabled == false) ? false : null,
                        Priority  = mergedPriority,
                        Children  = mergedChildren,
                    };
                }
            }
        }

        /// <summary>
        /// Returns the aggregated and priority-sorted item list, then clears internal state.
        /// </summary>
        public IReadOnlyList<ContextMenuItemDto> ConsumeItems()
        {
            if (_merged.Count == 0)
                return Array.Empty<ContextMenuItemDto>();

            var result = new List<ContextMenuItemDto>(_merged.Values);
            result.Sort((a, b) =>
            {
                int pa = a.Priority ?? int.MaxValue;
                int pb = b.Priority ?? int.MaxValue;
                return pa.CompareTo(pb);
            });

            _merged.Clear();
            return result;
        }

        // Combines two nullable arrays of children, de-duplicating by label when both have submenus.
        private static ContextMenuItemDto[]? MergeChildren(ContextMenuItemDto[]? a, ContextMenuItemDto[]? b)
        {
            if (a == null && b == null) return null;
            if (a == null) return b;
            if (b == null) return a;

            var combined = new List<ContextMenuItemDto>(a);
            foreach (var bItem in b)
            {
                // Simple concat: duplicate labels within a submenu are allowed (different backends).
                combined.Add(bItem);
            }
            return combined.ToArray();
        }

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };
    }
}
