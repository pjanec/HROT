using System;
using System.Collections.Generic;
using Bagira.BDC.SSTM;
using Bagira.BDC.SSTD;
using Bagira.Map.Common.Replication.Utils;
using FDP.Kernel.Logging;
using Fdp.Interfaces;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using FDP.Toolkit.NetworkSpawning.Events;
using ModuleHost.Core.Abstractions;
using ModuleHost.Core.Network.Interfaces;

namespace Bagira.SimHost.Systems
{
    /// <summary>
    /// Handles <see cref="CreateEntityRequest"/> messages arriving over DDS.
    ///
    /// <para>
    /// On every <see cref="Execute"/> call the system:
    /// <list type="number">
    ///   <item>Drains the source via a zero-allocation callback (no <c>List</c> alloc on ingress).</item>
    ///   <item>Validates each request and allocates a network ID immediately.</item>
    ///   <item>Sends a <see cref="CreateEntityAck"/> right away so the IG client unblocks
    ///     with minimal latency regardless of how many entities are queued.</item>
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
    public class CreateEntityRequestSystem : IModuleSystem
    {
        /// <summary>Maximum <see cref="SpawnEntityCommand"/> events published per tick.</summary>
        public const int MaxRequestsPerTick = 500;

        private readonly ICreateEntityRequestSource _requestSource;
        private readonly ICreateEntityAckSink       _ackSink;
        private readonly ITkbDatabase               _tkbDb;
        private readonly INetworkIdAllocator        _idAllocator;
        private readonly IGeographicTransform?      _geoTransform;
        private readonly int                        _localNodeId;

        // Pre-validated requests waiting to be converted into SpawnEntityCommands.
        // Capacity pre-allocated to absorb large bursts without resizing.
        private readonly Queue<PendingRequest> _pendingQueue = new(capacity: MaxRequestsPerTick * 4);

        /// <param name="requestSource">Source of incoming CreateEntityRequest messages (DDS-backed or stub).</param>
        /// <param name="ackSink">Sink for CreateEntityAck responses (DDS-backed or stub).</param>
        /// <param name="tkbDb">TKB database used to validate that the requested entity type exists.</param>
        /// <param name="idAllocator">Allocator that produces unique network entity IDs.</param>
        /// <param name="localNodeId">This node's ID used as <c>OwnerNodeId</c> in SpawnEntityCommand.</param>
        /// <param name="geoTransform">
        /// Optional geographic transform for converting WGS84 GeoSpatial positions to local Cartesian.
        /// When <c>null</c>, GeoSpatial descriptors are included without a VehicleState override.
        /// </param>
        public CreateEntityRequestSystem(
            ICreateEntityRequestSource requestSource,
            ICreateEntityAckSink       ackSink,
            ITkbDatabase               tkbDb,
            INetworkIdAllocator        idAllocator,
            int                        localNodeId,
            IGeographicTransform?      geoTransform = null)
        {
            _requestSource = requestSource ?? throw new ArgumentNullException(nameof(requestSource));
            _ackSink       = ackSink       ?? throw new ArgumentNullException(nameof(ackSink));
            _tkbDb         = tkbDb         ?? throw new ArgumentNullException(nameof(tkbDb));
            _idAllocator   = idAllocator   ?? throw new ArgumentNullException(nameof(idAllocator));
            _localNodeId   = localNodeId;
            _geoTransform  = geoTransform;
        }

        /// <summary>Number of requests currently buffered and awaiting spawn commands.</summary>
        public int PendingQueueCount => _pendingQueue.Count;

        /// <inheritdoc />
        public void Execute(ISimulationView view, float deltaTime)
        {
            // ── Phase 1: Drain all incoming requests this frame ───────────────
            // The callback fires synchronously for each valid DDS sample so no
            // List<CreateEntityRequest> is ever allocated on ingress (GC03).
            // Each valid request is validated, ID-allocated, ACK'd, and enqueued
            // on the same frame it arrives, giving the requester minimum latency (GC04).
            _requestSource.ProcessRequests(request =>
            {
                try
                {
                    // Validate TkbType presence.
                    long tkbType = DescriptorMapper.ExtractTkbType(
                        request.InitialDescriptors, out ulong disType);
                    if (tkbType == 0)
                    {
                        FdpLog<CreateEntityRequestSystem>.Warn(
                            $"[SimHost] CreateEntity {request.RequestId}: No EntityMaster descriptor or TkbType=0. Rejecting.");
                        SendErrorAck(request.RequestId, errorCode: 400);
                        return;
                    }

                    // Validate TkbType exists in the database.
                    if (!_tkbDb.TryGetByType(tkbType, out _))
                    {
                        FdpLog<CreateEntityRequestSystem>.Warn(
                            $"[SimHost] CreateEntity {request.RequestId}: TkbType={tkbType} not found. Rejecting.");
                        SendErrorAck(request.RequestId, errorCode: 404);
                        return;
                    }

                    // Allocate a network ID and immediately ACK — client unblocks now.
                    long newNetworkId = _idAllocator.AllocateId();
                    _ackSink.WriteAck(new CreateEntityAck
                    {
                        RequestId   = request.RequestId,
                        NewEntityId = (int)newNetworkId,
                        ErrorCode   = 0,
                    });

                    _pendingQueue.Enqueue(new PendingRequest
                    {
                        Request   = request,
                        NetworkId = newNetworkId,
                        TkbType   = tkbType,
                        DisType   = disType,
                    });
                }
                catch (Exception ex)
                {
                    FdpLog<CreateEntityRequestSystem>.Error(
                        $"[SimHost] CreateEntity ingress failed for request {request.RequestId}: {ex.Message}");
                    SendErrorAck(request.RequestId, errorCode: 500);
                }
            });

            // ── Phase 2: Time-sliced spawn dispatch ────────────────────────────
            // Convert at most MaxRequestsPerTick buffered requests into
            // SpawnEntityCommand events this tick to stay within the frame budget.
            int toProcess = Math.Min(_pendingQueue.Count, MaxRequestsPerTick);
            for (int i = 0; i < toProcess; i++)
                ProcessPendingRequest(view, _pendingQueue.Dequeue());
        }

