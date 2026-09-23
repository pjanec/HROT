namespace Fdp.Toolkit.Blueprints.Shared
{
    /// <summary>
    /// ⭐⭐⭐ <b>THE tier ladder's NUMBERS, in the ONE spelling both sides of the netstandard/net8
    /// wall agree on.</b> <c>O3a</c> / task <c>B3②</c> — 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c>
    /// §17.1 <c>N1</c> and §17.5.
    ///
    /// <para><b>Why this file exists.</b> 📐 Measured <c>2026-09-20</c>: <c>Stage2_Validate.cs</c>
    /// hard-coded the payload budgets <c>928 / 3936 / 16096</c> as <b>integer literals</b> — a fourth
    /// copy of the ladder, in <c>Hrot.Blueprints.Compiler</c>, which targets
    /// <c>netstandard2.0;net8.0</c> and references <c>Fdp.Toolkits</c> <b>only under net8.0</b>. ⇒ it
    /// <i>cannot</i> see <c>BlueprintBlackboard*.PayloadSize</c>, so re-picking <c>MaxSlots</c> would
    /// have silently desynced compile-time validation from runtime capacity: the compiler would keep
    /// accepting an asset the runtime can no longer seat, or reject one it could.</para>
    ///
    /// <para>⭐ <b>The fix is <c>A1</c>'s own precedent</b> (<c>BP-306</c>): an <c>internal</c>,
    /// netstandard2.0-subset source file <b>LINKED</b> into the assembly that cannot reference the
    /// toolkit — exactly what <c>Fdp.Toolkit.Behavior.Shared.OccurrenceSlotKey</c> does for the slot
    /// key. ⛔ <b>The NUMBERS travel; the behaviour does not.</b> <see cref="BlueprintTierSpec"/> needs
    /// <c>EntityRepository</c> and stays net8.0-only.</para>
    ///
    /// <para>⛔⛔ <b>This file is the source; the tier structs READ it.</b>
    /// <c>BlueprintBlackboard{1024,4096,16384}</c> derive their <c>MaxSlots</c> from here rather than
    /// declaring their own, so there is one number and not two. Rail
    /// <c>B3_R8_TheTierStructsAgreeWithTheLinkedLadderFile</c> pins that, and
    /// <c>B3_R9</c> pins the constants this file must mirror from the allocator.
    /// ⚠ Both live in <c>Fdp.Toolkits.Tests</c>, not in <c>Hrot.Blueprints.Tests</c>: that assembly
    /// holds <c>InternalsVisibleTo</c> from BOTH sides of the link and so sees two copies of this
    /// type (<c>CS0433</c>).</para>
    ///
    /// <para>⚠ <b>netstandard2.0 subset on purpose</b>: block-scoped namespace, <c>const</c> only, no
    /// modern syntax. ⛔ Do not add types, arrays or methods here — a linked file that needs the
    /// runtime is the thing this file exists to avoid.</para>
    /// </summary>
    internal static class BlueprintTierLadder
    {
        /// <summary>Mirrors <c>BlueprintBlackboardHeader</c>'s <c>[StructLayout(Size = 32)]</c>.</summary>
        public const int HeaderSize = 32;

        /// <summary>Mirrors <c>BlueprintBlackboardPartitions.SlotEntrySize</c>.</summary>
        public const int SlotEntrySize = 16;

        /// <summary>
        /// ⛔⛔ <b>The hard ceiling on any tier's <c>MaxSlots</c></b>, mirroring
        /// <c>BlueprintBlackboardPartitions.MaxKindSlots</c>. <c>A3</c>'s per-slot <c>Kind</c> nibble
        /// array is <b>4 bits × 16</b> in the header's 8-byte <c>Reserved</c> — an exact fit. A tier
        /// with more slots has slots whose kind cannot be recorded, and <c>BlueprintTickSystem</c>
        /// filters ON the declared kind ⇒ they would be <b>silently skipped</b>, not rejected.
        /// </summary>
        public const int MaxKindSlots = 16;

        // ── the ladder, smallest first ──────────────────────────────────────────────────────────
        //
        // 📐 RE-PICKED 2026-09-20 (task B3②, PLAN W1). The previous 4 / 8 / 16 was inherited, not
        //    chosen, and it was the ONLY reason a 9-occurrence entity reached for 16 KB.
        //
        //    MEASURED over the whole corpus before changing it (design §17, "B3②'s PRE-MEASUREMENT"):
        //      · behaviour manifests, 30 assets  -> max required payload 320 B
        //      · blueprint Instances, 41 defs    -> max StateSize        128 B
        //      · assets in the 801-928 B band that 12 slots would displace: ZERO, in both
        //    ⇒ the payload this costs is free on real content, and the slots it buys are decisive:
        //      PlatoonHillAttack2 holds 8 slots (9 after O4's root) at 320 B and is promoted to
        //      16384 ON SLOT COUNT ALONE, with payload 50x below that tier. At 12 slots the 1024
        //      tier seats it -- 16x less memory for the worst case in the corpus.
        //
        // ⛔ MaxSlots must be NON-DECREASING up the ladder. A larger tier offering FEWER slots would
        //    make promotion a capacity REDUCTION, and CopyToLargerTier would be handed a dstMaxSlots
        //    smaller than the slot count it is copying.
        //    ⭐ Pinned inside rail B3_R1 (the ladder-ordering rail), which asserts MaxSlots is
        //      non-decreasing alongside TotalSize and PayloadSize. ⚠ NON-decreasing, not strictly:
        //      MaxKindSlots caps the ladder at 16, so the top tiers legitimately share a value.

        /// <summary>
        /// ⭐⭐ <b>The SMALLEST tier — <c>O3b</c> / task <c>B4</c>.</b> It exists to price the simple
        /// case: one root occurrence and at most a couple of stateful slots.
        /// </summary>
        public const int Tier256TotalSize = 256;

        /// <summary>
        /// ⭐⭐⭐ <b>3, and that OVERTURNED the plan's lean of 2</b> *(PLAN <c>W4</c>)*.
        ///
        /// <para>📐 <c>W4</c> reasoned from slot COUNT alone — <i>"77 % of behaviours need ≤ 2"</i>.
        /// Measured with the BYTES included (design §17, "B4's PRE-MEASUREMENT"), over all 30
        /// generated behaviours, counting each asset's root occurrence plus every stateful slot:
        /// <c>@1</c> 56 % · <c>@2</c> 73 % · <b><c>@3</c> 83 %</b> · <c>@4</c> 80 % · <c>@6</c> 60 %.
        /// ⇒ <b>3 is a genuine maximum</b>, not a marginal preference.</para>
        ///
        /// <para>⚠ That measurement is POST-<c>O4</c> arithmetic for a root occurrence not yet built
        /// — ⭐ re-measure once <c>O4</c> lands. It holds pre-<c>O4</c> too, where the root does not
        /// exist and every asset needs strictly less.</para>
        /// </summary>
        public const int Tier256MaxSlots = 3;

        /// <summary><c>256 − 32 − 3×16</c> = <b>176 B</b>.</summary>
        public const int Tier256PayloadSize =
            Tier256TotalSize - HeaderSize - Tier256MaxSlots * SlotEntrySize;

        /// <summary>Small tier — total bytes.</summary>
        public const int Tier1024TotalSize = 1024;

        /// <summary>⭐ 4 → <b>12</b>: costs 128 B of payload, buys 8 slots. See the note above.</summary>
        public const int Tier1024MaxSlots = 12;

        /// <summary>928 → <b>800</b>. Measured: nothing in the corpus is displaced.</summary>
        public const int Tier1024PayloadSize =
            Tier1024TotalSize - HeaderSize - Tier1024MaxSlots * SlotEntrySize;

        /// <summary>Medium tier — total bytes.</summary>
        public const int Tier4096TotalSize = 4096;

        /// <summary>
        /// ⭐ 8 → <b>16</b>. ⛔ Not optional: with the small tier at 12, a medium tier at 8 would be a
        /// capacity REDUCTION on the slots axis. 16 is the ceiling, so this is the largest legal value.
        /// </summary>
        public const int Tier4096MaxSlots = 16;

        /// <summary>3936 → <b>3808</b>.</summary>
        public const int Tier4096PayloadSize =
            Tier4096TotalSize - HeaderSize - Tier4096MaxSlots * SlotEntrySize;

        /// <summary>Large tier — total bytes.</summary>
        public const int Tier16384TotalSize = 16384;

        /// <summary>Unchanged at the ceiling.</summary>
        public const int Tier16384MaxSlots = 16;

        /// <summary>Unchanged: <c>16096</c>.</summary>
        public const int Tier16384PayloadSize =
            Tier16384TotalSize - HeaderSize - Tier16384MaxSlots * SlotEntrySize;
    }
}
