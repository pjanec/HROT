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
}
