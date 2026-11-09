using System;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Core.Logging;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Perception;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Vis2D.Components;
using Hrot.Common.Serializers;
using Hrot.Map.Common.Components;

namespace Hrot.SimHost.Systems
{
    /// <summary>
    /// Resolves Intent DTO managed components (written by scenario translators) into
    /// their corresponding structural ECS components once all referenced entities are
    /// alive in the <see cref="NetworkEntityMap"/>.
    ///
    /// <para>On each simulation tick, the system queries for entities that carry an
    /// <c>Initial*Intent</c> managed component and attempts to resolve all cross-entity
    /// Network ID references via <see cref="NetworkEntityMap.TryGetEntity"/>. If all
    /// references resolve, the structural component is written and the Intent is removed.
    /// For <see cref="InitialTargetsIntent"/> partial materialisation is accepted and the
    /// intent is always removed after the first tick regardless.</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Input)]
    [TickMode(TickPolicy.EveryFrame)]
    public sealed class GenesisMaterializationSystem : IEcsModuleSystem
    {
        private readonly NetworkEntityMap _entityMap;

        public GenesisMaterializationSystem(NetworkEntityMap entityMap)
        {
            _entityMap = entityMap;
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (Fdp.Toolkit.Replay.ReplayState.IsActive(view)) return;   // ⭐ CE-3329 P2 — a replay: the recording owns the world (DESIGN_Replay_Time_Axis_And_Review §12l)
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(GenesisMaterializationSystem)} requires direct EntityRepository access " +
                    $"and cannot run on a read-only snapshot ({view.GetType().Name}).");

