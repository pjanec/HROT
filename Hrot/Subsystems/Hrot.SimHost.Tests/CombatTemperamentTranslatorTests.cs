using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Scenario;
using Hrot.Common.Serializers;
using Hrot.SimHost.Serializers;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐ AQ89 B (R-269) — the scenario's <c>"Temperament"</c> key: an authored field is SET (so it counts even at 0), an omitted one
    /// keeps reading the default, and a save writes back only what was authored — never today's default frozen into the file.
    /// </summary>
    public sealed class CombatTemperamentTranslatorTests : IDisposable
    {
        private readonly EntityRepository _repo = new();
        private readonly CombatTemperamentTranslator _t = new();
        public CombatTemperamentTranslatorTests() => _repo.RegisterComponent<CombatTemperament>();
        public void Dispose() => _repo.Dispose();

        private sealed class NoResolver : IGuidResolver
        {
            public string Resolve(Entity e) => e.ToString();
            public Entity Resolve(string guidStr) => Entity.Null;
        }

        [Fact]
        public void OneAuthoredField_IsSet_TheOtherReadsItsDefault_AndOnlyTheAuthoredOneIsSaved()
        {
            var e = _repo.CreateEntity();
            _t.Inject(_repo, e, new Dictionary<string, object> { ["Temperament"] = new JsonObject { ["riskAcceptance"] = 0f } }, new NoResolver());
            Assert.Equal(0f, TemperamentOf.RiskAcceptance(_repo, e));                          // a coward, on purpose
            Assert.Equal(TemperamentOf.DefaultObedience, TemperamentOf.Obedience(_repo, e));   // untouched ⇒ default

            Assert.True(_t.CanTranslate(_repo, e));
            var saved = (JsonObject)_t.Extract(_repo, e, new NoResolver())["Temperament"];
            Assert.Equal(0f, saved["riskAcceptance"]!.GetValue<float>());
            Assert.False(saved.ContainsKey("obedience"));
        }

        [Fact]
        public void NoKey_OrAnEmptyObject_AddsNothing_SoTheDefaultsApply()
        {
            var a = _repo.CreateEntity();
            var b = _repo.CreateEntity();
            _t.Inject(_repo, a, new Dictionary<string, object>(), new NoResolver());
            _t.Inject(_repo, b, new Dictionary<string, object> { ["Temperament"] = new JsonObject() }, new NoResolver());
            Assert.False(_repo.HasComponent<CombatTemperament>(a));
            Assert.False(_repo.HasComponent<CombatTemperament>(b));
            Assert.Equal(TemperamentOf.DefaultRiskAcceptance, TemperamentOf.RiskAcceptance(_repo, a));
        }
    }
}
