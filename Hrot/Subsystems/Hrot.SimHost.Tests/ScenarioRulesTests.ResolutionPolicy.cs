using System;
using Fdp.Core;
using Fdp.Toolkit.Aggregate;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.ScenarioRules;
using Hrot.AI.Behaviors.Brains.Objectives;
using Xunit;

namespace Hrot.SimHost.Tests
{
    public sealed partial class ScenarioRulesTests
    {
        /// <summary>⭐⭐ <c>CE-8300</c> (agg-behaviors ST-5, §16b, R-8326) — <c>SetResolutionPolicy</c> sets the bands / timings / a detail area on the World
        /// Environment's <see cref="ResolutionPolicy"/>; a PARTIAL setting keeps the rest; two triggers firing in ONE tick both land (the write is serial, on the
        /// LIVE component — the <c>SetSkillAmplifier</c> precedent, CE-3448); a negative number is refused at assignment; the manager's <see cref="ResolutionPolicies.BandsOf"/>
        /// reads it; ⭐ CE-8304 — a detail area given as a bare id is kept as the target's GUID. 🔴 Red-proofs: <c>Authored</c> returning the link unchanged ⇒ no spec ⇒ red · the verb writing a copy read BEFORE the after-phase hook (not the live component) ⇒ the second trigger's write overwrites the first's ⇒ red.</summary>
        [Fact]
        public void SetResolutionPolicy_SetsThePolicy_Partially_AndTwoSettingsInOneTickBothLand()
        {
            var w = new ObjWorld();
            if (!w.Repo.IsComponentTypeRegistered<ResolutionPolicy>()) w.Repo.RegisterComponent<ResolutionPolicy>();
            var env = w.Environment();
            var src = Cond(w);
            var zone = w.Repo.CreateEntity();
            w.Repo.AddComponent(zone, new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = 777 });
            if (!w.Repo.IsComponentTypeRegistered<Fdp.Toolkit.Scenario.EntityGuid>()) w.Repo.RegisterComponent<Fdp.Toolkit.Scenario.EntityGuid>();
            var zoneGuid = new Guid("5a1e0000-0000-4000-8000-000000000777");
            w.Repo.AddComponent(zone, new Fdp.Toolkit.Scenario.EntityGuid(zoneGuid));
            var a = Fire(w, src, Do(Step("SetResolutionPolicy", "{\"physicalBand\":650,\"quietSeconds\":20}")));
            var b = Fire(w, src, Do(Step("SetResolutionPolicy", "{\"leadMetres\":250,\"area\":777}")));
            Flip(w, src, true); w.Run(1);
            Assert.NotEqual(TriggerState.Acting, Phase(w, a));
            Assert.NotEqual(TriggerState.Acting, Phase(w, b));
            var p = w.Repo.GetComponentRO<ResolutionPolicy>(env);
            var d = ResolutionBands.Default;
            Assert.Equal(new ResolutionBands(650f, 250f, d.MergeMargin, 20f, d.ForcedRange, d.DeadlineSeconds), ResolutionPolicies.BandsOf(w.Repo));   // both landed, the rest default
            Assert.Equal((1, new EntityRef(777)), (p.AreaCount, p.AreaAt(0)));
            Assert.Equal(RefSpec.ByExact(zoneGuid), p.LinkAt(0).Spec);   // ⭐ CE-8304 (R-8380) — a bare id is kept as the target's GUID: the saved file names no network id
            Assert.Equal(20f, (float)ResolutionPolicies.BandsOf(w.Repo).QuietSeconds);   // S0 — the verb's JSON number lands as a SimDuration

            // the partial merge itself: a setting keeps what it does not name; clearAreas forgets the areas first
            var q = p;
            Hrot.AI.Behaviors.Brains.Objectives.Verbs.ResolutionVerbs.Apply(ref q, new Hrot.AI.Behaviors.Brains.Objectives.Verbs.SetResolutionPolicyParams { MergeMargin = 40f, ClearAreas = true });
            Assert.Equal((650f, 250f, 40f, 0), (q.Bands().PhysicalBand, q.Bands().LeadMetres, q.Bands().MergeMargin, q.AreaCount));
            Assert.Throws<FormatException>(() => Fdp.Toolkit.ScenarioRules.Actions.ActionCatalogue.RunnerFor("SetResolutionPolicy").CheckArgs("{\"physicalBand\":-5}"));
        }
    }
}
