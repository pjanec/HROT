using System.IO;
using Fdp.Toolkit.Serialization;
using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;
using Hrot.Editor.AiShared.Documents;
using Hrot.Editor.AiShared.Windows;
using NodeEditor.Primitives;

namespace Hrot.Blueprints.Editor;

/// <summary>
/// Implements "Save active blueprint to its .bp.json file."
///
/// PROJECTION-ONLY rule (DEBT-BCP-005, MVE-BATCH-04):
/// <see cref="Node.Pins"/> entries are an editor-projection populated at render time.
/// Saved files must store every node with <c>Pins = []</c> so the file stays byte-stable
/// and the compiler's pins-empty path is preserved.  On reload the two-pass binding
/// rehydrates pins from the persisted <see cref="Link"/> records.
///
/// Implementation: immediately before serialization the Pins list on each node whose Pins
/// is non-empty is <b>temporarily</b> swapped to an empty sentinel list.  The original list
/// reference is restored in a <c>finally</c> block so the live in-memory asset is
/// <b>never left mutated</b> — even if serialization throws.
/// </summary>
public sealed class SaveActiveBlueprintCommand
{
    // ── result ────────────────────────────────────────────────────────────────

    public enum SaveStatus
    {
        /// <summary>Blueprint was saved successfully.</summary>
        Saved,
        /// <summary>No blueprint is currently open; nothing was saved.</summary>
        NoBlueprintOpen,
        /// <summary>The open blueprint has no source-file path; nothing was saved.</summary>
        NoSourcePath,
    }

    public sealed class SaveResult
    {
        public SaveStatus Status    { get; }
        public string?    SavedPath { get; }
        public string     Message   { get; }

        private SaveResult(SaveStatus status, string? savedPath, string message)
        {
            Status    = status;
            SavedPath = savedPath;
            Message   = message;
        }

        public static SaveResult Ok(string path)  => new(SaveStatus.Saved,           path, $"Saved → {path}");
        public static SaveResult NoBlueprint()    => new(SaveStatus.NoBlueprintOpen, null, "No blueprint is open.");
        public static SaveResult NoPath()         => new(SaveStatus.NoSourcePath,    null, "Cannot save: blueprint has no source file path.");
    }

    // ── headless-testable core ────────────────────────────────────────────────

