using System.Numerics;
using Raylib_cs;
using Fdp.Toolkit.Vis2D.Abstractions;
using CarKinem.Road;

namespace Fdp.Examples.CarKinem.Visualization
{
    public class RoadMapLayer : IMapLayer
    {
        private readonly RoadNetworkBlob _network;
        
        public string Name => "Road Network";
        public int LayerBitIndex => 0; // Layer 0
        
        public RoadMapLayer(RoadNetworkBlob network)
        {
            _network = network;
        }

        public void Update(float dt) { }

        public void Draw(RenderContext ctx)
        {
            if (!_network.Nodes.IsCreated || !_network.Segments.IsCreated) return;
            
            // Draw segments (roads)
            for (int i = 0; i < _network.Segments.Length; i++)
            {
                var segment = _network.Segments[i];
                Vector2 start = segment.P0;
                Vector2 end = segment.P1;
                
                Raylib.DrawLineEx(start, end, segment.LaneWidth * segment.LaneCount, Color.Gray);
                Raylib.DrawLineEx(start, end, 1.0f, Color.Yellow);
            }
            
            // Draw nodes (intersections)
            for (int i = 0; i < _network.Nodes.Length; i++)
            {
                var node = _network.Nodes[i];
                Raylib.DrawCircleV(node.Position, 2.0f, Color.Blue);
            }
        }

        public bool HandleInput(Vector2 worldPos, MouseButton button, bool pressed) => false;
        
        public Fdp.Core.Entity? PickEntity(Vector2 worldPos) => null;
    }
}
