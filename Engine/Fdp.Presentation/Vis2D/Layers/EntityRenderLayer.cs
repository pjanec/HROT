using System.Numerics;
using Fdp.Kernel;
using Fdp.ModuleHost.Abstractions;
using FDP.Toolkit.Vis2D.Abstractions;
using FDP.Toolkit.Vis2D.Components;
using Raylib_cs;

namespace FDP.Toolkit.Vis2D.Layers
{
    public class EntityRenderLayer : IMapLayer
    {
        public string Name { get; private set; }
        public int LayerBitIndex { get; private set; }

        /// <summary>
        /// Canvas reference used for catch-all mode (<see cref="LayerBitIndex"/> == -1):
        /// entity masks are checked against <see cref="MapCanvas.ActiveLayerMask"/> so
        /// entities on hidden layers are skipped.
        /// Set by the host application after construction (BUG2-V001).
        /// </summary>
        public MapCanvas? Canvas { get; set; }

        private readonly EntityQuery _query;
        private readonly IVisualizerAdapter _adapter;
        private readonly ISelectionState _selection;
        private readonly ISimulationView _view;

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
            _view = view;
            _query = query;
            _adapter = adapter;
            _selection = selection;
        }

        public void Update(float dt) { /* No per-frame state */ }

        public void Draw(RenderContext ctx)
        {
            // For a specific layer bit (>= 0): skip the entire layer if its bit is off.
            if (LayerBitIndex >= 0)
            {
                uint maskBit = 1u << LayerBitIndex;
                if ((ctx.VisibleLayersMask & maskBit) == 0) return;
            }

            foreach (var entity in _query)
            {
                uint entityMask = _view.HasComponent<MapDisplayComponent>(entity)
                    ? _view.GetComponentRO<MapDisplayComponent>(entity).LayerMask
                    : 1u;

                // In catch-all mode (-1): skip entities whose entire layer mask is hidden.
                if ((entityMask & ctx.VisibleLayersMask) == 0) continue;

                // In specific-bit mode (>= 0): entity must belong to this layer's bit.
                if (LayerBitIndex >= 0)
                {
                    uint bit = 1u << LayerBitIndex;
                    if ((entityMask & bit) == 0) continue;
                }

                var pos = _adapter.GetPosition(_view, entity);
                if (!pos.HasValue) continue;

                bool isSelected = _selection.IsSelected(entity);
                bool isHovered  = _selection.HoveredEntity == entity;

                _adapter.Render(_view, entity, pos.Value, ctx, isSelected, isHovered);
            }
        }

        public bool HandleInput(Vector2 worldPos, MouseButton button, bool isPressed)
        {
            if (!isPressed) return false;

            float bestDistSq = float.MaxValue;
            Entity bestEntity = Entity.Null;

            foreach (var entity in _query)
            {
                uint entityMask = _view.HasComponent<MapDisplayComponent>(entity)
                    ? _view.GetComponentRO<MapDisplayComponent>(entity).LayerMask
                    : 1u;

                if (LayerBitIndex >= 0)
                {
                    uint bit = 1u << LayerBitIndex;
                    if ((entityMask & bit) == 0) continue;
                }

                Vector2? pos = _adapter.GetPosition(_view, entity);
                if (!pos.HasValue) continue;

                float radius = _adapter.GetHitRadius(_view, entity);
                float distSq = Vector2.DistanceSquared(pos.Value, worldPos);

                if (distSq <= radius * radius && distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestEntity = entity;
                }
            }

            if (_view.IsAlive(bestEntity))
            {
                _selection.PrimarySelected = bestEntity;
                return true;
            }

            return false;
        }

        public Entity? PickEntity(Vector2 worldPos)
        {
            float bestDistSq = float.MaxValue;
            Entity bestEntity = Entity.Null;
            bool found = false;

            foreach (var entity in _query)
            {
                uint entityMask = _view.HasComponent<MapDisplayComponent>(entity)
                    ? _view.GetComponentRO<MapDisplayComponent>(entity).LayerMask
                    : 1u;

                if (LayerBitIndex >= 0)
                {
                    uint bit = 1u << LayerBitIndex;
                    if ((entityMask & bit) == 0) continue;
                }

                Vector2? pos = _adapter.GetPosition(_view, entity);
                if (!pos.HasValue) continue;

                float radius = _adapter.GetHitRadius(_view, entity);
                float distSq = Vector2.DistanceSquared(pos.Value, worldPos);

                if (distSq <= radius * radius && distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestEntity = entity;
                    found = true;
                }
            }
            
            return found ? bestEntity : null;
        }
    }
}
