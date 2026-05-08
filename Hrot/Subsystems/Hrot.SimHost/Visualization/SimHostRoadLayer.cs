using System.Numerics;
using Raylib_cs;
using Fdp.Toolkit.Vis2D.Abstractions;
using CarKinem.Road;

namespace Hrot.SimHost.Visualization
{
    /// <summary>
    /// Renders the road-network graph (nodes + segments) on the 2-D map canvas.
    /// Mirrors <c>Fdp.Examples.CarKinem.Visualization.RoadMapLayer</c>.
    /// </summary>
    public class SimHostRoadLayer : IMapLayer
    {
        private readonly RoadNetworkBlob _network;

        public string Name        => "Road Network";
        public int    LayerBitIndex => 0;

        public SimHostRoadLayer(RoadNetworkBlob network)
            => _network = network;

        public void Update(float dt) { }

        public void Draw(RenderContext ctx)
        {
            if (!_network.Nodes.IsCreated || !_network.Segments.IsCreated)
                return;

            // Segments (roads)
            for (int i = 0; i < _network.Segments.Length; i++)
            {
                var seg = _network.Segments[i];
                Raylib.DrawLineEx(seg.P0, seg.P1, seg.LaneWidth * seg.LaneCount, Color.Gray);
                Raylib.DrawLineEx(seg.P0, seg.P1, 1.0f, Color.Yellow);
            }

            // Nodes (intersections)
            for (int i = 0; i < _network.Nodes.Length; i++)
            {
                var node = _network.Nodes[i];
                Raylib.DrawCircleV(node.Position, 2.0f, Color.Blue);
            }
        }

        public bool HandleInput(Vector2 worldPos, MapMouseButton button, bool pressed)
            => false;

        public Fdp.Core.Entity? PickEntity(Vector2 worldPos) => null;
    }
}
