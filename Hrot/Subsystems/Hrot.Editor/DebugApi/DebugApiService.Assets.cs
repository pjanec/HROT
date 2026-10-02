using System;
using System.Linq;
using System.Text.Json.Nodes;
using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Documents;
using Hrot.Editor.AiShared;

namespace Hrot.Editor.DebugApi
{
    /// <summary>
    /// ⭐⭐⭐ <b>cgf==editor SLICE 2 — the AI-asset DRIVE surface: discover · open · switch tab · focus.</b>
    /// 📄 <c>docs/DESIGN_Cgf_Editor_Sharing_Slice2_Open_Asset.md</c> §3 *(why it is in-slice)* · §3a
    /// *(addressing)* · §4/§5 *(the diagrams)*.
    ///
    /// <para>⛔⛔ <b>Why this group had to exist before a populated asset could be PROVEN.</b> 📐 A panel
    /// publishes its view-model only when it DRAWS, and the graph canvas draws the ACTIVE document. ⇒ with
    /// no way to open an asset over HTTP, every authoring panel could only ever be captured in its EMPTY
    /// state — which is exactly what slice 1 measured and what made its conformance comparison weaker than
    /// it looked *(two empty panels agree perfectly)*.</para>
    ///
    /// <para>⭐⭐ <b>It DRIVES existing machinery; it re-implements nothing.</b>
    /// <see cref="AiDocumentManager"/> already owns the tab model *(<c>Open</c> · <c>Activate</c> ·
    /// <c>OpenDocuments</c> · <c>Active</c>)* and <see cref="AssetCatalog"/> already owns the index. ⛔ A
    /// second tab list here would be the duplicate ruling 9 forbids — 📌 the same reasoning that made
    /// <c>SwitchPerspective</c> delegate its validation to the window manager.</para>
    ///
    /// <para>⭐⭐⭐ <b>THREE WAYS IN, and none of them puts a raw path in a URL segment</b> *(§3a)*:
    /// the <c>Guid</c> *(stable, URL-safe as a segment)*, the relative <c>SourceFilePath</c> *(human, in the
    /// BODY)*, and discovery through <c>GET /assets</c>. ⚠ <c>Name</c> is deliberately NOT an address —
    /// two subfolders may hold <c>blueprint1.bp.json</c>.</para>
    /// </summary>
    public sealed partial class DebugApiService
    {
        // ⭐⭐⭐ LATE-BOUND for the same measured reason as `_perspectives`: this service is constructed in
        //    Initialize, and the catalog / document manager / window manager only exist once the shell is
        //    built in RegisterWindows. ⛔ The silent-default shape is controlled the same two ways —
        //    Attach is called on the line after the shell is built, and a rail asserts HasAssetAccess on
        //    the CONSTRUCTED object rather than trusting the composition root.
        private AssetCatalog?      _assets;
        private AiDocumentManager? _documents;
        private WindowManager?     _windows;

        /// <summary>
        /// ⭐ Hands this service the asset shell. Called from the composition root that built it —
        /// <c>EditorSubsystem.RegisterWindows</c> and <c>CgfSubsystem.BuildAiShell</c>.
        /// </summary>
        public void AttachAssetShell(AssetCatalog catalog, AiDocumentManager documents, WindowManager windows)
        {
            _assets    = catalog   ?? throw new ArgumentNullException(nameof(catalog));
            _documents = documents ?? throw new ArgumentNullException(nameof(documents));
            _windows   = windows   ?? throw new ArgumentNullException(nameof(windows));
        }

        /// <summary>⭐ Exposed for the forwarding rail — ⛔ a rail must reach the CONSTRUCTED object.</summary>
        internal bool HasAssetAccess => _assets != null && _documents != null;

        private const string NoAssetAccess =
            "No AI-asset shell is wired into this host. The DebugApiService is constructed before the "
            + "window manager exists, so the composition root must call AttachAssetShell(...) once the "
            + "AiShared shell is built. This is a wiring defect, not a missing capability.";

        // ══ discovery ═════════════════════════════════════════════════════════

