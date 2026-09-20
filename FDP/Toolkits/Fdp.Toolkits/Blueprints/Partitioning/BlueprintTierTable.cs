using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Blueprints.Components;

namespace Fdp.Toolkit.Blueprints.Partitioning;

/// <summary>
/// ⭐⭐⭐ <b>THE tier ladder. One ordered list; everything that had a hand-rolled three-way chain
/// reads it.</b> <c>O3a</c> / task <c>B3</c> — 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §17.
///
/// <para><b>Adding a tier</b> — <c>O3b</c>'s 256 — is <b>one entry in <see cref="Ascending"/></b>,
/// plus its component struct and its <c>GlobalComponentIds</c> entry. ⛔ It must be APPENDED to
/// <see cref="BlackboardTier"/>, never inserted: §17.1 <c>N2</c> measured THREE enums spelling this
/// ladder and all three are ordinal, one of them <c>: byte</c> reaching compiled artefacts.</para>
///
/// <para>⛔⛔ <b>What must NOT move into this table.</b> Resolving <i>"this entity's store bytes"</i>
/// belongs to <see cref="OccurrenceStoreAccess"/>, and <i>"how full is it"</i> belongs to
/// <see cref="BlueprintBlackboardHeader"/>, which is self-describing. 📌 §17.1 <c>N3</c>: six
/// <c>BehaviorIngressSystem</c> helpers looked like tier branching and were not — routing them here
/// would have grown the table for nothing. ⭐ <b>The table answers "which TYPE", never "which
/// bytes".</b></para>
///
/// <para>⚠ <b>The ladder VALUES are unchanged by <c>B3</c>-① on purpose</b> (§17.6): the collapse
/// ships with 4 / 8 / 16 so that <i>"did the refactor change behaviour?"</i> has a provable answer.
/// The re-pick is <c>B3</c>-②, and it also has to move <c>Stage2_Validate</c>'s hard-coded
/// <c>928 / 3936 / 16096</c> budgets (§17.1 <c>N1</c>, §17.5).</para>
/// </summary>
public static class BlueprintTierTable
{
    /// <summary>
    /// ⭐ <b>Smallest first</b> — the order <see cref="Select"/> walks, so the first fit is the
    /// cheapest fit.
    /// </summary>
    public static IReadOnlyList<BlueprintTierSpec> Ascending { get; } = new[]
    {
        // ⚠ FIRST because this list is ordered by SIZE. ⛔ BlackboardTier.B256 is the enum's LAST
        //   member (3) because the ordinal is ABI and a new tier must be appended (§17.1 N2) —
        //   so the enum order and this order deliberately disagree. This list is the size order.
        BlueprintTierSpec.For<BlueprintBlackboard256>(
            BlackboardTier.B256,
            BlueprintBlackboard256.TotalSize,
            BlueprintBlackboard256.MaxSlots,
            BlueprintBlackboard256.PayloadSize),

        BlueprintTierSpec.For<BlueprintBlackboard1024>(
            BlackboardTier.B1024,
            BlueprintBlackboard1024.TotalSize,
            BlueprintBlackboard1024.MaxSlots,
            BlueprintBlackboard1024.PayloadSize),

        BlueprintTierSpec.For<BlueprintBlackboard4096>(
            BlackboardTier.B4096,
            BlueprintBlackboard4096.TotalSize,
            BlueprintBlackboard4096.MaxSlots,
            BlueprintBlackboard4096.PayloadSize),

        BlueprintTierSpec.For<BlueprintBlackboard16384>(
            BlackboardTier.B16384,
            BlueprintBlackboard16384.TotalSize,
            BlueprintBlackboard16384.MaxSlots,
            BlueprintBlackboard16384.PayloadSize),
    };

    /// <summary>
    /// ⭐⭐ <b>Largest first</b> — the PROBE order, and it is load-bearing rather than cosmetic: an
    /// entity carries AT MOST ONE tier, so the first match is authoritative. Every ladder this
    /// replaces probed largest-first, and <see cref="OccurrenceStoreAccess"/> documents why.
    /// </summary>
    public static IReadOnlyList<BlueprintTierSpec> Descending { get; } = BuildDescending();

    /// <summary>
    /// ⭐ The adjacent (smaller → larger) pairs a promotion can take, smallest pair first.
    /// <c>BlueprintMaintenanceSystem</c>'s two hand-written <c>UpgradeTier_A_to_B</c> methods are
    /// exactly this list. ⚠ With a 4th tier it grows to 3 entries and NO code changes.
    /// </summary>
    public static IReadOnlyList<(BlueprintTierSpec From, BlueprintTierSpec To)> AdjacentPairs { get; }
        = BuildAdjacentPairs();

