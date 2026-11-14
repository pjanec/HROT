using System;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Scenario;
using Xunit;

namespace Fdp.Toolkit.Scenario.Tests
{
    /// <summary>
    /// TKB-021 -- ⛔⛔ <c>R-4018</c>: a scenario never names a TKB. These rails pin that the format cannot carry one.
    /// (Was: <c>TkbName</c> is written into the header.)
    /// </summary>
    public sealed class ScenarioSerializerTkbNameTests : IDisposable
    {
        private readonly EntityRepository _repo;

        public ScenarioSerializerTkbNameTests()
        {
            ComponentTypeRegistry.Clear();
            _repo = new EntityRepository();
        }

        public void Dispose() => _repo.Dispose();

        private static ScenarioSerializer BuildSerializer()
        {
            return new ScenarioSerializerBuilder("Hrot.SimHost").Build();
        }

        // ── Test 1 ────────────────────────────────────────────────────────────

        [Fact]
        public void TheScenarioHeader_HasNoTkbName()
        {
            Assert.Null(typeof(ScenarioHeader).GetProperty("TkbName"));
        }

        [Fact]
        public void Serialize_WritesTheTerrainName_AndNeverATkbName()
        {
            var serializer = BuildSerializer();
            var dom = serializer.Serialize(_repo, new ScenarioHeader("Hrot.SimHost", TerrainName: "basic-desert"));

            var header = dom["Header"]!.AsObject();
            Assert.Equal("basic-desert", header["TerrainName"]!.GetValue<string>());
            Assert.False(header.ContainsKey("TkbName"));
        }

        // ── Test 2 ────────────────────────────────────────────────────────────

        [Fact]
        public void Serialize_WithoutTkbName_OmitsTkbNameFromHeader()
        {
            var serializer = BuildSerializer();
            var header     = new ScenarioHeader("Hrot.SimHost");

            var dom = serializer.Serialize(_repo, header);

            // Phase 2: with no terrain name the entire Header node is omitted.
            Assert.Null(dom["Header"]);
        }
    }
}
