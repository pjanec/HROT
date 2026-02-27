using System;
using Fdp.Kernel;
using FDP.Kernel.Logging;
using FDP.Toolkit.Lifecycle;
using FDP.Toolkit.NetworkSpawning.Events;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Services;
using Fdp.Interfaces;
using ModuleHost.Core.Abstractions;
using ModuleHost.Core.Network;
using ModuleHost.Core.Network.Interfaces;

namespace FDP.Toolkit.NetworkSpawning.Systems
{
    /// <summary>
    /// Strategy delegate that inspects a single component object and extracts a DIS entity type.
    /// Injected into <see cref="NetworkSpawningSystem"/> to keep the Toolkit free of any
    /// dependency on <c>Bagira.DDS.DataModel</c> or other concrete component assemblies.
    /// </summary>
    /// <param name="component">A component instance from <see cref="Events.SpawnEntityCommand.InitialComponents"/>.</param>
    /// <param name="disType">Receives the extracted DIS type value when the delegate returns <c>true</c>.</param>
    /// <returns>
    /// <c>true</c> if this component carries a DIS type (extraction performed);
    /// <c>false</c> if the component is not the one that holds a DIS type.
    /// </returns>
    public delegate bool DisTypeExtractor(object component, out ulong disType);

    /// <summary>
    /// Centralised system that replaces per-node entity-spawning boilerplate.
    /// Consumes <see cref="SpawnEntityCommand"/>, <see cref="UpdateEntityCommand"/>,
    /// and <see cref="DestroyEntityCommand"/> managed events each tick and drives
    /// the ECS + ELM lifecycle machinery.
    /// </summary>
    [UpdateInPhase(SystemPhase.BeforeSync)]
    public class NetworkSpawningSystem : IModuleSystem
    {
        private readonly ITkbDatabase _tkbDb;
        private readonly EntityLifecycleModule _elm;
        private readonly NetworkEntityMap _networkMap;
        private readonly INetworkIdAllocator _idAllocator;
        private readonly int _localNodeId;
        private readonly DisTypeExtractor? _disTypeExtractor;
        private readonly Action<EntityRepository, Entity, bool>? _onEntitySpawned;

        /// <param name="tkbDb">TKB template registry.</param>
        /// <param name="elm">Entity lifecycle module that manages construction/destruction handshakes.</param>
        /// <param name="networkMap">Entity ↔ network-ID registry.</param>
        /// <param name="idAllocator">Network-ID allocator (stub or DDS-backed).</param>
        /// <param name="localNodeId">This node's logical ID, used to fill NetworkOwnership.</param>
        /// <param name="disTypeExtractor">
        /// Optional strategy that extracts a DIS entity type from a component object.
        /// Decouples the Toolkit from concrete component assemblies.
        /// When <c>null</c>, <see cref="ExtractDisType"/> always returns 0.
        /// Typical injection:
        /// <code>
        /// (object c, out ulong dis) =&gt; {
        ///     if (c is EntityMaster m) { dis = m.DisType; return true; }
        ///     dis = 0; return false;
        /// }
        /// </code>
        /// </param>
        public NetworkSpawningSystem(
            ITkbDatabase tkbDb,
            EntityLifecycleModule elm,
            NetworkEntityMap networkMap,
            INetworkIdAllocator idAllocator,
            int localNodeId,
            DisTypeExtractor? disTypeExtractor = null,
            Action<EntityRepository, Entity, bool>? onEntitySpawned = null)
        {
            _tkbDb            = tkbDb       ?? throw new ArgumentNullException(nameof(tkbDb));
            _elm              = elm         ?? throw new ArgumentNullException(nameof(elm));
            _networkMap       = networkMap  ?? throw new ArgumentNullException(nameof(networkMap));
            _idAllocator      = idAllocator ?? throw new ArgumentNullException(nameof(idAllocator));
            _localNodeId      = localNodeId;
            _disTypeExtractor = disTypeExtractor; // null → ExtractDisType returns 0
            _onEntitySpawned  = onEntitySpawned;
        }

        /// <inheritdoc />
        public void Execute(ISimulationView view, float deltaTime)
        {
            var world = view as EntityRepository;
            if (world == null) return;

            var cmdBuffer = view.GetCommandBuffer();

            foreach (var cmd in view.ConsumeManagedEvents<SpawnEntityCommand>())
                ProcessSpawn(world, view.Tick, cmd, cmdBuffer);

            foreach (var cmd in view.ConsumeManagedEvents<UpdateEntityCommand>())
                ProcessUpdate(world, cmd);

            foreach (var cmd in view.ConsumeManagedEvents<DestroyEntityCommand>())
                ProcessDestroy(world, view.Tick, cmd, cmdBuffer);
        }

        // ─── Spawn ────────────────────────────────────────────────────────────

