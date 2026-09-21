using System;
using Fdp.Core;
using Fdp.Toolkit.Blueprints.Partitioning;

namespace Fdp.Toolkit.Behavior;

/// <summary>
/// ⭐⭐⭐ <b><c>P3</c> — THE ONE WAY TO *LOCATE* A BEHAVIOUR'S ROOT PARAMS.</b>
///
/// <para>⚠ <b>Distinct from <see cref="BehaviorParams"/>, deliberately.</b> That type is <c>G1</c>'s
/// SUPPLY seam — it composes deserialize+resolve into a <c>ParseParamsDelegate</c>. This one answers
/// <i>"where do this entity's root params LIVE"</i>. ⛔ Same subject, different question; folding them
/// into one type would make a JSON helper a storage authority.</para>
/// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §29.6/§29.7.
///
/// <para>🔴 <b>What this replaces.</b> Until <c>P3</c> the root behaviour's params lived in
/// <c>BrainBlackboard.BehaviorParameters</c> and every reader projected them itself —
/// <c>JoinFormationExecutor</c>, <c>PredicateCompiler</c>, the BTree adapters, the editor's live
/// value providers, the renderers. ⛔ Six spellings of one lookup is the shape
/// <c>OccurrenceSlotKey</c> exists to prevent, and it is why this type is a seam rather than a
/// helper someone may bypass.</para>
///
/// <para>⭐⭐ <b>Generated code uses this too, for the ROOT path</b> (user, <c>2026-09-21</c>). The
/// root key is a RUNTIME value — <c>BehaviorState.ActiveBehaviorHash</c> — so nothing needs baking
/// into a thunk. ⛔ Per-SITE occurrences stay inlined in the emitters: their identity comes from the
/// kernel's <c>writer</c> stamp, which no generic accessor can see.</para>
///
/// <para>⚠ <b>The layout guard is the params BYTE SIZE</b>, not a compiled structure hash. A root
/// params region IS the packed variable table, so its size is what changes when the table changes;
/// a different behaviour is already a different key.</para>
/// </summary>
public static unsafe class RootParamsAccess
{
    /// <summary>
    /// ⭐ The slot key for this entity's CURRENT root behaviour, or <c>0</c> when it has none.
    /// ⛔ Computed, never stored — see <c>OccurrenceSlotKey.ComputeRootParamsKey</c>.
    /// </summary>
    public static int KeyFor(EntityRepository world, Entity self)
    {
        if (!world.HasComponent<Components.BehaviorState>(self)) return 0;
        ref readonly var state = ref world.GetComponentRO<Components.BehaviorState>(self);
        return state.ActiveBehaviorHash == 0
            ? 0
            : Shared.OccurrenceSlotKey.ComputeRootParamsKey(state.ActiveBehaviorHash);
    }

    /// <summary>The same key from an explicit behaviour hash — for ingress, which has it in hand.</summary>
    public static int KeyForBehaviour(int behaviourHash)
        => behaviourHash == 0 ? 0 : Shared.OccurrenceSlotKey.ComputeRootParamsKey(behaviourHash);

    /// <summary>
    /// ⭐⭐⭐ <b>The root params region as a typed struct.</b> Returns <c>false</c> — leaving
    /// <paramref name="ptr"/> null — when the entity has no behaviour, no occurrence store, or no
    /// root params slot yet.
    ///
    /// <para>⛔⛔ <b>It does NOT attach.</b> Attaching is ingress's job and happens once per behaviour
    /// assign; a reader that attached on miss would silently manufacture a zero-filled params region
    /// and hide the real fault — which is the silent-default shape this programme keeps filing.</para>
    /// </summary>
    public static bool TryGetRoot<T>(EntityRepository world, Entity self, out T* ptr)
        where T : unmanaged
    {
        ptr = null;

        int key = KeyFor(world, self);
        if (key == 0) return false;

        byte* store = OccurrenceStoreAccess.TryGetStore(world, self, out _);
        if (store == null) return false;

        if (!BlueprintBlackboardPartitions.TryGetSlotOffset(store, key, out int offset, out _))
            return false;

        ptr = (T*)(store + offset);
        return true;
    }

    /// <summary>
    /// ⭐ The root params region as raw bytes — for the inspector surfaces, which project a DTO whose
    /// type they only know by reflection.
    /// </summary>
    public static bool TryGetRootBytes(EntityRepository world, Entity self, out byte* ptr)
    {
        ptr = null;

        int key = KeyFor(world, self);
        if (key == 0) return false;

        byte* store = OccurrenceStoreAccess.TryGetStore(world, self, out _);
        if (store == null) return false;

        if (!BlueprintBlackboardPartitions.TryGetSlotOffset(store, key, out int offset, out _))
            return false;

        ptr = store + offset;
        return true;
    }

    /// <summary>
    /// ⭐⭐ <b>Attach-or-find, for INGRESS only.</b> The one place a root params slot is created.
    /// ⚠ <paramref name="paramsBytes"/> is the packed table's size, which doubles as the layout guard.
    /// ⚠ <paramref name="kind"/> follows the entity's BRAIN TIER — there is no "Behavior" kind, and
    /// inventing one would put a fourth value in an enum whose three existing ones already classify
    /// every occurrence the walker filters on (<c>A3</c>/<c>D1′</c>).
    /// </summary>
    public static byte* ResolveOrAttachRoot(
        EntityRepository world, Entity self, int behaviourHash, int paramsBytes,
        OccurrenceKind kind, out bool freshlyAttached)
    {
        freshlyAttached = false;

        int key = KeyForBehaviour(behaviourHash);
        if (key == 0 || paramsBytes <= 0) return null;

        byte* store = OccurrenceStoreAccess.TryGetStore(world, self, out _);
        if (store == null) return null;

        ulong guard = unchecked((ulong)paramsBytes);

        if (BlueprintBlackboardPartitions.TryGetSlotOffset(store, key, out int offset, out uint existing))
        {
            if (existing == (uint)guard) return store + offset;
            BlueprintBlackboardPartitions.TryDetach(store, key);
        }

        if (!BlueprintBlackboardPartitions.TryAttach(
                store, key, paramsBytes, guard, kind, out int newOffset))
            return null;

        freshlyAttached = true;
        return store + newOffset;
    }
}
