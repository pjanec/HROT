using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.NetworkSpawning;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Extensions;
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

        /// <summary>⭐ E-6 / SR-184 — FRAMES the expansion waits for the solver's placement answer before it places on this node's own terrain
        /// (frames, not ticks: a scenario loads while the cluster is paused).</summary>
        public const int PlacementWaitFrames = 300;

        private long _frame;
        private long _nextPlacementRequest;
        private readonly Dictionary<long, Fdp.Toolkit.Navigation.PlacementResponseBatch> _answers = new();

        private readonly MacroExpander? _expander;
        private readonly NetworkEntityMap _entityMap;
        private readonly INetworkIdAllocator _idAllocator;
        private readonly NodeId _localNode;
        private readonly List<Entity> _work = new();

        private readonly Fdp.Interfaces.ITkbDatabase? _tkb;

        /// <param name="tkb">⭐ Macros debt A6 (SR-180) — the TKB a child's mobility LAYER comes from (its template's <c>LocomotionDto</c>); null =
        /// the DIS-type guess (tests).</param>
        public MacroExpansionSystem(MacroExpander? expander, NetworkEntityMap entityMap, INetworkIdAllocator idAllocator, NodeId localNode,
                                    Fdp.Interfaces.ITkbDatabase? tkb = null)
        {
            _tkb = tkb;
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

            _frame++;
            _answers.Clear();
            if (repo.Bus.IsRegisteredManaged<Fdp.Toolkit.Navigation.PlacementResponseBatch>())
                foreach (var a in view.ReadManagedEvents<Fdp.Toolkit.Navigation.PlacementResponseBatch>())
                    if (a.SourceNodeId == _localNode) _answers.TryAdd(a.RequestId, a);   // a second answer (two solvers) is ignored

            _work.Clear();
            foreach (var e in repo.Query().WithManaged<MacroExpansionIntent>().Build()) _work.Add(e);
            foreach (var e in _work) Step(repo, e);

            // ⭐ SR-150 / SR-157 / SR-172 — an args / anchor edit of a done instance this node owns ⇒ RECONCILE by GUID
            if (_expander != null && repo.IsComponentTypeRegistered<MacroOrigin>())
            {
                RebuildBaselines(repo);   // ⭐ debt A4 — a restore (or a branch) drops the transient baseline; rebuild it before comparing

                _work.Clear();
                foreach (var e in repo.Query().WithManaged<MacroExpandedWith>().Build()) _work.Add(e);
                foreach (var e in _work) MaybeReconcile(repo, e);
            }

            // ⭐ SR-170 — an instance that is gone takes every entity it made with it (each by its owner; a nested instance going takes its own)
            if (repo.IsComponentTypeRegistered<MacroOrigin>() && repo.Bus.IsRegisteredManaged<DestroyEntityCommand>())
                Cascade(repo);
        }

        private readonly HashSet<int> _destroySent = new();

        private void Cascade(EntityRepository repo)
        {
            foreach (var e in repo.Query().With<MacroOrigin>().Build())
            {
                int instance = repo.GetComponentRO<MacroOrigin>(e).Instance.NetworkId;
                if (instance == 0 || _entityMap.TryGetEntity(instance, out _)) continue;
                if (!((ISimulationView)repo).HasAuthority(e) || !_entityMap.TryGetNetworkId(e, out int id) || !_destroySent.Add(id)) continue;
                repo.Bus.PublishManaged(new DestroyEntityCommand { NetworkId = id, Reason = "its macro instance is gone (SR-170)" });
            }
            if (_destroySent.Count > 4096) _destroySent.Clear();
        }

        private void Step(EntityRepository repo, Entity e)
        {
            var intent = ((ISimulationView)repo).GetManagedComponentRO<MacroExpansionIntent>(e);
            if (intent is null) return;
            if (intent.Parked != null) { AwaitPlacement(repo, e, intent); return; }
            if (!intent.Published) { Begin(repo, e); return; }

            // (3) wait until every child exists HERE — no gap between the intent's removal and the children's construction (E-5)
            int left = 0;
            foreach (var id in intent.Awaited) if (!_entityMap.TryGetEntity(id, out _)) left++;
            if (left == 0)
            {
                string said = repo.HasComponent<MacroExpansionStatus>(e) ? repo.GetComponentRO<MacroExpansionStatus>(e).Reason.ToString() : "";
                Finish(repo, e, MacroExpansionState.Done, said);   // a placement fallback stays said (E-6)
                if (((ISimulationView)repo).GetManagedComponentRO<MacroInstance>(e) is { } done)
                    repo.SetManagedComponent(e, new MacroExpandedWith { Instance = done, Anchor = AnchorOf(repo, e) });
                return;
            }
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

            var result = _expander.Expand(mi, instanceId, guid, _localNode, anchor, new Fdp.Toolkit.ScenarioRules.Actions.WorldRefScope(repo), _idAllocator);
            if (!result.Ok)
            {
                FdpLog<MacroExpansionSystem>.Error($"[Macros] instance #{instanceId} '{mi.Macro}' failed: {result.Failure}");
                Finish(repo, e, MacroExpansionState.Failed, result.Failure!);
                return;
            }

            // ⭐ ⑤ (SR-180, SR-185) — ONE placement query for the whole flattened tree; the children wait, parked on the intent
            if (result.Relocatable && repo.Bus.IsRegisteredManaged<Fdp.Toolkit.Navigation.PlacementRequestBatch>()
                && BuildPlacement(result.Children, result.MaxShift, null, LayerOf) is { Slots.Count: > 0 } ask)
            {
                ask.RequestId = ((long)(int)_localNode << 40) | ++_nextPlacementRequest;
                ask.SourceNodeId = _localNode;
                repo.Bus.PublishManaged(ask);
                repo.SetManagedComponent(e, new MacroExpansionIntent
                {
                    Parked = result.Children, PlacementRequestId = ask.RequestId, AskedAtFrame = _frame, MaxShift = result.MaxShift,
                });
                return;
            }
            Publish(repo, e, result.Children, $"instance #{instanceId} '{mi.Macro}'");
        }

        private static void Publish(EntityRepository repo, Entity e, IReadOnlyList<EntityCreationRequest> children, string who)
        {
            var awaited = new HashSet<int>();
            foreach (var child in children)
            {
                repo.Bus.PublishManaged(child);
                if (child.PreAllocatedNetworkId != 0) awaited.Add(child.PreAllocatedNetworkId);
            }
            repo.SetManagedComponent(e, new MacroExpansionIntent { Published = true, Awaited = awaited, PublishedAtTick = repo.SimulationTick });
            FdpLog<MacroExpansionSystem>.Info($"[Macros] {who}: {children.Count} entities requested");
        }

        /// <summary>
        /// ⭐ ⑤ — the parked children's answer: every slot placed ⇒ each child moves to its spot and the children are published; ANY slot
        /// Failed ⇒ the whole expansion fails, naming the child, and nothing is created (SR-183). No answer within
        /// <see cref="PlacementWaitFrames"/> (no solver in reach — E-6 / SR-184) ⇒ placed by this node on its own terrain, said in the status.
        /// </summary>
        private void AwaitPlacement(EntityRepository repo, Entity e, MacroExpansionIntent intent)
        {
            var parked = intent.Parked!;
            string fallback = "";
            if (!_answers.TryGetValue(intent.PlacementRequestId, out var answer))
            {
                if (_frame - intent.AskedAtFrame < PlacementWaitFrames) return;
                var ask = intent.Ask ?? BuildPlacement(parked, intent.MaxShift, null, LayerOf)!;
                var terrain = repo.HasSingletonManaged<Fdp.Toolkit.Terrain.TerrainWorld>() ? repo.GetSingletonManaged<Fdp.Toolkit.Terrain.TerrainWorld>() : null;
                answer = new Fdp.Toolkit.Navigation.Systems.PlacementSolverSystem(null)
                    .Solve(ask, terrain, Fdp.Toolkit.Navigation.Systems.PlacementSolverSystem.ExistingEntities(repo, ask));
                fallback = terrain is null ? "placed unchecked: no solver answered and this node holds no terrain" : "placed on terrain only: no solver answered";
                FdpLog<MacroExpansionSystem>.Warn($"[Macros] {fallback} ({PlacementWaitFrames} frames)");
            }

            foreach (var a in answer.Answers)
            {
                if (a.Key < 0 || a.Key >= parked.Count) continue;
                if (a.Outcome == Fdp.Toolkit.Navigation.PlacementOutcome.Failed)
                {
                    if (intent.ReconcileFrom != null)
                    {
                        // ⭐ A3 — a failed RE-placement fails loudly and leaves the old placement (nothing moved, nothing made)
                        FdpLog<MacroExpansionSystem>.Error($"[Macros] re-placement of '{NameOf(parked[a.Key])}' failed: {a.Reason} — the old placement is kept");
                        Finish(repo, e, MacroExpansionState.Failed, $"re-placement: '{NameOf(parked[a.Key])}' — {a.Reason}; old placement kept");
                        repo.SetManagedComponent(e, intent.ReconcileTo!);   // not retried until the next edit
                        return;
                    }
                    Finish(repo, e, MacroExpansionState.Failed, $"placement: '{NameOf(parked[a.Key])}' — {a.Reason}");
                    return;
                }
                var comps = parked[a.Key].InitialComponents!;
                for (int i = 0; i < comps.Count; i++)
                    if (comps[i] is SimTransform st)
                    {
                        st.Position = new WorldPoint(a.Point.X, a.Point.Y, a.Point.Z);
                        comps[i] = st;
                    }
            }
            if (intent.ReconcileFrom != null)
            {
                repo.RemoveComponent<MacroExpansionIntent>(e);
                ApplyReconcile(repo, e, intent.ReconcileTo!, parked, intent.ReconcileFrom);
                return;
            }
            Publish(repo, e, parked, $"instance #{(_entityMap.TryGetNetworkId(e, out int id) ? id : 0)} (placed)");
            if (fallback.Length > 0)
            {
                var said = new MacroExpansionStatus { State = MacroExpansionState.Expanding, Reason = new FixedString128(fallback.Length > 127 ? fallback[..127] : fallback) };
                if (repo.HasComponent<MacroExpansionStatus>(e)) repo.SetComponent(e, said); else repo.AddComponent(e, said);
            }
        }

        /// <summary>One slot per child that stands on something: key = its index in <paramref name="children"/>; layer and radius from its DIS
        /// type (life form ⇒ infantry 0.5 m · land platform ⇒ vehicle 3 m · surface / subsurface ⇒ amphibious 5 m · air ⇒ air). ⛔ A nested
        /// instance is a marker, not a body — not placed (its men are).</summary>
        /// <param name="selfOf">⭐ A3 — the network id of the EXISTING entity a child stands for (a reconcile), 0 = a new one.</param>
        /// <param name="layerOf">⭐ A6 — the layer and footprint radius a child's TEMPLATE states (null = not stated ⇒ the DIS-type guess).</param>
        public static Fdp.Toolkit.Navigation.PlacementRequestBatch? BuildPlacement(IReadOnlyList<EntityCreationRequest> children, float maxShift,
                                                                                  Func<EntityCreationRequest, int>? selfOf = null,
                                                                                  Func<EntityCreationRequest, (Fdp.Toolkit.Navigation.PlacementLayer Layer, float Radius)?>? layerOf = null)
        {
            var ask = new Fdp.Toolkit.Navigation.PlacementRequestBatch { MaxShift = maxShift };
            for (int i = 0; i < children.Count; i++)
            {
                var comps = children[i].InitialComponents;
                if (comps is null) continue;
                SimTransform? at = null;
                bool marker = false;
                foreach (var c in comps)
                {
                    if (c is SimTransform st) at = st;
                    else if (c is MacroInstance) marker = true;
                }
                if (at is not { } p || marker) continue;
                var dis = new DISEntityType { Value = children[i].DisType };
                var (layer, radius) = layerOf?.Invoke(children[i]) ?? (dis.Kind, dis.Domain) switch
                {
                    (1, 1) => (Fdp.Toolkit.Navigation.PlacementLayer.Vehicle, 3f),
                    (1, 2) => (Fdp.Toolkit.Navigation.PlacementLayer.Air, 5f),
                    (1, 3) or (1, 4) => (Fdp.Toolkit.Navigation.PlacementLayer.Amphibious, 5f),
                    _ => (Fdp.Toolkit.Navigation.PlacementLayer.Infantry, 0.5f),
                };
                ask.Slots.Add(new Fdp.Toolkit.Navigation.PlacementSlot
                {
                    Key = i, Point = new System.Numerics.Vector3(p.Position.X, p.Position.Y, p.Position.Z), Layer = layer, Radius = radius,
                    Self = selfOf?.Invoke(children[i]) ?? 0,
                });
            }
            return ask;
        }

        /// <summary>⭐ Macros debt A6 (SR-180: "the layer comes from the child's template mobility") — the template's <c>LocomotionDto.Class</c>:
        /// pedestrian ⇒ infantry · wheeled / tracked ⇒ vehicle · rotorcraft / fixed wing ⇒ air · surface ⇒ amphibious; the radius is half the
        /// template's larger footprint side (its <c>VehicleParametersDto</c>), else the class default. Null when the type has no template or
        /// states no class — the caller falls back to the DIS type.</summary>
        public (Fdp.Toolkit.Navigation.PlacementLayer Layer, float Radius)? LayerOf(EntityCreationRequest child)
        {
            if (_tkb is null || !_tkb.TryGetByType(child.TkbType, out var template)) return null;
            var loco = template.GetDescriptor<Fdp.Toolkit.Tkb.Domain.LocomotionDto>();
            if (loco is null) return null;
            var vp = template.GetDescriptor<Fdp.Toolkit.Tkb.Domain.VehicleParametersDto>();
            float half = vp is null ? 0f : MathF.Max(vp.Length, vp.Width) / 2f;
            return loco.Class switch
            {
                Fdp.Toolkit.Tkb.Domain.LocomotionClass.Pedestrian => (Fdp.Toolkit.Navigation.PlacementLayer.Infantry, 0.5f),
                Fdp.Toolkit.Tkb.Domain.LocomotionClass.Wheeled or Fdp.Toolkit.Tkb.Domain.LocomotionClass.Tracked
                    => (Fdp.Toolkit.Navigation.PlacementLayer.Vehicle, half > 0f ? half : 3f),
                Fdp.Toolkit.Tkb.Domain.LocomotionClass.Rotorcraft or Fdp.Toolkit.Tkb.Domain.LocomotionClass.FixedWing
                    => (Fdp.Toolkit.Navigation.PlacementLayer.Air, half > 0f ? half : 5f),
                Fdp.Toolkit.Tkb.Domain.LocomotionClass.Surface => (Fdp.Toolkit.Navigation.PlacementLayer.Amphibious, half > 0f ? half : 5f),
                _ => null,
            };
        }

        private static string NameOf(EntityCreationRequest r)
        {
            foreach (var c in r.InitialComponents!) if (c is EntityInfo info && info.Name.Length > 0) return info.Name.ToString();
            return r.PreAllocatedNetworkId.ToString();
        }

        private static SimTransform AnchorOf(EntityRepository repo, Entity e)
            => repo.HasComponent<SimTransform>(e) ? repo.GetComponentRO<SimTransform>(e) : default;

        private static bool SameAnchor(SimTransform a, SimTransform b)
        {
            float dx = a.Position.X - b.Position.X, dy = a.Position.Y - b.Position.Y, dz = a.Position.Z - b.Position.Z;
            float dh = MathF.Abs(a.Rotation.HeadingDeg - b.Rotation.HeadingDeg) % 360f;
            return dx * dx + dy * dy + dz * dz < 0.01f && MathF.Min(dh, 360f - dh) < 0.1f;
        }

        /// <summary>
        /// ⭐ SR-150 / SR-157 / SR-172 — the instance's args or anchor changed since it expanded (an edit replaces the immutable
        /// <see cref="MacroInstance"/> record, or the instance was dragged): expand again from the files and RECONCILE BY GUID — a child whose
        /// GUID stays keeps its entity and network id and gets its place / identity re-applied; a GUID new to the new args is created; one
        /// the new args no longer make is destroyed; ⭐ one the OLD args made but the run destroyed stays destroyed (old set vs new set,
        /// both from the files — no tombstones). ⚠ Re-applied: <see cref="SimTransform"/> and <c>EntityInfo</c> (what an anchor or a
        /// side / name arg changes); a changed behaviour param is applied to NEW children only (S3b).
        /// </summary>
        private void MaybeReconcile(EntityRepository repo, Entity e)
        {
            var view = (ISimulationView)repo;
            var with = view.GetManagedComponentRO<MacroExpandedWith>(e);
            var mi = repo.HasManagedComponent<MacroInstance>(e) ? view.GetManagedComponentRO<MacroInstance>(e) : null;
            if (with is null || mi is null || !view.HasAuthority(e)) return;
            if (repo.HasManagedComponent<MacroExpansionIntent>(e)) return;   // an expansion or a re-placement is in flight
            var anchor = AnchorOf(repo, e);
            // ⚠ debt A3 — was `ReferenceEquals(with.Instance, mi) || …`: a MOVE keeps the same MacroInstance object, so a moved anchor never
            //   reconciled. The record compares by value (args + macro); the anchor is compared on its own.
            if (with.Instance == mi && SameAnchor(with.Anchor, anchor)) return;
            if (!_entityMap.TryGetNetworkId(e, out int instanceId)) return;

            Guid guid = repo.IsComponentTypeRegistered<EntityGuid>() && repo.HasComponent<EntityGuid>(e) ? repo.GetComponentRO<EntityGuid>(e).Value
                      : EntityGuidDerivation.Derive(Guid.Empty, "instance:" + instanceId);
            var scope = new Fdp.Toolkit.ScenarioRules.Actions.WorldRefScope(repo);
            var now = _expander!.Expand(mi, instanceId, guid, _localNode, anchor, scope, _idAllocator);
            if (!now.Ok)
            {
                FdpLog<MacroExpansionSystem>.Error($"[Macros] instance #{instanceId} re-expansion of '{mi.Macro}' failed: {now.Failure} — its entities are left as they were");
                Finish(repo, e, MacroExpansionState.Failed, now.Failure!);
                repo.SetManagedComponent(e, new MacroExpandedWith { Instance = mi, Anchor = anchor });
                return;
            }
            var target = new MacroExpandedWith { Instance = mi, Anchor = anchor };

            // ⭐⭐ A3 (SR-157 / SR-180) — a MOVED anchor is a re-placement: every child (kept and new) is placed again by the solver, the kept
            //   ones not counting as obstacles to themselves (PlacementSlot.Self); the answer is applied by GUID in ApplyReconcile.
            if (!SameAnchor(with.Anchor, anchor) && now.Relocatable && repo.Bus.IsRegisteredManaged<Fdp.Toolkit.Navigation.PlacementRequestBatch>())
            {
                var live = LiveChildrenByGuid(repo);
                var ask = BuildPlacement(now.Children, now.MaxShift,
                    c => live.TryGetValue(GuidOf(c), out var x) && _entityMap.TryGetNetworkId(x, out int nid) ? nid : 0, LayerOf);
                if (ask is { Slots.Count: > 0 })
                {
                    ask.RequestId = ((long)(int)_localNode << 40) | ++_nextPlacementRequest;
                    ask.SourceNodeId = _localNode;
                    repo.Bus.PublishManaged(ask);
                    repo.SetManagedComponent(e, new MacroExpansionIntent
                    {
                        Parked = now.Children, PlacementRequestId = ask.RequestId, AskedAtFrame = _frame, MaxShift = now.MaxShift, Ask = ask,
                        ReconcileFrom = with, ReconcileTo = target,
                    });
                    return;
                }
            }
            ApplyReconcile(repo, e, target, now.Children, with);
        }

        /// <summary>
        /// ⭐ Macros debt A4 (SR-150) — <see cref="MacroExpandedWith"/> is TRANSIENT (derivable, never recorded), so a restore, a branch or a
        /// replay → live hand-over leaves a <c>Done</c> instance with none, and its next edit would not reconcile. Rebuilt here from the
        /// instance as it now stands — what the restored world was expanded with. ⛔ Not for a NESTED instance (it carries
        /// <see cref="MacroOrigin"/>): its men are reconciled by its parent's flattened set, and a second baseline would re-place them twice.
        /// </summary>
        private void RebuildBaselines(EntityRepository repo)
        {
            var view = (ISimulationView)repo;
            _work.Clear();
            foreach (var e in repo.Query().WithManaged<MacroInstance>().With<MacroExpansionStatus>().Build())
            {
                if (repo.GetComponentRO<MacroExpansionStatus>(e).State != MacroExpansionState.Done) continue;
                if (repo.HasComponent<MacroOrigin>(e) || repo.HasManagedComponent<MacroExpandedWith>(e) || repo.HasManagedComponent<MacroExpansionIntent>(e)) continue;
                if (!view.HasAuthority(e)) continue;
                _work.Add(e);
            }
            foreach (var e in _work)
                repo.SetManagedComponent(e, new MacroExpandedWith { Instance = view.GetManagedComponentRO<MacroInstance>(e)!, Anchor = AnchorOf(repo, e) });
        }

        private static Dictionary<Guid, Entity> LiveChildrenByGuid(EntityRepository repo)
        {
            var existing = new Dictionary<Guid, Entity>();
            foreach (var x in repo.Query().With<MacroOrigin>().Build())
                if (repo.HasComponent<EntityGuid>(x)) existing[repo.GetComponentRO<EntityGuid>(x).Value] = x;
            return existing;
        }

        /// <summary>The reconcile's APPLY (SR-150 / SR-172): <paramref name="children"/> — the new args' set, placed — against the entities alive,
        /// by GUID: kept ⇒ <see cref="SimTransform"/> + <c>EntityInfo</c> re-applied · new ⇒ requested · made before but destroyed ⇒ stays destroyed ·
        /// no longer made ⇒ destroyed. <paramref name="with"/> is the baseline; <paramref name="target"/> becomes the new one.</summary>
        private void ApplyReconcile(EntityRepository repo, Entity e, MacroExpandedWith target, IReadOnlyList<EntityCreationRequest> children,
                                    MacroExpandedWith with)
        {
            if (!_entityMap.TryGetNetworkId(e, out int instanceId)) return;
            Guid guid = repo.IsComponentTypeRegistered<EntityGuid>() && repo.HasComponent<EntityGuid>(e) ? repo.GetComponentRO<EntityGuid>(e).Value
                      : EntityGuidDerivation.Derive(Guid.Empty, "instance:" + instanceId);
            var scope = new Fdp.Toolkit.ScenarioRules.Actions.WorldRefScope(repo);
            var anchor = target.Anchor;
            var mi = target.Instance;
            var now = new MacroExpander.Result(children, null);
            var before = _expander!.Expand(with.Instance, instanceId, guid, _localNode, with.Anchor, scope, new Hrot.Core.Network.SequentialIdAllocator());
            var madeBefore = new HashSet<Guid>();
            foreach (var c in before.Children) madeBefore.Add(GuidOf(c));

            // what exists now, by GUID (every level: a nested instance's men too)
            var existing = LiveChildrenByGuid(repo);

            // a kept nested instance keeps its network id: a NEW child under it must name that id, not the fresh one
            var keptIds = new Dictionary<int, int>();
            foreach (var c in now.Children)
                if (existing.TryGetValue(GuidOf(c), out var kept) && _entityMap.TryGetNetworkId(kept, out int keptId)) keptIds[c.PreAllocatedNetworkId] = keptId;

            var nowGuids = new HashSet<Guid>();
            int created = 0, kept2 = 0, gone = 0;
            foreach (var c in now.Children)
            {
                var g = GuidOf(c);
                nowGuids.Add(g);
                if (existing.TryGetValue(g, out var x))
                {
                    foreach (var comp in c.InitialComponents!)
                    {
                        if (comp is SimTransform st) MoveKept(repo, x, st);
                        else if (comp is EntityInfo info) repo.SetComponent(x, info);
                    }
                    kept2++;
                }
                else if (!madeBefore.Contains(g))
                {
                    for (int i = 0; i < c.InitialComponents!.Count; i++)
                        if (c.InitialComponents[i] is MacroOrigin o && keptIds.TryGetValue(o.Instance.NetworkId, out int real))
                            c.InitialComponents[i] = new MacroOrigin { Instance = new EntityRef(real), Local = o.Local };
                    repo.Bus.PublishManaged(c);
                    created++;
                }
                // else: the old args made it and the run destroyed it — it stays destroyed (SR-172)
            }
            foreach (var g in madeBefore)
                if (!nowGuids.Contains(g) && existing.TryGetValue(g, out var x) && _entityMap.TryGetNetworkId(x, out int id)
                    && repo.Bus.IsRegisteredManaged<DestroyEntityCommand>())
                {
                    repo.Bus.PublishManaged(new DestroyEntityCommand { NetworkId = id, Reason = "no longer made by its macro's args (SR-150)" });
                    gone++;
                }
            repo.SetManagedComponent(e, new MacroExpandedWith { Instance = mi, Anchor = anchor });
            FdpLog<MacroExpansionSystem>.Info($"[Macros] instance #{instanceId} reconciled: {kept2} kept, {created} created, {gone} removed");
        }

        /// <summary>
        /// ⭐ Macros debt A3, found LIVE (<c>2026-11-02</c>) — a kept child's new pose. ⛔ Its <c>SimTransform</c> is usually NOT this node's:
        /// split authority gives the body to the simulation node, so a write here lands in a REPLICA and the owner's next sample puts the man
        /// back (📌 measured: "4 kept", the men never moved; the unit rails run one world and could not see it). ⭐ The engine's ONE
        /// "not mine, ask the owner" route — the one <c>POST /entities/{id}/attribute</c> and the gizmo use — applies it there:
        /// <see cref="Fdp.Toolkit.Replication.Attributes.EntityAttributeChangeRequests.TryPublishJsonPatch"/>, compiled by the owner's
        /// <c>JsonAttributeCompiler</c> (<c>Position.X/Y/Z</c> + <c>Heading</c>). The ownership rule is the patch context's own:
        /// the component's authority bit; no <see cref="NetworkAuthority"/> at all ⇒ a networkless world ⇒ local.
        /// </summary>
        private static void MoveKept(EntityRepository repo, Entity x, SimTransform st)
        {
            bool local = !repo.HasComponent<NetworkAuthority>(x) || repo.HasAuthority(x, ComponentType<SimTransform>.ID);
            if (local) { repo.SetComponent(x, st); return; }
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            float heading = Fdp.Modules.Geographic.Systems.SimTransformBridgeSystem.RotationToHeadingDeg(st.Rotation);
            string patch = string.Format(inv, "{{\"Position\":{{\"X\":{0},\"Y\":{1},\"Z\":{2}}},\"Heading\":{3}}}",
                st.Position.X, st.Position.Y, st.Position.Z, heading);
            if (!Fdp.Toolkit.Replication.Attributes.EntityAttributeChangeRequests.TryPublishJsonPatch(repo, x, patch))
                FdpLog<MacroExpansionSystem>.Warn($"[Macros] a kept child {x} is owned elsewhere and has no network id — its new pose could not be requested");
        }

        private static Guid GuidOf(EntityCreationRequest r)
        {
            foreach (var c in r.InitialComponents!) if (c is EntityGuid g) return g.Value;
            return Guid.Empty;
        }

        private static void Finish(EntityRepository repo, Entity e, MacroExpansionState state, string reason)
        {
            var status = new MacroExpansionStatus { State = state };
            if (!string.IsNullOrEmpty(reason)) status.Reason = new FixedString128(reason.Length > 127 ? reason.Substring(0, 127) : reason);
            if (repo.HasComponent<MacroExpansionStatus>(e)) repo.SetComponent(e, status); else repo.AddComponent(e, status);
            repo.RemoveComponent<MacroExpansionIntent>(e);
        }
    }

}