    /// <summary>
    /// The spec for the tier <paramref name="entity"/> currently carries, or <see langword="null"/>
    /// when it carries none — ⛔ a normal, expected answer.
    /// </summary>
    public static BlueprintTierSpec? Of(EntityRepository repo, Entity entity)
    {
        var descending = Descending;
        for (int i = 0; i < descending.Count; i++)
            if (descending[i].Has(repo, entity))
                return descending[i];
        return null;
    }

    /// <summary>The spec whose <see cref="BlueprintTierSpec.TotalSize"/> is
    /// <paramref name="totalSize"/>. ⛔ Throws for an unknown size — a caller holding a tier size
    /// that names no tier is a bug, not a condition to swallow.</summary>
    public static BlueprintTierSpec ByTotalSize(int totalSize)
    {
        var ascending = Ascending;
        for (int i = 0; i < ascending.Count; i++)
            if (ascending[i].TotalSize == totalSize)
                return ascending[i];

        throw new ArgumentOutOfRangeException(
            nameof(totalSize), totalSize,
            $"No blueprint blackboard tier has TotalSize {totalSize}.");
    }

    /// <summary>The spec for a <see cref="BlackboardTier"/> value.</summary>
    public static BlueprintTierSpec ByTier(BlackboardTier tier)
    {
        var ascending = Ascending;
        for (int i = 0; i < ascending.Count; i++)
            if (ascending[i].Tier == tier)
                return ascending[i];

        throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown blackboard tier.");
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The smallest tier that fits BOTH axes</b>, or the largest when nothing fits.
    ///
    /// <para>⛔⛔ <b>Both axes, always.</b> 📌 §5a's <c>F3</c>: <c>MaxSlots</c> is 4 / 8 / 16 and a
    /// byte-only fit silently seats a behaviour whose slot table is already full. ⚠ And the ROOT
    /// occurrence is a slot that does not exist today, so after <c>O4</c> every tier loses one
    /// stateful slot to it.</para>
    ///
    /// <para>⚠ <b>Falling through to the largest tier is the PRESERVED behaviour</b>, not a new
    /// policy: <c>SelectTierForPayload</c> and <c>ChooseTier</c> both ended with an unconditional
    /// <c>return …16384</c>. The over-size case is caught downstream — <c>TryAttach</c> fails and
    /// <c>BlueprintMaterializationSystem</c> truncates with a log.</para>
    /// </summary>
    public static BlueprintTierSpec Select(int requiredPayload, int requiredSlots)
    {
        var ascending = Ascending;
        for (int i = 0; i < ascending.Count; i++)
        {
            var spec = ascending[i];
            if (requiredPayload <= spec.PayloadSize && requiredSlots <= spec.MaxSlots)
                return spec;
        }
        return ascending[ascending.Count - 1];
    }

    /// <summary>
    /// The smallest tier whose PAYLOAD holds <paramref name="stateSize"/>, ignoring the slot axis.
    ///
    /// <para>⚠ <b>Kept separate from <see cref="Select"/> deliberately.</b> It is what
    /// <c>BlueprintInstanceService.ChooseTier</c> did, and its caller is sizing ONE instance's state
    /// rather than a whole manifest — it has no slot count to offer. ⛔ Do not "improve" it into
    /// <see cref="Select"/> with <c>requiredSlots: 1</c>: that would change which tier a single large
    /// instance lands on the moment <c>W1</c> re-picks <c>MaxSlots</c>.</para>
    /// </summary>
    public static BlueprintTierSpec SelectByPayload(int stateSize)
    {
        var ascending = Ascending;
        for (int i = 0; i < ascending.Count; i++)
            if (stateSize <= ascending[i].PayloadSize)
                return ascending[i];
        return ascending[ascending.Count - 1];
    }

    /// <summary>
    /// The spec for the tier <paramref name="entity"/> carries IN A VIEW, or <see langword="null"/>.
    /// ⭐ The view form of <see cref="Of"/> — for the debug session and replay inspection, which read
    /// a snapshot rather than the live repository.
    /// </summary>
    public static BlueprintTierSpec? OfInView(ISimulationView view, Entity entity)
    {
        var descending = Descending;
        for (int i = 0; i < descending.Count; i++)
            if (descending[i].HasInView(view, entity))
                return descending[i];
        return null;
    }

    /// <summary>The largest tier — the cap every "does this asset fit at all?" check quotes.</summary>
    public static BlueprintTierSpec Largest => Ascending[Ascending.Count - 1];

    /// <summary>
    /// Registers every tier component on <paramref name="world"/>.
    /// ⚠ <c>BlueprintBlackboardTiers.RegisterAll</c> is the documented entry point and forwards here;
    /// its header carries the <c>CE-161</c> argument for why this is Hrot-wide.
    /// </summary>
    public static void RegisterAll(EntityRepository world)
        => RegisterUpTo(world, int.MaxValue);

    /// <summary>
    /// Registers every tier whose <see cref="BlueprintTierSpec.TotalSize"/> is at most
    /// <paramref name="maxTotalSize"/>.
    ///
    /// <para>⭐⭐ <b>Why a bounded form exists, and it is not test sugar.</b> A registered component
    /// costs a virtual-address reservation of <c>TotalSize × MAX_ENTITIES</c>; at
    /// <c>MAX_ENTITIES = 1 000 000</c> the 16384 tier alone reserves <b>~16 GB</b>, which exceeds
    /// <c>NativeMemoryAllocator</c>'s paranoid-mode cap. ⇒ several scratch worlds deliberately carry
    /// only the small tiers. ⛔ Before <c>B4</c> each of those spelled a hand-list of
    /// <c>RegisterComponent&lt;…&gt;</c> calls, and <b>every one of them broke the moment <c>O3b</c>
    /// appended the 256 tier</b> — 192 tests failed with <i>"Component BlueprintBlackboard256 is not
    /// registered"</i>. ⭐ A bound on SIZE keeps the deliberate exclusion while still being
    /// table-driven, so the next tier is picked up automatically.</para>
    /// </summary>
    public static void RegisterUpTo(EntityRepository world, int maxTotalSize)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));

        var ascending = Ascending;
        for (int i = 0; i < ascending.Count; i++)
            if (ascending[i].TotalSize <= maxTotalSize)
                ascending[i].Register(world);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>THE promotion body: add the larger, copy, remove the smaller.</b> <c>O3b</c> / task
    /// <c>B4</c> — 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §17.7.
    ///
    /// <para>🔴🔴 <b>Why it had to become a shared member.</b> §17.1's inventory found THREE copies of
    /// this three-line sequence (<c>BehaviorIngressSystem.UpgradeTier</c>,
    /// <c>BlueprintMaintenanceSystem.UpgradePair</c>, <c>EntityBlueprintsPanel.UpgradeTier</c>) and
    /// <c>B3</c>-① left them as three, because each was already correct. ⛔ <c>B4</c> found a FOURTH
    /// site that needs it — <c>BlueprintInstanceService.AttachToEntity</c> — and adding a fourth copy
    /// is what ruling 9 forbids. ⭐ One body; the callers keep their own eligibility rules.</para>
    ///
    /// <para>⛔⛔ <c>CopyToLargerTier</c> is where <c>H1</c> lives — it carries the header's
    /// <c>Reserved</c>, which since <c>A3</c> holds the per-slot <c>Kind</c> nibble array. Rail
    /// <c>A3_R2</c> pins it, and every caller must keep going THROUGH this helper: a hand-rolled copy
    /// zeroes every slot's kind and the tick walker then skips the entity entirely.</para>
    ///
    /// <para>⚠ A no-op when <paramref name="to"/> is not larger, or when the entity does not carry
    /// <paramref name="from"/> — both were guards the callers already had, hoisted here so the fourth
    /// caller cannot forget them.</para>
    /// </summary>
    public static unsafe void Promote(
        EntityRepository repo, Entity entity, BlueprintTierSpec from, BlueprintTierSpec to)
    {
        if (repo is null) throw new ArgumentNullException(nameof(repo));
        if (from is null) throw new ArgumentNullException(nameof(from));
        if (to   is null) throw new ArgumentNullException(nameof(to));

        if (to.TotalSize <= from.TotalSize) return;   // ⛔ no downgrade path, ever
        if (!from.Has(repo, entity)) return;          // nothing to carry over

        if (!to.Has(repo, entity))
            to.Add(repo, entity);

        // ⚠ Both pointers are resolved AFTER the add and used within this call only — the seam's
        //   LIFETIME RULE. An add can move the source chunk, so a pointer taken before it is stale.
        BlueprintBlackboardPartitions.CopyToLargerTier(
            from.Memory(repo, entity), from.TotalSize,
            to.Memory(repo, entity),   to.TotalSize, (byte)to.MaxSlots);

        from.Remove(repo, entity);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The tier an attach must land on, given what the entity ALREADY carries — never a
    /// second store, never a downgrade.</b> <c>O3b</c> / task <c>B4</c> —
    /// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §17.7.
    ///
    /// <para>🔴🔴 <b>The invariant this protects</b> is the one <see cref="OccurrenceStoreAccess"/>
    /// documents and every consumer reads through: <i>an entity carries AT MOST ONE tier</i>.
    /// ⛔ Two sites picked a tier from the CONTENT alone — <c>BlueprintInstanceService.AttachToEntity</c>
    /// (one instance's <c>StateSize</c>) and <c>BlueprintMaterializationSystem</c> (a scenario's
    /// aggregate) — and then added that component. Whenever the pick differed from the tier already
    /// present, the entity ended up with <b>two</b> stores and the largest-first probe order decided
    /// which one was authoritative, silently orphaning the other's slots.</para>
    ///
    /// <para>⚠ <b>Before <c>O3b</c> this was reachable only UPWARDS</b> (a big instance landing on a
    /// 1024-carrying entity) and left the smaller store stranded. ⭐ The 256 tier made the DOWNGRADE
    /// direction the common case — every small instance on a 1024 entity — which is how it surfaced.
    /// 📌 Neither direction was covered by a rail; <c>B4_R3</c>/<c>B4_R4</c> now pin both.</para>
    ///
    /// <para>⛔ It does NOT promise the content fits: <c>TryAttach</c> still reports slot exhaustion
    /// and fragmentation, and the over-size case still truncates downstream. This decides only WHICH
    /// store the attach writes into.</para>
    /// </summary>
    public static BlueprintTierSpec EnsureAtLeast(
        EntityRepository repo, Entity entity, BlueprintTierSpec required)
    {
        if (required is null) throw new ArgumentNullException(nameof(required));

        var current = Of(repo, entity);
        if (current == null)
            return required;

        // ⭐ The store the entity already has is big enough — use it, never downgrade.
        if (required.TotalSize <= current.TotalSize)
            return current;

        // ⚠ It genuinely is not. PROMOTE — carrying the existing slots and their Kind nibbles —
        //   rather than bolting a second component on beside it.
        Promote(repo, entity, current, required);
        return required;
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Is <paramref name="a"/> a LARGER tier than <paramref name="b"/>?</b> <c>O3b</c> /
    /// task <c>B4</c> — 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §17.7.
    ///
    /// <para>⛔⛔ <b>Never compare <see cref="BlackboardTier"/> values with <c>&gt;</c>.</b> The enum's
    /// ordinal is <b>ABI</b> (§17.1 <c>N2</c>), so a new tier must be APPENDED — which means
    /// <c>B256 = 3</c> is the enum's LAST member while being the ladder's SMALLEST tier. ⇒ the
    /// ordinal order and the size order deliberately disagree, and <c>B256 &gt; B1024</c> is
    /// <c>true</c> by ordinal and <c>false</c> by size.</para>
    ///
    /// <para>📌 That is not hypothetical: two ordinal comparisons in
    /// <c>EntityBlueprintsEditModel</c> read a DOWNGRADE to 256 as <i>"upgrade needed"</i> and put
    /// it in the commit plan. ⭐ Pinned by <c>B4_R5</c>, which asserts the two orders really do
    /// disagree — so this helper cannot be "simplified" back into <c>&gt;</c>.</para>
    /// </summary>
    public static bool IsLargerThan(BlackboardTier a, BlackboardTier b)
        => ByTier(a).TotalSize > ByTier(b).TotalSize;

    private static BlueprintTierSpec[] BuildDescending()
    {
        var ascending = Ascending;
        var result = new BlueprintTierSpec[ascending.Count];
        for (int i = 0; i < ascending.Count; i++)
            result[i] = ascending[ascending.Count - 1 - i];
        return result;
    }

    private static (BlueprintTierSpec From, BlueprintTierSpec To)[] BuildAdjacentPairs()
    {
        var ascending = Ascending;
        var result = new (BlueprintTierSpec, BlueprintTierSpec)[ascending.Count - 1];
        for (int i = 0; i < ascending.Count - 1; i++)
            result[i] = (ascending[i], ascending[i + 1]);
        return result;
    }
}
