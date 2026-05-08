using System;
using System.Numerics;
using CarKinem.Spatial;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;

namespace Hrot.Common.Diagnostics.Gizmos
{
    /// <summary>
    /// ECS system that draws the SpatialHashGrid tile boundaries and per-cell entity counts
    /// when enabled via gizmo settings.
    ///
    /// Registered as an <see cref="IEcsModuleSystem"/> (not a stateless gizmo projector)
    /// because it reads the <see cref="SpatialGridData"/> singleton rather than iterating
    /// per-entity component data.
    /// </summary>
    [UpdateInPhase(SystemPhase.PostSimulation)]
    public sealed class SpatialGridGizmo : IEcsModuleSystem
    {
        private readonly IDebugDrawBuilder _draw;
        private readonly GizmoSettingsRegistry _settings;

        private static readonly Rgba32 GridColor  = new Rgba32(100, 100, 100, 128);
        private static readonly Rgba32 CountColor = new Rgba32(255, 255, 0, 255);

        public SpatialGridGizmo(IDebugDrawBuilder draw, GizmoSettingsRegistry settings)
        {
            _draw     = draw     ?? throw new ArgumentNullException(nameof(draw));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            SpatialGridGizmoSettings.Register(settings);
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            bool showTiles  = _settings.Read(GizmoSettingsRegistry.ComputeHash(SpatialGridGizmoSettings.ShowTilesKey)).BoolValue;
            bool showCounts = _settings.Read(GizmoSettingsRegistry.ComputeHash(SpatialGridGizmoSettings.ShowCountsKey)).BoolValue;

            if (!showTiles && !showCounts) return;

            if (view is not EntityRepository repo) return;
            if (!repo.HasSingleton<SpatialGridData>()) return;

            ref readonly var data = ref repo.GetSingleton<SpatialGridData>();
            ref readonly var grid = ref data.Grid;

            float cellSize = grid.CellSize;
            if (cellSize <= 0f) return;

            int width  = grid.Width;
            int height = grid.Height;
            float originX = grid.OriginX;
            float originY = grid.OriginY;

            if (showTiles)
            {
                // Draw vertical grid lines.
                for (int cx = 0; cx <= width; cx++)
                {
                    float x = originX + cx * cellSize;
                    var start = new Vector3(x, originY,                    0f);
                    var end   = new Vector3(x, originY + height * cellSize, 0f);
                    _draw.DrawLine(start, end, GridColor, 1f, SizeMode.ScreenPixels);
                }

                // Draw horizontal grid lines.
                for (int cy = 0; cy <= height; cy++)
                {
                    float y = originY + cy * cellSize;
                    var start = new Vector3(originX,                   y, 0f);
                    var end   = new Vector3(originX + width * cellSize, y, 0f);
                    _draw.DrawLine(start, end, GridColor, 1f, SizeMode.ScreenPixels);
                }
            }

            if (showCounts)
            {
                // Count entities per cell by walking each cell's linked-list chain.
                for (int cy = 0; cy < height; cy++)
                {
                    for (int cx = 0; cx < width; cx++)
                    {
                        int cellIdx = cy * width + cx;
                        int slot    = grid.GridHead[cellIdx];
                        int count   = 0;
                        while (slot >= 0 && slot < grid.EntityCount)
                        {
                            count++;
                            slot = grid.GridNext[slot];
                        }
                        if (count == 0) continue;

                        float labelX = originX + (cx + 0.5f) * cellSize;
                        float labelY = originY + (cy + 0.5f) * cellSize;

                        // FixedString32 supports at most 31 chars; a cell count fits easily.
                        _draw.DrawText(labelX, labelY, new Fdp.Core.FixedString32($"{count}"), CountColor);
                    }
                }
            }
        }
    }
}
