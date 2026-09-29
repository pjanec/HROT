using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Blueprints.Partitioning;

namespace Hrot.AiEditor.Generators.Tests.Demos;

/// <summary>
/// ⭐⭐⭐ <b><c>P3-C</c> — the proof tests' stand-in for what <c>BehaviorIngressSystem</c> does at
/// assign time: give an entity a ROOT PARAMS OCCURRENCE SLOT and read DTOs out of it.</b>
///
/// <para>🔴 <b>What this replaces, and why the tests had to change at all.</b> Until <c>P3-C</c> a
/// behaviour's params were a <c>fixed byte[100]</c> inside a <c>BrainBlackboard</c> component, so a
/// proof test could hold <c>var bb = new BrainBlackboard()</c> as a <b>local</b>, seed it, tick, and
/// read it back — with no world and no entity at all (<c>var ctx = new BTreeContext();</c>). ⛔ The
/// params now live in the entity's occurrence store, which means an emitted thunk resolves them from
/// <c>(ctx.World, ctx.Self)</c> ⇒ <b>a test with no world can no longer drive one.</b></para>
///
/// <para>⭐⭐ <b>This is a fixture change, not a weakening of the proofs.</b> Every assertion these
/// tests make — independent DTOs, disjoint offsets, per-node working state — is about the SAME bytes
/// at the SAME offsets; only the anchor moved (§29.6). ⚠ What they gain is that the entity is now
/// real, which is closer to production than a stack-local component ever was.</para>
/// </summary>
internal static unsafe class RootParamsTestHarness
{
    /// <summary>
    /// A behaviour hash for entities these tests build by hand rather than through ingress.
    /// ⚠ Any non-zero value works — it only has to be STABLE, because the root slot key is derived
    /// from it and <c>RootParamsAccess</c> re-derives the same key on every read.
    /// </summary>
    internal const int HarnessBehaviourHash = 0x7E5701;

    /// <summary>
    /// Creates an entity carrying <see cref="BehaviorState"/>, an occurrence store, and a full-width
    /// root params slot — then returns it ready to tick.
    ///
    /// <para>⚠ <paramref name="world"/> must already have the tier components registered
    /// (<c>BlueprintTierTable.RegisterAll</c>); without them there is nowhere for the slot to go and
    /// this fails loudly rather than handing back an entity whose params silently vanish.</para>
    /// </summary>
    internal static Entity NewBrainEntity(EntityRepository world, int behaviourHash = HarnessBehaviourHash)
    {
        var entity = world.CreateEntity();
        world.AddComponent(entity, new BehaviorState());
        RootStateAccess.EnsureRootState(world, entity);   // ⛔ O7c-②: BrainBTreeState retired — the root cursor is an occurrence slot (§31).

        ref var st = ref world.GetComponentRW<BehaviorState>(entity);
        st.ActiveBehaviorHash = behaviourHash;
        st.BrainTier = BehaviorConstants.BrainTierBTree;

        AttachRoot(world, entity, behaviourHash);
        return entity;
    }

    /// <summary>
    /// Attaches the root params slot on an entity that already exists — for tests that build the
    /// entity themselves (or drive the real ingress) and only need the slot.
    /// </summary>
    internal static byte* AttachRoot(
        EntityRepository world, Entity entity, int behaviourHash = HarnessBehaviourHash)
    {
        // ⭐ The smallest tier that fits a full-width params region, so the fixture does not silently
        //   depend on a bigger tier than the behaviour needs.
        if (OccurrenceStoreAccess.GetStoreSize(world, entity) == 0)
        {
            var spec = BlueprintTierTable.Select(
                BehaviorConstants.MaxBehaviorParamByteSize + BlueprintBlackboardPartitions.SlotEntrySize,
                requiredSlots: 1);
            spec.Add(world, entity);
            byte* mem = spec.Memory(world, entity);
            BlueprintBlackboardPartitions.Initialize(mem, spec.TotalSize, (byte)spec.MaxSlots);
        }

        byte* p = RootParamsAccess.ResolveOrAttachRoot(
            world, entity, behaviourHash, BehaviorConstants.MaxBehaviorParamByteSize,
            OccurrenceKind.BTree, out _);

        if (p == null)
            throw new System.InvalidOperationException(
                "the harness could not attach a root params slot — the entity's store had no room");

        return p;
    }

    /// <summary>
    /// A DTO projected at <paramref name="byteOffset"/> inside the entity's root params region — the
    /// EXACT expression the emitted thunks use, so a test reads what a thunk wrote.
    /// </summary>
    internal static ref T ReadDto<T>(EntityRepository world, Entity entity, int byteOffset)
        where T : unmanaged
        => ref System.Runtime.CompilerServices.Unsafe.As<byte, T>(
               ref System.Runtime.CompilerServices.Unsafe.AddByteOffset(
                   ref RootParamsAccess.RootRef(world, entity), (nint)byteOffset));

