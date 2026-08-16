using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Hrot.AiEditor.Generators;

/// <summary>
/// Resolves the **managed** (C# sequential) byte size of a type given its FQN string
/// and a Roslyn <see cref="Compilation"/>.
///
/// Mirrors <c>Fdp.Toolkits.Analyzers.BehaviorParameterSizeAnalyzer.ComputeStructSize</c> — keep in sync.
/// Uses managed layout rules: bool=1, enum=underlying, nested structs recursive,
/// sequential alignment cap = 8. This matches the size the <c>Unsafe.As</c> projection
/// assumes — NOT <c>Marshal.SizeOf</c> which uses unmanaged bool=4.
///
/// <para>
/// ⭐⭐ <b><c>S2</c> — this file is SHARED BY LINK, not copied.</b> <c>Hrot.Blueprints.Generators</c>
/// compiles this same source (<c>&lt;Compile Include=… Link="Shared/StructSizeResolver.cs" /&gt;</c>) so the
/// blueprint compiler's struct-size oracle and the BTree/HSM packer's are one algorithm, not two that
/// a comment promises to keep in step. ⛔ <b>That is why this file must have NO project dependency:</b>
/// it references Roslyn and nothing else — no <c>Hrot.AiEditor.Persistence</c>, no <c>…Schema</c>.
/// ⚠ Keep it that way, or the link breaks the other assembly's build.
/// </para>
///
/// <para>
/// 📌 <b>The triplication is real and was NEVER ACTUALLY FILED.</b>
/// <c>.dev/btree-ai-action-binding/reports/BATCH-03-REPORT.md:100</c> describes it and proposes the id
/// <b><i>"DEBT-AIB-012 (suggested)"</i></b> — ⛔ but that number was already taken: the programme's
/// <c>DEBT-TRACKER.md</c> gives <c>DEBT-AIB-012</c> to <i>"inspector multi-DTO read"</i>, <b>RESOLVED
/// BATCH-05</b>. ⇒ the debt has a description, a suggestion and no row. ⚠ <b>Do not cite it by that
/// id</b>; cite the report line.
/// </para>
///
/// <para>
/// ⭐ Three OTHER copies still exist — <c>Fbt.SourceGen.BTreeActionGenerator</c>,
/// <c>Fdp.Toolkits.Analyzers.BTreeActionGenerator</c>/<c>HsmActionGenerator</c>, and
/// <c>BehaviorParameterSizeAnalyzer</c> — held in step by a "keep in sync" comment. The same link
/// mechanism would absorb them; they are in <c>FDP/</c>, a different ownership tree, so that is not
/// this batch's to take.
/// </para>
/// </summary>
internal static class StructSizeResolver
{
    private const int AlignmentCap = 8;

    /// <summary>
    /// Known primitive / common-vector managed sizes.
    /// Mirrors <c>BTreeBlackboardPackHelper.KnownSizes</c>.
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
    /// <c>BTreeBlackboardPackHelper.Pack(vars, Func&lt;string,int?&gt;, out total)</c> — and, since
    /// <c>S2</c>, into <c>CompileOptions.StructSizeOracle</c>, which is the same shipped shape.
    /// </summary>
    public static Func<string, int?> MakeDelegate(Compilation compilation)
    {
        return typeId => Resolve(typeId, compilation);
    }

    /// <summary>
    /// ⭐ <c>S2</c> — the FIELD-size flavour, for <c>CompileOptions.StructSizeOracle</c>.
    ///
    /// <para>
    /// ⚠ <b>Deliberately <see cref="ResolveFieldSize"/> and not <see cref="Resolve"/>.</b> The blueprint
    /// compiler's <c>global::</c> arm accepts an ENUM as readily as a struct (that is what it was built
    /// for — <c>ENUM-DESIGN.md §RESOLVED</c>), and <see cref="Resolve"/> returns <c>null</c> for one
    /// because it demands <c>TypeKind.Struct</c>. ⇒ using it here would leave every enum on the guessed
    /// 4 bytes, ⛔ <b>including a <c>byte</c>- or <c>long</c>-backed one, where 4 is simply wrong.</b>
    /// </para>
    ///
    /// <para>⚠ Accepts either the bare FQN or the editor's <c>global::</c>-prefixed form.</para>
    /// </summary>
    public static Func<string, int?> MakeFieldSizeDelegate(Compilation compilation)
    {
        return typeId => ResolveFieldSize(
            typeId != null && typeId.StartsWith("global::", StringComparison.Ordinal)
                ? typeId.Substring("global::".Length)
                : typeId!,
            compilation);
    }

    /// <summary>
    /// Resolves the managed byte size for an arbitrary FIELD type — a known primitive/vector,
    /// a project enum (sized by its underlying integral type), or a nested struct-DTO.  Unlike
    /// <see cref="Resolve"/> (which requires the resolved symbol to itself be a struct — the shape
    /// of a managed blackboard variable's own type), this accepts any blittable field type, since a
    /// blueprint AiPrimitive Parameter may be an authored enum.  Used by
    /// <see cref="GeneratedBlueprintSchemaCatalog"/> to size the individual fields of a blueprint's
    /// <c>Params</c> struct from its <c>.bp.json</c> parameter schema (Option A — the struct itself
    /// isn't built yet, so it can't be resolved by name, but its field TYPES already exist).
    /// </summary>
    internal static int? ResolveFieldSize(string typeId, Compilation compilation)
    {
        if (string.IsNullOrEmpty(typeId))
            return null;

        if (KnownSizes.TryGetValue(typeId, out int knownSize))
            return knownSize;

        INamedTypeSymbol? symbol = compilation.GetTypeByMetadataName(typeId);
        if (symbol == null)
            return null;

        int size = GetTypeSize(symbol);
        return size >= 0 ? size : (int?)null;
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
            var fieldSizes = new List<int>(fields.Count);
            foreach (var field in fields)
            {
                int size = GetTypeSize(field.Type);
                if (size < 0) return -1;
                fieldSizes.Add(size);
            }
            return ComputeSequentialSize(fieldSizes);
        }
    }

    /// <summary>
    /// Computes the total managed size of a <c>[StructLayout(Sequential)]</c> struct given its
    /// fields' sizes in declaration order — natural alignment (each field aligned to
    /// <c>min(fieldSize, AlignmentCap)</c>), trailing pad to the struct's own max-field alignment.
    /// Shared by the Roslyn-symbol path (<see cref="ComputeStructSize"/>) and
    /// <see cref="GeneratedBlueprintSchemaCatalog"/>'s <c>.bp.json</c>-schema-driven Params sizing,
    /// so both paths use the exact same alignment math (Option A: never hand-roll a second copy).
    /// </summary>
    internal static int ComputeSequentialSize(IReadOnlyList<int> fieldSizesInOrder)
    {
        int offset = 0, maxAlign = 1;
        foreach (int size in fieldSizesInOrder)
        {
            int align = size <= 0 ? 1 : (size <= AlignmentCap ? size : AlignmentCap);
            if (align > maxAlign) maxAlign = align;
            if (size > 0) offset = AlignUp(offset, align);
            offset += size;
        }
        return AlignUp(offset, maxAlign);
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

    private static int AlignUp(int v, int a) => a <= 1 ? v : (v + a - 1) & ~(a - 1);
}
