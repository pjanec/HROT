using System;
using Hrot.Editor.AiShared.Catalog;

namespace Hrot.Editor.AiShared.References;

/// <summary>
/// ⭐ The outcome of reconciling a stored (name, assetId) pair against the catalogue.
/// </summary>
/// <param name="Name">The name to store — <b>healed</b> when the Guid resolved and the name did not.</param>
/// <param name="AssetId">The Guid to store. ⛔ Never cleared; see <see cref="SubtreeReferenceResolver"/>.</param>
/// <param name="IsResolved">Whether the reference currently points at a live asset of the wanted kind.</param>
/// <param name="Healed">⭐ True when the NAME was rewritten from the Guid ⇒ <b>the caller must mark the
/// document dirty</b>: a heal is a real edit and is lost if it is not saved.</param>
public readonly record struct SubtreeReference(
    string Name,
    Guid   AssetId,
    bool   IsResolved,
    bool   Healed);

/// <summary>
/// ⭐⭐⭐ <b>THE ONE DECISION both subsystems make about a stored asset reference.</b>
/// 📄 <c>AI_Editor_Shared_Infrastructure.md</c> §7.1a — <b>its sequence diagram is the specification
/// for this method.</b>
///
/// <para>🔒 User, <c>2026-09-26</c>: *"go with keep both and heal from guid; same would be good for
/// btree … no differences, consistency."*</para>
///
/// <para>⭐ <b>Why a name AND a Guid.</b> The <b>name</b> is what the designer picks, what the diff
/// shows and what the runtime looks up. The <b>Guid</b> is a persisted fallback identity that
/// survives a rename. <c>Q36-B = A</c> approved the pair; this method is the rule that makes the
/// redundancy earn its keep instead of becoming two sources of truth.</para>
///
/// <para>⛔⛔ <b>THE RULE THIS FIXES — NEVER ERASE.</b> 📐 The shipped <c>BTreeSubtreeResolver</c> set
/// <c>SubtreeAssetId = Guid.Empty</c> whenever the name missed. ⚠ <b>A missed name IS the rename
/// case</b>, so that line destroyed the one field that could still identify the asset, at exactly
/// the moment it was needed. ⇒ here, <b>a reference that cannot resolve keeps everything it has</b>:
/// the next load, or a restored file, may well find it.</para>
///
/// <para>⚠ <b>What this does NOT do:</b> it does not walk a model. The walk is subsystem-specific
/// (BTree nodes vs HSM states) and stays in each host design; only the DECISION is shared.</para>
/// </summary>
public static class SubtreeReferenceResolver
{
    /// <summary>
    /// ⭐ Reconcile a stored reference against <paramref name="catalog"/>, preferring the NAME and
    /// healing it from <paramref name="assetId"/> when the name no longer resolves.
    /// </summary>
    /// <param name="wantedKind">⭐ Both lookups are kind-checked: an asset of the wrong kind is
    /// <b>not</b> a match, so a renamed-and-retyped asset dangles rather than silently binding.</param>
    public static SubtreeReference Resolve(
        IAssetCatalog? catalog,
        string?        name,
        Guid           assetId,
        AssetKind      wantedKind)
    {
        string current = name ?? string.Empty;

        // ⚠ No reference at all is an ORDINARY state (a state that hosts nothing), not an error and
        //   not "dangling". ⛔ Reporting it as unresolved would light up every ordinary state.
        if (string.IsNullOrEmpty(current) && assetId == Guid.Empty)
            return new SubtreeReference(string.Empty, Guid.Empty, IsResolved: false, Healed: false);

        // ① The NAME wins when it resolves — it is what the designer last chose.
        if (!string.IsNullOrEmpty(current))
        {
            var byName = catalog?.FindByName(current);
            if (byName != null && byName.Kind == wantedKind)
                return new SubtreeReference(current, byName.AssetId, IsResolved: true, Healed: false);
        }

        // ② The name missed. If the GUID still resolves, the asset was RENAMED ⇒ heal the name.
        //    ⭐ This is the entire reason both are stored.
        if (assetId != Guid.Empty)
        {
            var byId = catalog?.FindByAssetId(assetId);
            if (byId != null && byId.Kind == wantedKind)
                return new SubtreeReference(byId.Name, assetId, IsResolved: true, Healed: true);
        }

        // ③ Neither resolves ⇒ DANGLING. ⛔ Keep BOTH fields exactly as they were.
        return new SubtreeReference(current, assetId, IsResolved: false, Healed: false);
    }
}
