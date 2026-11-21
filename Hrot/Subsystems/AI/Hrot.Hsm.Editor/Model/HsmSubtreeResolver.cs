using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.References;

namespace Hrot.Hsm.Editor.Model;

/// <summary>
/// ⭐⭐ <b>Reconciles every hosting state's subtree reference against the asset catalogue.</b>
/// 📄 <c>HSM_Editor_NodeEditor_Host_Design.md</c> §11.1a.
///
/// <para>⭐ <b>The WALK is HSM's; the DECISION is shared.</b> The heal rule lives in
/// <see cref="SubtreeReferenceResolver"/> (📄 <c>AI_Editor_Shared_Infrastructure.md</c> §7.1a) so
/// BTree and HSM cannot drift on what a rename means. ⛔ This type only knows how to find the
/// states — the twin, <c>BTreeSubtreeResolver</c>, only knows how to find the nodes.</para>
///
/// <para>⚠ Call it after load and after a hot reload, exactly like <c>BTreeSubtreeResolver</c>.</para>
/// </summary>
public static class HsmSubtreeResolver
{
    /// <summary>
    /// ⭐ Resolves every state's subtree reference in place.
    /// </summary>
    /// <returns>
    /// ⭐⭐ <b>How many names were HEALED from their Guid.</b> ⚠ Non-zero means the asset changed on
    /// disk-equivalent terms and <b>the caller must mark it dirty</b> — a heal that is never saved
    /// is re-done on every load and the file keeps the stale name for ever.
    /// </returns>
    public static int Resolve(HsmAsset asset, IAssetCatalog catalog)
    {
        if (asset is null) throw new System.ArgumentNullException(nameof(asset));

        int healed = 0;

        foreach (var s in asset.AllStates)
        {
            // ⚠ A state that hosts nothing is the ORDINARY case — skip without touching it, so an
            //   empty reference never reads as "unresolved".
            if (string.IsNullOrEmpty(s.SubtreeName) && s.SubtreeAssetId == System.Guid.Empty)
            {
                s.IsSubtreeResolved = false;
                continue;
            }

            var r = SubtreeReferenceResolver.Resolve(
                catalog, s.SubtreeName, s.SubtreeAssetId, AssetKind.BTree);

            s.SubtreeName       = string.IsNullOrEmpty(r.Name) ? null : r.Name;
            s.SubtreeAssetId    = r.AssetId;
            s.IsSubtreeResolved = r.IsResolved;

            if (r.Healed) healed++;
        }

        return healed;
    }
}