        /// <summary>
        /// <c>GET /assets</c> — every AI asset this host has indexed, with both of its addresses.
        /// </summary>
        /// <remarks>
        /// ⭐⭐ <b>An EMPTY list is a real answer here, and it is the one that needed a voice.</b> 📐 On a
        /// deployed node the source tree does not exist, so the catalog indexes nothing *(ruling 67)*. ⇒ the
        /// payload carries <c>count: 0</c> and a <c>note</c> saying why, ⛔ rather than looking like a host
        /// that simply has no assets.
        /// </remarks>
        public (JsonNode? Result, string? Error, string? HintCategory) ListAssets()
        {
            if (_assets == null) return (null, NoAssetAccess, DebugApiHints.Panel);

            var arr = new JsonArray();
            foreach (var a in _assets.All.OrderBy(a => a.SourceFilePath, StringComparer.Ordinal))
                arr.Add(Describe(a));

            var result = new JsonObject
            {
                ["count"]  = arr.Count,
                ["assets"] = arr,
            };

            if (arr.Count == 0)
                result["note"] = "The catalog indexed NOTHING. On a deployed node the source asset tree is "
                               + "absent (asset roots must come from config — ruling 67); in a dev run this "
                               + "means the Hrot.AI.Behaviors project directory was not found above the "
                               + "working directory. Check the host log for the resolution warning.";

            return (result, null, null);
        }

        /// <summary>⭐ ONE projection of an asset, so discovery and the open responses cannot disagree.</summary>
        private static JsonObject Describe(IEditableAsset a) => new()
        {
            ["assetId"]        = a.AssetId.ToString(),
            ["name"]           = a.Name,
            ["kind"]           = a.Kind.ToString(),
            // ⭐ The HUMAN address (§3a). ⚠ Normalised to forward slashes so a caller can paste it back
            //   into POST /assets/open unchanged on either OS.
            ["sourceFilePath"] = (a.SourceFilePath ?? string.Empty).Replace('\\', '/'),
            ["isDirty"]        = a.IsDirty,
        };

        // ══ open ══════════════════════════════════════════════════════════════

        /// <summary>
        /// <c>POST /assets/{assetId}/open</c> — open by the stable <c>Guid</c>.
        /// </summary>
        public (JsonNode? Result, string? Error, string? HintCategory) OpenAssetById(string? assetId)
        {
            if (_assets == null || _documents == null) return (null, NoAssetAccess, DebugApiHints.Panel);

            if (!Guid.TryParse(assetId, out var id))
                return (null,
                        $"'{assetId}' is not a GUID. Asset ids are GUIDs — call GET /assets to list them, "
                      + "or use POST /assets/open with the relative sourceFilePath instead.",
                        DebugApiHints.Panel);

            var asset = _assets.FindByAssetId(id);
            if (asset == null)
                return (null,
                        $"No asset with id {id}. This host has {_assets.All.Count} indexed; call GET /assets.",
                        DebugApiHints.Panel);

            return (OpenAndDescribe(asset), null, null);
        }

        /// <summary>
        /// <c>POST /assets/open {"path": "Assets/Blueprints/sub/x.bp.json"}</c> — open by the HUMAN address.
        /// </summary>
        /// <remarks>
        /// ⭐⭐ <b>The path travels in the BODY, and that is the whole point</b> *(§3a)*: a relative path
        /// carries slashes and dots, so a URL SEGMENT would need encoding the caller keeps getting wrong.
        /// <para>⚠ <b>An ambiguous suffix is REPORTED with its candidates, ⛔ never resolved by picking the
        /// first.</b> 📌 Two folders may hold the same file name — silently opening one of them is the
        /// wrong-asset bug this endpoint exists to avoid.</para>
        /// </remarks>
        public (JsonNode? Result, string? Error, string? HintCategory) OpenAssetByPath(JsonNode? body)
        {
            if (_assets == null || _documents == null) return (null, NoAssetAccess, DebugApiHints.Panel);

            var path = body?["path"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(path))
                return (null, "Body must be {\"path\": \"<relative sourceFilePath from GET /assets>\"}.",
                        DebugApiHints.Panel);

            var matches = _assets.FindAllBySourceFilePath(path!);

            if (matches.Count == 0)
                return (null,
                        $"No asset whose sourceFilePath ends with '{path}'. Call GET /assets and copy a "
                      + "sourceFilePath verbatim — the match is a path SUFFIX at a folder boundary, so "
                      + "'sub/x.bp.json' works but 'x' does not.",
                        DebugApiHints.Panel);

            if (matches.Count > 1)
                return (null,
                        $"'{path}' matches {matches.Count} assets — say which: ["
                      + string.Join(", ", matches.Select(m => m.SourceFilePath.Replace('\\', '/')))
                      + "]. Use a longer path suffix, or open by assetId.",
                        DebugApiHints.Panel);

            return (OpenAndDescribe(matches[0]), null, null);
        }

