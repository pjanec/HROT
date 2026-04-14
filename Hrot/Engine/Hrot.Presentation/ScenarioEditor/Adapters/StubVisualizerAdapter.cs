using System.Numerics;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Vis2D.Abstractions;
using Fdp.Kernel;
using Fdp.ModuleHost_Core.Abstractions;
using Raylib_cs;

namespace Hrot.ScenarioEditor.Adapters;

/// <summary>
    /// Renders a placeholder red circle (10 px) for every entity that has a
    /// <see cref="SimTransform"/> component.
    ///
    /// If the entity also carries a <see cref="NetworkIdentity"/> the network ID is
    /// overlaid as a text label below the circle.
    ///
    /// This adapter is the Phase-1 / stub visualizer â€” it will be replaced by full
    /// TKB-driven symbol rendering in a later batch.
    /// </summary>
    public class StubVisualizerAdapter : IVisualizerAdapter
    {
        // â”€â”€ IVisualizerAdapter â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <inheritdoc/>
        public Vector2? GetPosition(ISimulationView view, Entity entity)
        {
            if (!view.HasComponent<SimTransform>(entity))
                return null;

            ref readonly var transform = ref view.GetComponentRO<SimTransform>(entity);
            return new Vector2(transform.Position.X, transform.Position.Y);
        }

        /// <inheritdoc/>
        /// <remarks>Called inside Raylib <c>BeginMode2D</c>.</remarks>
        public void Render(
            ISimulationView view,
            Entity          entity,
            Vector2         position,
            RenderContext   ctx,
            bool            isSelected,
            bool            isHovered)
        {
            Color circleColor = isSelected ? Color.Yellow
                              : isHovered  ? Color.Orange
                              :              Color.Red;

            Raylib.DrawCircle(
                (int)position.X,
                (int)position.Y,
                StubVisualizerConstants.CircleRadiusPx,
                circleColor);

            // Entity ID label if a NetworkIdentity is present
            if (view.HasComponent<NetworkIdentity>(entity))
            {
                ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);
                string label  = $"#{netId.Value}";
                var    labelPos = new Vector2(
                    position.X,
                    position.Y + StubVisualizerConstants.LabelOffsetPx);

                Raylib.DrawText(
                    label,
                    (int)labelPos.X,
                    (int)labelPos.Y,
                    StubVisualizerConstants.LabelFontSize,
                    Color.White);
            }
        }

        /// <inheritdoc/>
        public float GetHitRadius(ISimulationView view, Entity entity)
            => StubVisualizerConstants.HitRadiusWorldUnits;

        /// <inheritdoc/>
        public string? GetHoverLabel(ISimulationView view, Entity entity)
        {
            if (view.HasComponent<NetworkIdentity>(entity))
            {
                ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);
                return $"Entity #{netId.Value}";
            }
            return null;
        }
    }
