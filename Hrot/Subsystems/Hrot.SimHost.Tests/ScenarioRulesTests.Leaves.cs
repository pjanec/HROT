using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Scheduling;
using Fdp.Toolkit.ScenarioRules;
using Hrot.AI.Behaviors.Brains.Objectives;
using Hrot.Common.Infrastructure;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>⭐ rules V-3 (📄 docs/DESIGN_Scenario_Rules.md §00.10) — a trigger's when-leaves with their live values: the plan (flattening, labels, the measured number) and the sampler (feature gate, rate, owner only).</summary>
    public sealed partial class ScenarioRulesTests
    {
        private static string PropHealth(int min, int max)
            => "{\"$type\":\"PropertyMatch\",\"componentType\":\"Health\",\"propertyPath\":\"Current\",\"operator\":\"Equals\",\"predicate\":{\"$type\":\"Numeric\",\"minValue\":" + min + ",\"maxValue\":" + max + "}}";

        /// <summary>⭐ <c>Compound(Or)[OnEntity(@target, Health.Current in 30..40), ScenarioTime] AND CountMatching(Health.Current in 0..100)</c> ⇒ THREE leaves (a Compound is descended, an OnEntity is ONE leaf),
        /// labelled; Measured = the property's value (read on the target), the sim time, the count; the plan is cached by the when JSON. ✅ Red-proof: <c>Measured</c> forced to the 0/1 in <c>RuleLeafPlan.Sample</c> ⇒ red.</summary>
        [Fact]
        public void TheLeafPlan_FlattensANestedWhen_IntoLabelledLeaves_AndMeasuresWhatEachReads()
        {
            var w = new ObjWorld();
            var target = w.Man(Outside, ForceId.Friend, 37f); w.Man(Outside, ForceId.Friend, 50f);
            string when = "{\"$type\":\"Compound\",\"operator\":\"And\",\"conditions\":[" +
                "{\"$type\":\"Compound\",\"operator\":\"Or\",\"conditions\":[{\"$type\":\"OnEntity\",\"ref\":" + w.Net(target) + ",\"predicate\":" + PropHealth(30, 40) + "},{\"$type\":\"ScenarioTime\",\"fromSeconds\":0}]}," +
                "{\"$type\":\"CountMatching\",\"min\":1,\"withComponent\":\"Health\",\"predicate\":" + PropHealth(0, 100) + "}]}";
            w.Run(2);
            var plan = TriggerLeaves.Get(when);
            Assert.NotNull(plan);
            Assert.Same(plan, TriggerLeaves.Get(string.Copy(when)));          // cached by the JSON content
            Assert.Equal(3, plan!.Count);
            Assert.Equal($"#{w.Net(target)} Health.Current", plan.LabelOf(0));
            Assert.Equal("ScenarioTime", plan.LabelOf(1));
            Assert.Equal("Count Health.Current", plan.LabelOf(2));

            var any = w.Man(Outside, ForceId.Friend);                          // the entity being asked about: only the CountMatching / ScenarioTime ignore it
            var l = default(RuleLeaves);
            plan.Sample(w.Repo, any, ref l);
            Assert.Equal(3, l.Count);
            Assert.Equal((1, 37f), (l.Leaves[0].Value, l.Leaves[0].Measured));      // the property, read on the TARGET
            Assert.Equal((1, 2f), (l.Leaves[1].Value, l.Leaves[1].Measured));       // the sim time
            Assert.Equal((1, 2f), (l.Leaves[2].Value, l.Leaves[2].Measured));       // the two men with Health (the third has none)

            w.Repo.GetComponentRW<Health>(target).Current = 5f;                // live: the next sample sees it
            plan.Sample(w.Repo, any, ref l);
            Assert.Equal((0, 5f), (l.Leaves[0].Value, l.Leaves[0].Measured));
            Assert.Equal("ScenarioTime", l.Leaves[1].Label.ToString());
        }

        /// <summary>More than eight leaves ⇒ the first eight and <c>Truncated</c>.</summary>
        [Fact]
        public void TheLeafPlan_HoldsAtMostEightLeaves()
        {
            string leaf = "{\"$type\":\"ScenarioTime\",\"fromSeconds\":0}";
            string when = "{\"$type\":\"Compound\",\"operator\":\"And\",\"conditions\":[" + string.Join(",", Enumerable.Repeat(leaf, 10)) + "]}";
            var plan = TriggerLeaves.Get(when)!;
            Assert.Equal((8, true), (plan.Count, plan.Truncated));
        }

        /// <summary>⭐ The sampler: [NodeFeature("ui.rules-live-values")], OFF at load ⇒ it writes NOTHING (no <c>RuleLeaves</c> appears); switched on it samples twice a second of SIM time (not every frame) for the
        /// triggers THIS node owns (a status with no <c>RuleControl</c> / authority — a replica's — gets nothing); switched off again it stops writing and the last sample STAYS. ✅ Red-proof: <c>[NodeFeature]</c>
        /// removed ⇒ the "off" rail writes ⇒ red; the period check removed ⇒ the rate rail (Passes ≤ 7 in 3 s of 0.1 s frames) ⇒ red.</summary>
        [Fact]
        public void TheSampler_IsANodeFeature_OffAtLoad_TwiceASecondWhenOn_OwnerOnly_AndTheLastSampleStaysWhenOff()
        {
            var features = new NodeFeatures(new FeatureRegistry(new[] { RuleLeafSampler.Feature }), NodeRole.Brain);
            Assert.False(features.On(RuleLeafSampler.FeatureId));              // AtLoad off
            Assert.Equal(FeatureChange.Live, RuleLeafSampler.Feature.Change);

            var w = new ObjWorld();
            var src = Source(w);
            var t = w.Trigger(TrigJson(w.Net(src)));
            var replica = w.Repo.CreateEntity();                               // a status applied by ingress: no RuleControl, no authority
            w.Repo.AddComponent(replica, new NetworkIdentity { Value = 777 });
            w.Repo.AddComponent(replica, new ObjectiveStatus { Role = ObjectiveRole.Trigger });
            w.Repo.AddComponent(replica, new Fdp.Toolkit.ScenarioRules.Actions.ActionArgs(new[] { WhenAchieved(w.Net(src)), "[]" }));

            var sys = new RuleLeafSampler();
            var sch = new SystemScheduler { FeatureGate = features };
            sch.RegisterSystem(sys); sch.BuildExecutionOrders();
            void Frames(int n) { for (int i = 0; i < n; i++) { w.Tick(0.1); sch.ExecutePhase(SystemPhase.Simulation, w.Repo, 0.1f); } }

            Frames(20);                                                         // 2 s, feature OFF
            Assert.Equal(0, sys.Passes);
            Assert.False(w.Repo.HasComponent<RuleLeaves>(t));

            features.SetNodeOverride(RuleLeafSampler.FeatureId, "{\"Enabled\":true}");
            Frames(30);                                                         // 3 s ON: two a second, not thirty
            Assert.InRange(sys.Passes, 5, 7);
            Assert.True(w.Repo.HasComponent<RuleLeaves>(t));
            var first = w.Repo.GetComponentRO<RuleLeaves>(t);
            Assert.Equal(1, first.Count);
            Assert.Equal(0, first.Leaves[0].Value);                            // the source is Active, not Achieved
            Assert.False(w.Repo.HasComponent<RuleLeaves>(replica));            // not owned here ⇒ not sampled

            Flip(w, src, true);
            Frames(6);                                                         // 0.6 s: at least one more pass
            Assert.Equal(1, w.Repo.GetComponentRO<RuleLeaves>(t).Leaves[0].Value);

            features.SetNodeOverride(RuleLeafSampler.FeatureId, "{\"Enabled\":false}");
            int passes = sys.Passes;
            Flip(w, src, false);
            Frames(15);
            Assert.Equal(passes, sys.Passes);                                  // off ⇒ nothing sampled
            Assert.Equal(1, w.Repo.GetComponentRO<RuleLeaves>(t).Leaves[0].Value);   // the last sample stays (a viewer shows it greyed)
        }

        /// <summary>The pack carries the sampler once (its feature is declared by both Brain capabilities, F-3 — the scheduler refuses an undeclared one). ✅ Red-proof: the registration removed from <c>CgfLogicPack</c> ⇒ red.</summary>
        [Fact]
        public void ThePack_CarriesTheSampler()
        {
            var pack = new Hrot.CGF.CgfLogicPack(new Fdp.Toolkit.Behavior.BehaviorRegistry(), new Fdp.Toolkit.Replication.Services.NetworkEntityMap(),
                new Fdp.Toolkit.Behavior.TacticalOrderMapper.TacticalIntentMapperRegistry(), new Fdp.Toolkit.Blueprints.BlueprintRegistry());
            Assert.Single(pack.SimulationSystems.OfType<RuleLeafSampler>());
        }
    }
}
