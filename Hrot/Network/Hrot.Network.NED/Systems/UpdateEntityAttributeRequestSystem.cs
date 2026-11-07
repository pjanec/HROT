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
using Fdp.Toolkit.Replication.Attributes;
using Fdp.Toolkit.Replication.Edits;
using CycloneDDS.Runtime;
using Fdp.Core.Logging;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.ModuleHost.Abstractions;
using NodeId = Hrot.NED.Common.NodeId;

namespace Hrot.Map.Common.Systems
{

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
    /// nothing is answered — except by the OWNER of an edit that asked for an outcome (<c>WantOutcome</c>).</para>
    ///
    /// <para>⭐ <b>The answer is <c>EntityEditOutcome</c></b> (edit framework F2, R-4126). ⛔ The old opt-in <c>CreateUpdateDeleteEntityAck</c>
    /// (<c>RequireAck</c> + a 512-bit applied-components mask with no production reader) is retired.</para>
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
    [TickMode(TickPolicy.EveryFrame)]
    public sealed class UpdateEntityAttributeRequestSystem : IEcsModuleSystem, IDisposable
    {
        private readonly IUpdateEntityAttributeRequestSource _requestSource;

        private readonly NetworkEntityMap                    _entityMap;
        private JsonAttributeCompiler?                       _jsonCompiler;
        private BinaryInterpreter<EntityAttributeChange>?               _binaryInterpreter;
        private readonly NodeId                              _localNodeId;

        // ── Interface-based constructor (test-friendly) ───────────────────────

