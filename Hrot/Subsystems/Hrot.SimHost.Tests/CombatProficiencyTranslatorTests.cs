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
    /// ⭐ <c>CE-2273</c> DA-A — the scenario's <c>"Proficiency"</c> key: a number or a tier name, AUTHORED (so 0 counts); it keeps a type
    /// default already projected; a save writes back only the authored value — never the unit's or the type's, which are derived.
    /// </summary>
    public sealed class CombatProficiencyTranslatorTests : IDisposable
    {
        private readonly EntityRepository _repo = new();
        private readonly CombatProficiencyTranslator _t = new();
        public CombatProficiencyTranslatorTests() => _repo.RegisterComponent<CombatProficiency>();
        public void Dispose() => _repo.Dispose();

        private sealed class NoResolver : IGuidResolver
        {
            public string Resolve(Entity e) => e.ToString();
            public Entity Resolve(string guidStr) => Entity.Null;
        }

        [Fact]
        public void CE2273_ANumberOrATierName_IsAuthored_AndOnlyTheAuthoredValueIsSaved()
        {
            var a = _repo.CreateEntity();
            var b = _repo.CreateEntity();
            _t.Inject(_repo, a, new Dictionary<string, object> { ["Proficiency"] = JsonValue.Create(0f)! }, new NoResolver());
            _t.Inject(_repo, b, new Dictionary<string, object> { ["Proficiency"] = JsonValue.Create("veteran")! }, new NoResolver());
            Assert.Equal(0f, ProficiencyOf.Value(_repo, a));                                   // untrained beyond the scale, on purpose
            Assert.Equal(ProficiencyTier.Veteran, ProficiencyOf.Tier(_repo, b));

            var c = _repo.CreateEntity();
            _repo.AddComponent(c, new CombatProficiency { TypeDefault = ProficiencyOf.Militia, Set = CombatProficiency.TypeDefaultSet });
            Assert.False(_t.CanTranslate(_repo, c));                                            // derived values are not saved
            _t.Inject(_repo, c, new Dictionary<string, object> { ["Proficiency"] = JsonValue.Create(0.9f)! }, new NoResolver());
            Assert.Equal(ProficiencyOf.Militia, _repo.GetComponentRO<CombatProficiency>(c).TypeDefault);   // kept
            Assert.True(_t.CanTranslate(_repo, c));
            Assert.Equal(0.9f, ((JsonValue)_t.Extract(_repo, c, new NoResolver())["Proficiency"]).GetValue<float>());
        }

        [Fact]
        public void CE2273_NoKey_OrAnUnknownTier_AddsNothing()
        {
            var a = _repo.CreateEntity();
            _t.Inject(_repo, a, new Dictionary<string, object>(), new NoResolver());
            _t.Inject(_repo, a, new Dictionary<string, object> { ["Proficiency"] = JsonValue.Create("Legendary")! }, new NoResolver());
            Assert.False(_repo.HasComponent<CombatProficiency>(a));
            Assert.Equal(ProficiencyOf.Regular, ProficiencyOf.Value(_repo, a));
        }
    }
}
