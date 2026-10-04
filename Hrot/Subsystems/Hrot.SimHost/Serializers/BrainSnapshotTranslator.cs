using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Core.Serialization;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Scenario;

namespace Hrot.SimHost.Serializers
{
    /// <summary>
    /// ⭐⭐ <c>CE-3042</c> (R-192) — the scenario saves a unit's AI as a SNAPSHOT of what is needed and nothing else: the TASK
    /// slot, the SOP slot and the ROE, each as <c>{Name, Params JSON, Origin}</c> (ROE: <c>{Fire, Reactions, SetBy}</c>). Never a
    /// hash, run token, tier, cursor or owned part. On load an <see cref="InitialBrainIntent"/> starts each one through the
    /// ingress at its saved origin. 📄 <c>docs/DESIGN_Sensors_And_Doctrine.md</c> §7.5.
    /// <para>⭐ "Only when it differs from the TKB default" is read from the ORIGIN, not by looking the template up: a template
    /// default starts at origin <see cref="BehaviorOrigin.Sop"/> (behaviour <c>CE-3047</c>, SOP <c>CE-2077</c>) and a template ROE
    /// carries <c>SetBy = Unmarked</c> — so anything an order set is exactly what carries a higher origin.</para>
    /// <list type="bullet">
    /// <item>task: a running reaction saves the task it PAUSED; the SOP's idle choice is not saved (the SOP chooses again); a
    ///   slot a mission plan drives is not saved (the plan is, by <c>MissionPlanTranslator</c>); a behaviour stamped directly
    ///   (origin Unmarked) saves as Superior — the scenario's own authority.</item>
    /// <item>SOP: saved when an order replaced the template's.</item>
    /// <item>ROE: saved when an order changed it.</item>
    /// </list>
    /// </summary>
    public sealed class BrainSnapshotTranslator : IEntityScenarioTranslator
    {
        public const string BehaviorKey = "Behavior", SopKey = "Sop", RoeKey = "Roe";

        private readonly BehaviorRegistry _registry;

        public BrainSnapshotTranslator(BehaviorRegistry registry)
            => _registry = registry ?? throw new ArgumentNullException(nameof(registry));

        /// <summary>Consumes nothing: every component it reads is <c>NoScenario</c> already.</summary>
        public BitMask512 GetConsumedComponentsMask() => new BitMask512();

        public bool CanTranslate(EntityRepository repo, Entity entity)
            => (repo.IsComponentTypeRegistered<BehaviorState>() && repo.HasComponent<BehaviorState>(entity))
            || (repo.IsComponentTypeRegistered<SopState>() && repo.HasComponent<SopState>(entity))
            || (repo.IsComponentTypeRegistered<Roe>() && repo.HasComponent<Roe>(entity));

        public IEnumerable<string> GetOutputDomKeys()
        {
            yield return BehaviorKey;
            yield return SopKey;
            yield return RoeKey;
        }

        public Dictionary<string, object> Extract(EntityRepository repo, Entity entity, IGuidResolver guidResolver)
        {
            var result = new Dictionary<string, object>();
            if (TaskOf(repo, entity) is { } task) result[BehaviorKey] = Node(task);
            if (SopOf(repo, entity) is { } sop)   result[SopKey]      = Node(sop);
            if (RoeOf(repo, entity) is { } roe)   result[RoeKey]      = Node(roe);
            return result;
        }

        public void Inject(EntityRepository repo, Entity entity, Dictionary<string, object> scenarioData, IGuidResolver guidResolver)
        {
            var intent = new InitialBrainIntent
            {
                Behavior = Read<SavedBrainSlot>(scenarioData, BehaviorKey),
                Sop      = Read<SavedBrainSlot>(scenarioData, SopKey),
                Roe      = Read<SavedRoe>(scenarioData, RoeKey),
            };
            if (intent.Behavior == null && intent.Sop == null && intent.Roe == null) return;
            repo.SetManagedComponent(entity, intent);   // ⚠ registered by GenesisIntentRegistry, like every genesis intent
        }

        // ── what is saved ────────────────────────────────────────────────────────────────────────────────────────

