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
                if (totalSlots > BlueprintBlackboard16384.MaxSlots || totalBytes > BlueprintBlackboard16384.PayloadSize)
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
                        if (truncated >= BlueprintBlackboard16384.MaxSlots) break;
                        if (truncatedBytes + r.Def.StateSize > BlueprintBlackboard16384.PayloadSize) break;
                        truncatedList.Add(r);
                        truncated++;
                        truncatedBytes += r.Def.StateSize;
                    }
                    resolved = truncatedList;
                    totalSlots = truncated;
                    totalBytes = truncatedBytes;
                }

                BlackboardTier tier = ChooseTierFromAggregate(totalSlots, totalBytes);

                // Step 3: Pre-provision the tier component
                AddTierComponentIfMissing(repo, entity, tier);

                // Step 4: Attach each blueprint directly into the pre-provisioned tier
                // Using low-level partition API to respect aggregate tier (not per-blueprint tier).
                GetTierMemoryAndMeta(repo, entity, tier,
                    out byte* memory, out int totalSize, out byte maxSlots);
                BlueprintBlackboardPartitions.Initialize(memory, totalSize, maxSlots);

                foreach (var (bpId, def, dto) in resolved)
                {
                    // Check if already attached (idempotent)
                    if (BlueprintBlackboardPartitions.TryGetSlotOffset(memory, bpId, out _))
                        continue;

                    if (!BlueprintBlackboardPartitions.TryAttach(
                            memory, bpId, def.StateSize, def.StructureHash, out int payloadOffset))
                    {
                        FdpLog<BlueprintMaterializationSystem>.Error(
                            $"[BlueprintMat] NoSlotAvailable for bpId 0x{bpId:X8} " +
                            $"on entity {entity} (tier {tier}). This should not happen after pre-provision.");
                        continue;
                    }

                    if (def.InitDefault != null)
                    {
                        ref byte payloadRef = ref Unsafe.AsRef<byte>(memory + payloadOffset);
                        var initSpan = MemoryMarshal.CreateSpan(ref payloadRef, def.StateSize);
                        def.InitDefault(initSpan);
                    }

                    // ⭐⭐ MX-032 — re-apply persisted params (the resolver-shape bytes) AFTER InitDefault,
                    //    through the SAME writer AttachToEntity uses. Guarded by StructureHash: a blueprint
                    //    recompiled since save has a different layout, so its stale bytes are ignored and
                    //    the declared defaults stand (logged), rather than being read at the wrong offsets.
                    if (dto.Params is { Length: > 0 } paramBytes && def.ParamsSize > 0)
                    {
                        if (dto.ParamsStructureHash == def.StructureHash)
                        {
                            BlueprintInstanceService.WriteParamsRegion(memory + payloadOffset, def, paramBytes);
                        }
                        else
                        {
                            FdpLog<BlueprintMaterializationSystem>.Warn(
                                $"[BlueprintMat] Persisted params for bpId 0x{bpId:X8} on entity {entity} " +
                                $"were saved under StructureHash {dto.ParamsStructureHash:X} but the live " +
                                $"definition is {def.StructureHash:X}; ignoring stale params, defaults stand.");
                        }
                    }
                }

                // Step 5: Remove intent via ECB (NOT direct repo removal)
                cmd.RemoveManagedComponent<InitialBlueprintsIntent>(entity);
            }
        }

        // ── Tier selection ─────────────────────────────────────────────────────

        /// <summary>
        /// Choose smallest tier satisfying BOTH slot count and payload bytes.
        /// </summary>
        private static BlackboardTier ChooseTierFromAggregate(int totalSlots, int totalBytes)
        {
            if (totalSlots <= BlueprintBlackboard1024.MaxSlots && totalBytes <= BlueprintBlackboard1024.PayloadSize)
                return BlackboardTier.B1024;
            if (totalSlots <= BlueprintBlackboard4096.MaxSlots && totalBytes <= BlueprintBlackboard4096.PayloadSize)
                return BlackboardTier.B4096;
            return BlackboardTier.B16384;
        }

        // ── Tier memory access ─────────────────────────────────────────────────

        private static unsafe void GetTierMemoryAndMeta(
            EntityRepository repo, Entity entity, BlackboardTier tier,
            out byte* memory, out int totalSize, out byte maxSlots)
        {
            switch (tier)
            {
                case BlackboardTier.B1024:
                {
                    ref var bb = ref repo.GetComponentRW<BlueprintBlackboard1024>(entity);
                    memory    = (byte*)Unsafe.AsPointer(ref Unsafe.As<BlueprintBlackboard1024, byte>(ref bb));
                    totalSize = BlueprintBlackboard1024.TotalSize;
                    maxSlots  = BlueprintBlackboard1024.MaxSlots;
                    return;
                }
                case BlackboardTier.B4096:
                {
                    ref var bb = ref repo.GetComponentRW<BlueprintBlackboard4096>(entity);
                    memory    = (byte*)Unsafe.AsPointer(ref Unsafe.As<BlueprintBlackboard4096, byte>(ref bb));
                    totalSize = BlueprintBlackboard4096.TotalSize;
                    maxSlots  = BlueprintBlackboard4096.MaxSlots;
                    return;
                }
                default:
                {
                    ref var bb = ref repo.GetComponentRW<BlueprintBlackboard16384>(entity);
                    memory    = (byte*)Unsafe.AsPointer(ref Unsafe.As<BlueprintBlackboard16384, byte>(ref bb));
                    totalSize = BlueprintBlackboard16384.TotalSize;
                    maxSlots  = BlueprintBlackboard16384.MaxSlots;
                    return;
                }
            }
        }

        // ── Tier component helper ──────────────────────────────────────────────

        private static void AddTierComponentIfMissing(EntityRepository repo, Entity entity, BlackboardTier tier)
        {
            switch (tier)
            {
                case BlackboardTier.B1024:
                    if (!repo.HasComponent<BlueprintBlackboard1024>(entity))
                        repo.AddComponent(entity, default(BlueprintBlackboard1024));
                    break;
                case BlackboardTier.B4096:
                    if (!repo.HasComponent<BlueprintBlackboard4096>(entity))
                        repo.AddComponent(entity, default(BlueprintBlackboard4096));
                    break;
                case BlackboardTier.B16384:
                    if (!repo.HasComponent<BlueprintBlackboard16384>(entity))
                        repo.AddComponent(entity, default(BlueprintBlackboard16384));
                    break;
            }
        }
    }
}
