using System;
using System.Collections.Generic;
using Hrot.Core.Network;
using Fdp.Toolkit.Replication.Patching;
using Fdp.Core.Logging;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Network.Interfaces;

namespace Hrot.CGF.Systems
{
    /// <summary>
    /// Handles entity creation requests arriving from the network (protocol-neutral).
    ///
    /// <para>
    /// On every <see cref="Execute"/> call the system:
    /// <list type="number">
    ///   <item>Applies the Level-1 routing guard (<see cref="_isDefaultProcessor"/>) to prevent
    ///     cluster-wide broadcast storms where multiple nodes attempt to process the same default request.</item>
    ///   <item>Drains requests via a zero-allocation callback (no list alloc on ingress).</item>
    ///   <item>Validates each request and allocates a network ID immediately.</item>
    ///   <item>Sends a Phase 1 InProgress ACK right away so the ExCon client unblocks with minimal latency.</item>
    ///   <item>Registers the request with <see cref="EntityRequestFinalizationSystem"/> for Phase 2 tracking.</item>
    ///   <item>Enqueues the pre-validated data for time-sliced processing.</item>
    ///   <item>Pops up to <see cref="MaxRequestsPerTick"/> items and publishes
    ///     <see cref="SpawnEntityCommand"/> events for <c>NetworkSpawningSystem</c>.</item>
    /// </list>
    /// </para>
    ///
    /// Design constraint: this system is a thin translator.
    /// It must NOT call CreateEntity, ApplyTo, or BeginConstruction directly —
    /// all ECS spawning is delegated to NetworkSpawningSystem via the event bus.
    /// </summary>
    [UpdateInPhase(SystemPhase.Input)]
    public class CreateEntityRequestSystem : IEcsModuleSystem
    {
        /// <summary>Maximum <see cref="SpawnEntityCommand"/> events published per tick.</summary>
        public const int MaxRequestsPerTick = 500;

        private readonly IEntityCreationRequestSource       _requestSource;
        private readonly IEntityAckSink                     _ackSink;
        private readonly ITkbDatabase                       _tkbDb;
        private readonly INetworkIdAllocator                _idAllocator;
        private readonly int                                _localNodeId;
        private readonly bool                               _isDefaultProcessor;
        private readonly JsonAttributeCompiler?             _jsonCompiler;
        private readonly EntityRequestFinalizationSystem?      _finalizationSystem;
        private readonly IOwnershipDistributionStrategy?    _ownershipStrategy;

        /// <summary>
        /// Reusable <see cref="ListPatchContext"/> instance. Reset per entity-creation call
        /// to avoid allocating a new instance (and two internal dictionaries) for every
        /// entity in a burst spawn, eliminating per-entity Gen0 GC pressure.
        /// </summary>
        private ListPatchContext? _reusablePatchContext;

        // Pre-validated requests waiting to be converted into SpawnEntityCommands.
        // Capacity pre-allocated to absorb large bursts without resizing.
        private readonly Queue<PendingRequest> _pendingQueue = new(capacity: MaxRequestsPerTick * 4);

        /// <param name="requestSource">Source of incoming entity-creation requests.</param>
        /// <param name="ackSink">Sink for entity lifecycle ACK messages.</param>
        /// <param name="tkbDb">TKB database used to validate that the requested entity type exists.</param>
        /// <param name="idAllocator">Allocator that produces unique network entity IDs.</param>
        /// <param name="localNodeId">This node's ID used as <c>OwnerNodeId</c> in SpawnEntityCommand.</param>
        /// <param name="isDefaultProcessor">
        ///   When <c>true</c> this node intercepts broadcast requests where <c>Owner == 0</c>
        ///   (no explicit target node).  Exactly one node in the cluster must set this to <c>true</c>
        ///   to avoid duplicate ID allocation.  The Brain (CGF) node is always the default processor;
        ///   Muscle (SimHost) nodes must set this to <c>false</c>.
        /// </param>
        /// <param name="jsonAttributeCompiler">
        /// Optional <see cref="JsonAttributeCompiler"/> for applying <c>InitialAttributesJson</c>
        /// overrides after descriptors have been mapped to components.
        /// When <c>null</c>, <c>InitialAttributesJson</c> is ignored.
        /// </param>
        /// <param name="finalizationSystem">
        /// Optional <see cref="EntityRequestFinalizationSystem"/> for Two-ACK lifecycle tracking.
        /// When provided, the system registers each creation request for Phase 2 ACK dispatch.
        /// </param>
        /// <param name="ownershipStrategy">
        ///   Optional <see cref="IOwnershipDistributionStrategy"/> used by the default processor
        ///   to build the pre-genesis <c>DeferredTakeOwnership</c> routing table.
        ///   When <c>null</c>, the default processor retains full ownership of all descriptors.
        /// </param>
        public CreateEntityRequestSystem(
            IEntityCreationRequestSource        requestSource,
            IEntityAckSink                      ackSink,
            ITkbDatabase                        tkbDb,
            INetworkIdAllocator                 idAllocator,
            int                                 localNodeId,
            JsonAttributeCompiler?              jsonAttributeCompiler = null,
            EntityRequestFinalizationSystem?       finalizationSystem    = null,
            bool                                isDefaultProcessor    = false,
            IOwnershipDistributionStrategy?     ownershipStrategy     = null)
        {
            _requestSource      = requestSource  ?? throw new ArgumentNullException(nameof(requestSource));
            _ackSink            = ackSink        ?? throw new ArgumentNullException(nameof(ackSink));
            _tkbDb              = tkbDb          ?? throw new ArgumentNullException(nameof(tkbDb));
            _idAllocator        = idAllocator    ?? throw new ArgumentNullException(nameof(idAllocator));
            _localNodeId        = localNodeId;
            _isDefaultProcessor = isDefaultProcessor;
            _jsonCompiler       = jsonAttributeCompiler;
            _finalizationSystem = finalizationSystem;
            _ownershipStrategy  = ownershipStrategy;
            _processRequestDelegate = ProcessIncomingRequest;
        }

