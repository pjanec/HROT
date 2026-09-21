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
        world.AddComponent(entity, new BrainBTreeState());

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
}
