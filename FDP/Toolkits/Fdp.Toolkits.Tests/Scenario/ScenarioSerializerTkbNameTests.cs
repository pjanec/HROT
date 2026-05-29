using System;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Scenario;
using Xunit;

namespace Fdp.Toolkit.Scenario.Tests
{
    /// <summary>
    /// TKB-021 -- Tests for TkbName serialization in ScenarioSerializer.
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
        public void Serialize_WithTkbName_IncludesTkbNameInHeader()
        {
            var serializer = BuildSerializer();
            var header     = new ScenarioHeader("Hrot.SimHost", TkbName: "Alpha_v1");

            var dom = serializer.Serialize(_repo, header);

            Assert.Equal("Alpha_v1", dom["Header"]!["TkbName"]!.GetValue<string>());
        }

        // ── Test 2 ────────────────────────────────────────────────────────────

        [Fact]
        public void Serialize_WithoutTkbName_OmitsTkbNameFromHeader()
        {
            var serializer = BuildSerializer();
            var header     = new ScenarioHeader("Hrot.SimHost");

            var dom = serializer.Serialize(_repo, header);

            // Phase 2: when TkbName is null the entire Header node is omitted.
            Assert.Null(dom["Header"]);
        }
    }
}
