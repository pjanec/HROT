using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Hrot.NED.Messages;
using Hrot.NED.Common;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Replication.Patching;
using Hrot.SimHost.Installers;
using CycloneDDS.Runtime;
using Fdp.Core.Logging;
using Fdp.Toolkit.Replication.Services;
using Fdp.Core;
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
    [UpdateInPhase(SystemPhase.Input)]
    public sealed class UpdateEntityAttributeRequestSystem : IEcsModuleSystem, IDisposable
    {
        private readonly IUpdateEntityAttributeRequestSource _requestSource;
        private readonly IUpdateEntityAttributeAckSink       _ackSink;
        private readonly NetworkEntityMap                    _entityMap;
        private JsonAttributeCompiler?                       _jsonCompiler;
        private BinaryInterpreter<EntityAttributeChange>?               _binaryInterpreter;
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
            BinaryInterpreter<EntityAttributeChange>?    binaryInterpreter = null)
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
        /// ⭐⭐⭐ <b><c>AX-012</c> — this is now USED: it builds the BINARY interpreter.</b>
        ///
        /// <para>⚠ It previously said *"retained for API compatibility; not used directly by this
        /// system"*, and that was true — which is exactly how the binary arm came to be dead in
        /// production.</para>
        /// </param>
        /// <param name="jsonAttributeCompiler">
        /// ⭐⭐ Optional override. ⚠ <see langword="null"/> no longer means *"no JSON arm"* — it means
        /// *"build the standard one"*, exactly as the binary arm does. See the constructor's remarks.
        /// </param>
        /// <param name="localNodeId">
        /// This node's <see cref="NodeId"/>, embedded in every ACK as <c>RespondingNode</c>.
        /// </param>
        // ────────────────────────────────────────────────────────────────────────────────────────────
        // ⭐⭐⭐ AX-014 — BOTH ARMS ARE SOURCED THE SAME WAY, and the inconsistency was mine.
        //
        // 🔴 AX-012 fixed the dead binary arm by having this constructor BUILD its interpreter — while the
        //    JSON arm stayed PASSED IN by the factory. ⇒ two sibling dependencies of one system, obtained by
        //    two different conventions, from the SAME factory class and the SAME `geoTransform`. ⛔ That is
        //    the shape that produced AX-012 in the first place: a reader cannot tell which arm is the
        //    caller's job.
        //
        // ⭐⭐ Now: the constructor DEFAULTS BOTH from `geoTransform`, and either may be OVERRIDDEN.
        //    ⇒ omitting an argument can no longer silently disable an arm — the failure mode is gone for
        //      both, not just for the one that happened to be found.
        //    ⚠ The override is not decoration: `SimHostAppTests` passes its own JSON compiler.
        //
        // 📐 Measured: `NedNetworkFactory` was the ONLY production caller, and it passed
        //    `AttributeCompilerFactory.Build(_geoTransform)` — byte-for-byte what the default now builds.
        //    ⇒ no behaviour change, one fewer thing for a caller to get wrong.
        // ────────────────────────────────────────────────────────────────────────────────────────────
        public UpdateEntityAttributeRequestSystem(
            DdsParticipant        participant,
            NetworkEntityMap      entityMap,
            IGeographicTransform? geoTransform = null,
            JsonAttributeCompiler? jsonAttributeCompiler = null,
            NodeId                 localNodeId = default,
            BinaryInterpreter<EntityAttributeChange>? binaryInterpreter = null)
            : this(
                new DdsUpdateEntityAttributeRequestSource(participant),
                new DdsUpdateEntityAttributeAckSink(participant),
                entityMap,
                // ⭐⭐ AX-016 — like the binary arm, resolved from the WORLD on first Execute, not built
                //    here. ⭐ AX-014's requirement (both arms sourced the SAME way) is what keeps them
                //    together; only the source changed, from "the constructor" to "the world".
                jsonAttributeCompiler,
                localNodeId,
                // ⭐⭐⭐ AX-016 — DELIBERATELY NOT BUILT HERE. It is resolved from the WORLD on the first
                //    Execute (see below), because 🔒 *"the interpreter should not be bound to any network."*
                //    ⛔ AX-012 fixed a dead arm by building one here, and AX-014 made the JSON arm match —
                //    both were the right call at the time and both are now SUPERSEDED: a per-network-stack
                //    instance is the thing being removed. ⚠ An explicit override still wins, for tests.
                binaryInterpreter)
        {
        }

        // ── IEcsModuleSystem lifecycle ──────────────────────────────────────────

        public void Execute(ISimulationView view, float deltaTime)
        {
            // ⭐⭐⭐ AX-016 — resolve the WORLD's one interpreter, once. ⭐ Idempotent and allocation-free
            //    after the first tick. ⛔ It cannot be absent: the provider builds it if the world has none,
            //    so the AX-012 failure mode (a silently null arm) is unrepresentable rather than merely fixed.
            if (view is EntityRepository repoForInterpreter)
            {
                _binaryInterpreter ??= AttributeInterpreterProvider.GetOrCreate(repoForInterpreter);
                _jsonCompiler      ??= AttributeInterpreterProvider.GetOrCreateJson(repoForInterpreter);
            }

            _requestSource.ProcessRequests(req => ProcessRequest(req, view, (EntityRepository)view));
        }

        public void Dispose()
        {
            (_requestSource as IDisposable)?.Dispose();
            (_ackSink       as IDisposable)?.Dispose();
        }

        // ── Request handling ───────────────────────────────────────────────────

        private void ProcessRequest(UpdateEntityAttributeRequest req, ISimulationView view, EntityRepository repo)
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
                var ecsPatchCtx  = EcsPatchContext.Create(repo, entity);
                var binaryCtx    = _binaryInterpreter!.CreateContext(ecsPatchCtx);

                // ⭐⭐⭐ R-134 — THE INGRESS BOUNDARY. The DDS record is converted to the FDP-internal
                //    EntityAttributeChange HERE, and nothing downstream sees a network type: the
                //    interpreter, its installers and every conversion they hold are FDP-internal.
                //    📄 DESIGN_Cgf_AxisB_Rotation_Slice.md §11.1/§11.3.
                //    ⚠ This replaced a zero-copy `CollectionsMarshal.AsSpan(req.AttributeRecords)`. 📐 The
                //    cost is one array per REQUEST — an operator gesture or a script call, never per tick —
                //    and it buys the separation the ruling requires. ⛔ Keeping the span would have left the
                //    wire type as the interpreter's record type, which is the coupling AX-005a removes.
                _binaryInterpreter.Apply(binaryCtx,
                    AttributeRecordConversion.ToInternal(req.AttributeRecords));

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

            // 3a. Pre-intercept "CommanderId" from the JSON patch.
            //     This key was removed from EntityInfo (CS009) so the reflection compiler
            //     would silently drop it. We must handle it explicitly.
            //     Runs even when no JsonAttributeCompiler is present.
            bool commanderIntercepted = false;
            if (!string.IsNullOrEmpty(req.AttributePatchJson))
            {
                req.AttributePatchJson = InterceptCommanderId(
                    req.AttributePatchJson, req.EntityId, entity, view, repo,
                    out commanderIntercepted);
            }

            // 3. No compiler — acknowledge no-op only when explicitly requested.
            if (_jsonCompiler == null)
            {
                FdpLog<UpdateEntityAttributeRequestSystem>.Info(
                    "[UpdAttrReq] No JsonAttributeCompiler injected. Acknowledging no-op. " +
                    "EntityId={0}, RequestId={1}",
                    req.EntityId, req.RequestId);
                if (req.RequireAck)
                {
                    // If CommanderId was the only payload, send a success ACK.
                    if (commanderIntercepted)
                        _ackSink.WriteAck(req.RequestId, (int)NedStatusCode.Success, _localNodeId, ReadOnlySpan<byte>.Empty);
                    else
                        _ackSink.WriteErrorAck(req.RequestId, (int)NedStatusCode.Success);
                }
                return;
            }

            // 4. Build live-ECS patch context — component-level authority guard is wired
            //    into the compiler's Compile() loop via EcsPatchContext.HasAuthority.
            //    TODO ATTR-BATCH-03: investigate pooling EcsPatchContext for high-frequency updates.
            var context = _jsonCompiler.CreatePatchContext(repo, entity);

            // 5. Stream the JSON patch through the routing table.
            //    Routes whose component ID is not authorised by this node are silently skipped
            //    (reader.Skip() — zero allocation, no ECS mutation).
            _jsonCompiler.Compile(req.AttributePatchJson, context);

            // 6. Flush per-entity dirty marks, bypassing chunk-level egress ticks.
            context.FlushDirtyMarks();

            // 7. SILENT BYSTANDER RULE — if this node applied nothing, leave quietly.
            if (!context.HasAppliedAny && !commanderIntercepted)
                return;

            // 7a. CommanderId-only patch: applied via event bus but no ECS mutations.
            //     Send ACK with empty mask to confirm receipt.
            if (!context.HasAppliedAny && commanderIntercepted)
            {
                if (req.RequireAck)
                    _ackSink.WriteAck(req.RequestId, (int)NedStatusCode.Success, _localNodeId, ReadOnlySpan<byte>.Empty);
                return;
            }

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

        /// <summary>
        /// Scans <paramref name="json"/> for a "CommanderId" property.
        /// If found: resolves the entity, publishes
        /// <see cref="CmdAssignSubordinate"/> or <see cref="CmdRemoveSubordinate"/>, then returns
        /// a sanitized JSON string without the "CommanderId" key.
        /// </summary>
        private string InterceptCommanderId(
            string json, int entityNetId, Entity entity, ISimulationView view, EntityRepository repo,
            out bool intercepted)
        {
            intercepted = false;
            try
            {
                using var doc  = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("CommanderId", out var cmdIdProp))
                    return json;

                intercepted = true;
                long commanderNetId = cmdIdProp.GetInt64();
                long packedKey = Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey(1L, 0);
                if (!Fdp.Toolkit.Replication.Extensions.AuthorityExtensions.HasAuthority(view, entity, packedKey))
                {
                    FdpLog<UpdateEntityAttributeRequestSystem>.Warn(
                        "[UpdAttrReq] Unauthorized hierarchy patch attempt on entity {0}. Dropping change.",
                        entityNetId);
                    return RebuildJsonWithout(root, "CommanderId");
                }

                if (commanderNetId != 0)
                {
                    if (_entityMap.TryGetEntity(commanderNetId, out var commander))
                    {
                        repo.Bus.Publish(new CmdAssignSubordinate
                        {
                            Subordinate = entity,
                            Commander   = commander,
                            Designation = TacticalDesignation.Undefined,
                        });
                    }
                    else
                    {
                        FdpLog<UpdateEntityAttributeRequestSystem>.Warn(
                            "[UpdAttrReq] CommanderId {0} not found in entity map for entity {1}.",
                            commanderNetId, entityNetId);
                    }
                }
                else
                {
                    // Zero = remove subordination.
                    if (repo.HasComponent<UnitSubordinate>(entity))
                        repo.Bus.Publish(new CmdRemoveSubordinate { Subordinate = entity });
                }

                // Rebuild JSON without "CommanderId".
                return RebuildJsonWithout(root, "CommanderId");
            }
            catch (JsonException ex)
            {
                FdpLog<UpdateEntityAttributeRequestSystem>.Warn(
                    "[UpdAttrReq] Failed to parse AttributePatchJson for CommanderId intercept: {0}", ex.Message);
                return json;
            }
        }

        private static string RebuildJsonWithout(JsonElement root, string excludeProperty)
        {
            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms))
            {
                writer.WriteStartObject();
                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.Name == excludeProperty) continue;
                    prop.WriteTo(writer);
                }
                writer.WriteEndObject();
            }
            return Encoding.UTF8.GetString(ms.ToArray());
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

