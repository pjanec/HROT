using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Hrot.AiEditor.Persistence.Emit;

namespace Hrot.AiEditor.Generators;

/// <summary>
/// Resolves the **managed** (C# sequential) byte size of a type given its FQN string
/// and a Roslyn <see cref="Compilation"/>.
///
/// Mirrors <c>Fdp.Toolkits.Analyzers.BehaviorParameterSizeAnalyzer.ComputeStructSize</c> — keep in sync.
/// Uses managed layout rules: bool=1, enum=underlying, nested structs recursive,
/// sequential alignment cap = 8. This matches the size the <c>Unsafe.As</c> projection
/// assumes — NOT <c>Marshal.SizeOf</c> which uses unmanaged bool=4.
/// </summary>
internal static class StructSizeResolver
{
    private const int AlignmentCap = 8;

    /// <summary>
    /// Known primitive / common-vector managed sizes.
    /// Mirrors <see cref="BTreeBlackboardPackHelper"/> <c>KnownSizes</c>.
    /// </summary>
    private static readonly Dictionary<string, int> KnownSizes =
        new Dictionary<string, int>(StringComparer.Ordinal)
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
            // Unity/engine math aliases
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
    /// Tries to resolve the managed byte size for <paramref name="typeId"/>.
    /// </summary>
    /// <param name="typeId">CLR FQN stored in the asset variable (may use <c>+</c> for nested types).</param>
    /// <param name="compilation">The Roslyn compilation to look up struct symbols in.</param>
    /// <returns>The managed byte size, or <c>null</c> if the type cannot be resolved or sized.</returns>
    public static int? Resolve(string typeId, Compilation compilation)
    {
        if (string.IsNullOrEmpty(typeId))
            return null;

        // Fast path: known primitive / vector.
        if (KnownSizes.TryGetValue(typeId, out int knownSize))
            return knownSize;

        // Look up the symbol via metadata name (handles `+` nested separator correctly).
        INamedTypeSymbol? symbol = compilation.GetTypeByMetadataName(typeId);
        if (symbol == null)
            return null;

        // Must be a value-type struct (not a class, interface, etc.).
        if (symbol.TypeKind != TypeKind.Struct)
            return null;

        int size = ComputeStructSize(symbol);
        return size >= 0 ? size : (int?)null;
    }

    /// <summary>
    /// Builds a resolver delegate suitable for injection into
    /// <see cref="BTreeBlackboardPackHelper.Pack(System.Collections.Generic.IReadOnlyList{Hrot.AiEditor.Persistence.BTree.BlackboardVariableDto},System.Func{string,int?},out int)"/>.
    /// </summary>
    public static Func<string, int?> MakeDelegate(Compilation compilation)
    {
        return typeId => Resolve(typeId, compilation);
    }

    // ── Struct layout computation ─────────────────────────────────────────────
    // Mirrors Fdp.Toolkits.Analyzers.BehaviorParameterSizeAnalyzer.ComputeStructSize — keep in sync.

    private static int ComputeStructSize(INamedTypeSymbol type)
    {
        // Detect [StructLayout(Explicit)] (LayoutKind.Explicit == 2).
        bool isExplicit = type.GetAttributes()
            .Any(a => a.AttributeClass?.Name == "StructLayoutAttribute"
                   && a.ConstructorArguments.Length > 0
                   && a.ConstructorArguments[0].Value is int v && v == 2);

        var fields = type.GetMembers()
            .OfType<IFieldSymbol>()
            .Where(f => !f.IsStatic && !f.IsConst)
            .ToList();

        if (isExplicit)
        {
            int max = 0;
            foreach (var field in fields)
            {
                var fa = field.GetAttributes()
                    .FirstOrDefault(a => a.AttributeClass?.Name == "FieldOffsetAttribute");
                if (fa == null || fa.ConstructorArguments.Length == 0) return -1;
                int fo = (int)fa.ConstructorArguments[0].Value!;
                int fs = GetTypeSize(field.Type);
                if (fs < 0) return -1;
                max = System.Math.Max(max, fo + fs);
            }
            return max;
        }
        else
        {
            int offset = 0, maxAlign = 1;
            foreach (var field in fields)
            {
                int size  = GetTypeSize(field.Type);
                int align = GetTypeAlign(field.Type);
                if (size < 0) return -1;
                if (align > maxAlign) maxAlign = align;
                if (size > 0) offset = AlignUp(offset, align);
                offset += size;
            }
            return AlignUp(offset, maxAlign);
        }
    }

    private static int GetTypeSize(ITypeSymbol type)
    {
        switch (type.SpecialType)
        {
            case SpecialType.System_Boolean:
            case SpecialType.System_Byte:
            case SpecialType.System_SByte:   return 1;
            case SpecialType.System_Char:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:  return 2;
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Single:  return 4;
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
            case SpecialType.System_Double:
            case SpecialType.System_IntPtr:
            case SpecialType.System_UIntPtr: return 8;
            default:
                // Check by metadata FQN for known vectors (may not have SpecialType).
                if (type is INamedTypeSymbol named)
                {
                    string metaName = named.ContainingNamespace?.IsGlobalNamespace == false
                        ? named.ContainingNamespace.ToDisplayString() + "." + named.MetadataName
                        : named.MetadataName;
                    if (KnownSizes.TryGetValue(metaName, out int known))
                        return known;

                    if (type.TypeKind == TypeKind.Enum)
                        return named.EnumUnderlyingType != null ? GetTypeSize(named.EnumUnderlyingType) : 4;

                    if (type.TypeKind == TypeKind.Struct)
                        return ComputeStructSize(named);
                }
                return -1;
        }
    }

    private static int GetTypeAlign(ITypeSymbol type)
    {
        int size = GetTypeSize(type);
        return size <= 0 ? 1 : (size <= AlignmentCap ? size : AlignmentCap);
    }

    private static int AlignUp(int v, int a) => a <= 1 ? v : (v + a - 1) & ~(a - 1);
}
