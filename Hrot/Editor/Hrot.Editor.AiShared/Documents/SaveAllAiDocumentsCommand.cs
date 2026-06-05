using System;
using System.IO;
using Hrot.AiEditor.Persistence;
using Hrot.Editor.AiShared.Documents;

namespace Hrot.Editor.AiShared;

/// <summary>
/// Iterates all open AI editor documents and saves each dirty, path'd document to its
/// source file.  Kind-specific serialization is injected as delegates to avoid circular
/// assembly references (mirrors <c>AiAssetEmitService</c>'s delegate pattern, design §PU-602).
///
/// <para>
/// <b>Routing by kind:</b>
/// <list type="bullet">
///   <item><see cref="AssetKind.Blueprint"/> → <paramref name="saveBlueprintDelegate"/> +
///       <see cref="AiDocument.MarkClean"/>.</item>
///   <item><see cref="AssetKind.BTree"/> → <paramref name="saveBTreeDelegate"/> (maps to DTO,
///       serializes, atomically writes to <see cref="IEditableAsset.SourceFilePath"/>) +
///       <see cref="AiDocument.MarkClean"/>.</item>
///   <item><see cref="AssetKind.Hsm"/> → <paramref name="saveHsmDelegate"/> + <see cref="AiDocument.MarkClean"/>.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>No-path rule:</b> when <see cref="IEditableAsset.SourceFilePath"/> is null or empty
/// (assembly-loaded assets awaiting migration), the document is <em>skipped</em> with a
/// <paramref name="report"/> WARNING.  It is <em>never</em> thrown upon and remains dirty.
/// (Design §PU-602 / BATCH-06 CRITICAL scope: most BTree/HSM docs are assembly-loaded today
/// and will appear as skipped until PU-401 migration runs.)
/// </para>
///
/// <para>
/// <b>Clean docs:</b> skipped (no write, no report entry).
/// </para>
/// </summary>
public static class SaveAllAiDocumentsCommand
{
    /// <summary>
    /// Delegate type for per-document kind saves.
    /// Receives the document's backing asset and its resolved source path.
    /// Implementations must write the asset JSON to <paramref name="path"/> and may throw
    /// on IO failure (the caller will propagate the exception).
    /// <c>doc.MarkClean()</c> is called by <see cref="Execute"/> after the delegate returns.
    /// </summary>
    public delegate void SaveDelegate(IEditableAsset asset, string path);

    /// <summary>
    /// Executes the save-all operation.
    /// </summary>
    /// <param name="manager">
    ///   The document manager whose <see cref="AiDocumentManager.OpenDocuments"/> are iterated.
    ///   No-op when null.
    /// </param>
    /// <param name="saveBlueprintDelegate">
    ///   Saves a Blueprint asset to <c>path</c>.  Wired to
    ///   <c>SaveActiveBlueprintCommand.Save(asset, path)</c> in production.
    ///   No-op for Blueprint docs when null.
    /// </param>
    /// <param name="saveBTreeDelegate">
    ///   Saves a BTree asset to <c>path</c> as JSON.  Wired to
    ///   <c>BehaviorTreeAssetMapper.ToDto → BTreeJsonServices.Serialize → AtomicFileWriter.Write</c>
    ///   in production.  No-op for BTree docs when null.
    /// </param>
    /// <param name="saveHsmDelegate">
    ///   Saves an HSM asset to <c>path</c> as JSON.  Wired to
    ///   <c>HsmAssetMapper.ToDto → HsmJsonServices.Serialize → AtomicFileWriter.Write</c>
    ///   in production.  No-op for HSM docs when null.
    /// </param>
    /// <param name="report">
    ///   Optional callback for status messages (success and WARNING-level skip notices).
    ///   May be null.
    /// </param>
    public static void Execute(
        AiDocumentManager? manager,
        SaveDelegate?       saveBlueprintDelegate,
        SaveDelegate?       saveBTreeDelegate,
        SaveDelegate?       saveHsmDelegate,
        Action<string>?     report = null)
    {
        if (manager is null)
            return;

        // Snapshot the list; saving may trigger further ActiveChanged events but the list
        // itself is not mutated by a save operation, so a snapshot is safe.
        var docs = manager.OpenDocuments;
        // ToArray to avoid mutating issues if Close is called during save.
        var snapshot = new AiDocument[docs.Count];
        for (int i = 0; i < docs.Count; i++)
            snapshot[i] = docs[i];

        foreach (var doc in snapshot)
        {
            if (!doc.IsDirty)
                continue;  // clean — no action needed

            var asset = doc.Asset;
            var path  = asset.SourceFilePath;

            if (string.IsNullOrEmpty(path))
            {
                // No path → skip with WARNING; leave dirty; never throw.
                report?.Invoke($"[WARN] Skipped '{asset.Name}': no source path (awaiting migration/path-at-creation).");
                continue;
            }

            try
            {
                switch (doc.Kind)
                {
                    case AssetKind.Blueprint:
                        if (saveBlueprintDelegate != null)
                        {
                            saveBlueprintDelegate(asset, path);
                            doc.MarkClean();
                            report?.Invoke($"Saved Blueprint '{asset.Name}' → {path}");
                        }
                        break;

                    case AssetKind.BTree:
                        if (saveBTreeDelegate != null)
                        {
                            var btreeCollision = AssetBaseNameCollisionGuard.CheckCollisionOnDisk(
                                path, dir => Directory.EnumerateFiles(dir));
                            if (btreeCollision != null)
                            {
                                // D5 collision: block the write, leave dirty, never throw.
                                report?.Invoke($"[BLOCKED] '{asset.Name}': {btreeCollision}");
                                break;
                            }
                            saveBTreeDelegate(asset, path);
                            doc.MarkClean();
                            report?.Invoke($"Saved BTree '{asset.Name}' → {path}");
                        }
                        break;

                    case AssetKind.Hsm:
                        if (saveHsmDelegate != null)
                        {
                            var hsmCollision = AssetBaseNameCollisionGuard.CheckCollisionOnDisk(
                                path, dir => Directory.EnumerateFiles(dir));
                            if (hsmCollision != null)
                            {
                                // D5 collision: block the write, leave dirty, never throw.
                                report?.Invoke($"[BLOCKED] '{asset.Name}': {hsmCollision}");
                                break;
                            }
                            saveHsmDelegate(asset, path);
                            doc.MarkClean();
                            report?.Invoke($"Saved HSM '{asset.Name}' → {path}");
                        }
                        break;

                    default:
                        report?.Invoke($"[WARN] Skipped '{asset.Name}': unsupported kind {doc.Kind}.");
                        break;
                }
            }
            catch (Exception ex)
            {
                // Report the failure but do NOT rethrow — save-all should continue for
                // remaining documents even if one fails.
                report?.Invoke($"[ERROR] Failed to save '{asset.Name}': {ex.Message}");
            }
        }
    }
}
