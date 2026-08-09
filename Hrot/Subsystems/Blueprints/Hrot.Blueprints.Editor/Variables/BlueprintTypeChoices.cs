using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Catalogs;

namespace Hrot.Blueprints.Editor.Variables;

/// <summary>
/// BP-87: the type choices a blueprint type picker offers, <b>projected from the compiler's own
/// <see cref="StaticTypeRegistry"/></b> rather than hand-maintained.
///
/// <para>
/// ⚠ <b>Why this is not <c>BlackboardTypeHelper.DefaultKnownTypeNames</c>.</b> That array lives in
/// <c>Hrot.Editor.AiShared</c> and is shared by three editors — blueprints, behaviour trees and HSM
/// (plus the shared Add-Variable dropdown). Widening it to fix a blueprint-only problem would change
/// the BTree and HSM blackboard pickers too. The consumer (<c>ParameterRowsView</c>) was already
/// blueprint-local; only the <i>list</i> was shared, so the fix is to make the list blueprint-local
/// as well and leave <c>Hrot.Editor.AiShared</c> alone.
/// </para>
///
/// <para>
/// ⭐ <b>The durable half.</b> The list is a projection of
/// <see cref="StaticTypeRegistry.EditorOfferableTypeIds"/>, which sits in the same file as the type
/// table and the coercion table it must agree with. Before BP-87 the picker offered <b>eight types
/// the compiler could not resolve</b>: <c>sbyte ushort uint ulong</c> were registered under no name at
/// all, and <c>Vector2/3/4 Quaternion</c> only under their fully-qualified names — so choosing one
/// produced an asset the editor itself could not compile.
/// </para>
/// </summary>
public static class BlueprintTypeChoices
{
    /// <summary>
    /// Type IDs offered by the picker, in display order. Guaranteed resolvable by the compiler —
    /// locked by <c>BP87_TypePickerTests</c>.
    /// </summary>
    public static IReadOnlyList<string> TypeIds => StaticTypeRegistry.EditorOfferableTypeIds;

    /// <summary>The type a newly added parameter gets before the designer picks one.</summary>
    public static string DefaultTypeId => TypeIds[0];

    /// <summary>
    /// BP-114: the resolved <c>IrTypeRef.FullName</c> for each entry of <see cref="TypeIds"/>, in the
    /// same order, computed once rather than re-resolving 17 types on every ImGui frame for every
    /// parameter row. Every entry is guaranteed non-null because <c>OfferedTypes_AllResolve</c> locks
    /// that every offered id resolves; a null here would mean that lock broke.
    /// </summary>
    private static readonly string?[] OfferedFullNames = TypeIds
        .Select(id => StaticTypeRegistry.Instance.TryResolve(new BlueprintTypeRef { TypeId = id }, out var ir)
            ? ir.FullName
            : null)
        .ToArray();

    /// <summary>
    /// BP-114: resolve a stored <c>ParameterDecl.Type.TypeId</c> to the index of the matching entry
    /// in <see cref="TypeIds"/>, for driving the parameter Type combo's selected index.
    ///
    /// <para>
    /// ⚠ <b>Why this exists.</b> The picker offers <i>aliases</i> (<c>"int"</c>, <c>"float"</c>, …)
    /// but most shipped assets store the compiler's <i>canonical FQN</i> (<c>"System.Int32"</c>,
    /// <c>"System.Single"</c>, …) — the exact-string match the combo used to do never matched, and
    /// silently fell back to index 0 (<c>"bool"</c>). This resolves both spellings to the same
    /// underlying type before comparing, so <c>"System.Int32"</c> and <c>"int"</c> collapse onto the
    /// same combo entry.
    /// </para>
    ///
    /// <para>
    /// ⚠ <b>Why unresolvable/null/empty returns -1, not 0.</b> Returning 0 IS the BP-114 defect: it
    /// displays "bool" for a type that is not a bool. -1 is a legal Dear ImGui "no selection" index —
    /// <c>ImGui::Combo</c> only invokes the item getter when the index is in range, so passing -1
    /// renders a blank combo preview instead of a false one. Blank is honest; a wrong type name is a
    /// lie the designer can act on (e.g. "correct" the visibly-wrong entry, silently retyping the
    /// parameter for real).
    /// </para>
    /// </summary>
    public static int IndexOfTypeId(string? typeId)
    {
        if (string.IsNullOrEmpty(typeId))
            return -1;

        var typeIds = TypeIds;

        // 1) Exact ordinal match against the offered aliases wins first -- cheap, and preserves the
        //    exact alias the asset stored when it IS already an offered alias.
        for (int i = 0; i < typeIds.Count; i++)
        {
            if (typeIds[i] == typeId)
                return i;
        }

        // 2) Otherwise resolve the stored TypeId and compare its canonical FullName (ordinal,
        //    case-insensitive -- StaticTypeRegistry's own type table uses OrdinalIgnoreCase) against
        //    each offered entry's precomputed FullName.
        if (!StaticTypeRegistry.Instance.TryResolve(new BlueprintTypeRef { TypeId = typeId }, out var resolved))
            return -1;

        for (int i = 0; i < OfferedFullNames.Length; i++)
        {
            if (OfferedFullNames[i] != null &&
                string.Equals(OfferedFullNames[i], resolved.FullName, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }
}
