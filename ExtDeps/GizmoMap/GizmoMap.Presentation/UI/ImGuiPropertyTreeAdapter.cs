using System.Collections.Generic;
using ImGuiNET;
using StructEdit.Core;

namespace GizmoMap.Presentation
{
    /// <summary>
    /// Minimal adapter that schedules ComponentInspector rendering via ImGui.
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
            public readonly long   NetworkId;
            public readonly uint   SchemaHash;
            public readonly float  ScreenX;
            public readonly float  ScreenY;
            public readonly bool   IsReadOnly;

            public ScheduledItem(long networkId, uint schemaHash, float screenX, float screenY, bool isReadOnly)
            {
                NetworkId  = networkId;
                SchemaHash = schemaHash;
                ScreenX    = screenX;
                ScreenY    = screenY;
                IsReadOnly = isReadOnly;
            }
        }

        public ImGuiPropertyTreeAdapter(GizmoSchemaRegistry? registry = null)
        {
            _registry = registry;
        }

        public void Schedule(long networkId, uint schemaHash, float screenX, float screenY, bool isReadOnly)
        {
            _items.Add(new ScheduledItem(networkId, schemaHash, screenX, screenY, isReadOnly));
        }

        public void DrawScheduled()
        {
            foreach (var item in _items)
            {
                ImGui.SetNextWindowPos(new System.Numerics.Vector2(item.ScreenX, item.ScreenY));
                if (ImGui.Begin($"Entity {item.NetworkId}"))
                {
                    if (_registry != null && _registry.TryGet(item.SchemaHash, out var doc) && doc != null)
                    {
                        DrawEditNode(doc.Root, item.IsReadOnly);
                    }
                    else
                    {
                        ImGui.Text($"Entity {item.NetworkId} schema 0x{item.SchemaHash:X}");
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
                // Leaf node: show name, kind and binding value if available.
                string valueText = TryGetBindingText(node) ?? $"<{node.Kind}>";
                if (ro)
                    ImGui.TextDisabled($"{node.Name}: {valueText}");
                else
                    ImGui.Text($"{node.Name}: {valueText}");
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