        /// <summary>⭐ The one open path — ⛔ two endpoints, one behaviour.</summary>
        private JsonObject OpenAndDescribe(IEditableAsset asset)
        {
            var doc = _documents!.Open(asset);

            var result = Describe(doc.Asset);
            result["opened"]            = true;
            result["activeAssetId"]     = _documents.Active?.Asset.AssetId.ToString();
            result["openDocumentCount"] = _documents.OpenDocuments.Count;
            // ⚠ Stated, not implied — the same frame-boundary contract GET /perspectives carries.
            result["note"] = "the canvas and outline publish the opened asset on the NEXT frame — step a "
                           + "tick before reading GET /panels, or you read the previous content.";
            return result;
        }

        // ══ tabs ══════════════════════════════════════════════════════════════

        /// <summary>
        /// <c>GET /documents</c> — the open graph tabs and which one is active.
        /// </summary>
        public (JsonNode? Result, string? Error, string? HintCategory) ListDocuments()
        {
            if (_documents == null) return (null, NoAssetAccess, DebugApiHints.Panel);

            var activeId = _documents.Active?.Asset.AssetId;

            var arr = new JsonArray();
            foreach (var d in _documents.OpenDocuments)
            {
                var o = Describe(d.Asset);
                o["isActive"] = activeId.HasValue && d.Asset.AssetId == activeId.Value;
                arr.Add(o);
            }

            return (new JsonObject
            {
                ["activeAssetId"] = activeId?.ToString(),
                ["count"]         = arr.Count,
                ["documents"]     = arr,
            }, null, null);
        }

        /// <summary>
        /// <c>POST /documents/{assetId}/activate</c> — make an already-open tab the active one.
        /// </summary>
        /// <remarks>
        /// ⚠ <b>Only an OPEN document can be activated</b>, and a closed one is a 404 rather than an
        /// implicit open: ⛔ *"activate"* and *"open"* are different intents, and collapsing them would
        /// hide a caller's wrong assumption about what is on screen.
        /// </remarks>
        public (JsonNode? Result, string? Error, string? HintCategory) ActivateDocument(string? assetId)
        {
            if (_documents == null) return (null, NoAssetAccess, DebugApiHints.Panel);

            if (!Guid.TryParse(assetId, out var id))
                return (null, $"'{assetId}' is not a GUID. Call GET /documents for the open tabs.",
                        DebugApiHints.Panel);

            var doc = _documents.OpenDocuments.FirstOrDefault(d => d.Asset.AssetId == id);
            if (doc == null)
                return (null,
                        $"No OPEN document for asset {id}. Open it first (POST /assets/{{assetId}}/open); "
                      + "activate only switches between tabs that are already open.",
                        DebugApiHints.Panel);

            _documents.Activate(doc);

            return (new JsonObject
            {
                ["activeAssetId"] = _documents.Active?.Asset.AssetId.ToString(),
                ["note"]          = "the panels re-publish for the newly active document on the NEXT frame.",
            }, null, null);
        }

        // ══ focus ═════════════════════════════════════════════════════════════

