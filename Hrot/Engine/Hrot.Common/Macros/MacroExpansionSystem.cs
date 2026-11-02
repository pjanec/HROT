using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.NetworkSpawning;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Scenario;

namespace Hrot.Common.Macros
{
    /// <summary>
    /// ⭐⭐ Macros S3a ④ (docs/DESIGN_Scenario_Macros.md §15 E-1…E-5, SR-96 / SR-147 / SR-174) — drives every expansion on the node that
    /// CREATED the instance: <c>CreateEntityRequestSystem</c> stamps a <see cref="MacroExpansionIntent"/> + <see cref="MacroExpansionState.Expanding"/>
    /// on an instance it creates; this system then (1) asks the <see cref="MacroExpander"/> for every child request, (2) publishes them on the
    /// bus — they take the ONE request path, owned by this node — and (3) keeps the intent until every child EXISTS, then sets
    /// <see cref="MacroExpansionState.Done"/> and drops the intent. A failure sets <see cref="MacroExpansionState.Failed"/> with its reason
    /// and creates nothing. ⭐ The intent is transient and never replicated, so a ghost (another node's copy, a promotion) never expands.
    /// ⛔ A node composed WITHOUT an expander (no macros library — E-4) refuses loudly; it never leaves an instance silently unexpanded.
    /// </summary>
    [UpdateInPhase(SystemPhase.Input)]
    [TickMode(TickPolicy.EveryFrame)]
    public sealed class MacroExpansionSystem : IEcsModuleSystem
    {
        /// <summary>Ticks a published expansion may wait for its children before it is declared failed.</summary>
        public const int ChildWaitLimitTicks = 3000;

        private readonly MacroExpander? _expander;
        private readonly NetworkEntityMap _entityMap;
        private readonly INetworkIdAllocator _idAllocator;
        private readonly NodeId _localNode;
        private readonly List<Entity> _work = new();

        public MacroExpansionSystem(MacroExpander? expander, NetworkEntityMap entityMap, INetworkIdAllocator idAllocator, NodeId localNode)
        {
            _expander = expander;
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _idAllocator = idAllocator ?? throw new ArgumentNullException(nameof(idAllocator));
            _localNode = localNode;
        }

        /// <summary><c>true</c> when this node can expand (the forwarding rail reads it on every host's constructed pack).</summary>
        public bool CanExpand => _expander != null;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (Fdp.Toolkit.Replay.ReplayState.IsActive(view)) return;   // a replay: the recording owns the world
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<MacroExpansionStatus>()) return;

