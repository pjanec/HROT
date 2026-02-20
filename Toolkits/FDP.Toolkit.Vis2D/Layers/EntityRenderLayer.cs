using System.Numerics;
using Fdp.Kernel;
using ModuleHost.Core.Abstractions;
using FDP.Toolkit.Vis2D.Abstractions;
using FDP.Toolkit.Vis2D.Components;
using Raylib_cs;

namespace FDP.Toolkit.Vis2D.Layers
{
    public class EntityRenderLayer : IMapLayer
    {
        public string Name { get; private set; }
        public int LayerBitIndex { get; private set; }

        private readonly EntityQuery _query;
        private readonly IVisualizerAdapter _adapter;
        private readonly ISelectionState _selection;
        private readonly ISimulationView _view; // Usually Global.World or passed in

        public EntityRenderLayer(
            string name, 
            int layerBitIndex, 
            ISimulationView view,
            EntityQuery query, 
            IVisualizerAdapter adapter, 
            ISelectionState selection)
        {
            Name = name;
            LayerBitIndex = layerBitIndex;
            _view = view; // We need a view to access components
            _query = query;
            _adapter = adapter;
            _selection = selection;
        }

        public void Update(float dt)
        {
            // No-op for static entity rendering usually
        }

        public void Draw(RenderContext ctx)
        {
            // Filter: Layer Mask
            // Check if this layer is enabled in the context
            uint maskBit = 1u << LayerBitIndex;
            if ((ctx.VisibleLayersMask & maskBit) == 0 && LayerBitIndex >= 0)
                return;

            foreach (var entity in _query)
            {
                // Entity-level filtering
                // If entity has MapDisplayComponent, check if it matches the current layer index
                // Actually, logic is: Does the entity belong to this layer?
                // The layer itself (EntityRenderLayer) is assigned a specific "Meaning" (e.g. Ground Units = Bit 0).
                // We check if the Entity has Bit 0 set in its MapDisplayComponent.
                
                // If entity doesn't have MapDisplayComponent, assume it's on Layer 0? Or hidden?
                // Default is usually Layer 0.
                
                uint entityMask = 1; // Default to layer 0
                if (_view.HasComponent<MapDisplayComponent>(entity))
                {
                    entityMask = _view.GetComponentRO<MapDisplayComponent>(entity).LayerMask;
                }
                
                // If the entity is NOT on this layer, skip it.
                if ((entityMask & maskBit) == 0 && LayerBitIndex >= 0)
                    continue;

                // Get Position
                var pos = _adapter.GetPosition(_view, entity);
                if (!pos.HasValue) continue;

                // Selection State
                bool isSelected = _selection.IsSelected(entity);
                bool isHovered = _selection.HoveredEntity == entity;

                // Render
                _adapter.Render(_view, entity, pos.Value, ctx, isSelected, isHovered);
            }
        }

        public bool HandleInput(Vector2 worldPos, MouseButton button, bool isPressed)
        {
            // Only handle clicks (Pressed)
            if (!isPressed) return false;

            // Hit Testing
            // Find closest entity that is hit
            float bestDistSq = float.MaxValue;
            Entity bestEntity = Entity.Null;

            foreach (var entity in _query)
            {
                // Redundant check for layer membership?
                // Yes, if entity is not on this layer, we shouldn't be valid target?
                // Or do we assume HandleInput is called only if layer is "Active"?
                // MapCanvas checks IsLayerVisible(layer) before calling HandleInput.
                // But we also need to check if entity belongs to this layer.
                
                uint maskBit = 1u << LayerBitIndex;
                uint entityMask = 1;
                if (_view.HasComponent<MapDisplayComponent>(entity))
                    entityMask = _view.GetComponentRO<MapDisplayComponent>(entity).LayerMask;

                if ((entityMask & maskBit) == 0 && LayerBitIndex >= 0)
                     continue;

                Vector2? pos = _adapter.GetPosition(_view, entity);
                if (!pos.HasValue) continue;

                float radius = _adapter.GetHitRadius(_view, entity);
                float distSq = Vector2.DistanceSquared(pos.Value, worldPos);

                if (distSq <= radius * radius)
                {
                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        bestEntity = entity;
                    }
                }
            }

            if (_view.IsAlive(bestEntity))
            {
                _selection.PrimarySelected = bestEntity;
                return true; // Consumed
            }

            return false;
        }

        public Entity? PickEntity(Vector2 worldPos)
        {
            float bestDistSq = float.MaxValue;
            Entity bestEntity = Entity.Null;
            bool found = false;

            uint maskBit = 1u << LayerBitIndex;
            // Note: We don't check ctx.VisibleLayersMask here because PickEntity is called 
            // by MapCanvas which assumes the caller (or MapCanvas) verified layer visibility.
            // MapCanvas checks IsLayerVisible(layer) before calling PickEntity.
            
            foreach (var entity in _query)
            {
                uint entityMask = 1; 
                if (_view.HasComponent<MapDisplayComponent>(entity))
                {
                    entityMask = _view.GetComponentRO<MapDisplayComponent>(entity).LayerMask;
                }
                
                if ((entityMask & maskBit) == 0 && LayerBitIndex >= 0)
                    continue;

                Vector2? pos = _adapter.GetPosition(_view, entity);
                if (!pos.HasValue) continue;

                float radius = _adapter.GetHitRadius(_view, entity);
                float distSq = Vector2.DistanceSquared(pos.Value, worldPos);

                if (distSq <= radius * radius)
                {
                    // Found a hit. Check if closer.
                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        bestEntity = entity;
                        found = true;
                        // For overlapping entities, we want the "top" one. 
                        // But query order is arbitrary. 
                        // Standard allows returning any, or closest to center.
                        // Closest to center (lowest distSq) is best.
                    }
                }
            }
            
            return found ? bestEntity : null;
        }
    }
}
