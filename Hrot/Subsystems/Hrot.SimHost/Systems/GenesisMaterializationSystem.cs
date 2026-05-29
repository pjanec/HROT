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
    public sealed class GenesisMaterializationSystem : IEcsModuleSystem
    {
        private readonly NetworkEntityMap _entityMap;

        public GenesisMaterializationSystem(NetworkEntityMap entityMap)
        {
            _entityMap = entityMap;
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
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

        private void MaterializePassengers(ISimulationView view, EntityCommandBuffer cmd, EntityRepository repo)
        {
            foreach (var entity in view.Query().WithManaged<InitialPassengersIntent>().Build())
            {
                var intent = view.GetManagedComponentRO<InitialPassengersIntent>(entity);
                var buffer = new PassengerBuffer();
                bool allResolved = true;
                foreach (var netId in intent.PassengerNetworkIds)
                {
                    if (!_entityMap.TryGetEntity(netId, out var passenger) || !view.IsAlive(passenger))
                    {
                        allResolved = false;
                        break;
                    }
                    if (buffer.Count < PassengerBuffer.Capacity)
                    {
                        buffer.Passengers[buffer.Count] = passenger;
                        buffer.Count++;
                    }
                }
                if (!allResolved) continue;
                repo.SetComponent(entity, buffer);
                cmd.RemoveManagedComponent<InitialPassengersIntent>(entity);
            }
        }

        private void MaterializeVehicle(ISimulationView view, EntityCommandBuffer cmd, EntityRepository repo)
        {
            foreach (var entity in view.Query().WithManaged<InitialVehicleIntent>().Build())
            {
                var intent = view.GetManagedComponentRO<InitialVehicleIntent>(entity);
                if (!_entityMap.TryGetEntity(intent.VehicleNetworkId, out var vehicle) || !view.IsAlive(vehicle))
                    continue;
                repo.SetComponent(entity, new IsEmbarkedTag { VehicleEntity = vehicle });
                cmd.RemoveManagedComponent<InitialVehicleIntent>(entity);
            }
        }

        private void MaterializeHierarchy(ISimulationView view, EntityCommandBuffer cmd, EntityRepository repo)
        {
            foreach (var entity in view.Query().WithManaged<InitialHierarchyIntent>().Build())
            {
                var intent = view.GetManagedComponentRO<InitialHierarchyIntent>(entity);

                Entity parent      = Entity.Null;
                Entity firstChild  = Entity.Null;
                Entity nextSibling = Entity.Null;

                if (intent.ParentNetworkId != 0)
                {
                    if (!_entityMap.TryGetEntity(intent.ParentNetworkId, out parent) || !view.IsAlive(parent))
                        continue;
                }
                if (intent.FirstChildNetworkId != 0)
                {
                    if (!_entityMap.TryGetEntity(intent.FirstChildNetworkId, out firstChild) || !view.IsAlive(firstChild))
                        continue;
                }
                if (intent.NextSiblingNetworkId != 0)
                {
                    if (!_entityMap.TryGetEntity(intent.NextSiblingNetworkId, out nextSibling) || !view.IsAlive(nextSibling))
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
                if (!_entityMap.TryGetEntity(intent.RouteNetworkId, out var route) || !view.IsAlive(route))
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

                if (intent.CommanderNetworkId == 0)
                {
                    cmd.RemoveManagedComponent<InitialUnitSubordinateIntent>(entity);
                    continue;
                }

                if (!_entityMap.TryGetEntity(intent.CommanderNetworkId, out var commander) || !view.IsAlive(commander))
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
                });

                roster.SubordinateEntities[roster.Count]  = (long)entity.PackedValue;
                roster.TacticalDesignations[roster.Count] = (ushort)intent.Designation;
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
                var mem = new TargetMemory();
                TargetMemory* ptr = &mem;
                int count = 0;
                foreach (var entry in intent.Entries)
                {
                    if (count >= PerceptionConstants.MaxTrackedTargets) break;
                    if (!_entityMap.TryGetEntity(entry.NetworkId, out var target) || !view.IsAlive(target))
                        continue;
                    ptr->EntityIds[count]    = (long)target.PackedValue;
                    ptr->PositionsX[count]   = entry.PosX;
                    ptr->PositionsY[count]   = entry.PosY;
                    ptr->PositionsZ[count]   = entry.PosZ;
                    ptr->ThreatScores[count] = entry.Score;
                    ptr->LastSeenTick[count] = entry.LastSeenTick;
                    ptr->Modalities[count]   = entry.Modality;
                    count++;
                }
                mem.Count = count;
                repo.SetComponent(entity, mem);
                // Always remove intent after first tick (partial materialisation is acceptable).
                cmd.RemoveManagedComponent<InitialTargetsIntent>(entity);
            }
        }
    }
}
