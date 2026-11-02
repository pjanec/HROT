using System;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Weather;
using Xunit;

namespace Fdp.Toolkit.Scenario.Tests
{
    /// <summary>
    /// ⭐ <c>CE-4154</c> / SR-71 — the World Environment is saved in <c>Header.World</c> (one entry per component, the entity skipped), the clock in ONE ISO-8601 string with its
    /// offset; a file without the section keeps the C-3 default; the merge takes the section from the one slice that has it.
    /// 🔴 Mutations (each compiles): the save leaving the entity in <c>Entities</c> ⇒ the "no environment entity" cell reds; the offset dropped from the string ⇒ the round-trip cells red;
    /// an offset-less string accepted ⇒ the refusal cells red; the merge dropping the section ⇒ the merge cell reds.
    /// </summary>
    public sealed class ScenarioWorldSectionTests : IDisposable
    {
        private readonly EntityRepository _repo;

        public ScenarioWorldSectionTests()
        {
            ComponentTypeRegistry.Clear();
            _repo = new EntityRepository();
        }

        public void Dispose() => _repo.Dispose();

        [Theory]
        [InlineData(638860968000000000L, (short)120)]       // 2025-06-21 10:00 UTC, +02:00
        [InlineData(638860968000000000L, (short)-330)]      // a negative, non-whole-hour offset
        [InlineData(638860968123456700L, (short)0)]         // sub-second ticks survive
        public void TheClock_RoundTripsTicksToStringToTicks_Exactly_WithItsOffset(long ticks, short offset)
        {
            string text = ScenarioWorldSection.ClockToString(ticks, offset);
            var (back, backOffset) = ScenarioWorldSection.ClockFromString(text);
            Assert.Equal((ticks, offset), (back, backOffset));
        }

        [Fact]
        public void TheClockString_IsTheHumanReadableLocalTimeWithItsOffset()
        {
            Assert.Equal("2025-06-21T12:00:00+02:00", ScenarioWorldSection.ClockToString(638860968000000000L, 120));
            var (ticks, offset) = ScenarioWorldSection.ClockFromString("2025-06-21T12:00:00+02:00");
            Assert.Equal((638860968000000000L, (short)120), (ticks, offset));
            Assert.Equal((638860968000000000L + 0, (short)0), ScenarioWorldSection.ClockFromString("2025-06-21T10:00:00Z"));   // Z = offset 0
        }

        [Theory]
        [InlineData("2025-06-21T08:00:00")]          // no offset: ambiguous
        [InlineData("2025-06-21 08:00:00+02:00")]    // not ISO-8601
        [InlineData("noon")]
        [InlineData("")]
        public void AClockWithoutAnOffset_OrNotIso_IsRefused(string text)
            => Assert.Throws<FormatException>(() => ScenarioWorldSection.ClockFromString(text));

        [Fact]
        public void Save_WritesHeaderWorld_AndNoEnvironmentEntity()
        {
            _repo.RegisterComponent<ScenarioClock>();
            _repo.RegisterComponent<WeatherConditions>();
            var env = _repo.CreateEntity();
            _repo.AddComponent(env, new ScenarioClock { StartUtcTicks = 638860968000000000L, DisplayUtcOffsetMinutes = 120 });
            _repo.AddComponent(env, WeatherConditions.Standard);

            var dom = new ScenarioSerializerBuilder("Hrot.SimHost").Build().Serialize(_repo, new ScenarioHeader("Hrot.SimHost"));

            var world = ScenarioWorldSection.Of(dom)!;
            Assert.Equal("2025-06-21T12:00:00+02:00", world["ScenarioClock"]!["Start"]!.GetValue<string>());
            Assert.NotNull(world["WeatherConditions"]);
            Assert.Empty(dom["Entities"]!.AsObject());                       // the entity is not saved as an entity
        }

        [Fact]
        public void Load_TurnsHeaderWorldIntoTheEnvironmentEntity_AndAFileWithoutItIsUntouched()
        {
            string without = "{\"Entities\":{}}";
            Assert.Equal(without, ScenarioWorldSection.InjectEntity(without, 8810));          // none ⇒ the C-3 default is the load's own

            string with = "{\"header\":{\"world\":{\"ScenarioClock\":{\"Start\":\"2025-06-21T12:00:00+02:00\"},\"WeatherConditions\":{\"VisibilityM\":500}}},\"entities\":{}}";
            var entity = ((JsonObject)JsonNode.Parse(ScenarioWorldSection.InjectEntity(with, 8810))!)["entities"]![ScenarioWorldSection.EntityKey]!;
            Assert.Equal(8810, entity["TkbIdentity"]!["TkbType"]!.GetValue<int>());
            Assert.Equal(638860968000000000L, entity["ScenarioClock"]!["StartUtcTicks"]!.GetValue<long>());
            Assert.Equal(120, entity["ScenarioClock"]!["DisplayUtcOffsetMinutes"]!.GetValue<int>());
            Assert.Equal(500, entity["WeatherConditions"]!["VisibilityM"]!.GetValue<int>());

            Assert.Throws<FormatException>(() => ScenarioWorldSection.InjectEntity(
                "{\"header\":{\"world\":{\"ScenarioClock\":{\"Start\":\"2025-06-21T12:00:00\"}}},\"entities\":{}}", 8810));   // offset-less: the load fails loudly
        }

        [Theory]
        [InlineData("ua-night-sensing")]
        [InlineData("ua-weather-regions")]
        [InlineData("ua-weather-sensing")]
        public void TheShippedWeatherScenarios_HoldTheirEnvironmentInTheHeader_NotAsAnEntity(string name)
        {
            var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "scenarios"))) dir = dir.Parent;
            Assert.NotNull(dir);
            var dom = (JsonObject)JsonNode.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(dir!.FullName, "scenarios", name, "scenario.json")))!;
            var world = ScenarioWorldSection.Of(dom);
            Assert.NotNull(world);
            ScenarioWorldSection.ClockFromString(world!["ScenarioClock"]!["Start"]!.GetValue<string>());   // parses, with its offset
            Assert.DoesNotContain(((JsonObject)dom["entities"]!).AsEnumerable(), kv => kv.Value is JsonObject o && o.ContainsKey("ScenarioClock"));
        }

        [Fact]
        public void TheMerge_TakesHeaderWorld_FromTheOneSliceThatHasIt_AndRefusesTwo()
        {
            JsonObject Slice(bool withWorld)
            {
                var dom = new JsonObject { ["Entities"] = new JsonObject() };
                if (withWorld) dom["Header"] = new JsonObject { ["World"] = new JsonObject { ["ScenarioClock"] = new JsonObject { ["Start"] = "2025-06-21T12:00:00+02:00" } } };
                Fdp.Core.Serialization.Migrations.JsonEnvelope.Write(dom, new Fdp.Core.Serialization.Migrations.DocumentMeta("Hrot.Scenario", 2));
                return dom;
            }
            ScenarioSlice S(int n, JsonObject d) => new((NodeId)(n), "Hrot.Scenario", d);

            var merged = ScenarioMergeCore.Merge(new[] { S(1, Slice(false)), S(400, Slice(true)) }, "Hrot.Scenario");
            Assert.Equal("2025-06-21T12:00:00+02:00", ScenarioWorldSection.Of(merged.CanonicalDom)!["ScenarioClock"]!["Start"]!.GetValue<string>());
            Assert.Throws<InvalidOperationException>(() => ScenarioMergeCore.Merge(new[] { S(1, Slice(true)), S(400, Slice(true)) }, "Hrot.Scenario"));
        }
    }
}
