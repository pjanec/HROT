using System;
using System.Collections.Generic;
using Hrot.AiEditor.Persistence.BTree;

namespace Hrot.AiEditor.Persistence.Emit;

/// <summary>
/// Build-time bin-packer for the managed blackboard block.
/// Replicates <c>BlackboardBinPacker</c> logic using CLR type-name strings instead
/// of runtime <c>Type</c> instances — safe to call inside a Roslyn IncrementalGenerator
/// (netstandard2.0, no <c>Marshal.SizeOf</c>) and in unit tests.
///
/// Design: §S1-2.  Single source of truth for byte offsets — both the struct emitter
/// (BTreeEmitCore) and the registrar emitter (BTreeBridgeEmitCore) derive offsets from
/// the same <see cref="Pack"/> call so blob keys and registry keys are always identical.
/// </summary>
public static class BTreeBlackboardPackHelper
{
    /// <summary>
    /// Maximum bytes a behaviour's packed variable table may occupy — mirrors
    /// <c>BehaviorConstants.MaxRootParamsByteSize</c>, the payload of the largest occurrence storage
    /// tier. Inlined because this assembly is netstandard2.0 and cannot reference the net8.0 runtime;
    /// pinned against every other copy by <c>InlineBudgetConstantAgreementTests</c>.
    ///
    /// <para>⭐⭐⭐ <b><c>CE-307</c> (2026-09-22) — was <b>100</b>.</b> ⛔ That number was a buffer-overrun
    /// guard for params stored inline in <c>BrainBlackboard</c> beside tail registers; <c>O2</c> moved
    /// the registers out and <c>P3-C</c> moved params into their own slot, so nothing neighbours them.
    /// 🔴 <b>This mirror is the one with teeth:</b> <c>BTreeJsonGenerator</c> treats an overflow as
    /// <i>"asset skipped"</i> — it emits NO code for the asset at all — so 100 was a hard authoring
    /// limit on every generated BTree blackboard.</para>
    /// </summary>
    public const int MaxInlineBytes = 16096;

    private const int AlignmentCap = 8;

