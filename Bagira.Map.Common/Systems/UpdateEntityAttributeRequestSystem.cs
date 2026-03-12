using System;
using Bagira.BDC.SSTM;
using Bagira.Map.Common.Replication.Utils;
using CycloneDDS.Runtime;
using FDP.Kernel.Logging;
using FDP.Toolkit.Replication.Services;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using ModuleHost.Core.Abstractions;

namespace Bagira.Map.Common.Systems
{
    using SstErrorCode = Bagira.BDC.SSTM.SstErrorCode;

    /// <summary>
    /// Consumes <see cref="UpdateEntityAttributeRequest"/> messages and applies
    /// fine-grained field-level patches to live ECS components via
    /// <see cref="JsonAttributeCompiler"/> and <see cref="EcsPatchContext"/>.
    ///
    /// <para>
    /// After all delegates fire, <see cref="EcsPatchContext.FlushDirtyMarks"/> is called
    /// to trigger targeted egress (ATTR-S5T3 / ATTR-§3.10). This bypasses coarse
    /// chunk-level ticks, guaranteeing per-entity egress precision.
    /// </para>
    ///
    /// <para>
    /// Two constructors are provided:
    /// <list type="bullet">
    ///   <item>Interface constructor — used by unit tests via injectable stubs.</item>
    ///   <item>DDS constructor — used by production code; creates DDS adapters internally.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// A <see cref="CreateUpdateDeleteEntityAck"/> is always written for every processed sample
    /// so the originating IG can correlate the response.
    /// </para>
    /// </summary>
    public sealed class UpdateEntityAttributeRequestSystem : ComponentSystem
    {
        private readonly IUpdateEntityAttributeRequestSource _requestSource;
        private readonly IUpdateEntityAttributeAckSink       _ackSink;
        private readonly NetworkEntityMap                    _entityMap;
        private readonly JsonAttributeCompiler?              _jsonCompiler;

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
        /// every request is acknowledged with <c>Success</c> without modifying ECS state.
        /// </param>
        public UpdateEntityAttributeRequestSystem(
            IUpdateEntityAttributeRequestSource requestSource,
            IUpdateEntityAttributeAckSink       ackSink,
            NetworkEntityMap                    entityMap,
            JsonAttributeCompiler?              jsonAttributeCompiler = null)
        {
            _requestSource = requestSource ?? throw new ArgumentNullException(nameof(requestSource));
            _ackSink       = ackSink       ?? throw new ArgumentNullException(nameof(ackSink));
            _entityMap     = entityMap     ?? throw new ArgumentNullException(nameof(entityMap));
            _jsonCompiler  = jsonAttributeCompiler;
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
        public UpdateEntityAttributeRequestSystem(
            DdsParticipant        participant,
            NetworkEntityMap      entityMap,
            IGeographicTransform? geoTransform = null,
            JsonAttributeCompiler? jsonAttributeCompiler = null)
            : this(
                new DdsUpdateEntityAttributeRequestSource(participant),
                new DdsUpdateEntityAttributeAckSink(participant),
                entityMap,
                jsonAttributeCompiler)
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
                _ackSink.WriteAck(req.RequestId, (int)SstErrorCode.EntityNotFound);
                return;
            }

            // 2. Apply JSON attribute patch via zero-allocation compiler (ATTR-S5T3).
            if (_jsonCompiler != null)
            {
                // 2a. Build live-ECS patch context for this entity.
                // TODO ATTR-BATCH-03: CreatePatchContext allocates a new EcsPatchContext and an inner HashSet<long>.
                // If high-frequency attribute updates (e.g. physics) are introduced, investigate pooling this context.
                var context = _jsonCompiler.CreatePatchContext(World, entity);

                // 2b. Stream the JSON patch through the routing table.
                _jsonCompiler.Compile(req.AttributePatchJson, context);

                // 2c. Flush per-entity dirty marks, bypassing chunk-level egress ticks.
                // TODO ATTR-BATCH-03: Consider adding [MustDisposeResource] and making EcsPatchContext IDisposable
                // to statically enforce that FlushDirtyMarks is always called before the context goes out of scope.
                context.FlushDirtyMarks();
            }
            else
            {
                FdpLog<UpdateEntityAttributeRequestSystem>.Info(
                    "[UpdAttrReq] No JsonAttributeCompiler injected. Acknowledging no-op. " +
                    "EntityId={0}, RequestId={1}",
                    req.EntityId, req.RequestId);
            }

            _ackSink.WriteAck(req.RequestId, (int)SstErrorCode.Success);
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

        public void WriteAck(Guid requestId, int errorCode)
            => _writer.Write(new CreateUpdateDeleteEntityAck
            {
                RequestId = requestId,
                ErrorCode = errorCode,
            });

        public void Dispose() => _writer.Dispose();
    }
}

