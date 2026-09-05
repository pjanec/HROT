using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication;

namespace Fdp.Toolkit.NetworkSpawning.Systems
{
    /// <summary>
    /// Centralised system that replaces per-node entity-spawning boilerplate.
    /// Consumes <see cref="SpawnEntityCommand"/>, <see cref="UpdateEntityCommand"/>,
    /// and <see cref="DestroyEntityCommand"/> managed events each tick and drives
    /// the ECS + ELM lifecycle machinery.
    /// </summary>
    [UpdateInPhase(SystemPhase.BeforeSync)]
    public class NetworkSpawningSystem : IEcsModuleSystem
    {
        private readonly ITkbDatabase _tkbDb;
        private readonly EntityLifecycleModule _elm;
        private readonly NetworkEntityMap _networkMap;
        private readonly INetworkIdAllocator _idAllocator;
        private readonly int _localNodeId;
        private readonly IReadOnlyList<ITkbEntityTranslator> _translators;
        private readonly Action<EntityRepository, Entity, bool>? _onEntitySpawned;

        /// <param name="tkbDb">TKB template registry.</param>
        /// <param name="elm">Entity lifecycle module that manages construction/destruction handshakes.</param>
        /// <param name="networkMap">Entity ↔ network-ID registry.</param>
        /// <param name="idAllocator">Network-ID allocator (stub or DDS-backed).</param>
        /// <param name="translators">
        /// 🔴🔴 <b>The node's TKB→ECS projection list. Omitting it means "spawn entities with NO TKB
        /// template components at all" — not "spawn a default set".</b>
        ///
        /// <para>⚠⚠ <b>This parameter is optional and defaults to <c>Array.Empty</c>, which is a
        /// SILENT no-op:</b> step 4 of <c>ProcessSpawn</c> is <c>foreach (var t in _translators)
        /// t.Inject(...)</c> — the only writer of descriptor-derived components in this system — so an
        /// empty list turns it into a zero-iteration loop. The entity still gets its
        /// <c>NetworkIdentity</c>, <c>NetworkOwnership</c>, <c>TkbIdentity</c> and DIS header, so it
        /// looks spawned; it simply carries none of its type's kinematics, combat, perception,
        /// behaviour or presentation. 📌 Measured 2026-08-30 (<c>CE-138</c>): one host reached
        /// production this way.</para>
        ///
        /// <para>⛔⛔ <b>Do NOT use a short list to narrow what a host materialises.</b> That is not the
        /// narrowing lever. Every <see cref="ITkbEntityTranslator"/> is contractually required to guard
        /// each write with <c>repo.IsComponentTypeRegistered&lt;T&gt;()</c>, so a translator whose
        /// components this host never registered is already a no-op. ⇒ ⭐ <b>the per-host difference is
        /// the REGISTRATION SET; the translator list should be the node's full projection set.</b>
        /// A component the host does not want is excluded by not registering it, which fails loudly at
        /// one place, rather than by omitting a translator, which fails silently everywhere.</para>
        ///
        /// <para>⭐ Pass the SAME instance to <see cref="EntityLifecycleModule"/> and
        /// <c>GhostPromotionSystem</c> — <c>docs/designs/tkb-1/DESIGN.md</c> §6.5 calls that the node's
        /// "single point of truth".</para>
        /// </param>
        /// <param name="onEntitySpawned">
        /// Optional post-spawn hook: <c>(world, entity, isLocalAuthority)</c>, invoked after components
        /// and authority bits are set and before the entity is registered in the network map.
        /// </param>
        /// <param name="localNodeId">This node's logical ID, used to fill NetworkOwnership.</param>
        public NetworkSpawningSystem(
            ITkbDatabase tkbDb,
            EntityLifecycleModule elm,
            NetworkEntityMap networkMap,
            INetworkIdAllocator idAllocator,
            int localNodeId,
            IReadOnlyList<ITkbEntityTranslator>? translators = null,
            Action<EntityRepository, Entity, bool>? onEntitySpawned = null)
        {
            _tkbDb            = tkbDb       ?? throw new ArgumentNullException(nameof(tkbDb));
            _elm              = elm         ?? throw new ArgumentNullException(nameof(elm));
            _networkMap       = networkMap  ?? throw new ArgumentNullException(nameof(networkMap));
            _idAllocator      = idAllocator ?? throw new ArgumentNullException(nameof(idAllocator));
            _localNodeId      = localNodeId;
            _translators      = translators ?? System.Array.Empty<ITkbEntityTranslator>();
            _onEntitySpawned  = onEntitySpawned;
        }