            var cmd = new EntityCommandBuffer();
            try
            {
                MaterializePassengers(view, cmd, repo);
                MaterializeVehicle(view, cmd, repo);
                MaterializeHierarchy(view, cmd, repo);
                MaterializeRoute(view, cmd, repo);
                MaterializeTargets(view, cmd, repo);
                MaterializeUnitSubordinate(view, cmd, repo);
                cmd.Playback(repo);
            }
            finally
            {
                cmd.Dispose();
            }
        }

        // ── Materialization helpers ────────────────────────────────────────────

        // ⭐ CE-3110 I-2a — both embarkation intents resolve through THE rules (Embarkation.TryEmbark), so a soldier loaded aboard
        //   gets the list entry, the tag AND the stripped CanMove/CanShoot — 🔴 genesis used to write the list and the tag itself and
        //   never strip anything, so a passenger loaded aboard could walk away. TryEmbark is idempotent: the vehicle's intent and the
        //   passenger's own intent may both name the same boarding.
        private void MaterializePassengers(ISimulationView view, EntityCommandBuffer cmd, EntityRepository repo)
        {
            foreach (var entity in view.Query().WithManaged<InitialPassengersIntent>().Build())
            {
                var intent = view.GetManagedComponentRO<InitialPassengersIntent>(entity);
                bool allResolved = true;
                foreach (var netId in intent.PassengerNetworkIds)
                {
                    if (!_entityMap.TryGetEntity(netId.Ref.NetworkId, out var passenger) || !view.IsAlive(passenger))
                    {
                        allResolved = false;
                        break;
                    }
                }
                // ⭐ Initial state S1 — the second man resolves too (a stretcher waits for both handles)
                Entity assistant = Entity.Null;
                if (allResolved && !intent.Assistant.IsNone
                    && (!_entityMap.TryGetEntity(intent.Assistant.Ref.NetworkId, out assistant) || !view.IsAlive(assistant))) allResolved = false;
                if (!allResolved) continue;
                // ⭐ WS-4 (W-I) — into the SAVED seat when the save has seats (a gunner into the gunner seat, the M2 into its station);
                //   ⭐ S1 — with the SAVED carry mode and second man (a casualty on a stretcher comes back on it, not seated)
                bool seated = intent.Seats.Count == intent.PassengerNetworkIds.Count;
                bool moded = seated && intent.Modes.Count == intent.Seats.Count;
                for (int i = 0; i < intent.PassengerNetworkIds.Count; i++)
                    if (_entityMap.TryGetEntity(intent.PassengerNetworkIds[i].Ref.NetworkId, out var passenger))
                        Count(seated ? Fdp.Toolkit.Behavior.Embarkation.TryEmbarkAt(repo, passenger, entity, intent.Seats[i],
                                           moded ? (Fdp.Toolkit.Behavior.Components.CarryMode)intent.Modes[i] : Fdp.Toolkit.Behavior.Components.CarryMode.Seated, assistant)
                                     : Fdp.Toolkit.Behavior.Embarkation.TryEmbark(repo, passenger, entity));
                cmd.RemoveManagedComponent<InitialPassengersIntent>(entity);
            }
        }

        private void MaterializeVehicle(ISimulationView view, EntityCommandBuffer cmd, EntityRepository repo)
        {
            foreach (var entity in view.Query().WithManaged<InitialVehicleIntent>().Build())
            {
                var intent = view.GetManagedComponentRO<InitialVehicleIntent>(entity);
                if (!_entityMap.TryGetEntity(intent.VehicleNetworkId.Ref.NetworkId, out var vehicle) || !view.IsAlive(vehicle))
                    continue;
                Count(intent.Seat >= 0 ? Fdp.Toolkit.Behavior.Embarkation.TryEmbarkAt(repo, entity, vehicle, intent.Seat)   // ⭐ WS-4 — its saved seat
                                       : Fdp.Toolkit.Behavior.Embarkation.TryEmbark(repo, entity, vehicle));
                cmd.RemoveManagedComponent<InitialVehicleIntent>(entity);
            }
        }

        /// <summary>⭐ <c>CE-2151</c> B-b (R-2009 B3) — boardings a scenario asked for that the rules refused: the vehicle's type declares no
        /// <c>Gen.Carrier</c> (it carries nobody) or has no free seat. ⛔ Genesis used to give such a vehicle a list (<c>EnsureBuffer</c>),
        /// which made any type a carrier the moment a save named passengers for it.</summary>
        public int RefusedBoardings { get; private set; }

        private void Count(Fdp.Toolkit.Behavior.EmbarkResult result)
        {
            if (result is Fdp.Toolkit.Behavior.EmbarkResult.Invalid or Fdp.Toolkit.Behavior.EmbarkResult.Full) RefusedBoardings++;
        }

        private void MaterializeHierarchy(ISimulationView view, EntityCommandBuffer cmd, EntityRepository repo)
        {
            foreach (var entity in view.Query().WithManaged<InitialHierarchyIntent>().Build())
            {
                var intent = view.GetManagedComponentRO<InitialHierarchyIntent>(entity);

                Entity parent      = Entity.Null;
                Entity firstChild  = Entity.Null;
                Entity nextSibling = Entity.Null;

                if (!intent.ParentNetworkId.IsNone)
                {
                    if (!_entityMap.TryGetEntity(intent.ParentNetworkId.Ref.NetworkId, out parent) || !view.IsAlive(parent))
                        continue;
                }
                if (!intent.FirstChildNetworkId.IsNone)
                {
                    if (!_entityMap.TryGetEntity(intent.FirstChildNetworkId.Ref.NetworkId, out firstChild) || !view.IsAlive(firstChild))
                        continue;
                }
                if (!intent.NextSiblingNetworkId.IsNone)
                {
                    if (!_entityMap.TryGetEntity(intent.NextSiblingNetworkId.Ref.NetworkId, out nextSibling) || !view.IsAlive(nextSibling))
                        continue;
                }

                repo.SetComponent(entity, new VisHierarchyNode
                {
                    Parent      = parent,
                    FirstChild  = firstChild,
                    NextSibling = nextSibling,
                });
                cmd.RemoveManagedComponent<InitialHierarchyIntent>(entity);
            }
        }

        private void MaterializeRoute(ISimulationView view, EntityCommandBuffer cmd, EntityRepository repo)
        {
            foreach (var entity in view.Query().WithManaged<InitialRouteIntent>().Build())
            {
                var intent = view.GetManagedComponentRO<InitialRouteIntent>(entity);
                if (!_entityMap.TryGetEntity(intent.RouteNetworkId.Ref.NetworkId, out var route) || !view.IsAlive(route))
                    continue;
                repo.SetComponent(entity, new PersonalRouteRef { RouteEntity = route });
                cmd.RemoveManagedComponent<InitialRouteIntent>(entity);
            }
        }

        private unsafe void MaterializeUnitSubordinate(ISimulationView view, EntityCommandBuffer cmd, EntityRepository repo)
        {
            foreach (var entity in view.Query().WithManaged<InitialUnitSubordinateIntent>().Build())
            {
                var intent = view.GetManagedComponentRO<InitialUnitSubordinateIntent>(entity);

                if (intent.CommanderNetworkId.IsNone)
                {
                    cmd.RemoveManagedComponent<InitialUnitSubordinateIntent>(entity);
                    continue;
                }

                if (!_entityMap.TryGetEntity(intent.CommanderNetworkId.Ref.NetworkId, out var commander) || !view.IsAlive(commander))
                {
                    // Escape hatch: if the entity is already Active, the commander will never arrive.
                    if (repo.GetLifecycleState(entity) == EntityLifecycle.Active)
                    {
                        FdpLog<GenesisMaterializationSystem>.Warn(
                            $"[GenesisMaterializationSystem] Commander network ID {intent.CommanderNetworkId} " +
                            $"not found for entity {entity.Index}; dropping intent.");
                        cmd.RemoveManagedComponent<InitialUnitSubordinateIntent>(entity);
                    }
                    // Otherwise retry next tick.
                    continue;
                }

                // Capacity check — do not set UnitSubordinate if roster is full.
                var roster = repo.HasComponent<UnitRoster>(commander)
                    ? repo.GetComponent<UnitRoster>(commander)
                    : new UnitRoster();

                if (roster.Count >= UnitRoster.Capacity)
                {
                    FdpLog<GenesisMaterializationSystem>.Warn(
                        $"[GenesisMaterializationSystem] Commander {commander.Index} roster is full; " +
                        $"cannot add subordinate {entity.Index}.");
                    cmd.RemoveManagedComponent<InitialUnitSubordinateIntent>(entity);
                    continue;
                }

                // Atomic write: subordinate component + roster append.
                repo.SetComponent(entity, new UnitSubordinate
                {
                    Commander   = commander,
                    Designation = intent.Designation,
                    FireTeam    = intent.FireTeam,   // ⭐ CE-2281 / R-3019
                });

                // ⭐⭐⭐ O1 — the SECOND roster creator, and the one the live run proved was needed:
                //   scenario-loaded hierarchies never go through UnitHierarchySystem's assign event.
                Fdp.Toolkit.Squad.SquadStateProvisioning.EnsureForCommander(repo, commander);

                // ⭐ CE-4137 — the roster is in NETWORK-ID order (the order the scenario file lists the members), never the order this query
                //   happens to visit them: a Live load after Edit reuses freed entity slots in reverse, so Bravo materialised before Alpha
                //   and PlatoonBound's slot 2 (support) became Alpha — the squads' roles swapped. A member with no network id appends.
                int at = roster.Count;
                if (_entityMap.TryGetNetworkId(entity, out var netId))
                    for (int i = 0; i < roster.Count; i++)
                        if (_entityMap.TryGetNetworkId(roster.SubordinateEntities[i], out var other) && other > netId) { at = i; break; }
                for (int i = roster.Count; i > at; i--)
                {
                    roster.SubordinateEntities[i]  = roster.SubordinateEntities[i - 1];
                    roster.TacticalDesignations[i] = roster.TacticalDesignations[i - 1];
                }
                roster.SubordinateEntities[at]  = entity;
                roster.TacticalDesignations[at] = (ushort)intent.Designation;
                roster.Count++;
                repo.SetComponent(commander, roster);

                cmd.RemoveManagedComponent<InitialUnitSubordinateIntent>(entity);
            }
        }

        private unsafe void MaterializeTargets(ISimulationView view, EntityCommandBuffer cmd, EntityRepository repo)
        {
            foreach (var entity in view.Query().WithManaged<InitialTargetsIntent>().Build())
            {
                var intent = view.GetManagedComponentRO<InitialTargetsIntent>(entity);
                // ⭐ CE-2232 / CE-4035 G-5 (Next_2) — MERGE the authored entries into what the unit already knows, never replace it:
                //   📐 measured live (ua-technical-scoot, editor Preview): an attack order designates and seeds its target on the first
                //   tick, and this write of the file's (empty) list wiped it in the same frame — the technicals never knew the tank.
                var mem = repo.HasComponent<TargetMemory>(entity) ? repo.GetComponent<TargetMemory>(entity) : new TargetMemory();
                TargetMemory* ptr = &mem;
                int count = mem.Count;
                foreach (var entry in intent.Entries)
                {
                    if (!_entityMap.TryGetEntity(entry.NetworkId.Ref.NetworkId, out var target) || !view.IsAlive(target))
                        continue;
                    int slot = count;
                    for (int i = 0; i < count; i++)
                        if (ptr->EntityIds[i] == (long)target.PackedValue) { slot = i; break; }
                    if (slot == count)
                    {
                        if (count >= PerceptionConstants.MaxTrackedTargets) continue;
                        count++;
                    }
                    ptr->EntityIds[slot]    = (long)target.PackedValue;
                    ptr->PositionsX[slot]   = entry.PosX;
                    ptr->PositionsY[slot]   = entry.PosY;
                    ptr->PositionsZ[slot]   = entry.PosZ;
                    ptr->Freshness[slot] = entry.Score;
                    ptr->LastSeenTick[slot] = entry.LastSeenTick;
                    ptr->Modalities[slot]   = entry.Modality;
                    ptr->SeenStance[slot]   = entry.SeenStance;
                }
                mem.Count = count;
                repo.SetComponent(entity, mem);
                // Always remove intent after first tick (partial materialisation is acceptable).
                cmd.RemoveManagedComponent<InitialTargetsIntent>(entity);
            }
        }
    }
}
