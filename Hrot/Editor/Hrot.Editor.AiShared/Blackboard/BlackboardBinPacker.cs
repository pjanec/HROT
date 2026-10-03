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
/// <param name="Role">⭐ CE-2029 — a <c>State</c> variable never joins the params region
/// (<c>Blackboard_Authoring_Detailed_Design.md</c> §6.2); the generator's packer already honoured that, this one did not.</param>
public record BlackboardVariableDescriptor(
    string Name, Type FieldType,
    Hrot.AiEditor.Persistence.BlackboardVariableRole Role = Hrot.AiEditor.Persistence.BlackboardVariableRole.Input);

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
/// <param name="InParamsRegion">⭐ CE-2029 — false for a <c>State</c> variable: it has a size, but no params-region offset
/// (<paramref name="ByteOffset"/> is then -1).</param>
public record PackedVariable(
    string Name,
    Type FieldType,
    int ByteOffset,
    int ByteSize,
    bool InParamsRegion = true);

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
    public const int MaxInlineBytes = Hrot.AiEditor.Persistence.Emit.BTreeBlackboardPackHelper.MaxInlineBytes;   // ⭐ CE-2029: one number

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

        // ⭐ CE-314: ONE region — master variables first, then aggregated ones continuing the same offset.
        var all = new List<BlackboardVariableDescriptor>(masterVars.Count + (aggregatedVars?.Count ?? 0));
        foreach (var desc in masterVars)
            all.Add(desc ?? throw new ArgumentException("masterVars contains a null entry."));
        if (aggregatedVars != null)
            foreach (var desc in aggregatedVars)
                all.Add(desc ?? throw new ArgumentException("aggregatedVars contains a null entry."));

        // ⭐⭐⭐ CE-2029 — THE GENERATOR'S PACKER, not a copy of it. 🔴 This class laid the region out itself: Marshal.SizeOf
        //   for a struct (a bool counted 4, an enum threw), and every Role=State variable counted into the params total —
        //   so the panel could show a size and an overflow the generated struct does not have. ⭐ Now
        //   BTreeBlackboardPackHelper.Pack lays out exactly what BTreeJsonGenerator/HsmJsonGenerator emit, and each size is
        //   the managed size (Fdp.Core.TypeLayout) — what the build-time RoslynStructLayout computes for the same type.
        var types = new Dictionary<string, Type>(StringComparer.Ordinal);
        var dtos = new List<Hrot.AiEditor.Persistence.BTree.BlackboardVariableDto>(all.Count);
        foreach (var desc in all)
        {
            string typeId = TypeIdOf(desc.FieldType);
            types[typeId] = desc.FieldType;
            dtos.Add(new Hrot.AiEditor.Persistence.BTree.BlackboardVariableDto
            {
                Name = desc.Name,
                Type = new Hrot.AiEditor.Persistence.BTree.BlackboardTypeRefDto { TypeId = typeId },
                Role = desc.Role,
            });
        }
        var fields = Hrot.AiEditor.Persistence.Emit.BTreeBlackboardPackHelper.Pack(
            dtos, typeId => types.TryGetValue(typeId, out var t) ? GetManagedSize(t) : (int?)null, out int totalInlineBytes);

        // Pack returns the params-region fields in declaration order; a State variable is absent from it.
        var packed = new List<PackedVariable>(all.Count);
        int next = 0;
        foreach (var desc in all)
        {
            if (desc.Role == Hrot.AiEditor.Persistence.BlackboardVariableRole.State)
            {
                packed.Add(new PackedVariable(desc.Name, desc.FieldType, -1, GetManagedSize(desc.FieldType), InParamsRegion: false));
                continue;
            }
            var f = fields[next++];
            packed.Add(new PackedVariable(desc.Name, desc.FieldType, f.ByteOffset, f.ByteSize));
        }

        var warning = totalInlineBytes > MaxInlineBytes ? PackWarning.InlineMemoryExceeded : PackWarning.None;
        return new PackResult(packed, totalInlineBytes, warning);
    }

    /// <summary>A CLR type's id as the generator spells it (nested types with <c>+</c>, as <c>Type.FullName</c> does).</summary>
    private static string TypeIdOf(Type t) => t.FullName ?? t.Name;

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

    /// <summary>⭐ CE-2030 — the managed size (<see cref="Fdp.Core.TypeLayout"/>) of a VALUE type; 0 for anything else. ⚠ A
    /// blackboard variable is blittable, so a reference type here is a variable whose CLR type could not be resolved (it falls
    /// back to <c>object</c>): it renders as 0 bytes — the visible symptom — instead of a reference's 8 or a crash.</summary>
    private static int GetManagedSize(Type t)
        => t.IsValueType && Fdp.Core.TypeLayout.TrySizeOf(t, out int size) ? size : 0;
}