    /// <summary>
    /// Core save logic — no ImGui, fully headless-testable.
    ///
    /// Each node's <c>Pins</c> list is temporarily replaced with an empty list for the
    /// duration of serialization, then immediately restored.  The live asset is
    /// <b>never persistently mutated</b>.
    /// </summary>
    /// <param name="asset">The in-memory blueprint to persist.</param>
    /// <param name="path">Absolute (or rooted) path to write.</param>
    public static void Save(BlueprintAsset asset, string path)
    {
        if (asset is null) throw new ArgumentNullException(nameof(asset));
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path must be non-empty.", nameof(path));

        // ── Anti-drift canonicalization (DEBT-BCP-005 deep fix) ──────────────────────────────
        // Because saves are projection-only ("Pins": []), pins are reconstructed on reload and
        // links reference pins by GUID only. Reconstruction (editor Rebuild + compiler Stage0, in
        // parity) binds a link BY NAME when its GUID equals the pin's deterministic
        // Deterministic("pin:{node}:{name}:{dir}") GUID, else falls back to ORDER-FRAGILE positional
        // binding. Editor-created pins are born with RANDOM GUIDs (IdGenerator.NewPinId), so their
        // links hit the positional path — and a non-canonical link order silently swaps pins that
        // share a direction bucket (e.g. Branch's exec-In vs data-In "Condition"), corrupting the
        // asset on the next round-trip.
        //
        // Fix: before stripping, rewrite every link endpoint whose pin is present in memory to that
        // pin's deterministic GUID. The saved file then binds entirely BY NAME on reload — the
        // positional swap becomes impossible. Serialize-only: link edits (and the pin swap) are
        // reverted in the finally, so the live asset is never mutated. Nodes with no in-memory pins
        // (already projection-only, loaded) are left untouched — their links are either already
        // deterministic or migrated by the one-time repo-wide pass.
        // Uses IdGenerator.Deterministic (≡ compiler DeterministicIds.PinId) for cross-tool parity.
        var pinToDeterministic = new Dictionary<Guid, Guid>();
        foreach (var graph in asset.Graphs)
            foreach (var node in graph.Nodes)
                foreach (var pin in node.Pins)
                    pinToDeterministic[pin.Id] =
                        IdGenerator.Deterministic($"pin:{node.Id:N}:{pin.Name}:{pin.Direction}");

        var linkEdits = new List<(Link link, Guid from, Guid to)>();
        foreach (var graph in asset.Graphs)
            foreach (var link in graph.Links)
            {
                var newFrom = pinToDeterministic.TryGetValue(link.FromPinId, out var f) ? f : link.FromPinId;
                var newTo   = pinToDeterministic.TryGetValue(link.ToPinId,   out var t) ? t : link.ToPinId;
                if (newFrom != link.FromPinId || newTo != link.ToPinId)
                {
                    linkEdits.Add((link, link.FromPinId, link.ToPinId));
                    link.FromPinId = newFrom;
                    link.ToPinId   = newTo;
                }
            }

        // Collect all nodes whose Pins list is non-empty; remember the original references.
        var swapped = new List<(Node node, List<Pin> original)>();
        foreach (var graph in asset.Graphs)
        {
            foreach (var node in graph.Nodes)
            {
                if (node.Pins.Count > 0)
                    swapped.Add((node, node.Pins));
            }
        }

        // Swap out (replace with empty) so the serializer sees Pins:[].
        foreach (var (node, _) in swapped)
            node.Pins = new List<Pin>(0);

        string json;
        try
        {
            json = BlueprintJsonServices.Serialize(asset);
        }
        finally
        {
            // Restore originals — live asset pins and link GUIDs are intact again.
            foreach (var (node, original) in swapped)
                node.Pins = original;
            foreach (var (link, from, to) in linkEdits)
            {
                link.FromPinId = from;
                link.ToPinId   = to;
            }
        }

        // Pretty-print with numeric arrays inlined (same post-process as ScenarioFileService).
        var prettyJson = JsonAestheticFormatter.FlattenNumericArrays(json);

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(path, prettyJson);
    }

    // ── resolver ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the active blueprint + source path from the production
    /// <see cref="AiDocumentManager"/>, saves, and marks the document clean.
    ///
    /// <para>
    /// Resolution chain (same as the run-button, MVE-BATCH-03):<br/>
    ///   <c>manager.Active?.ViewState as AiCanvasContext → ctx.AssetRef as BlueprintAsset</c><br/>
    ///   <c>manager.Active?.Asset.SourceFilePath</c>
    /// </para>
    /// <para>
    /// No-op + appropriate status when no blueprint is open or the source path is empty.
    /// The live document's dirty flag is cleared after a successful save.
    /// </para>
    /// </summary>
    /// <param name="manager">The active-document manager (production or test stub).</param>
    /// <param name="dirtyTracker">Used to mark the asset clean after save.</param>
    /// <param name="report">Optional callback for status messages (may be null).</param>
    public static SaveResult SaveFromActiveDocument(
        AiDocumentManager? manager,
        DirtyTracker       dirtyTracker,
        Action<string>?    report = null)
    {
        if (dirtyTracker is null) throw new ArgumentNullException(nameof(dirtyTracker));

        var active = manager?.Active;
        if (active is null)
        {
            var r = SaveResult.NoBlueprint();
            report?.Invoke(r.Message);
            return r;
        }

        // Resolve the live BlueprintAsset from the canvas context.
        var ctx   = active.ViewState as AiCanvasContext;
        var asset = ctx?.AssetRef as BlueprintAsset;

        if (asset is null)
        {
            var r = SaveResult.NoBlueprint();
            report?.Invoke(r.Message);
            return r;
        }

        // Source path comes from the IEditableAsset (BlueprintFileAsset.SourceFilePath).
        var path = active.Asset?.SourceFilePath;
        if (string.IsNullOrEmpty(path))
        {
            var r = SaveResult.NoPath();
            report?.Invoke(r.Message);
            return r;
        }

        Save(asset, path);

        // Mark both the AiDocument and the DirtyTracker clean.
        active.MarkClean();
        dirtyTracker.MarkClean(asset.AssetId);

        var ok = SaveResult.Ok(path);
        report?.Invoke(ok.Message);
        return ok;
    }
}
