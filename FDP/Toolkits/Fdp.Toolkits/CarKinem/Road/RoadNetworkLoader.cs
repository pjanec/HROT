using System;
using System.IO;
using System.Text.Json;
using System.Numerics;
using System.Threading;
using Fdp.Core.Serialization.Migrations.Adapters;
using Fdp.Core.Serialization;

namespace CarKinem.Road
{
    /// <summary>
    /// Loader for road networks from JSON files.
    /// </summary>
    public static class RoadNetworkLoader
    {
        /// <summary>
        /// Load road network from JSON file.
        /// </summary>
        public static RoadNetworkBlob LoadFromJson(string jsonPath, ReadOnlyMigrationAdapter? migrationAdapter = null)
        {
            if (!File.Exists(jsonPath))
                throw new FileNotFoundException($"Road network file not found: {jsonPath}");

            RoadNetworkJson? roadData;
            if (migrationAdapter != null)
            {
                var outcome = migrationAdapter.LoadAndMigrateAsync(jsonPath, CancellationToken.None).GetAwaiter().GetResult();
                roadData = JsonSerializer.Deserialize<RoadNetworkJson>(outcome.AsJsonString(), FdpJsonOptionsRegistry.DefaultRelaxed);
            }
            else
            {
                string jsonContent = Fdp.Core.ProgressStream.ReadAllText(jsonPath);   // ⭐ CE-2417 — reports while it reads
                roadData = JsonSerializer.Deserialize<RoadNetworkJson>(jsonContent, FdpJsonOptionsRegistry.DefaultRelaxed);
            }
            
            if (roadData == null)
                throw new InvalidOperationException("Failed to deserialize road network JSON");
            
            var builder = new RoadNetworkBuilder();
            
            // Add nodes
            foreach (var node in roadData.Nodes)
            {
                Fdp.Core.LoadProgress.Report();   // ⭐ CE-2417 — the load advances (docs/DESIGN_Cluster_Load_Phase.md §12)
                builder.AddNode(node.Position.ToVector2(), node.AboveGround);
            }
            
            // Add segments
            foreach (var seg in roadData.Segments)
            {
                Fdp.Core.LoadProgress.Report();   // ⭐ CE-2417 — the load advances (docs/DESIGN_Cluster_Load_Phase.md §12)
                builder.AddSegment(
                    seg.ControlPoints.P0.ToVector2(),
                    seg.ControlPoints.T0.ToVector2(),
                    seg.ControlPoints.P1.ToVector2(),
                    seg.ControlPoints.T1.ToVector2(),
                    seg.SpeedLimit,
                    seg.LaneWidth,
                    seg.LaneCount,
                    seg.StartNodeId,
                    seg.EndNodeId
                );
            }
            
            // Build with metadata
            float cellSize = roadData.Metadata?.GridCellSize ?? 5.0f;
            var bounds = roadData.Metadata?.WorldBounds;
            
            int width, height;
            if (bounds != null)
            {
                float worldWidth = bounds.Max.X - bounds.Min.X;
                float worldHeight = bounds.Max.Y - bounds.Min.Y;
                width = (int)MathF.Ceiling(worldWidth / cellSize);
                height = (int)MathF.Ceiling(worldHeight / cellSize);
            }
            else
            {
                // Default grid size
                width = 100;
                height = 100;
            }
            
            return builder.Build(cellSize, width, height);
        }
    }
}