        /// <inheritdoc />
        public void Execute(ISimulationView view, float deltaTime)
        {
            var world = view as EntityRepository;
            if (world == null) return;

            var cmdBuffer = view.GetCommandBuffer();

            foreach (var cmd in view.ReadManagedEvents<SpawnEntityCommand>())
                ProcessSpawn(world, view.Tick, cmd, cmdBuffer);

            foreach (var cmd in view.ReadManagedEvents<UpdateEntityCommand>())
                ProcessUpdate(world, cmd);

            foreach (var cmd in view.ReadManagedEvents<DestroyEntityCommand>())
                ProcessDestroy(world, view.Tick, cmd, cmdBuffer);
        }

        // ─── Spawn ────────────────────────────────────────────────────────────

        private void ProcessSpawn(EntityRepository world, uint tick,
            SpawnEntityCommand cmd, IEntityCommandBuffer cmdBuffer)
        {
            // 1. Resolve network ID (0 = allocate a new one)
            long networkId = cmd.NetworkId != 0 ? cmd.NetworkId : _idAllocator.AllocateId();
            FdpLog<NetworkSpawningSystem>.Debug(
                "[Node-{0}] ProcessSpawn: NetworkId={1} TkbType={2}", _localNodeId, networkId, cmd.TkbType);

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

            // 4. Create ECS entity and apply TKB blueprint defaults.
            // ⚠ This loop IS the "Apply TKB template components" step of the spawn flow in
            //   docs/projects/relationships/Hrot-Simulation-Pipeline.md §4.3, and it is the ONLY writer
            //   of descriptor-derived components in this method. With an empty _translators it is a
            //   zero-iteration loop and the entity is born with identity but no type — see the
            //   `translators` ctor doc.
            var entity = world.CreateEntity();
            // Set lifecycle header immediately so queries that filter by Constructing
            // can find this entity even before all peer ACKs arrive.
            world.SetLifecycleState(entity, EntityLifecycle.Constructing);
            foreach (var t in _translators)
                t.Inject(world, entity, template);

            // 5. Core network components (order matches design doc §4.3)
            world.SetComponent(entity, new NetworkIdentity(networkId));
            world.SetComponent(entity, new NetworkOwnership
            {
                PrimaryOwnerId = cmd.OwnerNodeId,
                LocalNodeId    = _localNodeId
            });
            world.AddComponent(entity, new NetworkAuthority(cmd.OwnerNodeId, _localNodeId));

            // Permanent identity component — lives on the entity forever and drives
            // the GhostPromotionSystem query on the receiver side.
            world.AddComponent(entity, new TkbIdentity { TkbType = cmd.TkbType });

            // Store the DIS entity type natively in the entity header so all systems
            // can access it without a component lookup. When the spawn command does not
            // carry an explicit DIS type (e.g. editor placement, or scenario load that
            // doesn't populate it), fall back to the TKB template's DIS type so the header
            // is always correctly stamped rather than left zero.
            ulong disValue = cmd.DisType != 0 ? cmd.DisType : template.DisType.Value;
            world.SetDisType(entity, new DISEntityType { Value = disValue });

            // 7. Optional reliable-init handshake component
            if (cmd.InitType != ReliableInitType.None)
                world.AddComponent(entity, new PendingNetworkAck { ExpectedType = cmd.InitType });

            // 7b. ⭐⭐⭐ D2 — a THROWAWAY entity is stamped so the scenario serializer skips it.
            //   Every node that materialises the entity runs this, so the sketch is excluded from the
            //   save on the SAVING node too -- which is the whole point: the creator (e.g. an IG) never
            //   answers the cluster-wide save, but its entity still replicates into worlds that do.
            //   ⛔ Derived HERE rather than replicated as component state: the decision must survive the
            //   authoring node disconnecting, and a receiver cannot resolve a departed node's role.
            //   📄 docs/DESIGN_Node_Roles_And_Policies.md §7.3 (R-140).
            if (cmd.IsTransient)
                world.AddComponent(entity, new Fdp.Toolkit.Scenario.ScenarioIgnoreTag());

            // 8. Apply caller-supplied component overrides on top of TKB defaults.
            // Fast path: explicitly typed fields, no boxing, no reflection.
            if (cmd.InitialTransform.HasValue)
                world.SetComponent(entity, cmd.InitialTransform.Value);
            if (cmd.InitialVelocity.HasValue)
                world.SetComponent(entity, cmd.InitialVelocity.Value);

            // Fallback: rare/managed components via reflection.
            if (cmd.InitialComponents != null)
                foreach (var component in cmd.InitialComponents)
                    EntityComponentReflector.SetComponent(world, entity, component);

            bool isLocalAuthority = cmd.OwnerNodeId == _localNodeId;
            if (isLocalAuthority)
            {
                // Locally spawned entities must start with authority bits enabled for
                // every component currently present on the entity.
                ref var compNS = ref world.GetComponentMask(entity.Index);
                ref var metaNS = ref world.GetMetadata(entity.Index);
                metaNS.AuthorityMask = compNS;
            }
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

    }
}
