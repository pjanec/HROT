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
/// Indicates which storage tier a packed variable occupies.
/// </summary>
public enum PackTier
{
    /// <summary>Variable fits within the 100-byte inline blackboard region.</summary>
    Inline,

    /// <summary>Variable requires the heavy (heap) component. Allocated in TASK-BB-1c-04.</summary>
    Heavy,
}

/// <summary>
/// Warning flags produced by <see cref="BlackboardBinPacker.Pack"/>.
/// </summary>
public enum PackWarning
{
    /// <summary>No warnings.</summary>
    None,

    /// <summary>Total inline bytes exceed <see cref="BlackboardBinPacker.MaxInlineBytes"/>.</summary>
    InlineMemoryExceeded,

    /// <summary>Total heavy bytes exceed <see cref="BlackboardBinPacker.MaxHeavyBytes"/>.</summary>
    HeavyMemoryExceeded,
}

/// <summary>
/// A single variable after bin-packing: resolved byte offset, size, and tier.
/// </summary>
/// <param name="Name">The variable identifier.</param>
/// <param name="FieldType">The CLR type.</param>
/// <param name="ByteOffset">Byte offset from the start of the inline region.</param>
/// <param name="ByteSize">Unmanaged size of the field in bytes.</param>
/// <param name="Tier">Storage tier assigned to this variable.</param>
public record PackedVariable(
    string Name,
    Type FieldType,
    int ByteOffset,
    int ByteSize,
    PackTier Tier);

/// <summary>
/// Result of a <see cref="BlackboardBinPacker.Pack"/> call.
/// </summary>
/// <param name="Variables">All packed variables in declaration order.</param>
/// <param name="TotalInlineBytes">Total bytes consumed in the inline region (including padding).</param>
/// <param name="TotalHeavyBytes">Total bytes consumed in the heavy region. 0 when no heavy variables.</param>
/// <param name="RequiresHeavyComponent">
/// True when any variable was spilled to the heavy tier.
/// </param>
/// <param name="Warning">
/// <see cref="PackWarning.InlineMemoryExceeded"/> when <see cref="TotalInlineBytes"/> exceeds
/// <see cref="BlackboardBinPacker.MaxInlineBytes"/>; <see cref="PackWarning.HeavyMemoryExceeded"/>
/// when heavy bytes exceed <see cref="BlackboardBinPacker.MaxHeavyBytes"/>.
/// </param>
public record PackResult(
    IReadOnlyList<PackedVariable> Variables,
    int TotalInlineBytes,
    int TotalHeavyBytes,
    bool RequiresHeavyComponent,
    PackWarning Warning);

/// <summary>
/// Computes sequential byte offsets for blackboard variables using C# struct-alignment rules,
/// enforcing the 100-byte inline ceiling (BB design SS6.1--6.2, SS6.6).
/// </summary>
public static class BlackboardBinPacker
{
    /// <summary>
    /// The maximum number of bytes available in the inline (master) blackboard region.
    /// Mirrors <c>BehaviorConstants.MaxBehaviorParamByteSize</c>.
    /// The last 28 bytes of BrainBlackboard (offsets 100--127) are reserved for tail registers
    /// and must never be allocated by the packer.
    /// </summary>
    public const int MaxInlineBytes = 100;

    /// <summary>
    /// The maximum number of bytes available in the heavy (Blackboard1024) component.
    /// </summary>
    public const int MaxHeavyBytes = 928;

    /// <summary>
    /// Maximum struct-field alignment cap (matches C# default struct layout rules).
    /// Types larger than 8 bytes still align to 8, not to their own size.
    /// </summary>
    private const int AlignmentCap = 8;

    /// <summary>
    /// Packs <paramref name="masterVars"/> into the inline tier, computing byte offsets
    /// with correct C# struct-alignment padding.
    /// </summary>
    /// <param name="masterVars">Variables belonging to the master asset's own blackboard.</param>
    /// <param name="aggregatedVars">
    /// Sub-tree-required DTO variables (not used in this slice; treated as empty when null).
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

        // ---- master vars always go inline ----
        foreach (var desc in masterVars)
        {
            if (desc == null) throw new ArgumentException("masterVars contains a null entry.");

            int size      = GetManagedSize(desc.FieldType);
            int alignment = Math.Min(size, AlignmentCap);

            // Round current offset up to the next alignment boundary.
            if (alignment > 0 && inlineOffset % alignment != 0)
                inlineOffset += alignment - (inlineOffset % alignment);

            packed.Add(new PackedVariable(
                desc.Name,
                desc.FieldType,
                ByteOffset: inlineOffset,
                ByteSize:   size,
                Tier:       PackTier.Inline));

            inlineOffset += size;
        }

        int totalInlineBytes = inlineOffset;

        // If master vars already overflow the inline budget, return early.
        // Heavy promotion cannot help when the master budget itself is exceeded.
        if (totalInlineBytes > MaxInlineBytes)
        {
            return new PackResult(
                packed,
                totalInlineBytes,
                TotalHeavyBytes:       0,
                RequiresHeavyComponent: false,
                PackWarning.InlineMemoryExceeded);
        }

        // ---- aggregated vars: try inline first, spill to heavy if needed ----
        int heavyOffset = 0;
        bool anyHeavy = false;

        if (aggregatedVars != null)
        {
            foreach (var desc in aggregatedVars)
            {
                if (desc == null) throw new ArgumentException("aggregatedVars contains a null entry.");

                int size      = GetManagedSize(desc.FieldType);
                int alignment = Math.Min(size, AlignmentCap);

                // Try to fit inline.
                int alignedInlineOffset = inlineOffset;
                if (alignment > 0 && alignedInlineOffset % alignment != 0)
                    alignedInlineOffset += alignment - (alignedInlineOffset % alignment);

                if (alignedInlineOffset + size <= MaxInlineBytes)
                {
                    // Fits inline.
                    packed.Add(new PackedVariable(
                        desc.Name,
                        desc.FieldType,
                        ByteOffset: alignedInlineOffset,
                        ByteSize:   size,
                        Tier:       PackTier.Inline));
                    inlineOffset = alignedInlineOffset + size;
                }
                else
                {
                    // Spill to heavy tier.
                    int alignedHeavyOffset = heavyOffset;
                    if (alignment > 0 && alignedHeavyOffset % alignment != 0)
                        alignedHeavyOffset += alignment - (alignedHeavyOffset % alignment);

                    packed.Add(new PackedVariable(
                        desc.Name,
                        desc.FieldType,
                        ByteOffset: alignedHeavyOffset,
                        ByteSize:   size,
                        Tier:       PackTier.Heavy));
                    heavyOffset = alignedHeavyOffset + size;
                    anyHeavy = true;
                }
            }
        }

        totalInlineBytes = inlineOffset;
        int totalHeavyBytes = heavyOffset;

        PackWarning warning;
        if (totalHeavyBytes > MaxHeavyBytes)
            warning = PackWarning.HeavyMemoryExceeded;
        else if (totalInlineBytes > MaxInlineBytes)
            warning = PackWarning.InlineMemoryExceeded;
        else
            warning = PackWarning.None;

        return new PackResult(
            packed,
            totalInlineBytes,
            TotalHeavyBytes:       totalHeavyBytes,
            RequiresHeavyComponent: anyHeavy,
            warning);
    }

    /// <summary>
    /// Optimization pass: sorts <paramref name="vars"/> to minimize alignment padding
    /// (largest-alignment-first within the same tier), then calls <see cref="Pack"/>.
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
