namespace Hrot.AiEditor.Persistence;

/// <summary>
/// ⭐⭐⭐ <b>Batch 91 (<c>91b</c>) — a persisted Approach-A alias binding.</b>
///
/// <para>📄 <b>Design basis, verbatim</b> — <c>BTree_HSM_JSON_Persistence_Detailed_Design.md:132</c>
/// lists what the DTO must carry: <i>"subtree sync bindings, <b>alias relationships</b>,
/// conflict/unused suppressions (today smuggled in the <c>[*Layout]</c> method — promoted to
/// first-class JSON)"</i>. ⚠⚠ <b>Three things; TWO were built.</b> Aliases are the one that was
/// skipped ⇒ 🔴 <b>every alias a designer authored was gone when the asset reopened</b>, together
/// with the badge, the type-match decision and the cross-region refusal that guarded it.</para>
///
/// <para>⭐⭐ <b>ONE type, both hosts.</b> It lives in the root persistence namespace rather than in
/// <c>…Persistence.BTree</c> and <c>…Persistence.Hsm</c> precisely so there is not one of these per
/// host — 📌 ruling 9, and the handoff's <i>"do not invent a third shape"</i>. ⭐ It mirrors
/// <c>SubtreeSyncBindingDto</c>'s shape: a flat property bag keyed from the owning collection.</para>
///
/// <para>⭐⭐⭐ <b><c>DtoTypeId</c> is a STRING, and that is not a compromise.</b>
/// <c>BlackboardAliasBinding.DtoType</c> is a <c>System.Type</c>, which cannot round-trip through
/// JSON. ⛔ The alias's DTO struct lives in a BEHAVIOR assembly *(<c>Hrot.AI.Behaviors</c>, …)*, not in
/// the editor — ⭐ which is exactly why both mappers already own a <c>ResolveClrType</c> that probes
/// the loaded assemblies. ⇒ <b>this field is written as <c>Type.FullName</c> and read back through
/// that EXISTING resolver</b>, ⛔ not through a new one.</para>
///
/// <para>⚠ <b>Unresolvable on load is NOT a throw.</b> A behavior assembly can legitimately be absent
/// *(a tool, a partial workspace)*; the resolver's own contract already returns
/// <c>typeof(object)</c> rather than failing, and an alias whose type cannot be resolved is still a
/// real authored relationship the designer should see rather than silently lose.</para>
/// </summary>
public sealed class BlackboardAliasBindingDto
{
    /// <summary>The sub-asset that requires this DTO.</summary>
    public System.Guid RequiringAssetId { get; set; }

    /// <summary>The element (node / state) inside that asset.</summary>
    public System.Guid RequiringElementId { get; set; }

    /// <summary>⚠ Display text, kept so a stale alias can NAME what it pointed at even after the
    /// sub-asset is gone — ⛔ which is what makes <c>PruneStaleAliasBindings</c>' report legible.</summary>
    public string RequiringAssetName { get; set; } = string.Empty;

    /// <summary>The path inside the requiring element.</summary>
    public string RequiredByPath { get; set; } = string.Empty;

    /// <summary>⭐ <c>Type.FullName</c> — resolved on load by the mapper's existing
    /// <c>ResolveClrType</c>. ⛔ Never an <c>AssemblyQualifiedName</c>: the surrounding corpus keys
    /// every other persisted type by full name, and mixing the two would be two spellings of one id.</summary>
    public string DtoTypeId { get; set; } = string.Empty;
}
