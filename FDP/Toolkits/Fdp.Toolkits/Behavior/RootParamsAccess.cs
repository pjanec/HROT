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
        => TryGetRootBytes(world, self, out ptr, out _);

    /// <summary>
    /// ⭐⭐ The same, also reporting HOW MANY bytes the region holds.
    ///
    /// <para>⭐ <b>The length is the slot's own layout guard</b>, which for a root params slot IS its
    /// byte count — <see cref="ResolveOrAttachRoot"/> stores <c>paramsBytes</c> there deliberately
    /// (a root params region is the packed variable table, so its size is what changes when the table
    /// does). ⇒ a reader gets the extent of THIS entity's region without knowing the behaviour.</para>
    ///
    /// <para>⛔⛔ <b>Ingress needs exactly this, and getting it wrong overreads.</b> When it seeds its
    /// parse shadow it is looking at the PREVIOUS behaviour's slot while computing for the NEW one;
    /// copying the new behaviour's extent out of the old slot walks into whatever occurrence was
    /// attached after it.</para>
    /// </summary>
    public static bool TryGetRootBytes(EntityRepository world, Entity self, out byte* ptr, out int length)
    {
        ptr = null;
        length = 0;

        int key = KeyFor(world, self);
        if (key == 0) return false;

        byte* store = OccurrenceStoreAccess.TryGetStore(world, self, out _);
        if (store == null) return false;

        if (!BlueprintBlackboardPartitions.TryGetSlotOffset(store, key, out int offset, out uint guard))
            return false;

        ptr = store + offset;
        length = unchecked((int)guard);
        return true;
    }

    /// <summary>
    /// ⭐⭐ <b>The read-only <see cref="Fdp.ModuleHost.Abstractions.ISimulationView"/> form</b>, for
    /// gizmos, ImGui renderers and the debug API — surfaces that are handed a view and may be looking
    /// at a SNAPSHOT, so they must not cast to <see cref="EntityRepository"/>.
    ///
    /// <para>⚠ Deliberately a <c>Try</c>: a debug surface draws what is there. ⛔ The loud
    /// <see cref="RequireRootBytes"/> is for EXECUTION paths, where a missing region is a fault.</para>
    /// </summary>
    public static bool TryGetRootBytesInView(
        Fdp.ModuleHost.Abstractions.ISimulationView view, Entity self, out byte* ptr)
    {
        ptr = null;

        if (!view.HasComponent<Components.BehaviorState>(self)) return false;
        ref readonly var state = ref view.GetComponentRO<Components.BehaviorState>(self);
        if (state.ActiveBehaviorHash == 0) return false;

        int key = Shared.OccurrenceSlotKey.ComputeRootParamsKey(state.ActiveBehaviorHash);

        byte* store = OccurrenceStoreAccess.TryGetStoreInView(view, self, out _);
        if (store == null) return false;

        if (!BlueprintBlackboardPartitions.TryGetSlotOffset(store, key, out int offset, out _))
            return false;

        ptr = store + offset;
        return true;
    }

    /// <summary>
    /// ⭐⭐⭐ <b>THE ANCHOR EVERY READER PROJECTS FROM — a <c>ref byte</c> at the base of this entity's
    /// root params region.</b> Replaces <c>ref bb.BehaviorParameters[0]</c>, one for one.
    ///
    /// <para>⛔⛔ <b>It THROWS rather than returning a null ref, and that is the whole design.</b> Until
    /// <c>P3-C</c> a missing region was impossible — the component was always there and always
    /// readable, so every reader was written assuming success. ⇒ handing back zeros on a miss would
    /// turn "this entity's store had no room" into "the behaviour was authored with all-default
    /// params", which is unobservable at runtime and exactly the silent-default shape this programme
    /// keeps filing. ⚠ The message names the three real causes so the fault is actionable.</para>
    ///
    /// <para>⭐ <b>Generated code calls THIS</b>, through <c>BlackboardParamsExpression</c> — the one
    /// home for the projection text (<c>BP-306</c>). ⛔ Per-SITE occurrences do not: their identity is
    /// the kernel's <c>writer</c> stamp, which no world+entity accessor can see.</para>
    /// </summary>
    public static ref byte RootRef(EntityRepository world, Entity self)
        => ref System.Runtime.CompilerServices.Unsafe.AsRef<byte>(RequireRootBytes(world, self));

    /// <summary>⭐ The same anchor as a raw pointer, for callers already in pointer arithmetic.</summary>
    public static byte* RequireRootBytes(EntityRepository world, Entity self)
        => RequireRootBytes(world, self, out _);

    /// <summary>
    /// ⭐⭐ <b>The anchor AND its EXTENT.</b> 🔴 <c>CE-305</c>: a caller that bounds-checks reads against
    /// the region must be told how wide it actually is.
    ///
    /// <para>⛔⛔ <b>Why a constant is wrong here now.</b> Until <c>P3-C</c> the region was always
    /// <see cref="BehaviorConstants.MaxBehaviorParamByteSize"/> — a <c>fixed byte[100]</c> that existed
    /// whether the behaviour declared params or not — so passing that constant as the length was
    /// correct by construction. ⇒ the region is now <see cref="RootParamsBytes"/> wide (52 for
    /// <c>PlatoonHillAttack</c>, 16 for <c>MoveToLocation</c>), and a 100-byte bound lets a read run
    /// off the end of the slot into whatever occurrence the allocator put after it.</para>
    ///
    /// <para>⚠ <b>This is §29.10's defect exactly</b> — <i>"§29.6 specified the ANCHOR and never the
    /// EXTENT"</i> — met on the one path that carried an extent at all.</para>
    /// </summary>
    public static byte* RequireRootBytes(EntityRepository world, Entity self, out int length)
    {
        if (TryGetRootBytes(world, self, out byte* ptr, out length)) return ptr;

        length = 0;
        throw new InvalidOperationException(
            $"Entity {self.Index} has no ROOT PARAMS slot, so its behaviour parameters cannot be read. " +
            "Causes, in the order worth checking: (1) no behaviour is assigned — BehaviorState." +
            "ActiveBehaviorHash is 0; (2) the entity carries no occurrence store, because its brain " +
            "tier is neither BTree nor HSM, or the BlueprintBlackboard* tier components were never " +
            "registered on this world; (3) the store had no room at assign time, which means the tier " +
            "demand under-counted (see CE-302 / RootParamsCost). " +
            "Before P3-C this read came from BrainBlackboard.BehaviorParameters and could not fail.");
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

    /// <summary>
    /// ⭐ <b>Drop a behaviour's root params slot — for INGRESS only, on a behaviour CHANGE.</b>
    ///
    /// <para>⛔⛔ <b>Why this is not covered by <c>DetachHostedOccurrenceSlots</c>.</b> That sweep
    /// takes slots whose kind is <c>Hsm</c> or <c>Blueprint</c>; a root params slot on a BTree brain
    /// declares <c>OccurrenceKind.BTree</c> (<c>KindOf</c> follows the brain tier, <c>A3</c>/<c>D1′</c>)
    /// and is therefore invisible to it. ⇒ without this, each behaviour change leaks one slot.</para>
    ///
    /// <para>⚠ Keyed by the OLD behaviour hash, so it cannot touch the new behaviour's slot even when
    /// the two are attached in the same frame.</para>
    /// </summary>
    public static bool DetachRoot(EntityRepository world, Entity self, int behaviourHash)
    {
        int key = KeyForBehaviour(behaviourHash);
        if (key == 0) return false;

        byte* store = OccurrenceStoreAccess.TryGetStore(world, self, out _);
        if (store == null) return false;

        return BlueprintBlackboardPartitions.TryDetach(store, key);
    }

    /// <summary>
    /// ⭐⭐ <b>How many bytes this behaviour's root params region occupies.</b>
    ///
    /// <para>⭐ The EXTENT of the packed variable table — <c>max(ByteOffset + sizeof(Type))</c> over
    /// the manifest — not <c>MaxBehaviorParamByteSize</c>. ⛔ Allocating the full 100 bytes for every
    /// entity would waste most of a slot on the 256 tier, where the whole payload is 176 bytes.</para>
    ///
    /// <para>⚠ Falls back to the curated <c>BlackboardLayoutType</c>'s size when there is no manifest
    /// — that is the shape a hand-registered behaviour has. ⛔ Returns 0 when neither exists, which
    /// correctly means "this behaviour has no params" rather than "allocate something just in case".</para>
    /// </summary>
    public static int RootParamsBytes(BehaviorDefinition def)
    {
        if (def is null) return 0;

        var manifest = def.ManagedBlackboardVariables;
        if (manifest != null && manifest.Count > 0)
        {
            int extent = 0;
            for (int i = 0; i < manifest.Count; i++)
            {
                var v = manifest[i];
                if (v.Type == null) continue;
                int end = v.ByteOffset + System.Runtime.InteropServices.Marshal.SizeOf(v.Type);
                if (end > extent) extent = end;
            }
            return extent;
        }

        if (def.BlackboardLayoutType != null)
            return System.Runtime.InteropServices.Marshal.SizeOf(def.BlackboardLayoutType);

        // 🔴🔴 MEASURED AT THE CUT (2026-09-21) — and the original comment here was WRONG.
        //   It read "returns 0 when neither exists, which correctly means 'this behaviour has no
        //   params'". ⛔ That is only true when there is no PARSER. A behaviour that declares a
        //   ParseParams and NEITHER a manifest NOR a layout type does have params — it simply never
        //   said how wide they are — and before the cut it did not have to, because the whole
        //   100-byte BrainBlackboard region was there whether anyone declared it or not.
        // ⇒ reserve the documented MAXIMUM rather than nothing. ⭐ That reproduces the old
        //   behaviour exactly; ⛔ returning 0 would silently drop the parse on the floor, which is
        //   how BehaviorIngress_ParsesFleeBlackboard_FromJson found this.
        // ⚠ It costs a full-width slot for an under-declared behaviour. Accepted: every behaviour in
        //   the shipped corpus declares one of the two, so this arm is the escape hatch for
        //   hand-registered and test behaviours, not a path production takes.
        return def.ParseParams != null ? BehaviorConstants.MaxBehaviorParamByteSize : 0;
    }
}
