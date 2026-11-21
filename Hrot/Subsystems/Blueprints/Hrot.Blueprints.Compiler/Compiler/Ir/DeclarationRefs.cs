using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Core.Compiler.Ir;

/// <summary>
/// <b>U-9 — the bridge between the model's <see cref="DeclarationKind"/> and the IR's
/// <see cref="VariableKind"/>.</b>
///
/// <para>
/// ⭐ <b>It lives on the IR side on purpose.</b> The IR already depends on the asset model; the model
/// must not depend back on the compiler, or a tagged declaration would drag <c>Unresolved</c> — a
/// state no stored declaration can be in — into the persisted vocabulary.
/// </para>
///
/// <para>
/// ⚠ <b>Total in both directions, and no silent arm.</b> <c>Unresolved</c> has no declaration to map
/// to and throws rather than picking one; <c>TaggedDeclarationTests</c> walks both enums so a member
/// added to either cannot arrive without a mapping.
/// </para>
/// </summary>
public static class DeclarationRefs
{
    public static VariableKind ToVariableKind(this DeclarationKind kind) => kind switch
    {
        DeclarationKind.Parameter => VariableKind.Parameter,
        DeclarationKind.Variable  => VariableKind.Variable,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unmapped declaration kind."),
    };

    public static DeclarationKind ToDeclarationKind(this VariableKind kind) => kind switch
    {
        VariableKind.Parameter => DeclarationKind.Parameter,
        VariableKind.Variable  => DeclarationKind.Variable,
        VariableKind.Unresolved   => throw new ArgumentOutOfRangeException(
            nameof(kind), kind,
            "Unresolved names no declaration list — it is the 'nobody set this' sentinel, and mapping "
            + "it to a kind would restore exactly the quiet-wrong-field defect U-3 removed."),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unmapped variable kind."),
    };

    /// <summary>
    /// The <see cref="VariableRef"/> that addresses <paramref name="decl"/> — its kind and its position
    /// <b>within its own list</b>, which is what <c>EmissionContext.VarFieldName</c> indexes by.
    /// Returns <see cref="VariableRef.Unresolved"/> when the declaration is not in this asset.
    /// </summary>
    public static VariableRef RefOf(this BlueprintAsset asset, BlueprintDeclaration decl)
    {
        var local = asset.Declarations.LocalIndexOf(decl);
        return local < 0 ? VariableRef.Unresolved : new VariableRef(decl.Kind.ToVariableKind(), local);
    }

    /// <summary>The declaration a resolved <see cref="VariableRef"/> names, or null if out of range.</summary>
    public static BlueprintDeclaration? Resolve(this BlueprintAsset asset, VariableRef reference)
    {
        if (!reference.IsResolved) return null;
        var kind = reference.Kind.ToDeclarationKind();
        var of   = asset.Declarations.Of(kind).ToList();
        return reference.Index >= 0 && reference.Index < of.Count ? of[reference.Index] : null;
    }
}
