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
    /// Global stateless gizmo projector that draws the SpatialHashGrid tile boundaries and
    /// per-cell entity counts when enabled via gizmo settings.
    /// </summary>
    [GizmoProjector]
    public sealed class SpatialGridGizmo : IGlobalStatelessGizmo
    {
        private readonly GizmoSettingsRegistry _settings;

        private static readonly Rgba32 GridColor  = new Rgba32(100, 100, 100, 128);
        private static readonly Rgba32 CountColor = new Rgba32(255, 255, 0, 255);

        public SpatialGridGizmo(GizmoSettingsRegistry settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            SpatialGridGizmoSettings.Register(settings);
        }

        public void Draw(ISimulationView view, IDebugDrawBuilder draw)
        {
            bool showTiles  = _settings.Read(GizmoSettingsRegistry.ComputeHash(SpatialGridGizmoSettings.ShowTilesKey)).BoolValue;
            bool showCounts = _settings.Read(GizmoSettingsRegistry.ComputeHash(SpatialGridGizmoSettings.ShowCountsKey)).BoolValue;

            if (!showTiles && !showCounts) return;

            if (showTiles)
            {
                float cellSize = SpatialHashConstants.CellSizeMeters;
                int width  = SpatialHashConstants.GridWidth;
                int height = SpatialHashConstants.GridHeight;
                float originX = SpatialHashConstants.OriginX;
                float originY = SpatialHashConstants.OriginY;

                // Draw vertical grid lines.
                for (int cx = 0; cx <= width; cx++)
                {
                    float x = originX + cx * cellSize;
                    var start = new Vector3(x, originY,                    0f);
                    var end   = new Vector3(x, originY + height * cellSize, 0f);
                    draw.DrawLine(start, end, GridColor, 1f, SizeMode.ScreenPixels);
                }

                // Draw horizontal grid lines.
                for (int cy = 0; cy <= height; cy++)
                {
                    float y = originY + cy * cellSize;
                    var start = new Vector3(originX,                   y, 0f);
                    var end   = new Vector3(originX + width * cellSize, y, 0f);
                    draw.DrawLine(start, end, GridColor, 1f, SizeMode.ScreenPixels);
                }
            }

            if (showCounts)
            {
                if (view is not EntityRepository repo) return;
                if (!repo.HasSingleton<SpatialGridData>()) return;

                ref readonly var data = ref repo.GetSingleton<SpatialGridData>();
                ref readonly var grid = ref data.Grid;

                // Count entities per cell by walking each cell's linked-list chain.
                for (int cy = 0; cy < grid.Height; cy++)
                {
                    for (int cx = 0; cx < grid.Width; cx++)
                    {
                        int cellIdx = cy * grid.Width + cx;
                        int slot    = grid.GridHead[cellIdx];
                        int count   = 0;
                        while (slot >= 0 && slot < grid.EntityCount)
                        {
                            count++;
                            slot = grid.GridNext[slot];
                        }
                        if (count == 0) continue;

                        float labelX = grid.OriginX + (cx + 0.5f) * grid.CellSize;
                        float labelY = grid.OriginY + (cy + 0.5f) * grid.CellSize;

                        // FixedString32 supports at most 31 chars; a cell count fits easily.
                        draw.DrawText(labelX, labelY, new Fdp.Core.FixedString32($"{count}"), CountColor);
                    }
                }
            }
        }
    }
}
