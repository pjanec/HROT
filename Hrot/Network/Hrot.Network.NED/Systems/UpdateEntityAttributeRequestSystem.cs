using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Hrot.NED.Messages;
using Hrot.NED.Common;
using Fdp.Toolkit.Replication.Patching;
using CycloneDDS.Runtime;
using Fdp.Kernel.Logging;
using Fdp.Toolkit.Replication.Services;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Map.Common.Systems
{
    using NedStatusCode = Hrot.NED.Messages.NedStatusCode;

    /// <summary>
    /// Consumes <see cref="UpdateEntityAttributeRequest"/> messages and applies
    /// fine-grained field-level patches to live ECS components via
    /// <see cref="JsonAttributeCompiler"/> and <see cref="EcsPatchContext"/>.
    ///
    /// <para>
    /// Authority is checked at ECS component type ID level: a route entry is only
    /// dispatched when <c>EntityHeader.AuthorityMask.IsSet(componentId)</c> returns true.
    /// Components owned by other nodes are silently skipped (zero-alloc <c>reader.Skip()</c>).
    /// </para>
    ///
    /// <para>
    /// After all delegates fire, <see cref="EcsPatchContext.FlushDirtyMarks"/> is called
    /// to trigger targeted egress (ATTR-S5T3 / ATTR-§3.10). This bypasses coarse
    /// chunk-level ticks, guaranteeing per-entity egress precision.
    /// </para>
    ///
    /// <para><b>Silent bystander rule:</b> if this node did not apply any component
    /// mutations (either because it has no authority or the JSON matched no routes),
    /// no acknowledgment is sent regardless of <c>RequireAck</c>.</para>
    ///
    /// <para><b>Opt-in ACK:</b> a <see cref="CreateUpdateDeleteEntityAck"/> is sent only
    /// when the request specifies <c>RequireAck = true</c> AND this node applied at least
    /// one mutation.  The returned <c>OpaqueData</c> is a 256-bit bitmask where
    /// bit N encodes that ECS component type ID N was applied.</para>
    ///
    /// <para>
    /// Two constructors are provided:
    /// <list type="bullet">
    ///   <item>Interface constructor — used by unit tests via injectable stubs.</item>
    ///   <item>DDS constructor — used by production code; creates DDS adapters internally.</item>
    /// </list>
    /// </para>
    /// </summary>
    public sealed class UpdateEntityAttributeRequestSystem : ComponentSystem
    {
        private readonly IUpdateEntityAttributeRequestSource _requestSource;
        private readonly IUpdateEntityAttributeAckSink       _ackSink;
        private readonly NetworkEntityMap                    _entityMap;
        private readonly JsonAttributeCompiler?              _jsonCompiler;
        private readonly BinaryInterpreter<AttributeRecord>?            _binaryInterpreter;
        private readonly NodeId                              _localNodeId;

        // ── Interface-based constructor (test-friendly) ───────────────────────

        /// <summary>
        /// Creates a new system instance using injectable source and sink abstractions.
        /// </summary>
        /// <param name="requestSource">Source of incoming <see cref="UpdateEntityAttributeRequest"/> messages.</param>
        /// <param name="ackSink">Sink for writing <see cref="CreateUpdateDeleteEntityAck"/> responses.</param>
        /// <param name="entityMap">Shared net-ID → entity lookup service.</param>
        /// <param name="jsonAttributeCompiler">
        /// Optional zero-allocation JSON attribute compiler.  When non-null, incoming
        /// <c>AttributePatchJson</c> is applied to live ECS components.  When <c>null</c>,
        /// every request with <c>RequireAck=true</c> is acknowledged with <c>Success</c>
        /// without modifying ECS state.
        /// </param>
        /// <param name="localNodeId">
        /// This node's <see cref="NodeId"/>, embedded in every ACK as <c>RespondingNode</c>.
        /// Defaults to <c>default</c> (zero) when not provided.
        /// </param>
        /// <param name="binaryInterpreter">
        /// Optional <see cref="BinaryInterpreter"/> for applying <see cref="UpdateEntityAttributeRequest.AttributeRecords"/>
        /// binary attribute records. Processed instead of JSON when records are present.
        /// When <c>null</c>, binary records are ignored and the JSON path is used.
        /// </param>
        public UpdateEntityAttributeRequestSystem(
            IUpdateEntityAttributeRequestSource requestSource,
            IUpdateEntityAttributeAckSink       ackSink,
            NetworkEntityMap                    entityMap,
            JsonAttributeCompiler?              jsonAttributeCompiler = null,
            NodeId                              localNodeId = default,
            BinaryInterpreter<AttributeRecord>?          binaryInterpreter = null)
        {
            _requestSource     = requestSource ?? throw new ArgumentNullException(nameof(requestSource));
            _ackSink           = ackSink       ?? throw new ArgumentNullException(nameof(ackSink));
            _entityMap         = entityMap     ?? throw new ArgumentNullException(nameof(entityMap));
            _jsonCompiler      = jsonAttributeCompiler;
            _localNodeId       = localNodeId;
            _binaryInterpreter = binaryInterpreter;
        }

        // ── DDS-backed constructor (production) ───────────────────────────────

        /// <summary>
        /// Creates a new system instance that reads from and writes to CycloneDDS topics.
        /// </summary>
        /// <param name="participant">DDS participant used for topic subscriptions and publications.</param>
        /// <param name="entityMap">Shared net-ID → entity lookup service.</param>
        /// <param name="geoTransform">
        /// Retained for API compatibility; not used directly by this system.
        /// Geographic conversion is handled via the <paramref name="jsonAttributeCompiler"/>
        /// routing delegates registered at startup.
        /// </param>
        /// <param name="jsonAttributeCompiler">
        /// Optional zero-allocation JSON attribute compiler.
        /// </param>
        /// <param name="localNodeId">
        /// This node's <see cref="NodeId"/>, embedded in every ACK as <c>RespondingNode</c>.
        /// </param>
        public UpdateEntityAttributeRequestSystem(
            DdsParticipant        participant,
            NetworkEntityMap      entityMap,
            IGeographicTransform? geoTransform = null,
            JsonAttributeCompiler? jsonAttributeCompiler = null,
            NodeId                 localNodeId = default)
            : this(
                new DdsUpdateEntityAttributeRequestSource(participant),
                new DdsUpdateEntityAttributeAckSink(participant),
                entityMap,
                jsonAttributeCompiler,
                localNodeId)
        {
        }

        // ── ComponentSystem lifecycle ──────────────────────────────────────────

        /// <inheritdoc/>
        protected override void OnUpdate()
        {
            _requestSource.ProcessRequests(ProcessRequest);
        }

        /// <inheritdoc/>
        protected override void OnDestroy()
        {
            (_requestSource as IDisposable)?.Dispose();
            (_ackSink       as IDisposable)?.Dispose();
        }

        // ── Request handling ───────────────────────────────────────────────────

        private void ProcessRequest(UpdateEntityAttributeRequest req)
        {
            // 1. Resolve the entity from the network ID.
            if (!_entityMap.TryGetEntity((long)req.EntityId, out var entity))
            {
                FdpLog<UpdateEntityAttributeRequestSystem>.Warn(
                    "[UpdAttrReq] Entity {0} not found. RequestId={1}",
                    req.EntityId, req.RequestId);
                // Only ACK if the requester asked for one.
                if (req.RequireAck)
                    _ackSink.WriteErrorAck(req.RequestId, (int)NedStatusCode.EntityNotFound);
                return;
            }

            bool hasBinaryRecords = _binaryInterpreter != null
                && req.AttributeRecords != null
                && req.AttributeRecords.Count > 0;

            // 2. Binary path: apply AttributeRecords via BinaryInterpreter.
            if (hasBinaryRecords)
            {
                // Build a bare EcsPatchContext directly — independent of the JSON compiler.
                // Authority checks and component access work without a routing table;
                // FlushDirtyMarks is a no-op here because the binary installer flushers
                // drive SmartEgress themselves (BinaryInterpreter.Apply calls FlushDirtyMarks
                // at the end via IEntityPatchContext contract).
                var ecsPatchCtx  = EcsPatchContext.Create(World, entity);
                var binaryCtx    = _binaryInterpreter!.CreateContext(ecsPatchCtx);
                _binaryInterpreter.Apply(binaryCtx,
                    CollectionsMarshal.AsSpan(req.AttributeRecords));

                // SILENT BYSTANDER RULE — nothing applied, leave quietly.
                if (!ecsPatchCtx.HasAppliedAny)
                    return;

                // OPT-IN ACK.
                if (req.RequireAck)
                {
                    Span<byte> opaqueMask = stackalloc byte[32];
                    foreach (int compId in ecsPatchCtx.AppliedComponentIds)
                        opaqueMask[compId >> 3] |= (byte)(1 << (compId & 7));

                    _ackSink.WriteAck(req.RequestId, (int)NedStatusCode.Success, _localNodeId, opaqueMask);
                }
                return;
            }

            // 3. No compiler — acknowledge no-op only when explicitly requested.
            if (_jsonCompiler == null)
            {
                FdpLog<UpdateEntityAttributeRequestSystem>.Info(
                    "[UpdAttrReq] No JsonAttributeCompiler injected. Acknowledging no-op. " +
                    "EntityId={0}, RequestId={1}",
                    req.EntityId, req.RequestId);
                if (req.RequireAck)
                    _ackSink.WriteErrorAck(req.RequestId, (int)NedStatusCode.Success);
                return;
            }

            // 4. Build live-ECS patch context — component-level authority guard is wired
            //    into the compiler's Compile() loop via EcsPatchContext.HasAuthority.
            //    TODO ATTR-BATCH-03: investigate pooling EcsPatchContext for high-frequency updates.
            var context = _jsonCompiler.CreatePatchContext(World, entity);

            // 5. Stream the JSON patch through the routing table.
            //    Routes whose component ID is not authorised by this node are silently skipped
            //    (reader.Skip() — zero allocation, no ECS mutation).
            _jsonCompiler.Compile(req.AttributePatchJson, context);

            // 6. Flush per-entity dirty marks, bypassing chunk-level egress ticks.
            context.FlushDirtyMarks();

            // 7. SILENT BYSTANDER RULE — if this node applied nothing, leave quietly.
            if (!context.HasAppliedAny)
                return;

            // 8. OPT-IN ACK — send only when the requester asked for a response.
            if (req.RequireAck)
            {
                // Build 32-byte bitmask: bit N = ECS component type ID N was mutated.
                Span<byte> opaqueMask = stackalloc byte[32];
                foreach (int compId in context.AppliedComponentIds)
                    opaqueMask[compId >> 3] |= (byte)(1 << (compId & 7));

                _ackSink.WriteAck(req.RequestId, (int)NedStatusCode.Success, _localNodeId, opaqueMask);
            }
        }
    }

    // ── DDS-backed adapters (production-only) ─────────────────────────────────

    /// <summary>DDS-backed <see cref="IUpdateEntityAttributeRequestSource"/>.</summary>
    internal sealed class DdsUpdateEntityAttributeRequestSource
        : IUpdateEntityAttributeRequestSource, IDisposable
    {
        private readonly DdsReader<UpdateEntityAttributeRequest> _reader;

        public DdsUpdateEntityAttributeRequestSource(DdsParticipant participant)
            => _reader = new DdsReader<UpdateEntityAttributeRequest>(participant, "UpdateEntityAttributeRequest");

        public void ProcessRequests(Action<UpdateEntityAttributeRequest> processor)
        {
            using var loan = _reader.Take();
            foreach (var sample in loan)
                if (sample.IsValid)
                    processor(sample.Data);
        }

        public void Dispose() => _reader.Dispose();
    }

    /// <summary>DDS-backed <see cref="IUpdateEntityAttributeAckSink"/>.</summary>
    internal sealed class DdsUpdateEntityAttributeAckSink
        : IUpdateEntityAttributeAckSink, IDisposable
    {
        private readonly DdsWriter<CreateUpdateDeleteEntityAck> _writer;

        public DdsUpdateEntityAttributeAckSink(DdsParticipant participant)
            => _writer = new DdsWriter<CreateUpdateDeleteEntityAck>(participant, "CreateUpdateDeleteEntityAck");

        public void WriteAck(Guid requestId, int errorCode, NodeId respondingNode, ReadOnlySpan<byte> opaqueData)
            => _writer.Write(new CreateUpdateDeleteEntityAck
            {
                RequestId      = requestId,
                StatusCode     = errorCode,
                RespondingNode = respondingNode,
                OpaqueData     = opaqueData.ToArray(),
            });

        public void WriteErrorAck(Guid requestId, int errorCode)
            => _writer.Write(new CreateUpdateDeleteEntityAck
            {
                RequestId  = requestId,
                StatusCode = errorCode,
            });

        public void Dispose() => _writer.Dispose();
    }
}

