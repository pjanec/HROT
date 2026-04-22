using Fdp.Core;
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
    public sealed class GenesisMaterializationSystem : ComponentSystem
    {
        private readonly NetworkEntityMap _entityMap;

        public GenesisMaterializationSystem(NetworkEntityMap entityMap)
        {
            _entityMap = entityMap;
        }

        protected override void OnUpdate()
        {
            var view = (ISimulationView)World;
            var cmd = new EntityCommandBuffer();
            try
            {
                MaterializePassengers(view, cmd);
                MaterializeVehicle(view, cmd);
                MaterializeHierarchy(view, cmd);
                MaterializeRoute(view, cmd);
                MaterializeTargets(view, cmd);
                cmd.Playback(World);
            }
            finally
            {
                cmd.Dispose();
            }
        }

        // ── Materialization helpers ────────────────────────────────────────────

        private void MaterializePassengers(ISimulationView view, EntityCommandBuffer cmd)
        {
            foreach (var entity in view.Query().WithManaged<InitialPassengersIntent>().Build())
            {
                var intent = view.GetManagedComponentRO<InitialPassengersIntent>(entity);
                var buffer = new PassengerBuffer();
                bool allResolved = true;
                foreach (var netId in intent.PassengerNetworkIds)
                {
                    if (!_entityMap.TryGetEntity(netId, out var passenger) || !World.IsAlive(passenger))
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
                World.SetComponent(entity, buffer);
                cmd.RemoveManagedComponent<InitialPassengersIntent>(entity);
            }
        }

        private void MaterializeVehicle(ISimulationView view, EntityCommandBuffer cmd)
        {
            foreach (var entity in view.Query().WithManaged<InitialVehicleIntent>().Build())
            {
                var intent = view.GetManagedComponentRO<InitialVehicleIntent>(entity);
                if (!_entityMap.TryGetEntity(intent.VehicleNetworkId, out var vehicle) || !World.IsAlive(vehicle))
                    continue;
                World.SetComponent(entity, new IsEmbarkedTag { VehicleEntity = vehicle });
                cmd.RemoveManagedComponent<InitialVehicleIntent>(entity);
            }
        }

        private void MaterializeHierarchy(ISimulationView view, EntityCommandBuffer cmd)
        {
            foreach (var entity in view.Query().WithManaged<InitialHierarchyIntent>().Build())
            {
                var intent = view.GetManagedComponentRO<InitialHierarchyIntent>(entity);

                Entity parent      = Entity.Null;
                Entity firstChild  = Entity.Null;
                Entity nextSibling = Entity.Null;

                if (intent.ParentNetworkId != 0)
                {
                    if (!_entityMap.TryGetEntity(intent.ParentNetworkId, out parent) || !World.IsAlive(parent))
                        continue;
                }
                if (intent.FirstChildNetworkId != 0)
                {
                    if (!_entityMap.TryGetEntity(intent.FirstChildNetworkId, out firstChild) || !World.IsAlive(firstChild))
                        continue;
                }
                if (intent.NextSiblingNetworkId != 0)
                {
                    if (!_entityMap.TryGetEntity(intent.NextSiblingNetworkId, out nextSibling) || !World.IsAlive(nextSibling))
                        continue;
                }

                World.SetComponent(entity, new VisHierarchyNode
                {
                    Parent      = parent,
                    FirstChild  = firstChild,
                    NextSibling = nextSibling,
                });
                cmd.RemoveManagedComponent<InitialHierarchyIntent>(entity);
            }
        }

        private void MaterializeRoute(ISimulationView view, EntityCommandBuffer cmd)
        {
            foreach (var entity in view.Query().WithManaged<InitialRouteIntent>().Build())
            {
                var intent = view.GetManagedComponentRO<InitialRouteIntent>(entity);
                if (!_entityMap.TryGetEntity(intent.RouteNetworkId, out var route) || !World.IsAlive(route))
                    continue;
                World.SetComponent(entity, new PersonalRouteRef { RouteEntity = route });
                cmd.RemoveManagedComponent<InitialRouteIntent>(entity);
            }
        }

        private unsafe void MaterializeTargets(ISimulationView view, EntityCommandBuffer cmd)
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
                    if (!_entityMap.TryGetEntity(entry.NetworkId, out var target) || !World.IsAlive(target))
                        continue;
                    ptr->EntityIds[count]    = (long)target.PackedValue;
                    ptr->PositionsX[count]   = entry.PosX;
                    ptr->PositionsY[count]   = entry.PosY;
                    ptr->ThreatScores[count] = entry.Score;
                    ptr->LastSeenTick[count] = entry.LastSeenTick;
                    ptr->Modalities[count]   = entry.Modality;
                    count++;
                }
                mem.Count = count;
                World.SetComponent(entity, mem);
                // Always remove intent after first tick (partial materialisation is acceptable).
                cmd.RemoveManagedComponent<InitialTargetsIntent>(entity);
            }
        }
    }
}
