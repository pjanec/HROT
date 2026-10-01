using System;
using Hrot.Editor.AiShared.Catalog;

namespace Hrot.Editor.AiShared.Inspector;

/// <summary>
/// ⭐⭐⭐ <b>Marks a StructEdit string field that names ANOTHER ASSET, to be chosen from the catalogue
/// rather than typed.</b>
/// 📄 <c>AI_Editor_Shared_Infrastructure.md</c> §7.1a.
///
/// <para>🔒 User, <c>2026-09-26</c>: *"the tree asset must be pickable."*</para>
///
/// <para>📐 <b>Measured before building: there was NO asset picker.</b> All ELEVEN picker attributes
/// in the repo pick a <b>symbol</b> — a method (the binding drawer, <c>CE-417</c>), event, guard, state,
/// blackboard field, anim marker, montage, property path, working slot. ⛔ <b>Not one picked an
/// asset</b>, which is why <c>BTreeSubtreeFacet.SubtreeName</c> is labelled <i>"Referenced asset"</i>
/// and is plain free text. ⇒ ⭐ <b>this is a BUILD, not an adoption</b> — the rarer answer in this
/// codebase, and stated plainly because the usual one is the opposite.</para>
///
/// <para>⚠ <b>The field holds the asset's NAME, never its Guid.</b> A Guid is unreadable in an
/// inspector and unmergeable in a diff. Where a stable identity is also needed, the owning model
/// keeps a Guid <b>beside</b> the name and <c>SubtreeReferenceResolver</c> reconciles them
/// (<c>Q36-B = A</c>).</para>
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class AiAssetPickerAttribute : Attribute
{
    /// <summary>⭐ Only assets of this kind are offered. ⚠ A picker that offered every kind would let
    /// a designer point an HSM state at another HSM — which the cycle detector would then have to
    /// reject after the fact, instead of the choice never being offered.</summary>
    public AssetKind Kind { get; }

    public AiAssetPickerAttribute(AssetKind kind) => Kind = kind;
}
