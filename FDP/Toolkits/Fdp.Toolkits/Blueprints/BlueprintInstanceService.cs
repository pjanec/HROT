using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;

namespace Fdp.Toolkit.Blueprints;

/// <summary>Outcome of a <see cref="BlueprintInstanceService.AttachToEntity"/> call.</summary>
public enum BlueprintAttachStatus
{
    /// <summary>A fresh slot was allocated and initialized for the blueprint on the entity.</summary>
    Attached,

    /// <summary>The blueprint was already attached to the entity; nothing changed (idempotent).</summary>
    AlreadyAttached,

    /// <summary>The blueprint id is not present in the registry.</summary>
    NotRegistered,

    /// <summary>The registered blueprint is not an Instance-dispatch blueprint (cannot attach to an entity).</summary>
    NotInstanceKind,

    /// <summary>The chosen blackboard tier had no free slot or payload space for the blueprint.</summary>
    NoSlotAvailable,

    /// <summary>
    /// ⭐⭐ The supplied params JSON could not be parsed, so NOTHING was attached.
    /// <c>DESIGN_Parameter_Model.md</c> §3.3's parse-before-commit: mirroring <c>BehaviorIngressSystem</c>,
    /// <i>"a failed parse leaves the entity 100% on its old behaviour"</i> — ⛔ not an allocated-then-freed
    /// slot, and not a slot carrying half-applied params.
    /// </summary>
    ParamsParseFailed,
}

/// <summary>
/// Result of <see cref="BlueprintInstanceService.AttachToEntity"/>.
/// </summary>
/// <param name="Status">Classified outcome.</param>
/// <param name="Tier">The blackboard tier the blueprint occupies (valid for Attached / AlreadyAttached).</param>
/// <param name="Message">Human-readable detail, useful for surfacing to the editor UI.</param>
public readonly record struct BlueprintAttachResult(
    BlueprintAttachStatus Status,
    BlackboardTier Tier,
    string Message)
{
    /// <summary>True when the entity ends up carrying the blueprint slot (newly or already).</summary>
    public bool Success =>
        Status is BlueprintAttachStatus.Attached or BlueprintAttachStatus.AlreadyAttached;
}

