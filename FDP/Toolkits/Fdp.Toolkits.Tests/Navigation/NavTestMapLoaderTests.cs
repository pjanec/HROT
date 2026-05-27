using System;
using System.IO;
using System.Numerics;
using Fdp.Toolkit.Navigation.Fake;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests
{
    /// <summary>
    /// Unit tests for <see cref="NavTestMapLoader"/>.
    /// </summary>
    public class NavTestMapLoaderTests
    {
        private static string DataPath(string filename)
            => Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                            "Navigation", "data", "navmaps", filename);

        [Fact]
        public void Load_Corridor_HasOneInfantryLayer()
        {
            var map = NavTestMapLoader.FromFile(DataPath("corridor.json"));
            Assert.Single(map.Layers);
            Assert.Equal((uint)NavLayerMask.Infantry, map.Layers[0].Layer);
        }

        [Fact]
        public void Load_Corridor_HasThreePolygons()
        {
            var map = NavTestMapLoader.FromFile(DataPath("corridor.json"));
            Assert.Equal(3, map.Layers[0].Polygons.Length);
        }

        [Fact]
        public void Load_Corridor_AdjacencyCorrect()
        {
            var map = NavTestMapLoader.FromFile(DataPath("corridor.json"));
            var adj = map.Layers[0].Adjacency;
            Assert.Equal(3, adj.Length);
            Assert.Contains(1, adj[0]);  // poly 0 adjacent to poly 1
            Assert.Contains(0, adj[1]);  // poly 1 adjacent to poly 0
            Assert.Contains(2, adj[1]);  // poly 1 adjacent to poly 2
        }

        [Fact]
        public void Load_TwoLayers_HasTwoLayers()
        {
            var map = NavTestMapLoader.FromFile(DataPath("two_layers.json"));
            Assert.Equal(2, map.Layers.Length);
        }

        [Fact]
        public void Load_OffMeshJump_HasOffMeshLink()
        {
            var map = NavTestMapLoader.FromFile(DataPath("off_mesh_jump.json"));
            var layer = map.Layers[0];
            Assert.Single(layer.OffMeshLinks);
            Assert.Equal(TraversalKind.Jump, layer.OffMeshLinks[0].Kind);
            Assert.Equal(5f, layer.OffMeshLinks[0].Cost);
        }

        [Fact]
        public void Load_Replan_MiddlePolygonBlocked()
        {
            var map = NavTestMapLoader.FromFile(DataPath("replan.json"));
            Assert.True(map.Layers[0].Polygons[1].IsBlocked);
        }

        [Fact]
        public void Load_Flying_HasNoFlyZone()
        {
            var map = NavTestMapLoader.FromFile(DataPath("flying.json"));
            Assert.Single(map.NoFlyZones);
            Assert.Equal(new Vector3(10f, 0f, 0f), map.NoFlyZones[0].Bounds.Min);
            Assert.Equal(new Vector3(20f, 5f, 100f), map.NoFlyZones[0].Bounds.Max);
        }

        [Fact]
        public void Load_Flying_AltitudeBounds()
        {
            var map = NavTestMapLoader.FromFile(DataPath("flying.json"));
            Assert.Equal(0f,   map.MinAltitude);
            Assert.Equal(200f, map.MaxAltitude);
        }

        [Fact]
        public void Load_Naval_HasNavalLayer()
        {
            var map = NavTestMapLoader.FromFile(DataPath("naval.json"));
            Assert.Equal((uint)NavLayerMask.Naval, map.Layers[0].Layer);
        }

        [Fact]
        public void Load_Naval_PolygonsSurfaceTypeIsWater()
        {
            var map = NavTestMapLoader.FromFile(DataPath("naval.json"));
            foreach (var poly in map.Layers[0].Polygons)
                Assert.Equal(SurfaceType.Water, poly.SurfaceType);
        }

        [Fact]
        public void FromJson_EmptyLayers_ReturnsEmptyMap()
        {
            const string json = @"{ ""layers"": [] }";
            var map = NavTestMapLoader.FromJson(json);
            Assert.Empty(map.Layers);
            Assert.Empty(map.NoFlyZones);
            Assert.Equal(0f,    map.MinAltitude);
            Assert.Equal(5000f, map.MaxAltitude);
        }
    }
}
