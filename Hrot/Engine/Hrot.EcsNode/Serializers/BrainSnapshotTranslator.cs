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
    /// hash, run token, tier, cursor or owned part. On load an <see cref="UnitIntent"/> starts each one through the
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
            // ⭐⭐ CE-3494 (R9, DESIGN_Unit_Intent.md §3.3) — per slot: the PENDING intent (authored, not yet applied — e.g. a scenario
            //   edited and saved before it ever ran), else the running AI as before. ⛔ A template default (origin Sop / Unmarked) is not
            //   saved either way (R-192): a reload re-derives it. ⇒ a load followed by a save writes the SAME keys (no file change).
            var pending = repo.TryGetTable(typeof(UnitIntent), out _) && repo.HasManagedComponent<UnitIntent>(entity)
                ? ((ISimulationView)repo).GetManagedComponentRO<UnitIntent>(entity) : null;
            // ⭐ CE-3494 §10 P7 (R9) — a pending CLEAR (a person's release) saves no slot; a pending person's request is saved without its request id
            if (pending?.Behavior is not { Clear: true } && (Authored(pending?.Behavior) ?? TaskOf(repo, entity)) is { } task) result[BehaviorKey] = Node(task with { RequestId = System.Guid.Empty });
            if (pending?.Sop is not { Clear: true } && (Authored(pending?.Sop) ?? SopOf(repo, entity)) is { } sop) result[SopKey] = Node(sop with { RequestId = System.Guid.Empty });
            if ((pending?.Roe is { } r && r.SetBy != BehaviorOrigin.Unmarked ? r : RoeOf(repo, entity)) is { } roe) result[RoeKey] = Node(roe);
            return result;
        }

        public void Inject(EntityRepository repo, Entity entity, Dictionary<string, object> scenarioData, IGuidResolver guidResolver)
        {
            var intent = new UnitIntent
            {
                Behavior = Read<IntentSlot>(scenarioData, BehaviorKey),
                Sop      = Read<IntentSlot>(scenarioData, SopKey),
                Roe      = Read<SavedRoe>(scenarioData, RoeKey),
            };
            if (intent.Behavior == null && intent.Sop == null && intent.Roe == null) return;
            repo.SetManagedComponent(entity, intent);   // ⚠ registered by GenesisIntentRegistry, like every genesis intent
        }

        /// <summary>⭐ CE-3494 — a pending slot that is an order (not a template default), or null.</summary>
        private static IntentSlot? Authored(IntentSlot? slot)
            => slot is { } s && s.Origin != BehaviorOrigin.Sop && s.Origin != BehaviorOrigin.Unmarked ? s : null;

        // ── what is saved: the ONE reader, in the scenario's scope (CE-3048 shares it with the Brain hand-over) ─────

        private IntentSlot? TaskOf(EntityRepository repo, Entity entity)
            => BrainIntentReader.TaskOf(repo, entity, _registry, BrainIntentScope.Ordered);

        private static IntentSlot? SopOf(EntityRepository repo, Entity entity)
            => BrainIntentReader.SopOf(repo, entity, BrainIntentScope.Ordered);

        private static SavedRoe? RoeOf(EntityRepository repo, Entity entity)
            => BrainIntentReader.RoeOf(repo, entity, BrainIntentScope.Ordered);

        // ── JSON ─────────────────────────────────────────────────────────────────────────────────────────────────

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
