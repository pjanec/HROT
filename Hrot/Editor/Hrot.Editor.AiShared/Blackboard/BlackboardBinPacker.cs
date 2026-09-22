using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Hrot.Editor.AiShared.Blackboard;

/// <summary>
/// Describes a single blackboard variable to be packed: its name and CLR type.
/// </summary>
/// <param name="Name">The variable identifier.</param>
/// <param name="FieldType">The CLR type used to determine size and alignment.</param>
public record BlackboardVariableDescriptor(string Name, Type FieldType);

/// <summary>
/// Warning flags produced by <see cref="BlackboardBinPacker.Pack"/>.
///
/// <para>⭐⭐ <c>CE-314</c> (2026-09-22): <c>HeavyMemoryExceeded</c> is GONE with the heavy tier.
/// ⚠ <c>InlineMemoryExceeded</c> keeps its name although there is no longer a second tier for it to
/// contrast with — "inline" now simply means <i>the params region</i>. Renaming it reaches the panel,
/// four test files and a system-test golden, and is cosmetic; filed rather than bundled here.</para>
/// </summary>
public enum PackWarning
{
    /// <summary>No warnings.</summary>
    None,

    /// <summary>Total bytes exceed <see cref="BlackboardBinPacker.MaxInlineBytes"/>.</summary>
    InlineMemoryExceeded,
}

/// <summary>
/// A single variable after bin-packing: resolved byte offset and size.
///
/// <para>⛔ <c>CE-314</c>: the <c>Tier</c> member is GONE. It selected between the inline region and
/// <c>Blackboard1024</c>, which <c>P4</c>-① deleted — ⚠ a two-valued enum whose second value named
/// storage that no longer exists.</para>
/// </summary>
/// <param name="Name">The variable identifier.</param>
/// <param name="FieldType">The CLR type.</param>
/// <param name="ByteOffset">Byte offset from the start of the params region.</param>
/// <param name="ByteSize">Unmanaged size of the field in bytes.</param>
public record PackedVariable(
    string Name,
    Type FieldType,
    int ByteOffset,
    int ByteSize);

/// <summary>
/// Result of a <see cref="BlackboardBinPacker.Pack"/> call.
/// </summary>
/// <param name="Variables">All packed variables in declaration order.</param>
/// <param name="TotalInlineBytes">Total bytes consumed by the params region (including padding).</param>
/// <param name="Warning">
/// <see cref="PackWarning.InlineMemoryExceeded"/> when <see cref="TotalInlineBytes"/> exceeds
/// <see cref="BlackboardBinPacker.MaxInlineBytes"/>.
/// </param>
public record PackResult(
    IReadOnlyList<PackedVariable> Variables,
    int TotalInlineBytes,
    PackWarning Warning);

/// <summary>
/// Computes sequential byte offsets for blackboard variables using C# struct-alignment rules,
/// bounded by the params ceiling (<see cref="BlackboardBinPacker.MaxInlineBytes"/>).
///
/// <para>⭐⭐⭐ <b><c>CE-314</c> (2026-09-22) — THE INLINE/HEAVY SPLIT IS GONE.</b> This was a TWO-TIER
/// packer: variables that did not fit the 100-byte inline region spilled to a "heavy" tier addressing
/// <c>Blackboard1024</c>. ⛔ <c>P4</c>-① deleted that component, so the spill wrote offsets into
/// storage <b>no production site provisions</b> — and the authoring window surfaced
/// <c>RequiresHeavyComponent</c> for a component that cannot exist. 📄 §30.15 ruled it: <i>"the 'heavy
/// vs inline' split does not exist"</i>.</para>
///
/// <para>⭐ There is ONE region now, sized to itself and promoted up the occurrence tier ladder at
/// runtime, so the only bound is the largest tier's payload. ⚠ The <c>Inline</c> names survive and now
/// simply mean <i>the params region</i> — see <see cref="PackWarning"/>.</para>
/// </summary>
public static class BlackboardBinPacker
{
    /// <summary>
    /// The maximum number of bytes a behaviour's packed variable table may occupy.
    /// Mirrors <c>BehaviorConstants.MaxRootParamsByteSize</c> — the payload of the largest occurrence
    /// storage tier. Pinned against every other copy by <c>InlineBudgetConstantAgreementTests</c>.
    ///
    /// <para>⭐⭐⭐ <b><c>CE-307</c> (2026-09-22) — was <b>100</b>, the width of the inline
    /// <c>BrainBlackboard.BehaviorParameters</c> buffer, whose last bytes abutted tail registers that
    /// must never be allocated over.</b> ⛔ That layout is gone: <c>O2</c> moved the registers to
    /// <c>BrainInterrupts</c> and <c>P3-C</c> moved params into their own occurrence slot. ⇒ the table
    /// is sized to itself and promoted up the tier ladder, so the only real bound is the largest
    /// tier's payload.</para>
    ///
    /// <para>⭐ <b><c>CE-314</c> raised it from 100</b>, in the same change that removed the
    /// inline/heavy split — because in THIS packer the number was also the SPLIT POINT, so it could
    /// not be raised while the heavy arm still existed. ⇒ the editor and the generator now agree on
    /// one ceiling.</para>
    /// </summary>
    public const int MaxInlineBytes = 16096;

    /// <summary>
    /// Maximum struct-field alignment cap (matches C# default struct layout rules).
    /// Types larger than 8 bytes still align to 8, not to their own size.
    /// </summary>
    private const int AlignmentCap = 8;