/// <summary>
/// Core unified attach/detach seam for Instance blueprints, keyed by runtime
/// <c>int blueprintId</c>. Lives in <c>Fdp.Toolkits</c> so that CGF/genesis and
/// mid-runtime events can call it without depending on the editor assembly.
/// </summary>
/// <remarks>
/// <para>
/// The sequence mirrors the editor <c>BlueprintAttachService</c> (the proven path):
/// <list type="number">
///   <item><c>registry.TryGetById(blueprintId)</c> → require registered and <c>Kind == Instance</c>.</item>
///   <item><c>ChooseTier(def.StateSize)</c> → 1024 / 4096 / 16384.</item>
///   <item>Ensure the matching <c>BlueprintBlackboard*</c> component exists on the entity.</item>
///   <item><c>BlueprintBlackboardPartitions.Initialize</c> (idempotent on the header magic).</item>
///   <item><c>TryAttach</c> → allocate a slot; <c>InitDefault</c> the fresh payload.</item>
/// </list>
/// </para>
/// <para>
/// <b>Run-mode-agnostic:</b> this only mutates the entity's components. It does not require
/// the simulation to be running or in preview — attaching while paused is valid; the tick
/// system picks the slot up on the next frame that the sim group runs.
/// </para>
/// <para>
/// <b>Idempotent:</b> if a slot for the blueprint already exists on the entity (any tier),
/// the call is a no-op and returns <see cref="BlueprintAttachStatus.AlreadyAttached"/>.
/// </para>
/// </remarks>
public static unsafe class BlueprintInstanceService
{
    /// <summary>
    /// Attaches an Instance blueprint identified by <paramref name="blueprintId"/> to
    /// <paramref name="entity"/> in <paramref name="world"/>, allocating a blackboard slot
    /// in the smallest fitting tier. See the type remarks for the exact sequence, idempotency,
    /// and run-mode semantics.
    /// </summary>
    /// <param name="world">The live entity repository hosting the entity.</param>
    /// <param name="registry">The registry the runtime ticks against (must already contain the blueprint).</param>
    /// <param name="blueprintId">The runtime 32-bit blueprint identifier (<c>BlueprintIdHash.Compute(assetId)</c>).</param>
    /// <param name="entity">The target entity (must already exist in <paramref name="world"/>).</param>
    /// <param name="paramsJson">
    /// ⭐ Params for the blueprint, as a JSON object keyed by parameter name
    /// (<c>DESIGN_Parameter_Model.md</c> §3.3). null/empty means "declared defaults only", which is the
    /// shipped behaviour and every existing caller's answer. ⛔ At runtime attach this is the ONLY source
    /// of params — no side table. (Save→reload re-applies the persisted bytes through
    /// <see cref="WriteParamsRegion"/> from <c>BlueprintAssignmentDto.Params</c> — MX-031/032.)
    /// </param>
    /// <returns>A classified <see cref="BlueprintAttachResult"/>.</returns>
    public static BlueprintAttachResult AttachToEntity(
        EntityRepository world,
        BlueprintRegistry registry,
        int blueprintId,
        Entity entity,
        string? paramsJson = null)
    {
        if (world is null)    throw new ArgumentNullException(nameof(world));
        if (registry is null) throw new ArgumentNullException(nameof(registry));

        if (!registry.TryGetById(blueprintId, out var def) || def is null)
            return new BlueprintAttachResult(
                BlueprintAttachStatus.NotRegistered, default,
                $"Blueprint id 0x{blueprintId:X8} is not registered. " +
                "Compile/register it before attaching.");

        if (def.Kind != BlueprintDispatchKind.Instance)
            return new BlueprintAttachResult(
                BlueprintAttachStatus.NotInstanceKind, default,
                $"Blueprint '{def.Name}' is {def.Kind}, not Instance; only Instance " +
                "blueprints attach to an entity blackboard.");

        // Idempotent: already attached on any tier → no-op.
        if (TryFindExistingTier(world, entity, blueprintId, out var existingTier))
            return new BlueprintAttachResult(
                BlueprintAttachStatus.AlreadyAttached, existingTier,
                $"Blueprint '{def.Name}' is already attached to entity {entity} " +
                $"(tier {existingTier}).");

        // ⭐⭐⭐ PARSE BEFORE COMMIT (§3.3), mirroring BehaviorIngressSystem exactly.
        //   The params are resolved into a STACK buffer here, BEFORE any slot is allocated, so a
        //   malformed payload leaves the entity untouched — ⛔ not an allocated-then-freed slot, which
        //   would still have dense-compacted the slot table and could still have upgraded the tier.
        //   The resolved bytes are copied in AFTER InitDefault below; the reverse order wipes them.
        byte* resolvedParams = null;
        int   paramsSize     = def.ParseParams != null ? def.ParamsSize : 0;
        if (paramsSize > 0)
        {
            byte* scratch = stackalloc byte[paramsSize];
            new Span<byte>(scratch, paramsSize).Clear();
            try
            {
                // ⚠ host is null: IHostVariableAccess is declared-not-implemented and E7a populates it;
                //   null is its defined value for a root (non-hosted) occurrence.
                // ⭐⭐ CE-331 (2026-09-23): `paramsSize` is the stackalloc's exact extent, so it IS
                //   the capacity. ⚠ This is the SECOND invocation of the delegate and the one a
                //   `ParseParams(` grep misses — it is spelled `def.ParseParams!(`. The compiler
                //   found it; the search did not.
                def.ParseParams!(paramsJson ?? string.Empty, scratch, paramsSize, world, entity, host: null);
            }
            catch (Exception ex)
            {
                return new BlueprintAttachResult(
                    BlueprintAttachStatus.ParamsParseFailed, default,
                    $"Params for blueprint '{def.Name}' could not be parsed; entity {entity} is " +
                    $"unchanged and no slot was allocated. {ex.GetType().Name}: {ex.Message}");
            }
            resolvedParams = scratch;
        }

        var tier = ChooseTierHonouringCurrent(world, entity, def.StateSize);
        EnsureTierComponent(world, entity, tier);

        GetTierMemoryAndMeta(world, entity, tier, out byte* memory, out int totalSize, out byte maxSlots);
        BlueprintBlackboardPartitions.Initialize(memory, totalSize, maxSlots);

        // A3/D1': the occurrence declares its Kind at attach — see BlueprintTickSystem's note.
        if (!BlueprintBlackboardPartitions.TryAttach(
                memory, blueprintId, def.StateSize, def.StructureHash,
                OccurrenceKind.Blueprint, out int payloadOffset))
            return new BlueprintAttachResult(
                BlueprintAttachStatus.NoSlotAvailable, tier,
                $"No free slot/payload for blueprint '{def.Name}' on entity {entity} " +
                $"in tier {tier}.");

        if (def.InitDefault != null)
        {
            ref byte payloadRef = ref Unsafe.AsRef<byte>(memory + payloadOffset);
            var initSpan = MemoryMarshal.CreateSpan(ref payloadRef, def.StateSize);
            def.InitDefault(initSpan);
        }

        // ⭐⭐ ORDER IS THE RULING (§3.3): InitDefault FIRST, then the resolved params. The reverse
        //    zeroes them and would read as a resolver bug rather than an ordering one.
        // ⭐ The cursor at payload offset 0 is NOT touched: the copy lands at ParamsOffset (16), via the
        //    ONE param-region writer that BlueprintMaterializationSystem also uses (persisted round-trip).
        if (resolvedParams != null)
        {
            WriteParamsRegion(memory + payloadOffset, def, new ReadOnlySpan<byte>(resolvedParams, paramsSize));
        }

        return new BlueprintAttachResult(
            BlueprintAttachStatus.Attached, tier,
            $"Attached blueprint '{def.Name}' to entity {entity} (tier {tier}).");
    }

