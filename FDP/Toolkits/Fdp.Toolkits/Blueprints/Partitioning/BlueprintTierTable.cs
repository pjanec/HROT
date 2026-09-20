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
    {
        if (world is null) throw new ArgumentNullException(nameof(world));

        var ascending = Ascending;
        for (int i = 0; i < ascending.Count; i++)
            ascending[i].Register(world);
    }

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
