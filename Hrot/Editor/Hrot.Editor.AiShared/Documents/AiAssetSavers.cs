using System;
using System.Linq;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.AiEditor.Persistence.Hsm;

namespace Hrot.Editor.AiShared.Documents;

/// <summary>
/// The ONE implementation of "turn an AI asset DTO into the bytes on disk", shared by every host.
///
/// <para>⭐⭐ <b>Why this takes a DTO and not an asset.</b> 📐 Measured <c>2026-08-27</c>:
/// <c>Hrot.BTree.Editor</c>, <c>Hrot.Hsm.Editor</c> AND <c>Hrot.Blueprints.Editor</c> all reference
/// <c>Hrot.Editor.AiShared</c>. ⇒ ⛔ this assembly can NEVER name <c>BehaviorTreeAsset</c>,
/// <c>HsmAsset</c> or <c>BlueprintAsset</c> — doing so is a circular project reference, not a style
/// choice. ⭐ That constraint was already recorded, in
/// <see cref="SaveAllAiDocumentsCommand"/>'s own summary: <i>"Kind-specific serialization is injected
/// as delegates to avoid circular assembly references … design §PU-602"</i>.</para>
///
/// <para>⭐⭐⭐ <b>The way through:</b> the DTOs (<see cref="BehaviorTreeAssetDto"/>,
/// <see cref="HsmAssetDto"/>) live in <c>Hrot.AiEditor.Persistence</c>, which this assembly DOES
/// reference, and every serialize step already takes a DTO. ⇒ only <c>ToDto(asset)</c> needs the
/// concrete type. <b>The host maps; this class owns everything after the map.</b></para>
///
/// <para>⚠ Before this class the two hosts each carried their own copy of the three lines below.
/// 📐 They were SEMANTICALLY identical and syntactically drifted — the editor used <c>as</c> + a null
/// check and a <c>prettyJson</c> local, CGF used <c>is not … return</c> and inlined the flatten. ⛔ The
/// drift had already happened, which is the argument for one implementation, not against it.</para>
///
/// <para>📄 Design: <c>docs/DESIGN_Subsystem_Composition_Unification.md</c> §5c.6 (decision
/// <c>E1</c>).</para>
/// </summary>
public static class AiAssetSavers
{
    /// <summary>
    /// Serializes a BTree DTO to its aesthetic JSON form and writes it atomically.
    /// ⚠ The flatten pass is part of the contract, not a nicety: it is what keeps numeric arrays on
    /// one line, so a hand-diff of a saved asset stays readable.
    /// </summary>
    public static void SaveBTree(BehaviorTreeAssetDto dto, string path)
    {
        if (dto == null) throw new ArgumentNullException(nameof(dto));
        WriteJson(path, Hrot.AiEditor.Persistence.BTree.BTreeJsonServices.Serialize(dto));
    }

    /// <summary>Serializes an HSM DTO to its aesthetic JSON form and writes it atomically.</summary>
    public static void SaveHsm(HsmAssetDto dto, string path)
    {
        if (dto == null) throw new ArgumentNullException(nameof(dto));
        WriteJson(path, Hrot.AiEditor.Persistence.Hsm.HsmJsonServices.Serialize(dto));
    }

    /// <summary>
    /// The shared "which in-memory model does this document hold" lookup, previously duplicated in
    /// both hosts' Blueprint save delegate.
    ///
    /// <para>⭐ Returns <c>object?</c> deliberately — the caller casts to its own concrete asset type,
    /// which is the one step this assembly cannot express (see the class summary).</para>
    ///
    /// <para>⛔ The dirty-TRACKER stays with the caller: 📐 only the editor constructs one, and a
    /// null-tolerant shared field would be a capability that silently does nothing on CGF
    /// (ruling 49 — absent-and-explained beats present-and-broken). Design §5c.6 <c>E5</c>.</para>
    /// </summary>
    public static object? ResolveAssetRef(AiDocumentManager? documents, Guid assetId)
    {
        var doc = documents?.OpenDocuments.FirstOrDefault(d => d.Asset.AssetId == assetId);
        return (doc?.ViewState as Hrot.Editor.AiShared.Windows.AiCanvasContext)?.AssetRef;
    }

    /// <summary>
    /// Flatten + atomic write. ⭐ Internal so the reload path and the rails can assert on exactly the
    /// bytes the save path produces, rather than on a re-implementation of them.
    /// </summary>
    internal static void WriteJson(string path, string json)
        => Hrot.AiEditor.Persistence.AtomicFileWriter.Write(
            path, Fdp.Toolkit.Serialization.JsonAestheticFormatter.FlattenNumericArrays(json));
}
