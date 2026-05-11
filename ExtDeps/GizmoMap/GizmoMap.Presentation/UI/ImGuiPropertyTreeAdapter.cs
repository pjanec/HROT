using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using ImGuiNET;
using StructEdit.Core;
using StructEdit.Json;

namespace GizmoMap.Presentation
{
    /// <summary>
    /// Minimal adapter that schedules StructInspector rendering via ImGui.
    /// Call <see cref="Schedule"/> from the Raylib 2D pass; call <see cref="DrawScheduled"/>
    /// from the ImGui pass.
    /// When a <see cref="GizmoSchemaRegistry"/> is provided the adapter renders the full
    /// <see cref="EditDocument"/> property tree via ImGui tree nodes instead of a stub label.
    /// </summary>
    public sealed class ImGuiPropertyTreeAdapter
    {
        private readonly List<ScheduledItem> _items = new();
        private readonly GizmoSchemaRegistry? _registry;

        private readonly struct ScheduledItem
        {
            public readonly long         NetworkId;
            public readonly uint         SchemaHash;
            public readonly ScreenAnchor Anchor;
            public readonly float        OffsetX;
            public readonly float        OffsetY;
            public readonly SizeMode     SizeMode;
            public readonly bool         IsReadOnly;

            public ScheduledItem(long networkId, uint schemaHash, ScreenAnchor anchor, float offsetX, float offsetY, SizeMode sizeMode, bool isReadOnly)
            {
                NetworkId  = networkId;
                SchemaHash = schemaHash;
                Anchor     = anchor;
                OffsetX    = offsetX;
                OffsetY    = offsetY;
                SizeMode   = sizeMode;
                IsReadOnly = isReadOnly;
            }
        }

        public ImGuiPropertyTreeAdapter(GizmoSchemaRegistry? registry = null)
        {
            _registry = registry;
        }

        public void Schedule(long networkId, uint schemaHash, ScreenAnchor anchor, float offsetX, float offsetY, SizeMode sizeMode, bool isReadOnly)
        {
            _items.Add(new ScheduledItem(networkId, schemaHash, anchor, offsetX, offsetY, sizeMode, isReadOnly));
        }

        // Legacy overload without anchor positioning (defaults to TopLeft, ScreenPixels).
        public void Schedule(long networkId, uint schemaHash, float screenX, float screenY, bool isReadOnly)
        {
            _items.Add(new ScheduledItem(networkId, schemaHash, ScreenAnchor.TopLeft, screenX, screenY, SizeMode.ScreenPixels, isReadOnly));
        }

        public void DrawScheduled(Action<long, string>? onStructUpdate = null)
        {
            var viewport = ImGui.GetMainViewport();

            foreach (var item in _items)
            {
                // Resolve offset units against viewport.
                float deltaX = item.SizeMode == SizeMode.ScreenPercent ? item.OffsetX * viewport.WorkSize.X : item.OffsetX;
                float deltaY = item.SizeMode == SizeMode.ScreenPercent ? item.OffsetY * viewport.WorkSize.Y : item.OffsetY;

                // Resolve anchor base position and ImGui pivot.
                var basePos = viewport.WorkPos;
                var pivot   = new Vector2(0f, 0f);

                switch (item.Anchor)
                {
                    case ScreenAnchor.TopCenter:
                        basePos.X += viewport.WorkSize.X * 0.5f;
                        pivot = new Vector2(0.5f, 0f);
                        break;
                    case ScreenAnchor.TopRight:
                        basePos.X += viewport.WorkSize.X;
                        pivot = new Vector2(1f, 0f);
                        break;
                    case ScreenAnchor.Center:
                        basePos.X += viewport.WorkSize.X * 0.5f;
                        basePos.Y += viewport.WorkSize.Y * 0.5f;
                        pivot = new Vector2(0.5f, 0.5f);
                        break;
                    case ScreenAnchor.BottomLeft:
                        basePos.Y += viewport.WorkSize.Y;
                        pivot = new Vector2(0f, 1f);
                        break;
                    case ScreenAnchor.BottomCenter:
                        basePos.X += viewport.WorkSize.X * 0.5f;
                        basePos.Y += viewport.WorkSize.Y;
                        pivot = new Vector2(0.5f, 1f);
                        break;
                    case ScreenAnchor.BottomRight:
                        basePos.X += viewport.WorkSize.X;
                        basePos.Y += viewport.WorkSize.Y;
                        pivot = new Vector2(1f, 1f);
                        break;
                    // TopLeft: default (no adjustment)
                }

                // ImGuiCond.Appearing applies the layout intent once, but permits manual user drag afterwards.
                ImGui.SetNextWindowPos(basePos + new Vector2(deltaX, deltaY), ImGuiCond.Appearing, pivot);

                // Resolve schema early so we can use the struct name as the visible window title.
                EditDocument? doc = null;
                bool hasSchema = _registry != null && _registry.TryGet(item.SchemaHash, out doc) && doc != null;

                // Use ImGui ### syntax to decouple the visible title from the stable window ID.
                string windowTitle = hasSchema
                    ? $"{doc!.Root.Name} ({item.NetworkId})###StructInsp_{item.NetworkId}"
                    : $"Inspector {item.NetworkId} (0x{item.SchemaHash:X})###StructInsp_{item.NetworkId}";

                if (ImGui.Begin(windowTitle))
                {
                    if (hasSchema)
                    {
                        DrawEditNode(doc!.Root, item.IsReadOnly);

                        if (!item.IsReadOnly && onStructUpdate != null)
                        {
                            ImGui.Separator();
                            if (ImGui.Button("Apply"))
                            {
                                // Enforce the canonical StructEdit JSON schema across the network boundary.
                                string json = EditDocumentJsonSerializer.Serialize(doc!);
                                onStructUpdate.Invoke(item.NetworkId, json);
                            }
                        }
                    }
                    else
                    {
                        ImGui.Text($"Schema 0x{item.SchemaHash:X} not registered.");
                        if (item.IsReadOnly)
                            ImGui.TextDisabled("(read-only)");
                    }
                }
                ImGui.End();
            }
            _items.Clear();
        }

        // Recursively renders an EditNode and its children as an ImGui tree.
        private static void DrawEditNode(EditNode node, bool parentReadOnly)
        {
            bool ro = parentReadOnly || node.IsReadOnly;

            if (node.Children.Count > 0)
            {
                bool open = ImGui.TreeNode($"{node.Name}##{node.Id.Value}");
                if (ro) { ImGui.SameLine(); ImGui.TextDisabled("(read-only)"); }
                if (open)
                {
                    foreach (var child in node.Children)
                        DrawEditNode(child, ro);
                    ImGui.TreePop();
                }
            }
            else
            {
                // Leaf node: show name and binding value, allow editing when not read-only.
                string valueText = TryGetBindingText(node) ?? $"<{node.Kind}>";
                if (ro)
                {
                    ImGui.TextDisabled($"{node.Name}: {valueText}");
                }
                else if (node.Binding != null && node.Binding.ValueType == typeof(bool))
                {
                    bool val = node.Binding.GetBoxed() is bool b && b;
                    if (ImGui.Checkbox(node.Name, ref val))
                        node.Binding.SetBoxed(val);
                }
                else
                {
                    ImGui.Text($"{node.Name}: {valueText}");
                }
            }
        }

        private static string? TryGetBindingText(EditNode node)
        {
            if (node.Binding == null) return null;
            try
            {
                object? v = node.Binding.GetBoxed();
                return v?.ToString();
            }
            catch
            {
                return null;
            }
        }
    }
}

