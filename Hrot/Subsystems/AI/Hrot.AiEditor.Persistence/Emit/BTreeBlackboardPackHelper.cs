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
    /// <summary>Maximum inline bytes available (mirrors BehaviorConstants.MaxBehaviorParamByteSize).</summary>
    public const int MaxInlineBytes = 100;

    private const int AlignmentCap = 8;

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

    /// <summary>
    /// Known managed sizes for CLR type FQNs.
    /// Mirrors <c>BlackboardBinPacker.PrimitiveSizes</c> plus common value-type structs.
    /// bool = 1 (C# sequential layout, not Win32 BOOL = 4).
    /// </summary>
    private static readonly Dictionary<string, int> KnownSizes = new(StringComparer.Ordinal)
    {
        { "System.Boolean",  1 },
        { "System.Byte",     1 },
        { "System.SByte",    1 },
        { "System.Char",     2 },
        { "System.Int16",    2 },
        { "System.UInt16",   2 },
        { "System.Int32",    4 },
        { "System.UInt32",   4 },
        { "System.Single",   4 },
        { "System.Int64",    8 },
        { "System.UInt64",   8 },
        { "System.Double",   8 },
        // Common game-math value types
        { "System.Numerics.Vector2",    8  },
        { "System.Numerics.Vector3",    12 },
        { "System.Numerics.Vector4",    16 },
        { "System.Numerics.Quaternion", 16 },
        // Unity/engine math aliases (common in game code)
        { "UnityEngine.Vector2",    8  },
        { "UnityEngine.Vector3",    12 },
        { "UnityEngine.Vector4",    16 },
        { "UnityEngine.Quaternion", 16 },
        // C# alias forms — mirror of BlackboardTypeHelper
        { "bool",       1 },
        { "byte",       1 },
        { "sbyte",      1 },
        { "char",       2 },
        { "short",      2 },
        { "ushort",     2 },
        { "int",        4 },
        { "uint",       4 },
        { "float",      4 },
        { "long",       8 },
        { "ulong",      8 },
        { "double",     8 },
        { "Vector2",    8  },
        { "Vector3",    12 },
        { "Vector4",    16 },
        { "Quaternion", 16 },
    };

    /// <summary>
    /// Returns the byte size for a known type FQN, or 0 if not in the table.
    /// </summary>
    public static bool TryGetSize(string typeId, out int size) =>
        KnownSizes.TryGetValue(typeId, out size);

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
    /// size resolver for struct-DTO types not in <see cref="KnownSizes"/>.
    /// Lookup order: <see cref="KnownSizes"/> → <paramref name="extraSizeResolver"/>(<c>TypeId</c>).
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
    /// Resolves the byte size for <paramref name="typeId"/> via <see cref="KnownSizes"/>
    /// then <paramref name="extraSizeResolver"/>. Returns true when resolved.
    /// </summary>
    private static bool TryResolveSize(string typeId, Func<string, int?>? extraSizeResolver, out int size)
    {
        if (KnownSizes.TryGetValue(typeId, out size))
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
