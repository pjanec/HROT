using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using CarKinem.Road;
using Fdp.Toolkit.Navigation;
using Xunit;

namespace Fdp.Toolkit.Terrain.Tests
{
    /// <summary>
    /// ⭐ T-7a (CE-4423, docs/DESIGN_Terrain_Authoring.md §18k-2) — a Bridge feature is ROAD: the compile snaps its ends to the base's road nodes (≤ 25 m), writes the segment into the overlay's roads file, and the load appends
    /// it after the base's. The base here is cut by a river between x 200 and 230: a west road and an east road that never meet.
    /// </summary>
    public sealed class BridgeRoadsTests
    {
        private const string River = "c1000000-0000-4000-8000-000000000001", Bridge = "c2000000-0000-4000-8000-000000000002";
        private static JsonObject RiverEntity() => OverlayFixture.Feature(TerrainFeature.Water, 215, 200, "", 0, 0, 0, (-15, -200), (15, -200), (15, 200), (-15, 200));
        private static JsonObject BridgeEntity(float x = 198) => OverlayFixture.Feature(TerrainFeature.Bridge, x, 200, "", 0, 4f, 0, (0, 0), (34, 0));

        private static bool Routes(in RoadNetworkBlob blob, Vector2 from, Vector2 to)
        {
            var a = RoadGraphRouter.NearestAccess(blob, from, 30f); var b = RoadGraphRouter.NearestAccess(blob, to, 30f);
            return a.IsValid && b.IsValid && new RoadGraphRouter().Route(blob, a, b, out _);
        }

        /// <summary>
        /// The vehicles' net: a route from the west bank to the east bank does not exist over the base, and does once the overlay's bridge road is appended. 🔴 Red-proof: skip writing the roads file in the compile ⇒ no route.
        /// </summary>
        [Fact]
        public void ARouteBetweenTheBanks_UsesTheBridge_AfterTheCompile_AndDoesNotExistBefore()
        {
            using var fx = new OverlayFixture(); fx.WithRoads();
            string baseRoads = Path.Combine(fx.TerrainFolder, "roads.json");
            using var before = new Holder(RoadNetworkLoader.LoadFromJson(baseRoads));
            Assert.False(Routes(before.Blob, new Vector2(160, 200), new Vector2(290, 200)), "the river cuts the road net");

            var r = fx.Compile(fx.Scenario((River, RiverEntity()), (Bridge, BridgeEntity())));
            Assert.Equal(1, r.BridgeRoads);
            string overlayRoads = Path.Combine(fx.Overlay, TerrainOverlayHeader.RoadsFileName);
            Assert.True(File.Exists(overlayRoads));

            using var after = new Holder(RoadNetworkLoader.LoadFromJson(baseRoads, appendJsonPath: overlayRoads));
            Assert.True(Routes(after.Blob, new Vector2(160, 200), new Vector2(290, 200)), "the bridge joins the banks");
        }

        /// <summary>Base road nodes and segments keep their indices: the bridge's segment is the LAST (index 2), and segments 0 and 1 are the base's, field for field.</summary>
        [Fact]
        public void TheBridgeSegment_IsAppended_AndEveryBaseIndexIsUnchanged()
        {
            using var fx = new OverlayFixture(); fx.WithRoads();
            string baseRoads = Path.Combine(fx.TerrainFolder, "roads.json");
            fx.Compile(fx.Scenario((River, RiverEntity()), (Bridge, BridgeEntity())));
            using var plain = new Holder(RoadNetworkLoader.LoadFromJson(baseRoads));
            using var merged = new Holder(RoadNetworkLoader.LoadFromJson(baseRoads, appendJsonPath: Path.Combine(fx.Overlay, TerrainOverlayHeader.RoadsFileName)));
            Assert.Equal(2, plain.Blob.Segments.Length);
            Assert.Equal(3, merged.Blob.Segments.Length);
            Assert.Equal(plain.Blob.Nodes.Length, merged.Blob.Nodes.Length);
            for (int i = 0; i < plain.Blob.Segments.Length; i++)
            {
                Assert.Equal(plain.Blob.Segments[i].P0, merged.Blob.Segments[i].P0);
                Assert.Equal(plain.Blob.Segments[i].P1, merged.Blob.Segments[i].P1);
                Assert.Equal(plain.Blob.Segments[i].StartNodeIndex, merged.Blob.Segments[i].StartNodeIndex);
                Assert.Equal(plain.Blob.Segments[i].EndNodeIndex, merged.Blob.Segments[i].EndNodeIndex);
            }
            var bridge = merged.Blob.Segments[2];
            Assert.Equal(1, bridge.StartNodeIndex); Assert.Equal(2, bridge.EndNodeIndex);   // snapped to the bank nodes
            var header = JsonNode.Parse(File.ReadAllText(Path.Combine(fx.Overlay, TerrainOverlayHeader.FileName)))!;
            var row = header["bridges"]![0]!;
            Assert.Equal(Bridge, (string)row["key"]!); Assert.True((bool)row["snapped"]!);
            Assert.Equal(2, (int)row["segments"]![0]!);
        }

