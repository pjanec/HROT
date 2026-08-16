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
/// ⭐ <b>The durable half.</b> The list is <b>seeded</b> from
/// <see cref="StaticTypeRegistry.EditorOfferableTypeIds"/>, which sits in the same file as the type
/// table and the coercion table it must agree with. Before BP-87 the picker offered <b>eight types
/// the compiler could not resolve</b>: <c>sbyte ushort uint ulong</c> were registered under no name at
/// all, and <c>Vector2/3/4 Quaternion</c> only under their fully-qualified names — so choosing one
/// produced an asset the editor itself could not compile.
/// </para>
///
/// <para>
/// ⭐⭐ <b><c>S5</c>: the projection is no longer DIRECT.</b> It goes through
/// <c>BlueprintTypeSystem.SelectableTypeIds</c>, which canonicalises that seed and unions it with the
/// discovered <c>[BlackboardDtoStruct]</c> types — so the parameter combo and the variable modal are
/// one set rather than two.
/// </para>
/// </summary>
public static class BlueprintTypeChoices
{
    /// <summary>
    /// Type IDs offered by the picker, in display order. Guaranteed resolvable by the compiler —
    /// locked by <c>BP87_TypePickerTests</c>.
    ///
    /// <para>
    /// ⭐⭐⭐ <b><c>S5</c> — this is now THE offerable set, shared with the variable modal.</b> It used
    /// to be <c>StaticTypeRegistry.EditorOfferableTypeIds</c> directly, while
    /// <c>VariableCreateModal</c> read <c>BlueprintTypeSystem.SelectableTypeIds</c> — 🔴 <b>two
    /// disjoint answers to one question</b>, so a designer could give a VARIABLE a
    /// <c>[BlackboardDtoStruct]</c> type and could not give a PARAMETER one. ⛔ Ruling 9, in the UI.
    /// </para>
    ///
    /// <para>
    /// ⚠ <b>The spelling changed with it: canonical FQNs, not aliases.</b> That is the union's own
    /// rail (<c>NoShortNamesAreOffered</c>) and it is the safer direction — a bare <c>"int"</c> written
    /// into an asset is the alias-vs-FQN split <c>BP-203</c> was filed for. Aliases remain valid
    /// INPUT: <see cref="IndexOfTypeId"/> resolves both spellings onto one entry, so every shipped
    /// asset still selects correctly.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> TypeIds => Host.BlueprintTypeSystem.SelectableTypeIds;

    /// <summary>The type a newly added parameter gets before the designer picks one.</summary>
    public static string DefaultTypeId => TypeIds[0];

    /// <summary>
    /// BP-114: the resolved <c>IrTypeRef.FullName</c> for each entry of <see cref="TypeIds"/>, in the
    /// same order, computed once rather than re-resolving 17 types on every ImGui frame for every
    /// parameter row. Every entry is guaranteed non-null because <c>OfferedTypes_AllResolve</c> locks
    /// that every offered id resolves; a null here would mean that lock broke.
    /// </summary>
    /// <remarks>
    /// ⚠ <b><c>S5</c> made this <see cref="Lazy{T}"/>, and it had to.</b> <see cref="TypeIds"/> now
    /// reflects over LOADED assemblies for its struct half, so a static initializer would force that
    /// discovery at type-load — freezing whatever happened to be loaded then, which in a test host is
    /// nothing. ⛔ Same load-order trap the union itself is deferred for.
    /// ⭐ A <c>null</c> entry is legal: a discovered <c>[BlackboardDtoStruct]</c> is accepted by the
    /// compiler through Stage 4's dotted-FQN path, not by the registry table, so it has no canonical
    /// alias to match — <see cref="IndexOfTypeId"/>'s exact-string pass finds it first anyway.
    /// </remarks>
    private static readonly Lazy<string?[]> OfferedFullNamesLazy = new(() => TypeIds
        .Select(id => StaticTypeRegistry.Instance.TryResolve(new BlueprintTypeRef { TypeId = id }, out var ir)
            ? ir.FullName
            : null)
        .ToArray());

    private static string?[] OfferedFullNames => OfferedFullNamesLazy.Value;

    /// <summary>
    /// BP-114: resolve a stored <c>ParameterDecl.Type.TypeId</c> to the index of the matching entry
    /// in <see cref="TypeIds"/>, for driving the parameter Type combo's selected index.
    ///
    /// <para>
    /// ⚠ <b>Why this exists.</b> Type ids reach the combo in two spellings — the compiler's
    /// <i>canonical FQN</i> (<c>"System.Int32"</c>) and the editor's older <i>alias</i>
    /// (<c>"int"</c>) — and the exact-string match the combo used to do silently fell back to index 0
    /// (<c>"bool"</c>) whenever they differed. This resolves both onto one underlying type before
    /// comparing, so they collapse onto the same combo entry. ⭐ <c>S5</c> flipped which spelling is
    /// OFFERED (now the FQN) and which is merely accepted — both directions still work, and that is
    /// the point of resolving rather than string-matching.
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