        // ─── Private helpers ─────────────────────────────────────────────────

        private void ProcessPendingRequest(ISimulationView view, in PendingRequest pending)
        {
            try
            {
                // 1. Map descriptors → ECS component list.
                List<object> allComponents =
                    DescriptorMapper.MapToComponents(pending.Request.InitialDescriptors, _geoTransform);

                // 2. JSON attribute patches in InitialAttributesJson are applied by
                //    JsonAttributeCompiler (ATTR-S5T2). Not yet implemented.

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

                // 4. Publish SpawnEntityCommand — NetworkSpawningSystem handles all ECS work.
                if (view is EntityRepository repo)
                {
                    repo.Bus.PublishManaged(new SpawnEntityCommand
                    {
                        NetworkId         = pending.NetworkId,
                        TkbType           = pending.TkbType,
                        OwnerNodeId       = _localNodeId,
                        DisType           = pending.DisType,
                        InitType          = ReliableInitType.AllPeers,
                        InitialTransform  = initialTransform,
                        InitialVelocity   = initialVelocity,
                        InitialComponents = fallbackComponents,
                        RequestId         = pending.Request.RequestId,
                    });
                    // 5. Automatically spawn child entities if defined in the TKB template.
                    if (_tkbDb.TryGetByType(pending.TkbType, out var parentTemplate) && parentTemplate.ChildBlueprints.Count > 0)
                    {
                        Bagira.IG.Components.IgEntityData parentInfo = null;
                        if (fallbackComponents != null)
                        {
                            foreach (var comp in fallbackComponents)
                            {
                                if (comp is Bagira.IG.Components.IgEntityData info)
                                {
                                    parentInfo = info;
                                    break; // Found the metadata component
                                }
                            }
                        }

                        foreach (var childDef in parentTemplate.ChildBlueprints)
                        {
                            long childNetworkId = _idAllocator.AllocateId();
                            
                            // Try to get child template to retrieve its DisType
                            ulong childDisType = 0;
                            if (_tkbDb.TryGetByType(childDef.ChildTkbType, out var childTemplate))
                            {
                                childDisType = childTemplate.DisType.Value;
                            }

                            var childComponents = new List<object>();
                            if (parentInfo != null)
                            {
                                childComponents.Add(new Bagira.IG.Components.IgEntityData
                                {
                                    Name = $"{parentInfo.Name}-{childDef.InstanceId}",
                                    ForceId = parentInfo.ForceId,
                                    CommanderId = (int)pending.NetworkId
                                });
                            }

                            repo.Bus.PublishManaged(new SpawnEntityCommand
                            {
                                NetworkId         = childNetworkId,
                                TkbType           = childDef.ChildTkbType,
                                OwnerNodeId       = _localNodeId,
                                DisType           = childDisType,
                                InitType          = ReliableInitType.AllPeers,
                                InitialTransform  = initialTransform, // Spawn at parent pos
                                InitialVelocity   = initialVelocity,
                                InitialComponents = childComponents.Count > 0 ? childComponents : null,
                                RequestId         = Guid.NewGuid(), // Separate trace ID for child
                            });
                        }
                    }                }

                FdpLog<CreateEntityRequestSystem>.Info(
                    $"[SimHost] Queued spawn entity {pending.NetworkId} (TkbType={pending.TkbType}) " +
                    $"for request {pending.Request.RequestId}.");
            }
            catch (Exception ex)
            {
                FdpLog<CreateEntityRequestSystem>.Error(
                    $"[SimHost] SpawnEntityCommand creation failed for request " +
                    $"{pending.Request.RequestId}: {ex.Message}");
            }
        }

        private void SendErrorAck(Guid requestId, int errorCode)
        {
            _ackSink.WriteAck(new CreateEntityAck
            {
                RequestId   = requestId,
                NewEntityId = 0,
                ErrorCode   = errorCode,
            });
        }

        // ─── Inner types ─────────────────────────────────────────────────────

        /// <summary>Holds pre-validated spawn data waiting in the time-slice queue.</summary>
        private struct PendingRequest
        {
            public CreateEntityRequest Request;
            public long                NetworkId;
            public long                TkbType;
            public ulong               DisType;
        }
    }
}