    /// <summary>
    /// ⭐ <b><c>CE-455</c> — the root block's LAYOUT, as a stable 64-bit hash</b> (FNV-1a over the field list — ⛔ never
    /// <c>string.GetHashCode</c>, which differs per process). Emitted into <c>BehaviorDefinition.BlueprintStructureHash</c>
    /// for BTree and HSM roots, so <c>BrainTickSystem.RestartIfRelaidOut</c> — which already compares it for every tier —
    /// restarts a running behaviour when a hot reload reorders or retypes its parameters at the SAME width (a width change
    /// it already caught). 📄 <c>Architect_Question_77</c> §5.12.
    /// <para>Covers every Input field (name, type, offset, size) and every State-half variable (name, type, in order —
    /// their offsets follow from that). 0 is reserved for "no layout"; a computed 0 is mapped to 1.</para>
    /// </summary>
    public static ulong LayoutHash(
        IEnumerable<PackedField>? inputs, IEnumerable<KeyValuePair<string, string>>? stateFields = null)
    {
        const ulong offset = 14695981039346656037UL, prime = 1099511628211UL;
        ulong h = offset;
        void Mix(string s) { foreach (char c in s) { h ^= c; h *= prime; } h ^= 0x1F; h *= prime; }
        if (inputs != null)
            foreach (var f in inputs) { Mix("I"); Mix(f.Name); Mix(f.TypeId); Mix(f.ByteOffset.ToString(System.Globalization.CultureInfo.InvariantCulture)); Mix(f.ByteSize.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
        if (stateFields != null)
            foreach (var s in stateFields) { Mix("S"); Mix(s.Key); Mix(s.Value); }
        return h == 0 ? 1UL : h;
    }

    /// <summary>
    /// A single variable after packing.
    /// Immutable value object (plain class for netstandard2.0 compatibility — no record).
    /// </summary>
    public sealed class PackedField
    {
        public string Name      { get; }
        public string TypeId    { get; }
        public int ByteOffset   { get; }
        public int ByteSize     { get; }

        public PackedField(string name, string typeId, int byteOffset, int byteSize)
        {
            Name       = name;
            TypeId     = typeId;
            ByteOffset = byteOffset;
            ByteSize   = byteSize;
        }

        public override string ToString() =>
            $"PackedField({Name}, {TypeId}, offset={ByteOffset}, size={ByteSize})";
    }

    // ⭐⭐ CE-2027 — the known-type table is ONE file now, FDP/Toolkits/Fdp.Toolkits.Analyzers/Shared/KnownTypeLayouts.cs
    //    (linked here and into every analyzer/generator); it carries the CLR alignment too, which this table never did.

    /// <summary>
    /// Returns the byte size for a known type FQN, or 0 if not in the table.
    /// </summary>
    public static bool TryGetSize(string typeId, out int size) =>
        global::Fdp.Toolkit.Behavior.Shared.KnownTypeLayouts.TryGetSize(typeId, out size);

    /// <summary>
    /// Packs <paramref name="variables"/> using declaration order (master-var invariant:
    /// Pack preserves declaration order and computes natural-alignment padding).
    /// Throws <see cref="NotSupportedException"/> for unknown type FQNs.
    /// Returns total byte size via <paramref name="totalBytes"/>.
    /// </summary>
    public static IReadOnlyList<PackedField> Pack(
        IReadOnlyList<BlackboardVariableDto> variables,
        out int totalBytes)
        => Pack(variables, extraSizeResolver: null, out totalBytes);

    /// <summary>
    /// Packs <paramref name="variables"/> using declaration order, with an optional injected
    /// size resolver for struct-DTO types not in <c>KnownTypeLayouts</c>.
    /// Lookup order: <c>KnownTypeLayouts</c> → <paramref name="extraSizeResolver"/>(<c>TypeId</c>).
    /// Throws <see cref="NotSupportedException"/> when no resolver returns a size.
    /// Returns total byte size via <paramref name="totalBytes"/>.
    ///
    /// Design (S1-2b): the <paramref name="extraSizeResolver"/> is provided by
    /// <c>StructSizeResolver</c> in <c>Hrot.AiEditor.Generators</c> (Roslyn-aware assembly)
    /// so this netstandard2.0 Persistence assembly stays free of Roslyn dependencies.
    /// </summary>
    public static IReadOnlyList<PackedField> Pack(
        IReadOnlyList<BlackboardVariableDto> variables,
        Func<string, int?>? extraSizeResolver,
        out int totalBytes)
    {
        if (variables == null) throw new ArgumentNullException(nameof(variables));

        var result = new List<PackedField>(variables.Count);
        int offset = 0;

        foreach (var v in variables)
        {
            // S3-G: State-role variables are working state that lives in the partitioned
            // BlueprintBlackboard* tier (keyed by scope), NOT in the inline BrainBlackboard param
            // region — so they are excluded from param packing. Byte-identical for the existing
            // corpus (all Input-role); prevents a large working-state struct (e.g. the 120-byte
            // HillAttackMutableState) from overflowing the ≤100-byte inline param budget.
            if (v.Role == BlackboardVariableRole.State) continue;

            string typeId = v.Type?.TypeId ?? string.Empty;

            if (!TryResolveSize(typeId, extraSizeResolver, out int size))
                throw new NotSupportedException(
                    $"BTreeBlackboardPackHelper: unknown type '{typeId}' for variable '{v.Name}'. " +
                    $"Add it to BTreeBlackboardPackHelper.KnownSizes or provide an extraSizeResolver.");

            int alignment = Math.Min(size, AlignmentCap);

            // Align offset up to the next alignment boundary.
            if (alignment > 0 && offset % alignment != 0)
                offset += alignment - (offset % alignment);

            result.Add(new PackedField(v.Name, typeId, offset, size));
            offset += size;
        }

        totalBytes = offset;
        return result;
    }

    /// <summary>
    /// Returns whether packing <paramref name="variables"/> would overflow the 100-byte inline budget.
    /// Does NOT throw for unknown types — returns false + sets <paramref name="unknownTypeId"/>.
    /// </summary>
    public static bool WouldOverflow(
        IReadOnlyList<BlackboardVariableDto> variables,
        out string? unknownTypeId)
        => WouldOverflow(variables, extraSizeResolver: null, out unknownTypeId);

    /// <summary>
    /// Returns whether packing <paramref name="variables"/> would overflow the 100-byte inline budget,
    /// using an optional injected size resolver for struct-DTO types.
    /// Does NOT throw for unknown types — returns false + sets <paramref name="unknownTypeId"/>.
    /// </summary>
    public static bool WouldOverflow(
        IReadOnlyList<BlackboardVariableDto> variables,
        Func<string, int?>? extraSizeResolver,
        out string? unknownTypeId)
    {
        unknownTypeId = null;
        int offset = 0;
        foreach (var v in variables)
        {
            // S3-G: State-role vars live in the partition tier, not the inline param region
            // (see Pack) — exclude them from the overflow budget.
            if (v.Role == BlackboardVariableRole.State) continue;

            string typeId = v.Type?.TypeId ?? string.Empty;
            if (!TryResolveSize(typeId, extraSizeResolver, out int size))
            {
                unknownTypeId = typeId;
                return false; // can't determine — caller must handle
            }
            int alignment = Math.Min(size, AlignmentCap);
            if (alignment > 0 && offset % alignment != 0)
                offset += alignment - (offset % alignment);
            offset += size;
        }
        return offset > MaxInlineBytes;
    }

    /// <summary>
    /// Resolves the byte size for <paramref name="typeId"/> via <c>KnownTypeLayouts</c>
    /// then <paramref name="extraSizeResolver"/>. Returns true when resolved.
    /// </summary>
    private static bool TryResolveSize(string typeId, Func<string, int?>? extraSizeResolver, out int size)
    {
        if (global::Fdp.Toolkit.Behavior.Shared.KnownTypeLayouts.TryGetSize(typeId, out size))
            return true;

        if (extraSizeResolver != null)
        {
            int? resolved = extraSizeResolver(typeId);
            if (resolved.HasValue)
            {
                size = resolved.Value;
                return true;
            }
        }

        size = 0;
        return false;
    }
}
