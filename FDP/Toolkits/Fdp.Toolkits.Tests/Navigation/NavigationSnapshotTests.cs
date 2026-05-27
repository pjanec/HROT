using System.Text.Json;
using Fdp.Core;
using Fdp.Toolkit.Navigation.Fake;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests
{
    /// <summary>
    /// Unit tests for <see cref="NavigationSnapshotBuilder"/>.
    /// NAV-P7-T2: verifies the JSON snapshot schema for both fake and no-provider modes.
    /// </summary>
    public class NavigationSnapshotTests
    {
        [Fact]
        public void Build_FakeMode_ProducesValidJson()
        {
            var world  = NavigationTestWorldFactory.Create();
            var module = new NavigationFakesModule(NavTestMaps.LoadCorridor());
            module.RegisterProviders(world);

            string json = NavigationSnapshotBuilder.Build(world);

            // Must be non-empty parseable JSON.
            Assert.False(string.IsNullOrWhiteSpace(json));
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Required top-level keys.
            Assert.True(root.TryGetProperty("captured_at_tick", out _));
            Assert.True(root.TryGetProperty("loaded_map",       out _));
            Assert.True(root.TryGetProperty("navmesh",          out _));
            Assert.True(root.TryGetProperty("crowd",            out _));
            Assert.True(root.TryGetProperty("volumetric",       out _));
            Assert.True(root.TryGetProperty("path_registry",    out _));
        }

        [Fact]
        public void Build_FakeMode_LoadedMapIsFakeNavmesh()
        {
            var world  = NavigationTestWorldFactory.Create();
            var module = new NavigationFakesModule(NavTestMaps.LoadCorridor());
            module.RegisterProviders(world);

            string json = NavigationSnapshotBuilder.Build(world);
            using var doc = JsonDocument.Parse(json);

            Assert.Equal("fake-navmesh", doc.RootElement.GetProperty("loaded_map").GetString());
        }

        [Fact]
        public void Build_FakeMode_NavmeshTypePresentCrowdNull()
        {
            var world  = NavigationTestWorldFactory.Create();
            var module = new NavigationFakesModule();
            module.RegisterProviders(world);

            string json = NavigationSnapshotBuilder.Build(world);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Navmesh section must have a type field.
            Assert.Equal(JsonValueKind.Object, root.GetProperty("navmesh").ValueKind);
            Assert.Equal("FakeNavmeshProvider", root.GetProperty("navmesh").GetProperty("type").GetString());

            // Crowd and volumetric are always null (not registered as ECS singletons).
            Assert.Equal(JsonValueKind.Null, root.GetProperty("crowd").ValueKind);
            Assert.Equal(JsonValueKind.Null, root.GetProperty("volumetric").ValueKind);
        }

        [Fact]
        public void Build_NoProviders_LoadedMapIsNone()
        {
            var world = NavigationTestWorldFactory.Create();
            // No module registered -- no navmesh singleton in repo.

            string json = NavigationSnapshotBuilder.Build(world);
            using var doc = JsonDocument.Parse(json);

            Assert.Equal("none", doc.RootElement.GetProperty("loaded_map").GetString());
        }
    }
}