        private SavedBrainSlot? TaskOf(EntityRepository repo, Entity entity)
        {
            if (!repo.IsComponentTypeRegistered<BehaviorState>() || !repo.HasComponent<BehaviorState>(entity)) return null;
            ref readonly var state = ref repo.GetComponentRO<BehaviorState>(entity);
            if (DrivenByMissionPlan(repo, entity)) return null;

            if (state.Origin == BehaviorOrigin.Reaction)                  // the reaction is transient — keep what it paused
                return BehaviorIngressSystem.PausedTaskOf(repo, entity) is { } paused
                    ? new SavedBrainSlot { Name = paused.BehaviorName, Params = Json(paused.JsonParams), Origin = paused.Origin }
                    : null;
            if (state.ActiveBehaviorHash == BehaviorIds.None || state.Origin == BehaviorOrigin.Sop) return null;

            string? name = null, json = null;
            if (repo.HasManagedComponent<BehaviorStartRecord>(entity)
                && ((ISimulationView)repo).GetManagedComponentRO<BehaviorStartRecord>(entity) is { } record
                && record.InstanceId == state.InstanceId)
            {
                name = record.BehaviorName;
                json = record.JsonParams;
            }
            if (name == null && !_registry.TryGetName(state.ActiveBehaviorHash, out name)) return null;
            return new SavedBrainSlot
            {
                Name   = name!,
                Params = Json(json),
                Origin = state.Origin == BehaviorOrigin.Unmarked ? BehaviorOrigin.Superior : state.Origin,
            };
        }

        private static SavedBrainSlot? SopOf(EntityRepository repo, Entity entity)
        {
            if (!repo.IsComponentTypeRegistered<SopState>() || !repo.HasComponent<SopState>(entity)) return null;
            ref readonly var sop = ref repo.GetComponentRO<SopState>(entity);
            if (sop.SopHash == BehaviorIds.None || sop.SopOrigin == BehaviorOrigin.Sop || sop.SopOrigin == BehaviorOrigin.Unmarked)
                return null;
            if (!repo.HasManagedComponent<SopStartRecord>(entity)) return null;
            var record = ((ISimulationView)repo).GetManagedComponentRO<SopStartRecord>(entity);
            if (record == null || string.IsNullOrEmpty(record.BehaviorName)) return null;
            return new SavedBrainSlot { Name = record.BehaviorName, Params = Json(record.JsonParams), Origin = sop.SopOrigin };
        }

        private static SavedRoe? RoeOf(EntityRepository repo, Entity entity)
        {
            if (!repo.IsComponentTypeRegistered<Roe>() || !repo.HasComponent<Roe>(entity)) return null;
            ref readonly var roe = ref repo.GetComponentRO<Roe>(entity);
            if (roe.SetBy == BehaviorOrigin.Unmarked) return null;   // the template's default
            return new SavedRoe { Fire = roe.Fire, Reactions = roe.Reactions, SetBy = roe.SetBy };
        }

        /// <summary>A mission plan with phases left drives the task slot — the plan is saved, not the phase's behaviour.</summary>
        private static bool DrivenByMissionPlan(EntityRepository repo, Entity entity)
        {
            if (!repo.IsComponentTypeRegistered<MissionPlanQueue>() || !repo.HasComponent<MissionPlanQueue>(entity)) return false;
            ref readonly var queue = ref repo.GetComponentRO<MissionPlanQueue>(entity);
            return queue.PhaseCount > 0 && queue.CurrentPhase < queue.PhaseCount;
        }

        // ── JSON ─────────────────────────────────────────────────────────────────────────────────────────────────

        private static string Json(string? json) => string.IsNullOrWhiteSpace(json) ? "{}" : json!;

        private static JsonNode Node(object value)
            => JsonSerializer.SerializeToNode(value, value.GetType(), FdpJsonOptionsRegistry.DefaultRelaxed)!;

        private static T? Read<T>(Dictionary<string, object> data, string key) where T : class
        {
            if (!data.TryGetValue(key, out var raw) || raw == null) return null;
            var opts = FdpJsonOptionsRegistry.DefaultRelaxed;
            return raw switch
            {
                JsonNode node                                            => node.Deserialize<T>(opts),
                JsonElement e when e.ValueKind == JsonValueKind.Object   => e.Deserialize<T>(opts),
                string s when !string.IsNullOrWhiteSpace(s)              => JsonSerializer.Deserialize<T>(s, opts),
                _                                                        => null,
            };
        }
    }
}