        /// <summary>
        /// <c>POST /panels/{panelId}/focus</c> — open and focus a window by its panel id.
        /// </summary>
        /// <remarks>
        /// ⭐⭐ <b>The unknown-id case is a 404 with the reason, ⛔ not the silent no-op the underlying
        /// call would give.</b> 📐 <c>WindowManager.FocusWindow</c> is documented as a *"silent no-op for
        /// unknown ids"* — correct for a UI callback, ⛔ useless over HTTP, where a caller that typo'd an
        /// id would get a 200 and then read the wrong panel. ⇒ this checks first and says so.
        /// </remarks>
        public (JsonNode? Result, string? Error, string? HintCategory) FocusPanel(string? panelId)
        {
            if (_windows == null) return (null, NoAssetAccess, DebugApiHints.Panel);

            if (string.IsNullOrWhiteSpace(panelId))
                return (null, "Path must be /panels/{panelId}/focus.", DebugApiHints.Panel);

            if (!_windows.TryGetWindow(panelId!, out var win))
                return (null,
                        $"No registered window with id '{panelId}'. Call GET /panels for the captured ids — "
                      + "note a window id is the PANEL id, not the panel KIND.",
                        DebugApiHints.Panel);

            _windows.FocusWindow(panelId!);

            return (new JsonObject
            {
                ["panelId"]     = panelId,
                ["perspective"] = win.OwningPerspective,
                ["isOpen"]      = win.IsOpen,
                // ⚠ A perspective-bound window in ANOTHER perspective is shown by PINNING it — say so,
                //   because the caller may have expected a perspective switch instead.
                ["isPinned"]    = win.IsPinned,
                ["note"]        = "focus takes effect on the NEXT frame; a perspective-bound window in "
                                + "another perspective is pinned rather than switched to.",
            }, null, null);
        }

        // ══ save + reload (cgf==editor slice 3) ═══════════════════════════════
        //
        // ⭐⭐⭐ 📄 DESIGN_Cgf_Editor_Sharing_Slice3_Editing_HotReload.md §5/§6 ⑤. These make the
        //    EDIT→SAVE→RELOAD cycle drivable headlessly, which is the only way the slice is provable.
        // ⛔⛔ THE COLLISION BOUNDARY (§8): this file stays STRICTLY save/reload. Node authoring —
        //    create an asset, add/connect nodes, edit params over MCP — belongs to AQ56's parallel
        //    track in its OWN route file. ⚠ Adding an authoring route here is the merge conflict the
        //    partition exists to prevent.

        private Func<string, string>? _saveAsset;
        private Func<string, string>? _reloadAsset;

        /// <summary>
        /// ⭐ Hands this service the host's save and reload actions.
        ///
        /// <para>⚠⚠ <b>DELEGATES, not the services themselves</b>, and for the settled reason this
        /// codebase already uses for <c>WatchEntityPicker</c>: <c>QuickReloadService</c> lives in
        /// <c>Hrot.Blueprints.Editor</c> and the save delegates are composed per host. ⛔ Taking the
        /// concrete types would point this API at one host's composition — and BOTH hosts wire this.</para>
        ///
        /// <para>⭐ Each returns a STATUS STRING rather than a bool: a failed compile is a legitimate
        /// outcome of editing, and *"what went wrong"* is the whole value of the response.</para>
        /// </summary>
        public void AttachAssetEditing(Func<string, string> saveAsset, Func<string, string> reloadAsset)
        {
            _saveAsset   = saveAsset   ?? throw new ArgumentNullException(nameof(saveAsset));
            _reloadAsset = reloadAsset ?? throw new ArgumentNullException(nameof(reloadAsset));
        }

        /// <summary>⭐ Exposed for the forwarding rail — ⛔ a rail must reach the CONSTRUCTED object.</summary>
        internal bool HasAssetEditing => _saveAsset != null && _reloadAsset != null;

        private const string NoAssetEditing =
            "This host wires no AI-asset save/reload. The composition root must call "
            + "AttachAssetEditing(...) once the AiShared shell and the reload pipeline are built.";

