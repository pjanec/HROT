using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.ReplayBrowser.Search;
using Fdp.Toolkit.ScenarioRules;
using Fdp.Toolkit.ScenarioRules.Actions;
using Hrot.Common.Infrastructure;

namespace Hrot.AI.Behaviors.Brains.Objectives
{
    /// <summary>
    /// ⭐ rules V-3 — the <see cref="RuleLeafPlan"/> of a trigger's <c>when</c>, cached PROCESS-WIDE by its JSON string exactly like its compiled condition (<see cref="TriggerWhen"/>; it shares
    /// that class's compiler), so two triggers authored with the same condition share one plan. A <c>when</c> that does not compile caches <c>null</c> (the trigger itself was refused at assignment).
    /// </summary>
    public static class TriggerLeaves
    {
        private static readonly ConcurrentDictionary<string, RuleLeafPlan?> ByContent = new();

        /// <summary>The plan of <paramref name="json"/> (built once); null when it is not a predicate or does not compile.</summary>
        public static RuleLeafPlan? Get(string json) => ByContent.GetOrAdd(json, Build);

        private static RuleLeafPlan? Build(string json)
        {
            try
            {
                var dto = JsonSerializer.Deserialize<SearchPredicateDto>(json, BehaviorParams.JsonOptions);
                return dto is null ? null : RuleLeafPlan.Of(dto, TriggerWhen.Compiler);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException or InvalidOperationException) { return null; }
        }
    }

    /// <summary>
    /// ⭐ rules V-3 (📄 docs/DESIGN_Scenario_Rules.md §00.10) — samples the leaves of every trigger this node OWNS into <see cref="RuleLeaves"/> (label, 0/1, measured number), at most
    /// <see cref="PeriodSeconds"/> apart in SIM time (two a second), for the rules view's live values. <b>Only while the node feature <c>ui.rules-live-values</c> is on</b>
    /// (<c>[NodeFeature]</c> — the scheduler skips the system while it is off, so a node nobody is watching spends nothing; <c>AtLoad</c> off, switchable <c>Live</c>). When the
    /// feature goes off the owner simply stops writing: the last sample stays on the entity (a viewer shows it greyed). Registered beside <see cref="ExercisePauseOnEndSystem"/> by
    /// <c>CgfLogicPack</c> ⇒ every Brain node and the editor have it; a replica holds no authority and writes nothing (it receives the owner's <c>dtRuleLeaves</c>).
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.EveryFrame)]
    [NodeFeature(FeatureId)]
    public sealed class RuleLeafSampler : IEcsModuleSystem
    {
        public const string FeatureId = "ui.rules-live-values";

        /// <summary>The sampling period (sim s): two a second.</summary>
        public const double PeriodSeconds = 0.5;

        /// <summary>The declaration both Brain capabilities (CGF and the editor) put in their <c>Features</c> — declared ⇔ available (F-3), once per node. Off at load; switched on demand.</summary>
        public static readonly FeatureDeclaration Feature = FeatureDeclaration.Switch(FeatureId,
            "Each trigger's condition leaves with their live values are sampled (twice a second) for the rules view", FeatureChange.Live, _ => false);

        private double _last = double.NegativeInfinity;
        private readonly List<Entity> _triggers = new();

        /// <summary>How many sampling passes have run (the rate rail reads it).</summary>
        public int Passes { get; private set; }

        /// <summary>⭐ The ONE owner test of a rule entity (V-4): <c>ObjectiveHost.Enter</c> gives <see cref="RuleControl"/> and its authority bit to the node whose Brain ticks the entity — a replica has neither.</summary>
        private static bool Owns(EntityRepository repo, Entity e) => repo.HasComponent<RuleControl>(e) && repo.HasAuthority<RuleControl>(e);

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo) return;
            if (!repo.IsComponentTypeRegistered<ObjectiveStatus>() || !repo.IsComponentTypeRegistered<RuleLeaves>()
                || !repo.IsComponentTypeRegistered<RuleControl>()) return;
            double now = ObjectiveHost.Now(repo);
            if (now >= _last && now - _last < PeriodSeconds) return;   // a clock that went back (a seek, a reset) reopens the window
            _last = now;
            Passes++;

            _triggers.Clear();
            foreach (var e in repo.Query().With<ObjectiveStatus>().Build())
                if (repo.GetComponentRO<ObjectiveStatus>(e).Role == ObjectiveRole.Trigger && Owns(repo, e)) _triggers.Add(e);   // collected first: adding RuleLeaves is structural
            foreach (var e in _triggers)
            {
                if (!repo.HasManagedComponent<ActionArgs>(e)) continue;
                var items = ((ISimulationView)repo).GetManagedComponentRO<ActionArgs>(e).Items;
                if (items.Count <= TriggerBehavior.WhenItem) continue;
                var plan = TriggerLeaves.Get(items[TriggerBehavior.WhenItem]);
                if (plan is null) continue;
                if (!repo.HasComponent<RuleLeaves>(e))
                {
                    // N-11 (CE-4473) — added with its final value: inside the brains' parallel phase an add is deferred
                    var fresh = new RuleLeaves();
                    plan.Sample(repo, e, ref fresh);
                    repo.AddComponent(e, fresh);
                    continue;
                }
                plan.Sample(repo, e, ref repo.GetComponentRW<RuleLeaves>(e));
            }
        }
    }
}