        /// <summary>A bridge with no road node within 25 m of both ends is still a deck (it is in the world), but writes no roads file and is listed as not snapped.</summary>
        [Fact]
        public void ABridgeFarFromEveryRoad_IsADeckNotRoad()
        {
            using var fx = new OverlayFixture(); fx.WithRoads();
            var r = fx.Compile(fx.Scenario((Bridge, OverlayFixture.Feature(TerrainFeature.Bridge, 20, 20, "", 0, 4f, 0, (0, 0), (34, 0)))));
            Assert.True(r.Wrote); Assert.Equal(1, r.Bridges); Assert.Equal(0, r.BridgeRoads);
            Assert.False(File.Exists(Path.Combine(fx.Overlay, TerrainOverlayHeader.RoadsFileName)));
            var row = JsonNode.Parse(File.ReadAllText(Path.Combine(fx.Overlay, TerrainOverlayHeader.FileName)))!["bridges"]![0]!;
            Assert.False((bool)row["snapped"]!);
            Assert.Contains("\"slab\"", File.ReadAllText(Path.Combine(fx.Overlay, TerrainOverlayHeader.DefaultWorldFileName)));   // the deck is there
        }

        /// <summary>
        /// Two compiles are byte-identical (roads file, header, hash), and the overlay's hash covers the roads file: changing it changes <see cref="TerrainContentHash.OfOverlay"/> (a node refuses a torn overlay).
        /// 🔴 Red-proof: leave the roads file out of <c>OfOverlay</c> ⇒ the hash does not move.
        /// </summary>
        [Fact]
        public void TwoCompilesAreByteIdentical_AndTheHashCoversTheRoads()
        {
            using var fx = new OverlayFixture(); fx.WithRoads();
            var dom = fx.Scenario((River, RiverEntity()), (Bridge, BridgeEntity()));
            var first = fx.Compile(dom);
            string roads = Path.Combine(fx.Overlay, TerrainOverlayHeader.RoadsFileName);
            byte[] r1 = File.ReadAllBytes(roads), h1 = File.ReadAllBytes(Path.Combine(fx.Overlay, TerrainOverlayHeader.FileName));
            var second = fx.Compile(dom);
            Assert.Equal(first.ContentHash, second.ContentHash);
            Assert.Equal(r1, File.ReadAllBytes(roads)); Assert.Equal(h1, File.ReadAllBytes(Path.Combine(fx.Overlay, TerrainOverlayHeader.FileName)));
            Assert.Equal(first.ContentHash, TerrainContentHash.OfOverlay(fx.Overlay));
            File.AppendAllText(roads, " ");
            Assert.NotEqual(first.ContentHash, TerrainContentHash.OfOverlay(fx.Overlay));
        }

        /// <summary>The reader exposes road NODES (not only edges): the nearest within reach, none beyond it.</summary>
        [Fact]
        public void TerrainRoads_ExposesTheNodes_ForSnapping()
        {
            using var fx = new OverlayFixture(); fx.WithRoads();
            var roads = TerrainRoads.Load(fx.Definition, TerrainFolderWorld.ReadDefinition(fx.Definition))!;
            Assert.Equal(4, roads.NodeCount); Assert.Equal(2, roads.SegmentCount);
            Assert.True(roads.TryNearestNode(new Vector2(198, 200), 25f, out int i, out var pos)); Assert.Equal(1, i); Assert.Equal(new Vector2(190, 200), pos);
            Assert.False(roads.TryNearestNode(new Vector2(198, 260), 25f, out _, out _));
        }

        private sealed class Holder : System.IDisposable
        {
            public readonly RoadNetworkBlob Blob;
            public Holder(RoadNetworkBlob b) { Blob = b; }
            public void Dispose() => Blob.Dispose();
        }
    }
}
