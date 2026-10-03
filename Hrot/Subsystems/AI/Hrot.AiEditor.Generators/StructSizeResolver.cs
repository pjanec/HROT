using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Hrot.AiEditor.Generators;

/// <summary>
/// Resolves the **managed** (C# sequential) byte size of a type given its FQN string
/// and a Roslyn <see cref="Compilation"/>.
///
/// ⭐ <c>CE-2027</c> — the layout ITSELF is <c>Fdp.Toolkit.Behavior.Shared.RoslynStructLayout</c> (linked, one copy for every
/// analyzer and generator); this class is its string front door. Uses managed layout rules: bool=1, enum=underlying, nested structs recursive,
/// sequential alignment cap = 8. This matches the size the <c>Unsafe.As</c> projection
/// assumes — NOT <c>Marshal.SizeOf</c> which uses unmanaged bool=4.
///
/// <para>
/// ⭐⭐ <b><c>S2</c> — this file is SHARED BY LINK, not copied.</b> <c>Hrot.Blueprints.Generators</c>
/// compiles this same source (<c>&lt;Compile Include=… Link="Shared/StructSizeResolver.cs" /&gt;</c>) so the
/// blueprint compiler's struct-size oracle and the BTree/HSM packer's are one algorithm, not two that
/// a comment promises to keep in step. ⛔ <b>That is why this file must have NO project dependency:</b>
/// it references Roslyn and the two FDP <c>Shared/</c> files it delegates to (<c>RoslynStructLayout.cs</c>,
/// <c>KnownTypeLayouts.cs</c>, CE-2027) — ⚠ every assembly that links THIS file links THOSE too.
/// </para>
///
/// <para>
/// ⭐⭐ <b><c>CE-2027</c> — the triplication is RESOLVED.</b> The three FDP copies (<c>BehaviorParameterSizeAnalyzer</c>,
/// <c>Fdp.Toolkits.Analyzers.BTreeActionGenerator</c>/<c>HsmActionGenerator</c>) and this class's own math now all call
/// <c>Fdp.Toolkit.Behavior.Shared.RoslynStructLayout</c>, linked by the same mechanism; the known-type table is
/// <c>KnownTypeLayouts</c>, shared with the packer. ⚠ Unifying them exposed that the shared math was NOT the CLR's
/// (alignment guessed as <c>min(size, 8)</c>, no explicit trailing pad, empty structs sized 0) — corrected there and pinned
/// by <c>StructSizeResolverEnumTests.CE2027_*</c> against the runtime. 📄 <c>DESIGN_Unified_Behaviour_Run.md</c> "S8d".
/// (History: <c>.dev/_DONE/btree-ai-action-binding/reports/BATCH-03-REPORT.md:100</c> described it as the never-filed
/// "<c>DEBT-AIB-012</c> (suggested)"; that id belongs to a different, resolved row.)
/// </para>
/// </summary>
internal static class StructSizeResolver
{
    // ⭐⭐ CE-2027 — the known-type table is FDP/Toolkits/Fdp.Toolkits.Analyzers/Shared/KnownTypeLayouts.cs (one copy, with
    //    alignment), linked here, into the packer's assembly and into every analyzer.

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
        if (global::Fdp.Toolkit.Behavior.Shared.KnownTypeLayouts.TryGetSize(typeId, out int knownSize))
            return knownSize;

        // Look up the symbol via metadata name (handles `+` nested separator correctly).
        INamedTypeSymbol? symbol = compilation.GetTypeByMetadataName(typeId);
        if (symbol == null)
            return null;

        // Must be a value-type struct (not a class, interface, etc.).
        if (symbol.TypeKind != TypeKind.Struct)
            return null;

        int size = global::Fdp.Toolkit.Behavior.Shared.RoslynStructLayout.StructSize(symbol);
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
        => ResolveFieldLayout(typeId, compilation)?.Size;

    /// <summary>
    /// ⭐ <c>CE-2027</c> — a field type's (size, ALIGNMENT): what laying out a struct that does not exist yet needs, since a
    /// 12-byte <c>Vector3</c> is 4-aligned and a size alone cannot say so.
    /// </summary>
    internal static (int Size, int Align)? ResolveFieldLayout(string typeId, Compilation compilation)
    {
        if (string.IsNullOrEmpty(typeId))
            return null;

        if (global::Fdp.Toolkit.Behavior.Shared.KnownTypeLayouts.TryGet(typeId, out int knownSize, out int knownAlign))
            return (knownSize, knownAlign);

        INamedTypeSymbol? symbol = compilation.GetTypeByMetadataName(typeId);
        if (symbol == null)
            return null;

        int size = global::Fdp.Toolkit.Behavior.Shared.RoslynStructLayout.TypeSize(symbol);
        return size >= 0 ? (size, global::Fdp.Toolkit.Behavior.Shared.RoslynStructLayout.TypeAlign(symbol)) : ((int, int)?)null;
    }

    // ── Struct layout: ⭐ CE-2027 — the ONE algorithm is FDP/Toolkits/Fdp.Toolkits.Analyzers/Shared/RoslynStructLayout.cs,
    //    linked into this assembly (and so into Hrot.Blueprints.Generators, which links this file) beside the three FDP
    //    analyzers that used to carry their own copies. This class keeps only the STRING front door (type ids, aliases).
}
