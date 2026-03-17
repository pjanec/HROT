using System.Numerics;
using CarKinem.Road;

namespace Fdp.Examples.Common.Helpers
{
    /// <summary>
    /// Builds a minimal 4-way city intersection <see cref="RoadNetworkBlob"/> for use in
    /// deterministic offline test scenarios that exercise <c>CarKinematicsSystem</c>.
    ///
    /// <para>
    /// The graph has 5 nodes (centre + 4 cardinal endpoints) and 8 directed segments
    /// (one inbound + one outbound per arm), matching the layout used in
    /// <c>Fdp.Examples.UrbanCombat.Setup.DemoEnvironmentSetup</c>.
    /// The caller is responsible for <see cref="RoadNetworkBlob.Dispose"/>ing the returned blob.
    /// </para>
    /// </summary>
    public static class DemoRoadGraphFactory
    {
        private const float EndpointDistance = 100f;
        private const float GridCellSize     = 20f;
        private const int   GridWidth        = 20;
        private const int   GridHeight       = 20;

        /// <summary>
        /// Creates a 4-way intersection road network blob.
        /// </summary>
        /// <returns>
        /// A <see cref="RoadNetworkBlob"/> with 5 nodes and 8 segments.
        /// Nodes: 0 = Centre, 1 = North, 2 = South, 3 = East, 4 = West.
        /// </returns>
        public static RoadNetworkBlob CreateCityIntersection()
        {
            var builder = new RoadNetworkBuilder();

            var centre = new Vector2(0f, 0f);
            var north  = new Vector2(0f,  EndpointDistance);
            var south  = new Vector2(0f, -EndpointDistance);
            var east   = new Vector2( EndpointDistance, 0f);
            var west   = new Vector2(-EndpointDistance, 0f);

            builder.AddNode(centre); // 0
            builder.AddNode(north);  // 1
            builder.AddNode(south);  // 2
            builder.AddNode(east);   // 3
            builder.AddNode(west);   // 4

            float halfDist = EndpointDistance * 0.5f;

            var tNS = new Vector2(0f,  halfDist);
            var tSN = new Vector2(0f, -halfDist);
            var tEW = new Vector2( halfDist, 0f);
            var tWE = new Vector2(-halfDist, 0f);

            // Segment 0: North → Centre (inbound)
            builder.AddSegment(north,  tSN, centre, tSN, startNodeIdx: 1, endNodeIdx: 0);
            // Segment 1: Centre → North (outbound)
            builder.AddSegment(centre, tNS, north,  tNS, startNodeIdx: 0, endNodeIdx: 1);
            // Segment 2: South → Centre (inbound)
            builder.AddSegment(south,  tNS, centre, tNS, startNodeIdx: 2, endNodeIdx: 0);
            // Segment 3: Centre → South (outbound)
            builder.AddSegment(centre, tSN, south,  tSN, startNodeIdx: 0, endNodeIdx: 2);
            // Segment 4: East → Centre (inbound)
            builder.AddSegment(east,   tWE, centre, tWE, startNodeIdx: 3, endNodeIdx: 0);
            // Segment 5: Centre → East (outbound)
            builder.AddSegment(centre, tEW, east,   tEW, startNodeIdx: 0, endNodeIdx: 3);
            // Segment 6: West → Centre (inbound)
            builder.AddSegment(west,   tEW, centre, tEW, startNodeIdx: 4, endNodeIdx: 0);
            // Segment 7: Centre → West (outbound)
            builder.AddSegment(centre, tWE, west,   tWE, startNodeIdx: 0, endNodeIdx: 4);

            return builder.Build(GridCellSize, GridWidth, GridHeight);
        }
    }
}