    /// <summary>
    /// ⭐ Every slot key currently attached to <paramref name="entity"/>'s occurrence store — authored
    /// and root alike, in slot order.
    /// </summary>
    internal static System.Collections.Generic.List<int> AttachedSlotKeys(
        EntityRepository world, Entity entity)
    {
        byte* mem = OccurrenceStoreAccess.TryGetStore(world, entity, out _);
        if (mem == null)
            throw new System.InvalidOperationException(
                "entity has no BlueprintBlackboard* tier component — its slots cannot be read");

        var keys = new System.Collections.Generic.List<int>();
        int n = BlueprintBlackboardPartitions.GetSlotCount(mem);
        for (int i = 0; i < n; i++)
        {
            int id = BlueprintBlackboardPartitions.GetSlot(mem, i).BlueprintId;
            if (id != 0) keys.Add(id);   // 0 = unused slot
        }
        return keys;
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-376</c> — assert WHICH slots are attached, never HOW MANY.</b>
    ///
    /// <para>🔴 <b>The defect this replaces, and it is worth stating because it recurred silently for
    /// two slices.</b> Four rails asserted <c>GetSlotCount(store) == 1</c>. That counts <b>every</b>
    /// occurrence slot, and since <c>O7c</c>-② *(<c>6f64208d6</c> — the BTree root cursor became a
    /// slot)* plus <c>P3-C</c> *(root params became a slot)* the true total is <b>authored + 2</b>.
    /// ⛔ The runtime was right and the assertion was stale, but a bare count cannot say which of the
    /// two it is — so the rails read as a product defect for two slices.</para>
    ///
    /// <para>⭐⭐ <b>Why a SET and not a corrected number.</b> Writing <c>Be(3)</c> would be true today
    /// and would rot the next time a root slot is added — exactly the failure being repaired. A set
    /// comparison NAMES the newcomer instead of absorbing it: add a third root slot and this fails
    /// saying <i>"unexpected key"</i>, which is a finding rather than an off-by-one.</para>
    ///
    /// <para>⚠ The root keys are DERIVED here, not hard-coded — <see cref="RootParamsAccess.KeyFor"/>
    /// and <see cref="RootStateAccess.KeyFor"/> are the same functions production resolves through.</para>
    /// </summary>
    /// <param name="expectedAuthoredKeys">
    /// The slot keys the asset's manifest should have provisioned. ⛔ Root slots are added by this
    /// helper; do NOT include them.
    /// </param>
    internal static void AssertAuthoredSlotsAre(
        EntityRepository world, Entity entity,
        System.Collections.Generic.IEnumerable<int> expectedAuthoredKeys,
        string because)
    {
        var expected = new System.Collections.Generic.HashSet<int>(expectedAuthoredKeys)
        {
            RootParamsAccess.KeyFor(world, entity),   // P3-C
            RootStateAccess.KeyFor(world, entity),    // O7c-② (6f64208d6)
        };

        var actual  = new System.Collections.Generic.HashSet<int>(AttachedSlotKeys(world, entity));
        var missing = new System.Collections.Generic.HashSet<int>(expected); missing.ExceptWith(actual);
        var extra   = new System.Collections.Generic.HashSet<int>(actual);   extra.ExceptWith(expected);

        if (missing.Count != 0 || extra.Count != 0)
            throw new Xunit.Sdk.XunitException(
                $"attached slot keys do not match the expectation — {because}.\n" +
                $"  missing (expected, not attached): [{string.Join(", ", missing)}]\n" +
                $"  unexpected (attached, not expected): [{string.Join(", ", extra)}]\n" +
                $"  NOTE: the two ROOT slots (root params, root cursor) are expected by construction; " +
                "an unexpected key here is a NEW slot someone added — name it, do not widen a count.");
    }

    /// <summary>
    /// ⭐⭐ <c>CE-437</c> — read a <c>Role=State, Scope=Behavior</c> variable from where it lives now:
    /// the behaviour's BLOCK (<c>def.BlackboardLayoutType</c>), at <c>St.{varName}</c>. Offsets come
    /// from the compiled block type by reflection, so this works for a DTO compiled in a test ALC.
    /// ⭐ Also asserts the root slot is wide enough for the whole block (<c>CE-429</c>).
    /// </summary>
    internal static ref T ReadBlockState<T>(EntityRepository world, Entity entity, BehaviorDefinition def, string varName)
        where T : unmanaged
    {
        var block = def.BlackboardLayoutType
            ?? throw new System.InvalidOperationException($"{def.Name} declares no BlackboardLayoutType");
        var st = block.GetField("St")
            ?? throw new System.InvalidOperationException($"{block.Name} has no St half — '{varName}' is not block-resident");
        int offset = (int)System.Runtime.InteropServices.Marshal.OffsetOf(block, "St")
                   + (int)System.Runtime.InteropServices.Marshal.OffsetOf(st.FieldType, varName);

        if (!RootParamsAccess.TryGetRootBytes(world, entity, out byte* root, out int length))
            throw new System.InvalidOperationException("entity has no root block");
        if (length < System.Runtime.InteropServices.Marshal.SizeOf(block))
            throw new System.InvalidOperationException($"root slot {length} B is narrower than {block.Name} (CE-429)");

        return ref System.Runtime.CompilerServices.Unsafe.AsRef<T>(root + offset);
    }
}
