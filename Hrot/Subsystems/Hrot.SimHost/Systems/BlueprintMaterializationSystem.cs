using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using Hrot.Common.Serializers;

namespace Hrot.SimHost.Systems
{
    /// <summary>
    /// Resolves <see cref="InitialBlueprintsIntent"/> managed components (written by
    /// <see cref="Hrot.SimHost.Serializers.BlueprintStateTranslator"/>) into live
    /// <see cref="BlueprintBlackboard1024"/>/<c>4096</c>/<c>16384</c> slots via
    /// <see cref="BlueprintInstanceService.AttachToEntity"/>.
    ///
    /// <para>On each simulation tick (Input phase), the system queries for entities
    /// that carry an <c>InitialBlueprintsIntent</c> managed component, resolves each
    /// <see cref="BlueprintAssignmentDto.AssetId"/> to a registered
    /// <see cref="BlueprintDefinition"/>, pre-provisions the correct blackboard tier
    /// from the aggregate slot + byte requirements, attaches all blueprints via the
    /// core attach seam, and removes the intent via <see cref="EntityCommandBuffer"/>.</para>
    ///
    /// <para><b>Tier pre-provisioning (Design §5):</b> the tier is chosen BEFORE any
    /// attachment so that <see cref="BlueprintInstanceService.AttachToEntity"/> never
    /// has to upgrade mid-tick. The ceiling guard clamps at 16 slots / 16096 bytes
    /// (<see cref="BlueprintBlackboard16384"/> capacity); exceeding those bounds logs
    /// an error and truncates — it never throws.</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Input)]
    public sealed class BlueprintMaterializationSystem : IEcsModuleSystem
    {
        private readonly BlueprintRegistry _registry;

        public BlueprintMaterializationSystem(BlueprintRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(BlueprintMaterializationSystem)} requires direct EntityRepository access " +
                    $"and cannot run on a read-only snapshot ({view.GetType().Name}).");