    /// <summary>
    /// Packs <paramref name="masterVars"/> then <paramref name="aggregatedVars"/> into the ONE params
    /// region, computing byte offsets with correct C# struct-alignment padding.
    ///
    /// <para>⭐ <c>CE-314</c>: aggregated variables used to be tried inline and <b>spilled to a heavy
    /// tier</b> when they did not fit. ⛔ That tier was <c>Blackboard1024</c>, deleted by <c>P4</c>-①.
    /// ⇒ they now simply continue the same region, and overflow is reported as a warning exactly as it
    /// is for master variables — <b>one region, one bound, one warning</b>.</para>
    /// </summary>
    /// <param name="masterVars">Variables belonging to the master asset's own blackboard.</param>
    /// <param name="aggregatedVars">
    /// Sub-tree-required DTO variables (treated as empty when null).
    /// </param>
    /// <returns>A <see cref="PackResult"/> with resolved offsets and a warning if the ceiling
    /// is breached.</returns>
    public static PackResult Pack(
        IReadOnlyList<BlackboardVariableDescriptor> masterVars,
        IReadOnlyList<BlackboardVariableDescriptor>? aggregatedVars = null)
    {
        if (masterVars == null) throw new ArgumentNullException(nameof(masterVars));

        var packed = new List<PackedVariable>(masterVars.Count + (aggregatedVars?.Count ?? 0));
        int inlineOffset = 0;

        // ⭐ CE-314: ONE region. Master variables first, then aggregated ones continuing the same
        //   offset — there is no second tier to route between any more.
        foreach (var desc in masterVars)
        {
            if (desc == null) throw new ArgumentException("masterVars contains a null entry.");
            inlineOffset = Place(packed, desc, inlineOffset);
        }

        if (aggregatedVars != null)
        {
            foreach (var desc in aggregatedVars)
            {
                if (desc == null) throw new ArgumentException("aggregatedVars contains a null entry.");
                inlineOffset = Place(packed, desc, inlineOffset);
            }
        }

        // ⛔ The early return for a master-only overflow is GONE with the spill it guarded: its comment
        //   said "heavy promotion cannot help when the master budget itself is exceeded", and there is
        //   no promotion left. ⭐ Overflow is now reported the same way wherever it happens, and the
        //   offsets of every variable are still computed so the panel can show them.
        int totalInlineBytes = inlineOffset;

        PackWarning warning;
        if (totalInlineBytes > MaxInlineBytes)
            warning = PackWarning.InlineMemoryExceeded;
        else
            warning = PackWarning.None;

        return new PackResult(packed, totalInlineBytes, warning);
    }

    /// <summary>
    /// ⭐ <c>CE-314</c> — places one variable at the next aligned offset and returns the new offset.
    /// ⚠ Extracted because master and aggregated variables now take the SAME path; they used to differ
    /// only by the spill branch, and with that gone two near-identical loop bodies were one edit away
    /// from drifting apart.
    /// </summary>
    private static int Place(List<PackedVariable> packed, BlackboardVariableDescriptor desc, int offset)
    {
        int size      = GetManagedSize(desc.FieldType);
        int alignment = Math.Min(size, AlignmentCap);

        // Round current offset up to the next alignment boundary.
        if (alignment > 0 && offset % alignment != 0)
            offset += alignment - (offset % alignment);

        packed.Add(new PackedVariable(desc.Name, desc.FieldType, ByteOffset: offset, ByteSize: size));
        return offset + size;
    }

    /// <summary>
    /// Optimization pass: sorts <paramref name="vars"/> to minimize alignment padding
    /// (largest-alignment-first), then calls <see cref="Pack"/>.
    /// User-invoked only (never automatic on save).
    /// </summary>
    public static PackResult Repack(IReadOnlyList<BlackboardVariableDescriptor> vars)
    {
        if (vars == null) throw new ArgumentNullException(nameof(vars));

        // Sort descending by alignment (capped at AlignmentCap) to reduce padding.
        var sorted = vars
            .OrderByDescending(v =>
            {
                int size = GetManagedSize(v.FieldType);
                return Math.Min(size, AlignmentCap);
            })
            .ToList();

        return Pack(sorted);
    }

    // -------------------------------------------------------------------------
    // Size helpers
    // -------------------------------------------------------------------------

    // Known managed sizes for primitive types. Marshal.SizeOf(bool) returns 4 (Win32 BOOL)
    // but C# sequential struct layout uses 1 byte for bool. Use the managed size here.
    private static readonly Dictionary<Type, int> PrimitiveSizes = new()
    {
        { typeof(bool),   1 },
        { typeof(byte),   1 },
        { typeof(sbyte),  1 },
        { typeof(char),   2 },
        { typeof(short),  2 },
        { typeof(ushort), 2 },
        { typeof(int),    4 },
        { typeof(uint),   4 },
        { typeof(long),   8 },
        { typeof(ulong),  8 },
        { typeof(float),  4 },
        { typeof(double), 8 },
    };

    /// <summary>
    /// Returns the managed (C# struct sequential layout) size in bytes for <paramref name="t"/>.
    /// Falls back to <see cref="Marshal.SizeOf(Type)"/> for non-primitive types.
    /// </summary>
    private static int GetManagedSize(Type t)
    {
        if (PrimitiveSizes.TryGetValue(t, out int known))
            return known;
        try
        {
            return Marshal.SizeOf(t);
        }
        catch (ArgumentException)
        {
            // The type can't be marshaled (e.g. a variable whose CLR type could not be resolved
            // and fell back to System.Object, or a struct whose assembly isn't loaded). Degrade to
            // 0 instead of crashing the whole editor render loop; the variable still renders (as
            // 0 bytes), which surfaces the resolution problem without taking the app down.
            return 0;
        }
    }
}