        /// <summary>
        /// <c>POST /assets/{assetId}/save</c> — persist the edited asset to its source file.
        /// </summary>
        /// <remarks>
        /// ⚠⚠ <b>It saves EVERY dirty open document, not only this one — stated because the route shape
        /// suggests otherwise.</b> 📐 <c>SaveAllAiDocumentsCommand</c> is the shared save path and is
        /// all-documents by construction *(it is what the editor's "Save All" button runs)*. ⛔ A
        /// per-asset save here would re-implement its dirty check, its empty-path warning and its
        /// clean-marking — ruling 9's duplicate. ⭐ The <c>assetId</c> is not decorative: it must name an
        /// OPEN document, which is what makes the call meaningful and its refusal informative.
        /// </remarks>
        public (JsonNode? Result, string? Error, string? HintCategory) SaveAsset(string? assetId)
        {
            if (_saveAsset == null || _documents == null)
                return (null, NoAssetEditing, DebugApiHints.Panel);

            var (doc, error) = ResolveOpenDocument(assetId);
            if (error != null) return (null, error, DebugApiHints.Panel);

            var status = _saveAsset(assetId!);

            return (new JsonObject
            {
                ["assetId"]        = assetId,
                ["name"]           = doc!.Asset.Name,
                ["sourceFilePath"] = (doc.Asset.SourceFilePath ?? string.Empty).Replace('\\', '/'),
                ["status"]         = status,
                ["stillDirty"]     = doc.IsDirty,
                ["note"]           = "this runs the shared Save-All command, so every DIRTY open document "
                                   + "is written, not only this one. A document with no source path is "
                                   + "skipped with a warning rather than throwing.",
            }, null, null);
        }

        /// <summary>
        /// <c>POST /assets/{assetId}/reload</c> — recompile the asset and commit it into the running registry.
        /// </summary>
        /// <remarks>
        /// ⭐⭐ <b>Reload compiles from the IN-MEMORY asset, ⛔ not from the file on disk</b> — the
        /// editor's own documented behaviour. ⇒ ⚠ a reload reflects the EDIT even if it was never
        /// saved, and saving is therefore a separate intent rather than a precondition.
        /// <para>⭐ The asset is ACTIVATED first: the reload pipeline acts on the active document, so
        /// reloading a background tab without activating it would silently recompile the wrong graph.</para>
        /// <para>⚠ <b>Soft vs Hard is REPORTED, not decided here</b> *(§17)*: a Hard reload resets live
        /// instances, and ruling 53 puts that confirmation at the INTERACTIVE node — ⛔ never a modal on
        /// a headless host.</para>
        /// </remarks>
        public (JsonNode? Result, string? Error, string? HintCategory) ReloadAsset(string? assetId)
        {
            if (_reloadAsset == null || _documents == null)
                return (null, NoAssetEditing, DebugApiHints.Panel);

            var (doc, error) = ResolveOpenDocument(assetId);
            if (error != null) return (null, error, DebugApiHints.Panel);

            var status = _reloadAsset(assetId!);

            return (new JsonObject
            {
                ["assetId"] = assetId,
                ["name"]    = doc!.Asset.Name,
                ["kind"]    = doc.Kind.ToString(),
                ["status"]  = status,
                ["note"]    = "compiled from the IN-MEMORY asset, so this reflects unsaved edits. A Hard "
                            + "reload resets live instances by design; its confirmation belongs to the "
                            + "interactive node (ruling 53), not to this call.",
            }, null, null);
        }

        /// <summary>⭐ ONE resolution, so save and reload cannot refuse differently for the same input.</summary>
        private (AiDocument? Doc, string? Error) ResolveOpenDocument(string? assetId)
        {
            if (!Guid.TryParse(assetId, out var id))
                return (null, $"'{assetId}' is not a GUID. Call GET /documents for the open tabs.");

            var doc = _documents!.OpenDocuments.FirstOrDefault(d => d.Asset.AssetId == id);
            if (doc == null)
                return (null,
                        $"No OPEN document for asset {id}. Open it first (POST /assets/{{assetId}}/open) — "
                      + "save and reload act on open documents, not on files.");

            return (doc, null);
        }
    }
}