        /// <summary>
        /// Creates a new system instance using injectable source and sink abstractions.
        /// </summary>
        /// <param name="requestSource">Source of incoming <see cref="UpdateEntityAttributeRequest"/> messages.</param>
        /// <param name="entityMap">Shared net-ID → entity lookup service.</param>
        /// <param name="jsonAttributeCompiler">
        /// Optional zero-allocation JSON attribute compiler.  When non-null, incoming
        /// <c>AttributePatchJson</c> is applied to live ECS components.  When <c>null</c>,
        /// it is resolved from the world on the first Execute.
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
            NetworkEntityMap                    entityMap,
            JsonAttributeCompiler?              jsonAttributeCompiler = null,
            NodeId                              localNodeId = default,
            BinaryInterpreter<EntityAttributeChange>?    binaryInterpreter = null)
        {
            _requestSource     = requestSource ?? throw new ArgumentNullException(nameof(requestSource));
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
        //    `AttributeCompilerFactory.Build(_geoTransform)` — byte-for-byte what the default built then (CE-4073: no geo parameter now).
        //    ⇒ no behaviour change, one fewer thing for a caller to get wrong.
        // ────────────────────────────────────────────────────────────────────────────────────────────
        public UpdateEntityAttributeRequestSystem(
            DdsParticipant        participant,
            NetworkEntityMap      entityMap,
            JsonAttributeCompiler? jsonAttributeCompiler = null,
            NodeId                 localNodeId = default,
            BinaryInterpreter<EntityAttributeChange>? binaryInterpreter = null)
            : this(
                new DdsUpdateEntityAttributeRequestSource(participant),
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
        }

        // ── Request handling ───────────────────────────────────────────────────

        private void ProcessRequest(UpdateEntityAttributeRequest req, ISimulationView view, EntityRepository repo)
        {
            // 1. Resolve the entity from the network ID.
            if (!_entityMap.TryGetEntity(req.EntityId, out var entity))
            {
                FdpLog<UpdateEntityAttributeRequestSystem>.Warn(
                    "[UpdAttrReq] Entity {0} not found. RequestId={1}",
                    req.EntityId, req.RequestId);
                return;
            }

            bool hasBinaryRecords = _binaryInterpreter != null
                && req.AttributeRecords != null
                && req.AttributeRecords.Count > 0;

            // 2. Binary path: apply AttributeRecords via BinaryInterpreter.
            if (hasBinaryRecords)
            {
                // Build a bare EcsPatchContext directly — independent of the JSON compiler.
                // Authority checks and component access work without a routing table.
                //
                // ⭐⭐ AX-015 CORRECTED THIS COMMENT. It used to read "FlushDirtyMarks is a no-op here
                //    because the binary installer flushers drive SmartEgress themselves" — 🔴 and that
                //    was the DEFECT, described as if it were the design: the installers announced their
                //    descriptor through BinaryPatchContext.MarkDescriptorDirty, which set a local ulong
                //    mask nothing in production read, so NOTHING reached SmartEgress and an entity
                //    rename applied on the owner was never republished.
                // ⭐ Now the installers' MarkDescriptorDirty reaches EcsPatchContext's ordinal set, and
                //    BinaryInterpreter.Apply's closing FlushDirtyMarks() is what delivers it. ⇒ the
                //    context built by Create() has an empty ROUTING map but its dirty set is live.
                var ecsPatchCtx  = EcsPatchContext.Create(repo, entity);
                var binaryCtx    = _binaryInterpreter!.CreateContext(ecsPatchCtx);
                // ⭐ CE-2380 part 2 — the same context the local write router fills (Repo / Entity / RequestId), so a handler behaves alike on both paths and a deferred one can store the id.
                binaryCtx.Repo = repo; binaryCtx.Entity = entity; binaryCtx.RequestId = req.RequestId;

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

                // SILENT BYSTANDER RULE — nothing applied, leave quietly. ⭐ CE-2380: except the OWNER of an edit that asked for an outcome — it says what was not applied.
                if (!ecsPatchCtx.HasAppliedAny)
                {
                    if (req.WantOutcome && view.OwnsDescriptorKey(entity, 0))
                    {
                        var ids = new List<string>();
                        foreach (var rec in req.AttributeRecords!) ids.Add(rec.AttributeId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        ReportOutcome(repo, entity, req, EditStatus.Refused,
                            $"not applied: attribute(s) {string.Join(", ", ids)} — no handler accepted them on the owner");
                    }
                    return;
                }
                if (req.WantOutcome)   // ⭐ CE-2380 part 2: a deferred handler (a rule's edit / control) → Pending, its judge answers the verdict
                {
                    if (binaryCtx.Deferred) ReportOutcome(repo, entity, req, EditStatus.Pending, "taken by the owner; its verdict follows");
                    else ReportOutcome(repo, entity, req, EditStatus.Applied, "applied");
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
                if (req.WantOutcome && commanderIntercepted) ReportOutcome(repo, entity, req, EditStatus.Applied, "applied");   // ⭐ CE-2380
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
            {
                // ⭐ CE-2380 — only the entity's OWNER answers a refusal (every node receives the request; a bystander applies nothing as a matter of course).
                if (req.WantOutcome && view.OwnsDescriptorKey(entity, 0))
                    ReportOutcome(repo, entity, req, EditStatus.Refused, "not applied: nothing in the patch matched a field the owner accepts — " + Clip(req.AttributePatchJson, 200));
                return;
            }
            if (req.WantOutcome) ReportOutcome(repo, entity, req, EditStatus.Applied, "applied");   // ⭐ CE-2380 (also the CommanderId-only patch)
            // ⭐ Edit framework F2 (R-4126) — the answer is the EntityEditOutcome above; ⛔ the old opt-in CreateUpdateDeleteEntityAck (RequireAck + a
            //   512-bit applied-components mask nobody read) is retired.
        }

        /// <summary>⭐ CE-2380 part 1 — this node's answer to an edit that asked for one (<see cref="EditOutcomes.Report"/>: the local log and the bus; the egress sends it).</summary>
        private void ReportOutcome(EntityRepository repo, Entity entity, UpdateEntityAttributeRequest req, EditStatus status, string reason)
        {
            int node = EditOutcomes.LocalNodeOf(repo, entity);
            EditOutcomes.Report(repo, req.RequestId, req.EntityId, status, reason, node >= 0 ? node : _localNodeId.AppInstanceId);
        }

        private static string Clip(string? s, int max) => string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s.Substring(0, max) + "…";

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

                int commanderNetId = cmdIdProp.GetInt32();
                long packedKey = Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey(1L, 0);
                if (!Fdp.Toolkit.Replication.Extensions.AuthorityExtensions.HasAuthority(view, entity, packedKey))
                {
                    // ⭐⭐ S2b — a BYSTANDER, not an attack. Every host runs this handler since S2b, and the request
                    //   topic reaches all of them; only the owner applies. ⛔ `intercepted` stays false here, so the
                    //   silent-bystander rule holds: a non-owner never sends a (false) Success ack for a patch it
                    //   did not apply. 📄 docs/DESIGN_Ownership_Groups_And_Grants.md §5.6 S2b.
                    FdpLog<UpdateEntityAttributeRequestSystem>.Debug(
                        "[UpdAttrReq] Not authoritative for the hierarchy of entity {0}; bystander, ignoring CommanderId.",
                        entityNetId);
                    return RebuildJsonWithout(root, "CommanderId");
                }

                intercepted = true;

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
            => _reader = new DdsReader<UpdateEntityAttributeRequest>(participant);

        public void ProcessRequests(Action<UpdateEntityAttributeRequest> processor)
        {
            using var loan = _reader.Take();
            foreach (var sample in loan)
                if (sample.IsValid)
                    processor(sample.Data);
        }

        public void Dispose() => _reader.Dispose();
    }

}