        /// <summary>Number of requests currently buffered and awaiting spawn commands.</summary>
        public int PendingQueueCount => _pendingQueue.Count;

        // Cached delegate for ProcessIncomingRequest — avoids a per-tick lambda allocation.
        private readonly Action<EntityCreationRequest> _processRequestDelegate;

        /// <inheritdoc />
        public void Execute(ISimulationView view, float deltaTime)
        {
            // ── Phase 1: Drain all incoming requests this frame ───────────────
            // The callback fires synchronously for each valid network sample so no
            // per-sample heap allocation occurs on ingress (GC03).
            // Each valid request is validated, ID-allocated, ACK'd, and enqueued
            // on the same frame it arrives, giving the requester minimum latency (GC04).
            _requestSource.ProcessRequests(_processRequestDelegate);

            // ── Phase 2: Time-sliced spawn dispatch ────────────────────────────
            // Convert at most MaxRequestsPerTick buffered requests into
            // SpawnEntityCommand events this tick to stay within the frame budget.
            int toProcess = Math.Min(_pendingQueue.Count, MaxRequestsPerTick);
            for (int i = 0; i < toProcess; i++)
                ProcessPendingRequest(view, _pendingQueue.Dequeue());
        }

        /// <summary>
        /// Processes a single incoming entity-creation request during the ingress phase.
        /// Extracted from the inline lambda so the delegate instance can be cached once and
        /// reused every tick (eliminates continuous Gen0 GC pressure from lambda captures).
        /// </summary>
        private void ProcessIncomingRequest(EntityCreationRequest request)
        {
            // ── Level-1 routing guard ──────────────────────────────────────────
            // If the request specifies an explicit target node, only that node processes it.
            // If the target is 0 (broadcast / "any default"), only the designated default
            // processor intercepts it — all other nodes drop the packet silently to prevent
            // duplicate ID allocation and cluster-wide race conditions.
            int targetNodeId      = request.OwnerAppInstanceId;
            bool isTargetedAtMe   = targetNodeId == _localNodeId;
            bool isDefaultRequest = targetNodeId == 0;

            if (!isTargetedAtMe && !(isDefaultRequest && _isDefaultProcessor))
                return; // Not our responsibility — silently ignore.

            try
            {
                // Validate TkbType (already extracted by the adapter).
                if (request.TkbType == 0)
                {
                    FdpLog<CreateEntityRequestSystem>.Warn(
                        $"[Node-{_localNodeId}] CreateEntity {request.RequestId}: No EntityMaster descriptor or TkbType=0. Rejecting.");
                    _ackSink.WriteAck(request.RequestId, 0, EntityOperationStatus.UnknownDescriptorType);
                    return;
                }

                // Validate TkbType exists in the database.
                if (!_tkbDb.TryGetByType(request.TkbType, out _))
                {
                    FdpLog<CreateEntityRequestSystem>.Warn(
                        $"[Node-{_localNodeId}] CreateEntity {request.RequestId}: TkbType={request.TkbType} not found. Rejecting.");
                    _ackSink.WriteAck(request.RequestId, 0, EntityOperationStatus.UnknownDescriptorType);
                    return;
                }

                // Allocate a network ID and immediately send Phase 1 ACK (InProgress) — client unblocks now.
                long newNetworkId = _idAllocator.AllocateId();
                _ackSink.WriteAck(request.RequestId, newNetworkId, EntityOperationStatus.InProgress);

                // Register for Phase 2 ACK dispatch once ELM confirms lifecycle.
                _finalizationSystem?.Track(newNetworkId, request.RequestId, RequestKind.Create);

                _pendingQueue.Enqueue(new PendingRequest
                {
                    Request   = request,
                    NetworkId = newNetworkId,
                    TkbType   = request.TkbType,
                    DisType   = request.DisType,
                });
            }
            catch (Exception ex)
            {
                FdpLog<CreateEntityRequestSystem>.Error(
                    $"[Node-{_localNodeId}] CreateEntity ingress failed for request {request.RequestId}: {ex.Message}");
                _ackSink.WriteAck(request.RequestId, 0, EntityOperationStatus.UnknownDescriptorType);
            }
        }