            _work.Clear();
            foreach (var e in repo.Query().WithManaged<MacroExpansionIntent>().Build()) _work.Add(e);
            foreach (var e in _work) Step(repo, e);
        }

        private void Step(EntityRepository repo, Entity e)
        {
            var intent = ((ISimulationView)repo).GetManagedComponentRO<MacroExpansionIntent>(e);
            if (intent is null) return;
            if (!intent.Published) { Begin(repo, e); return; }

            // (3) wait until every child exists HERE — no gap between the intent's removal and the children's construction (E-5)
            int left = 0;
            foreach (var id in intent.Awaited) if (!_entityMap.TryGetEntity(id, out _)) left++;
            if (left == 0) { Finish(repo, e, MacroExpansionState.Done, ""); return; }
            if (unchecked(repo.SimulationTick - intent.PublishedAtTick) > ChildWaitLimitTicks)
                Finish(repo, e, MacroExpansionState.Failed, $"{left} of {intent.Awaited.Count} macro entities never appeared");
        }

        private void Begin(EntityRepository repo, Entity e)
        {
            if (_expander is null)
            {
                Finish(repo, e, MacroExpansionState.Failed, "this node has no macros library — it cannot expand a macro (E-4)");
                return;
            }
            var view = (ISimulationView)repo;
            if (!repo.HasManagedComponent<MacroInstance>(e) || view.GetManagedComponentRO<MacroInstance>(e) is not { } mi)
            {
                Finish(repo, e, MacroExpansionState.Failed, "the entity carries no MacroInstance");
                return;
            }
            if (!_entityMap.TryGetNetworkId(e, out int instanceId)) return;   // not mapped yet: next frame

            Guid guid = repo.IsComponentTypeRegistered<EntityGuid>() && repo.HasComponent<EntityGuid>(e)
                ? repo.GetComponentRO<EntityGuid>(e).Value
                : EntityGuidDerivation.Derive(Guid.Empty, "instance:" + instanceId);   // a transient instance (SR-167's runtime case is S3b)
            var anchor = repo.HasComponent<SimTransform>(e) ? repo.GetComponentRO<SimTransform>(e) : default;

            var result = _expander.Expand(mi, instanceId, guid, _localNode, anchor, new WorldRefScope(repo, _entityMap), _idAllocator);
            if (!result.Ok)
            {
                FdpLog<MacroExpansionSystem>.Error($"[Macros] instance #{instanceId} '{mi.Macro}' failed: {result.Failure}");
                Finish(repo, e, MacroExpansionState.Failed, result.Failure!);
                return;
            }

            var awaited = new HashSet<int>();
            foreach (var child in result.Children)
            {
                repo.Bus.PublishManaged(child);
                if (child.PreAllocatedNetworkId != 0) awaited.Add(child.PreAllocatedNetworkId);
            }
            repo.SetManagedComponent(e, new MacroExpansionIntent { Published = true, Awaited = awaited, PublishedAtTick = repo.SimulationTick });
            FdpLog<MacroExpansionSystem>.Info($"[Macros] instance #{instanceId} '{mi.Macro}': {result.Children.Count} entities requested");
        }

        private static void Finish(EntityRepository repo, Entity e, MacroExpansionState state, string reason)
        {
            var status = new MacroExpansionStatus { State = state };
            if (!string.IsNullOrEmpty(reason)) status.Reason = new FixedString128(reason.Length > 127 ? reason.Substring(0, 127) : reason);
            if (repo.HasComponent<MacroExpansionStatus>(e)) repo.SetComponent(e, status); else repo.AddComponent(e, status);
            repo.RemoveComponent<MacroExpansionIntent>(e);
        }
    }

    /// <summary>
    /// ⭐ Where an instance's ARGS resolve (SR-58: anything outside a macro comes in through its params) — the LIVE world of the expanding
    /// node: an entity's <see cref="EntityGuid"/> (and its <see cref="RefKey"/> where the world registers one) → its network id and place.
    /// </summary>
    public sealed class WorldRefScope : IRefScope
    {
        private readonly EntityRepository _repo;
        private readonly NetworkEntityMap _map;
        private Dictionary<Guid, Entity>? _byGuid;
        private Dictionary<string, Entity>? _byKey;

        public WorldRefScope(EntityRepository repo, NetworkEntityMap map) { _repo = repo; _map = map; }

        public string Describe => "the world outside the macro";

        private void Index()
        {
            if (_byGuid != null) return;
            _byGuid = new Dictionary<Guid, Entity>();
            _byKey = new Dictionary<string, Entity>(StringComparer.Ordinal);
            if (_repo.IsComponentTypeRegistered<EntityGuid>())
                foreach (var e in _repo.Query().With<EntityGuid>().Build()) _byGuid[_repo.GetComponentRO<EntityGuid>(e).Value] = e;
            if (_repo.IsComponentTypeRegistered<RefKey>())
                foreach (var e in _repo.Query().With<RefKey>().Build()) _byKey.TryAdd(_repo.GetComponentRO<RefKey>(e).Value.ToString(), e);   // SR-188: first wins
        }

        public bool TryResolve(in RefSpec spec, out EntityRef entity)
        {
            entity = EntityRef.None;
            Index();
            Entity e = default;
            bool hit = spec.Form switch
            {
                RefForm.Exact => _byGuid!.TryGetValue(spec.Exact, out e),
                RefForm.Key   => _byKey!.TryGetValue(spec.KeyText, out e),
                _ => false,
            };
            if (!hit || !_map.TryGetNetworkId(e, out int id)) return false;
            entity = new EntityRef(id);
            return true;
        }

        public bool TryPositionOf(EntityRef entity, out WorldPoint position)
        {
            position = default;
            if (!_map.TryGetEntity(entity.NetworkId, out var e) || !_repo.HasComponent<SimTransform>(e)) return false;
            position = _repo.GetComponentRO<SimTransform>(e).Position;
            return true;
        }
    }
}
