using System;
using System.Numerics;
using Fdp.Kernel;
using ModuleHost.Core.Abstractions;
using FDP.Toolkit.Vis2D.Abstractions;
using Raylib_cs;

namespace FDP.Toolkit.Vis2D.Defaults
{
    public class DelegateAdapter : IVisualizerAdapter
    {
        private readonly Func<ISimulationView, Entity, Vector2?> _positionExtractor;
        private readonly Action<ISimulationView, Entity, Vector2, RenderContext, bool, bool>? _drawFunc;
        private readonly Func<ISimulationView, Entity, float>? _radiusFunc;

        public DelegateAdapter(
            Func<ISimulationView, Entity, Vector2?> positionExtractor,
            Action<ISimulationView, Entity, Vector2, RenderContext, bool, bool>? drawFunc = null,
            Func<ISimulationView, Entity, float>? radiusFunc = null)
        {
            _positionExtractor = positionExtractor;
            _drawFunc = drawFunc;
            _radiusFunc = radiusFunc;
        }

        public Vector2? GetPosition(ISimulationView view, Entity entity)
        {
            return _positionExtractor(view, entity);
        }

        public void Render(ISimulationView view, Entity entity, Vector2 position, RenderContext ctx, bool isSelected, bool isHovered)
        {
            if (_drawFunc != null)
            {
                _drawFunc(view, entity, position, ctx, isSelected, isHovered);
            }
            else
            {
                // Default Rendering (fallout)
                Color color = isSelected ? Color.Green : (isHovered ? Color.Yellow : Color.Blue);
                float radius = GetHitRadius(view, entity);
                Raylib.DrawCircleV(position, radius, color);
            }
        }

        public float GetHitRadius(ISimulationView view, Entity entity)
        {
            return _radiusFunc?.Invoke(view, entity) ?? 5.0f; // Default 5 units
        }
    }
}