        // ─── Private helpers ─────────────────────────────────────────────────

        private void ProcessPendingRequest(ISimulationView view, in PendingRequest pending)
        {
            try
            {
                // 1. Seed component list from descriptor-converted components (if any).
                //    These are populated by the network adapter (e.g. NedEntityCreationRequestSource)
                //    via DescriptorMapper and represent the base state from the wire message.
                List<object> allComponents = pending.Request.InitialComponents != null
                    ? new List<object>(pending.Request.InitialComponents)
                    : new List<object>();

                // 2. Apply JSON attribute patches from InitialAttributesJson (ATTR-S5T2).
                //    Patches are applied AFTER descriptor-based defaults so fine-grained
                //    overrides win over template values.
                if (_jsonCompiler != null && !string.IsNullOrEmpty(pending.Request.InitialAttributesJson))
                {
                    // Reuse the single cached context (Reset clears slots without releasing
                    // the underlying Dictionary objects, eliminating per-entity heap traffic).
                    _reusablePatchContext ??= new ListPatchContext(null);
                    _reusablePatchContext.Reset(allComponents);
                    _jsonCompiler.Compile(pending.Request.InitialAttributesJson, _reusablePatchContext);
                    allComponents = _reusablePatchContext.FlushComponents();
                }

                // 3. Separate unmanaged structs into explicit typed SpawnEntityCommand fields
                //    to avoid boxing them as List<object> items on the LOH (GC02).
                SimTransform? initialTransform = null;
                SimVelocity?  initialVelocity  = null;
                List<object>? fallbackComponents = null;

                foreach (var comp in allComponents)
                {
                    if (comp is SimTransform st) { initialTransform = st; continue; }
                    if (comp is SimVelocity  sv) { initialVelocity  = sv; continue; }

                    // All other (managed or rare) components go into the fallback list.
                    fallbackComponents ??= new List<object>();
                    fallbackComponents.Add(comp);
                }

                // 3.5. Ensure parent has IgEntityData if it's an auto-spawn composite
                Hrot.IG.Components.EntityInfo? parentInfo = null;
                int parentInfoIdx = -1;
                if (_tkbDb.TryGetByType(pending.TkbType, out var parentTemplate) && parentTemplate.ChildBlueprints.Count > 0)
                {
                    if (fallbackComponents != null)
                    {
                        for (int fi = 0; fi < fallbackComponents.Count; fi++)
                        {
                            if (fallbackComponents[fi] is Hrot.IG.Components.EntityInfo info)
                            {
                                parentInfo = info;
                                parentInfoIdx = fi;
                                break; // Found the metadata component
                            }
                        }
                    }

                    // If the request didn't patch any metadata, ensure the parent has default metadata
                    // so it's visible in the ORBAT tree and can be tracked.
                    if (parentInfo == null)
                    {
                        var newInfo = new Hrot.IG.Components.EntityInfo
                        {
                            Name = parentTemplate.Name,
                            ForceId = Hrot.IG.Components.ForceId.Unknown
                        };
                        fallbackComponents ??= new List<object>();
                        fallbackComponents.Add(newInfo);
                        parentInfo = newInfo;
                    }
                    else if (parentInfo.Value.Name.IsEmpty)
                    {
                        // Ensure parent has a valid name; update both the local copy and the list entry.
                        var updated = parentInfo.Value;
                        updated.Name = parentTemplate.Name;
                        fallbackComponents![parentInfoIdx] = updated;
                        parentInfo = updated;
                    }
                }

                // 4. Publish SpawnEntityCommand — NetworkSpawningSystem handles all ECS work.
                if (view is EntityRepository repo)
                {
                    // CQRS fix: if the request explicitly names an owner node, honour it.
                    // If Owner == 0 the default processor (this node) claims ownership.
                    int assignedOwner = (pending.Request.OwnerAppInstanceId == 0 || pending.Request.OwnerAppInstanceId == _localNodeId)
                        ? _localNodeId
                        : pending.Request.OwnerAppInstanceId;

                    repo.Bus.PublishManaged(new SpawnEntityCommand
                    {
                        NetworkId         = pending.NetworkId,
                        TkbType           = pending.TkbType,
                        OwnerNodeId       = assignedOwner,
                        DisType           = pending.DisType,
                        InitType          = ReliableInitType.AllPeers,
                        InitialTransform  = initialTransform,
                        InitialVelocity   = initialVelocity,
                        InitialComponents = fallbackComponents,
                        RequestId         = pending.Request.RequestId,
                    });

                    // 4b  When this node is the default processor it MUST broadcast the
                    //     pre-genesis routing table BEFORE the EntityMaster is published
                    //     (strict egress ordering per Rule 1).  The routing table is built
                    //     by the injected IOwnershipDistributionStrategy.
                    if (_isDefaultProcessor && _ownershipStrategy != null)
                    {
                        var grants = BuildOwnershipGrants(pending, assignedOwner);
                        if (grants.Count > 0)
                        {
                            var dtoCmd = new Fdp.Toolkit.NetworkSpawning.Events.DeferredTakeOwnershipCommand
                            {
                                NetworkId = pending.NetworkId,
                            };
                            dtoCmd.Grants.AddRange(grants);
                            repo.Bus.PublishManaged(dtoCmd);
                        }
                    }
                    // 5. Automatically spawn child entities if defined in the TKB template.
                    if (parentTemplate != null && parentTemplate.ChildBlueprints.Count > 0)
                    {
                        foreach (var childDef in parentTemplate.ChildBlueprints)
                        {
                            long childNetworkId = _idAllocator.AllocateId();
                            
                            // Try to get child template to retrieve its DisType
                            ulong childDisType = 0;
                            if (_tkbDb.TryGetByType(childDef.ChildTkbType, out var childTemplate))
                            {
                                childDisType = childTemplate.DisType.Value;
                            }

                            var childComponents = new List<object>
                            {
                                new Hrot.IG.Components.EntityInfo
                                {
                                    Name = $"{parentInfo!.Value.Name}-{childDef.InstanceId}",
                                    ForceId = parentInfo.Value.ForceId,
                                    CommanderId = (int)pending.NetworkId
                                }
                            };

                            repo.Bus.PublishManaged(new SpawnEntityCommand
                            {
                                NetworkId         = childNetworkId,
                                TkbType           = childDef.ChildTkbType,
                                OwnerNodeId       = assignedOwner,
                                DisType           = childDisType,
                                InitType          = ReliableInitType.AllPeers,
                                InitialTransform  = initialTransform, // Spawn at parent pos
                                InitialVelocity   = initialVelocity,
                                InitialComponents = childComponents.Count > 0 ? childComponents : null,
                                RequestId         = Guid.NewGuid(), // Separate trace ID for child
                            });
                        }
                    }
                }

                FdpLog<CreateEntityRequestSystem>.Info(
                    $"[Node-{_localNodeId}] Queued spawn entity {pending.NetworkId} (TkbType={pending.TkbType}) " +
                    $"for request {pending.Request.RequestId}.");
            }
            catch (Exception ex)
            {
                FdpLog<CreateEntityRequestSystem>.Error(
                    $"[Node-{_localNodeId}] SpawnEntityCommand creation failed for request " +
                    $"{pending.Request.RequestId}: {ex.Message}");
            }
        }

