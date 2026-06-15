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
    {
        if (variables == null) throw new ArgumentNullException(nameof(variables));

        var result = new List<PackedField>(variables.Count);
        int offset = 0;

        foreach (var v in variables)
        {
            string typeId = v.Type?.TypeId ?? string.Empty;

            if (!KnownSizes.TryGetValue(typeId, out int size))
                throw new NotSupportedException(
                    $"BTreeBlackboardPackHelper: unknown type '{typeId}' for variable '{v.Name}'. " +
                    $"Add it to BTreeBlackboardPackHelper.KnownSizes.");

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
    {
        unknownTypeId = null;
        int offset = 0;
        foreach (var v in variables)
        {
            string typeId = v.Type?.TypeId ?? string.Empty;
            if (!KnownSizes.TryGetValue(typeId, out int size))
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
}