            var cmd = new EntityCommandBuffer();
            try
            {
                MaterializeBlueprints(view, cmd, repo);
                cmd.Playback(repo);
            }
            finally
            {
                cmd.Dispose();
            }
        }

        // ── Materialization helpers ────────────────────────────────────────────

        private unsafe void MaterializeBlueprints(ISimulationView view, EntityCommandBuffer cmd, EntityRepository repo)
        {
            foreach (var entity in view.Query().WithManaged<InitialBlueprintsIntent>().Build())
            {
                var intent = view.GetManagedComponentRO<InitialBlueprintsIntent>(entity);
                if (intent.Blueprints.Count == 0)
                {
                    cmd.RemoveManagedComponent<InitialBlueprintsIntent>(entity);
                    continue;
                }

                // Step 1: Resolve AssetIds → definitions (carry the DTO so persisted params apply below)
                var resolved = new List<(int BlueprintId, BlueprintDefinition Def, BlueprintAssignmentDto Dto)>();
                foreach (var dto in intent.Blueprints)
                {
                    int bpId = BlueprintIdHash.Compute(dto.AssetId);
                    if (_registry.TryGetById(bpId, out var def) && def != null)
                        resolved.Add((bpId, def, dto));
                    else
                        FdpLog<BlueprintMaterializationSystem>.Warn(
                            $"[BlueprintMat] AssetId {dto.AssetId} not registered; skipping.");
                }

                if (resolved.Count == 0)
                {
                    cmd.RemoveManagedComponent<InitialBlueprintsIntent>(entity);
                    continue;
                }

                // Step 2: Compute aggregate → pick tier (Design §5)
                int totalSlots = resolved.Count;
                int totalBytes = 0;
                foreach (var (_, def, _) in resolved)
                    totalBytes += def.StateSize;

                // Ceiling guard (16 slots / 16096 bytes)
                if (totalSlots > BlueprintTierTable.Largest.MaxSlots || totalBytes > BlueprintTierTable.Largest.PayloadSize)
                {
                    FdpLog<BlueprintMaterializationSystem>.Error(
                        $"[BlueprintMat] Entity {entity} exceeds absolute ceiling " +
                        $"({totalSlots} slots / {totalBytes} bytes). Truncating to tier capacity.");
                    // Truncate to ceiling capacity
                    int truncated = 0;
                    int truncatedBytes = 0;
                    var truncatedList = new List<(int, BlueprintDefinition, BlueprintAssignmentDto)>();
                    foreach (var r in resolved)
                    {
                        if (truncated >= BlueprintTierTable.Largest.MaxSlots) break;
                        if (truncatedBytes + r.Def.StateSize > BlueprintTierTable.Largest.PayloadSize) break;
                        truncatedList.Add(r);
                        truncated++;
                        truncatedBytes += r.Def.StateSize;
                    }
                    resolved = truncatedList;
                    totalSlots = truncated;
                    totalBytes = truncatedBytes;
                }

                // ⭐⭐ CE-3137 U-0 (R-236) — 📄 DESIGN_Occurrence_Scoped_Storage.md §34. A unit with no store gets its
                //   FIRST block sized for this scenario's aggregate; a unit that already carries one (a behaviour's)
                //   gets a block APPENDED for any shortfall. ⛔ No promotion: no slot already attached moves.
                if (!OccurrenceStoreAccess.HasStore(repo, entity))
                {
                    var first = BlueprintTierTable.ByTier(ChooseTierFromAggregate(totalSlots, totalBytes));
                    if (first.IsRegistered(repo)) OccurrenceStoreAccess.AddBlock(repo, entity, first);
                }
                else
                {
                    OccurrenceStoreAccess.EnsureRoom(repo, entity, totalBytes, totalSlots);
                }

                // Step 4: attach each blueprint into any block with room (an appended one on a miss).
                foreach (var (bpId, def, dto) in resolved)
                {
                    // Check if already attached (idempotent) — in any block.
                    if (OccurrenceStoreAccess.TryFindSlot(repo, entity, bpId, out _, out _, out _))
                        continue;

                    // A3/D1': the occurrence declares its Kind at attach — see BlueprintTickSystem's note.
                    if (!OccurrenceStoreAccess.TryAttachSlot(
                            repo, entity, bpId, def.StateSize, def.StructureHash,
                            OccurrenceKind.Blueprint, out byte* memory, out int payloadOffset))
                    {
                        FdpLog<BlueprintMaterializationSystem>.Error(
                            $"[BlueprintMat] NoSlotAvailable for bpId 0x{bpId:X8} on entity {entity}: every block is " +
                            "full and every tier is already carried.");
                        continue;
                    }

                    if (def.InitDefault != null)
                    {
                        ref byte payloadRef = ref Unsafe.AsRef<byte>(memory + payloadOffset);
                        var initSpan = MemoryMarshal.CreateSpan(ref payloadRef, def.StateSize);
                        def.InitDefault(initSpan);
                    }

                    // ⭐⭐ CE-3044 (R-191) — the params through the blueprint's OWN ParseParams, AFTER InitDefault, exactly as
                    //    AttachToEntity does: the declared defaults first, then the saved JSON overlaid by name.
                    //    ⭐ ALWAYS, even with nothing saved — InitDefault bakes STATE defaults only, so skipping this left
                    //    a reloaded instance with all-zero params where a fresh attach had the declared defaults.
                    //    A key the blueprint no longer declares (renamed / removed since the save) loses only itself.
                    if (def.ParseParams != null && def.ParamsSize > 0)
                    {
                        foreach (var key in BlueprintInstanceService.UnknownParamKeys(def, dto.Params))
                            FdpLog<BlueprintMaterializationSystem>.Warn(
                                $"[BlueprintMat] Saved param '{key}' of blueprint '{def.Name}' on entity {entity} is not " +
                                "declared by the blueprint any more (renamed or removed since the save); it is ignored and " +
                                "the parameter keeps its default.");
                        string? error = BlueprintInstanceService.ApplyParams(
                            memory + payloadOffset, def, dto.Params?.ToJsonString(), repo, entity);
                        if (error != null)
                        {
                            BlueprintInstanceService.ApplyParams(memory + payloadOffset, def, null, repo, entity);
                            FdpLog<BlueprintMaterializationSystem>.Warn(
                                $"[BlueprintMat] Saved params of blueprint '{def.Name}' on entity {entity} could not be " +
                                $"parsed; the declared defaults stand. {error}");
                        }
                    }
                }

                // Step 5: Remove intent via ECB (NOT direct repo removal)
                cmd.RemoveManagedComponent<InitialBlueprintsIntent>(entity);
            }
        }

        // ── Tier selection ─────────────────────────────────────────────────────

        // ═══ O3a / B3 — the three tier helpers are now the one ladder ══════════════════════════
        //  📄 DESIGN_Occurrence_Scoped_Storage.md §17. ⛔ ChooseTierFromAggregate, GetTierMemoryAndMeta
        //  and AddTierComponentIfMissing were a select ladder and two switch copies of the same
        //  three-way dispatch that ~36 sites carried. They now read BlueprintTierTable.
        //  ⚠ Semantics preserved exactly, including "fall through to the largest tier" and
        //  AddTierComponentIfMissing's idempotence.

        /// <summary>
        /// Choose smallest tier satisfying BOTH slot count and payload bytes.
        /// </summary>
        private static BlackboardTier ChooseTierFromAggregate(int totalSlots, int totalBytes)
            => BlueprintTierTable.Select(totalBytes, totalSlots).Tier;

        // ── Tier memory access ─────────────────────────────────────────────────


        // ── Tier component helper ──────────────────────────────────────────────

    }
}