        private void ProcessSpawn(EntityRepository world, uint tick,
            SpawnEntityCommand cmd, IEntityCommandBuffer cmdBuffer)
        {
            // 1. Resolve network ID (0 = allocate a new one)
            long networkId = cmd.NetworkId != 0 ? cmd.NetworkId : _idAllocator.AllocateId();
            FdpLog<NetworkSpawningSystem>.Debug(
                "[TRACE-SH] ECS: Spawned NetworkId={0} TkbType={1}", networkId, cmd.TkbType);

            // 2. Duplicate guard — silently drop if already spawned
            if (_networkMap.TryGetEntity(networkId, out _))
                return;

            // 3. Validate TKB type before creating the entity
            if (!_tkbDb.TryGetByType(cmd.TkbType, out var template))
            {
                Console.Error.WriteLine(
                    $"[NS] Unknown TkbType {cmd.TkbType} – spawn of network entity {networkId} skipped.");
                return;
            }

            // 4. Create ECS entity and apply TKB blueprint defaults
            var entity = world.CreateEntity();
            // Set lifecycle header immediately so queries that filter by Constructing
            // can find this entity even before all peer ACKs arrive.
            world.SetLifecycleState(entity, EntityLifecycle.Constructing);
            template.ApplyTo(world, entity);

            // 5. Core network components (order matches design doc §4.3)
            world.SetComponent(entity, new NetworkIdentity(networkId));
            world.SetComponent(entity, new NetworkOwnership
            {
                PrimaryOwnerId = cmd.OwnerNodeId,
                LocalNodeId    = _localNodeId
            });
            world.AddComponent(entity, new NetworkAuthority(cmd.OwnerNodeId, _localNodeId));

            // Signals NetworkGatewaySystem that this entity needs replication
            world.AddComponent(entity, new NetworkSpawnRequest
            {
                DisType = ExtractDisType(cmd.InitialComponents),
                OwnerId = (ulong)cmd.OwnerNodeId
            });

            // 7. Optional reliable-init handshake component
            if (cmd.InitType != ReliableInitType.None)
                world.AddComponent(entity, new PendingNetworkAck { ExpectedType = cmd.InitType });

            // 8. Apply caller-supplied component overrides on top of TKB defaults
            if (cmd.InitialComponents != null)
                foreach (var component in cmd.InitialComponents)
                    EntityComponentReflector.SetComponent(world, entity, component);

            bool isLocalAuthority = cmd.OwnerNodeId == _localNodeId;
            _onEntitySpawned?.Invoke(world, entity, isLocalAuthority);


            // 9. Register BEFORE starting lifecycle so any system that responds to
            //    ConstructionOrder can already resolve the entity via the map.
            _networkMap.Register(networkId, entity);

            // 10. ELM BeginConstruction — must be the very last call
            _elm.BeginConstruction(entity, cmd.TkbType, tick, cmdBuffer);
        }

        // ─── Update ───────────────────────────────────────────────────────────

        private void ProcessUpdate(EntityRepository world, UpdateEntityCommand cmd)
        {
            if (!_networkMap.TryGetEntity(cmd.NetworkId, out var entity))
            {
                Console.Error.WriteLine(
                    $"[NS] UpdateEntityCommand for unknown network entity {cmd.NetworkId} – ignored.");
                return;
            }

            if (cmd.ComponentsToUpdate == null) return;

            foreach (var component in cmd.ComponentsToUpdate)
                EntityComponentReflector.SetComponent(world, entity, component);
        }

        // ─── Destroy ──────────────────────────────────────────────────────────

        private void ProcessDestroy(EntityRepository world, uint tick,
            DestroyEntityCommand cmd, IEntityCommandBuffer cmdBuffer)
        {
            if (!_networkMap.TryGetEntity(cmd.NetworkId, out var entity))
            {
                Console.Error.WriteLine(
                    $"[NS] DestroyEntityCommand for unknown network entity {cmd.NetworkId} – ignored.");
                return;
            }

            // Mark TearDown in lifecycle state so queries / other systems see the intent
            // immediately (before ACK round-trip completes).
            cmdBuffer.SetLifecycleState(entity, EntityLifecycle.TearDown);

            _elm.BeginDestruction(entity, tick, cmd.Reason ?? "destroyed", cmdBuffer);
        }

        // ─── Helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Iterates <paramref name="components"/> and returns the DIS entity type found by
        /// the injected <see cref="DisTypeExtractor"/> delegate.
        /// Returns 0 if no extractor was supplied or no component matches.
        /// </summary>
        private ulong ExtractDisType(System.Collections.Generic.List<object>? components)
        {
            if (components == null || _disTypeExtractor == null)
                return 0;

            foreach (var comp in components)
            {
                if (_disTypeExtractor(comp, out ulong disType))
                    return disType;
            }

            return 0;
        }
    }
}
