using System.Collections.Generic;
using ImGuiNET;

namespace GizmoMap.Presentation
{
    /// <summary>
    /// Minimal adapter that schedules ComponentInspector rendering via ImGui.
    /// Call <see cref="Schedule"/> from the Raylib 2D pass; call <see cref="DrawScheduled"/>
    /// from the ImGui pass.
    /// Full StructEdit integration is out of scope for this batch.
    /// </summary>
    public sealed class ImGuiPropertyTreeAdapter
    {
        private readonly List<ScheduledItem> _items = new();

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
                    ImGui.Text($"Entity {item.NetworkId} schema 0x{item.SchemaHash:X}");
                    if (item.IsReadOnly)
                        ImGui.TextDisabled("(read-only)");
                }
                ImGui.End();
            }
            _items.Clear();
        }
    }
}
