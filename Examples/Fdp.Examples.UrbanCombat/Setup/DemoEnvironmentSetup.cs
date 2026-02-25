using System.Numerics;
using CarKinem.Road;

namespace Fdp.Examples.UrbanCombat.Setup
{
    /// <summary>
    /// Builds the static environment geometry for the Urban Ambush demo scenario.
    /// </summary>
    public static class DemoEnvironmentSetup
    {
        // ── Road graph constants ─────────────────────────────────────────────────────

        /// <summary>Distance from the intersection centre to each road endpoint (metres).</summary>
        private const float EndpointDistance = 100f;

        /// <summary>Spatial grid cell size for the road network builder (metres).</summary>
        private const float GridCellSize = 20f;

        /// <summary>Grid width in cells — covers the intersection plus road arms.</summary>
        private const int GridWidth = 20;

        /// <summary>Grid height in cells — covers the intersection plus road arms.</summary>
        private const int GridHeight = 20;

        /// <summary>
        /// Creates a 4-way city intersection road graph:<br/>
        /// 5 nodes: center (0,0) + N (0,100) + S (0,-100) + E (100,0) + W (-100,0).<br/>
        /// 8 segments: 4 inbound (endpoint → centre) + 4 outbound (centre → endpoint).<br/>
        /// Returns a <see cref="RoadNetworkBlob"/> ready for <c>CarKinematicsSystem</c>.<br/>
        /// <b>Caller is responsible for disposing the returned blob</b> when done.
        /// </summary>
        /// <remarks>
        /// Node indices:
        /// <list type="bullet">
        ///   <item>0 = Centre  (0, 0)</item>
        ///   <item>1 = North   (0, +100)</item>
        ///   <item>2 = South   (0, −100)</item>
        ///   <item>3 = East    (+100, 0)</item>
        ///   <item>4 = West    (−100, 0)</item>
        /// </list>
        /// Segment layout (inbound = arm endpoint → centre, outbound = centre → arm endpoint):
        /// <list type="bullet">
        ///   <item>0  North → Centre (inbound)</item>
        ///   <item>1  Centre → North (outbound)</item>
        ///   <item>2  South → Centre (inbound)</item>
        ///   <item>3  Centre → South (outbound)</item>
        ///   <item>4  East  → Centre (inbound)</item>
        ///   <item>5  Centre → East  (outbound)</item>
        ///   <item>6  West  → Centre (inbound)</item>
        ///   <item>7  Centre → West  (outbound)</item>
        /// </list>
        /// </remarks>
        public static RoadNetworkBlob CreateCityIntersection()
        {
            var builder = new RoadNetworkBuilder();

            // ── Add 5 nodes ──────────────────────────────────────────────────────────
            // Index 0: Centre
            var centre = new Vector2(0f, 0f);
            builder.AddNode(centre);

            // Index 1: North
            var north = new Vector2(0f, EndpointDistance);
            builder.AddNode(north);

            // Index 2: South
            var south = new Vector2(0f, -EndpointDistance);
            builder.AddNode(south);

            // Index 3: East
            var east = new Vector2(EndpointDistance, 0f);
            builder.AddNode(east);

            // Index 4: West
            var west = new Vector2(-EndpointDistance, 0f);
            builder.AddNode(west);

            // ── Hermite tangent vectors ───────────────────────────────────────────────
            // For straight roads the tangent magnitude equals half the segment length, which
            // gives a smooth linear-like curve through the Hermite parameterisation.
            float halfDist = EndpointDistance * 0.5f;

            var tangentNS = new Vector2(0f,  halfDist);  // North–South axis tangent (pointing north)
            var tangentSN = new Vector2(0f, -halfDist);  // South–North axis tangent (pointing south)
            var tangentEW = new Vector2( halfDist, 0f);  // East–West axis tangent (pointing east)
            var tangentWE = new Vector2(-halfDist, 0f);  // West–East axis tangent (pointing west)

            // ── Add 8 segments ───────────────────────────────────────────────────────

            // Segment 0: North → Centre (inbound)
            builder.AddSegment(north, tangentSN, centre, tangentSN, startNodeIdx: 1, endNodeIdx: 0);

            // Segment 1: Centre → North (outbound)
            builder.AddSegment(centre, tangentNS, north, tangentNS, startNodeIdx: 0, endNodeIdx: 1);

            // Segment 2: South → Centre (inbound)
            builder.AddSegment(south, tangentNS, centre, tangentNS, startNodeIdx: 2, endNodeIdx: 0);

            // Segment 3: Centre → South (outbound)
            builder.AddSegment(centre, tangentSN, south, tangentSN, startNodeIdx: 0, endNodeIdx: 2);

            // Segment 4: East → Centre (inbound)
            builder.AddSegment(east, tangentWE, centre, tangentWE, startNodeIdx: 3, endNodeIdx: 0);

            // Segment 5: Centre → East (outbound)
            builder.AddSegment(centre, tangentEW, east, tangentEW, startNodeIdx: 0, endNodeIdx: 3);

            // Segment 6: West → Centre (inbound)
            builder.AddSegment(west, tangentEW, centre, tangentEW, startNodeIdx: 4, endNodeIdx: 0);

            // Segment 7: Centre → West (outbound)
            builder.AddSegment(centre, tangentWE, west, tangentWE, startNodeIdx: 0, endNodeIdx: 4);

            // ── Build and return ──────────────────────────────────────────────────────
            return builder.Build(GridCellSize, GridWidth, GridHeight);
        }
    }
}