    /// <summary>
    /// Detaches an Instance blueprint identified by <paramref name="blueprintId"/> from
    /// <paramref name="entity"/>, freeing its slot and dense-compacting the slot table.
    /// </summary>
    /// <param name="world">The live entity repository hosting the entity.</param>
    /// <param name="blueprintId">The runtime 32-bit blueprint identifier.</param>
    /// <param name="entity">The target entity.</param>
    /// <returns><c>true</c> if a slot was found and removed; <c>false</c> if the blueprint
    /// was not attached on any tier.</returns>
    public static bool DetachFromEntity(
        EntityRepository world,
        int blueprintId,
        Entity entity)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));

        // ⭐ O3a / B3: scan every tier, in ladder order. ⛔ Was a hand-written three-arm scan.
        //   ⚠ The scan is kept (rather than "resolve the entity's one tier") because it is what the
        //     method did: a slot is looked for on ANY tier the entity happens to carry, which also
        //     covers the transient mid-promotion moment when it carries two.
        var tiers = BlueprintTierTable.Ascending;
        for (int i = 0; i < tiers.Count; i++)
        {
            var spec = tiers[i];
            if (!spec.Has(world, entity)) continue;

            byte* mem = spec.Memory(world, entity);
            if (HasInitializedSlot(mem, blueprintId))
            {
                BlueprintBlackboardPartitions.TryDetach(mem, blueprintId);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Selects the smallest blackboard tier whose payload can hold <paramref name="stateSize"/>.
    /// Bounds match <c>BlueprintBlackboard{1024,4096,16384}.PayloadSize</c>.
    /// </summary>
    public static BlackboardTier ChooseTier(int stateSize)
    {
        // ⭐ O3a / B3: SelectByPayload is deliberately the payload-ONLY selector — this caller sizes
        //   ONE instance's state and has no slot count to offer. See BlueprintTierTable.SelectByPayload.
        return BlueprintTierTable.SelectByPayload(stateSize).Tier;
    }

    // ── param-region round-trip (MX-030) ─────────────────────────────────────
    // ⭐⭐ The ONE place that knows WHERE params live inside a payload — [ParamsOffset .. +ParamsSize).
    //    AttachToEntity writes them from resolved JSON; BlueprintStateTranslator.Extract reads them for
    //    save; BlueprintMaterializationSystem writes the persisted bytes back on load. Centralising the
    //    offset here keeps those three from disagreeing (ruling 9).

    /// <summary>
    /// Copies resolved param bytes into a slot payload's param region
    /// <c>[ParamsOffset .. ParamsOffset+ParamsSize)</c>. Copies <c>min(ParamsSize, paramBytes.Length)</c>
    /// bytes and never touches the cursor (offset 0..16) or the state region beyond params.
    /// </summary>
    /// <param name="payload">Pointer to the slot payload base (i.e. <c>memory + payloadOffset</c>).</param>
    public static void WriteParamsRegion(byte* payload, BlueprintDefinition def, ReadOnlySpan<byte> paramBytes)
    {
        if (def.ParamsSize <= 0 || paramBytes.IsEmpty) return;
        int n = Math.Min(def.ParamsSize, paramBytes.Length);
        var dst = new Span<byte>(payload + def.ParamsOffset, def.ParamsSize);
        paramBytes.Slice(0, n).CopyTo(dst);
    }

    /// <summary>Reads a slot payload's param region into a fresh array (the inverse of <see cref="WriteParamsRegion"/>).</summary>
    /// <param name="payload">Pointer to the slot payload base (i.e. <c>memory + payloadOffset</c>).</param>
    public static byte[] ReadParamsRegion(byte* payload, BlueprintDefinition def)
    {
        if (def.ParamsSize <= 0) return Array.Empty<byte>();
        return new ReadOnlySpan<byte>(payload + def.ParamsOffset, def.ParamsSize).ToArray();
    }

    /// <summary>
    /// The param region a freshly-<c>InitDefault</c>'d payload carries — the baseline
    /// <c>BlueprintStateTranslator.Extract</c> diffs against, so only NON-default
    /// params are persisted (a default assignment stays <c>{AssetId}</c> only).
    /// </summary>
    public static byte[] GetDefaultParamsRegion(BlueprintDefinition def)
    {
        if (def.ParamsSize <= 0) return Array.Empty<byte>();
        var scratch = new byte[def.StateSize];
        def.InitDefault?.Invoke(scratch);
        return new ReadOnlySpan<byte>(scratch, def.ParamsOffset, def.ParamsSize).ToArray();
    }

    // ── private helpers ──────────────────────────────────────────────────────

    private static bool TryFindExistingTier(
        EntityRepository world, Entity entity, int blueprintId, out BlackboardTier tier)
    {
        // ⭐ O3a / B3: same walk as DetachFromEntity, same reason for scanning rather than resolving.
        var tiers = BlueprintTierTable.Ascending;
        for (int i = 0; i < tiers.Count; i++)
        {
            var spec = tiers[i];
            if (!spec.Has(world, entity)) continue;

            if (HasInitializedSlot(spec.Memory(world, entity), blueprintId))
            {
                tier = spec.Tier;
                return true;
            }
        }

        tier = BlueprintTierTable.Ascending[0].Tier;
        return false;
    }

    // A freshly-added (zeroed) tier component has no header magic; treat it as "no slot" so
    // TryGetSlotOffset is not called on uninitialized memory.
    private static bool HasInitializedSlot(byte* memory, int blueprintId)
    {
        ref var header = ref Unsafe.AsRef<BlueprintBlackboardHeader>(memory);
        if (header.MagicAndVersion != BlueprintBlackboardHeader.MagicValue)
            return false;
        return BlueprintBlackboardPartitions.TryGetSlotOffset(memory, blueprintId, out _);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The tier this attach must land on, given the tier the entity ALREADY carries.</b>
    /// <c>O3b</c> / task <c>B4</c> — 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §17.7.
    ///
    /// <para>🔴🔴 <b>The defect this fixes, and it is a PRODUCTION one.</b> This call site used
    /// <see cref="ChooseTier"/> alone and then added that component. ⛔ <c>ChooseTier</c> sizes the
    /// ONE instance being attached and knows nothing about the entity — so whenever it named a
    /// DIFFERENT tier from the one already present, <see cref="EnsureTierComponent"/> added a
    /// <b>second</b> blackboard component and the entity ended up carrying two. That breaks the
    /// <see cref="OccurrenceStoreAccess"/> invariant every consumer relies on — <i>"an entity carries
    /// AT MOST ONE tier"</i> — and the probe order then decides which store is authoritative, so the
    /// slots just written can become invisible.</para>
    ///
    /// <para>⚠ <b>Reachable before <c>B4</c> only upwards, and now in both directions.</b> With the
    /// ladder <c>1024 / 4096 / 16384</c>, <c>ChooseTier</c> could only name a tier LARGER than a
    /// present one (a big instance on a 1024 entity) — rare, and it left the small tier orphaned.
    /// ⛔ <c>O3b</c>'s 256 tier made the DOWNGRADE direction the common case: every small instance
    /// attached to an entity already carrying 1024 chose 256. 📌 That is what reddened
    /// <c>BlueprintStateTranslatorTests.Extract_TwoBlueprintsAttached</c> — the slots landed in a
    /// fresh 256 store while <c>Extract</c> probes largest-first and found the empty 1024.</para>
    ///
    /// <para>⭐ The rule: <b>never downgrade</b>. A tier already present is kept when the state fits
    /// it. When it genuinely does not, the store is <b>PROMOTED</b> through
    /// <see cref="BlueprintTierTable.Promote"/> — carrying the existing slots and their
    /// <c>Kind</c> nibbles — rather than a second component being bolted on beside it.</para>
    /// </summary>
    private static BlackboardTier ChooseTierHonouringCurrent(
        EntityRepository world, Entity entity, int stateSize)
        => BlueprintTierTable.EnsureAtLeast(
               world, entity, BlueprintTierTable.SelectByPayload(stateSize)).Tier;

    private static void EnsureTierComponent(EntityRepository world, Entity entity, BlackboardTier tier)
    {
        var spec = BlueprintTierTable.ByTier(tier);
        if (!spec.Has(world, entity))
            spec.Add(world, entity);
    }

    private static void GetTierMemoryAndMeta(
        EntityRepository world, Entity entity, BlackboardTier tier,
        out byte* memory, out int totalSize, out byte maxSlots)
    {
        var spec  = BlueprintTierTable.ByTier(tier);
        memory    = spec.Memory(world, entity);
        totalSize = spec.TotalSize;
        maxSlots  = (byte)spec.MaxSlots;
    }
}