        /// <summary>
        /// Builds the list of descriptor grants that should be delegated to non-local
        /// nodes, according to the injected <see cref="_ownershipStrategy"/>.
        ///
        /// <para>Each known descriptor ordinal is evaluated; if the strategy returns a target
        /// node different from this node, a <see cref="DescriptorGrant"/> entry is added.</para>
        /// </summary>
        private List<DescriptorGrant> BuildOwnershipGrants(in PendingRequest pending, int assignedOwner)
        {
            var grants = new List<DescriptorGrant>();
            if (_ownershipStrategy == null) return grants;

            var disType = new Fdp.Core.DISEntityType { Value = pending.DisType };

            // Evaluate the strategy for every descriptor ordinal the Muscle node may own.
            foreach (long ordinal in new[]
            {
                DescriptorTypeOrdinals.WorldPos,
            })
            {
                int? targetNode = _ownershipStrategy.GetInitialOwner(
                    ordinal, disType, assignedOwner, instanceId: 0);

                if (targetNode.HasValue && targetNode.Value != _localNodeId)
                    grants.Add(new DescriptorGrant { DescriptorTypeId = ordinal, NodeId = targetNode.Value });
            }
            return grants;
        }

        // ─── Inner types ─────────────────────────────────────────────────────

        /// <summary>Holds pre-validated spawn data waiting in the time-slice queue.</summary>
        private struct PendingRequest
        {
            public EntityCreationRequest Request;
            public long                  NetworkId;
            public long                  TkbType;
            public ulong                 DisType;
        }
    }
}

