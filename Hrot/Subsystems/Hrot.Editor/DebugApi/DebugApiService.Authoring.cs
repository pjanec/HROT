using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using Hrot.Editor.AiShared.Documents;
using Hrot.Editor.AiShared.Windows;
using NodeEditor.Core;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Primitives;

namespace Hrot.Editor.DebugApi
{
    /// <summary>
    /// ⭐⭐⭐ <b>THE MCP AUTHORING SURFACE — read-then-edit-by-guid over the graph, and the world-delete
    /// that scenario authoring was missing.</b>
    /// 📄 <c>docs/DESIGN_Mcp_Authoring.md</c> §1 *(two surfaces)* · §3 *(the read shape)* · §5/§6 *(the
    /// diagrams)* · §7 *(the items)* · §8 *(the collision plan — this is the OWN route file)*.
    ///
    /// <para>⭐⭐⭐ <b>The one decision everything else follows from: an MCP edit IS a human edit.</b>
    /// *(`Q56-A`, resolved with the user.)* ⇒ every mutation here builds a
    /// <see cref="GraphCommand"/> with <see cref="CommandBuilder"/> and applies it through
    /// <see cref="GraphView.Execute"/> — ⭐ **the same undo stack, the same host sink, the same
    /// validator** the canvas uses. ⛔ There is no parallel authoring model, so there is nothing that
    /// can accept an edit the editor would refuse.</para>
    ///
    /// <para>⛔⛔ <b>THE COLLISION BOUNDARY (§8), from this side.</b> This file is STRICTLY authoring.
    /// Discovery / open / tabs / focus / save / reload live in <c>DebugApiService.Assets.cs</c> and are
    /// the CE-slices' — ⭐ this file CALLS their resolver *(one partial class)* and re-implements none
    /// of them.</para>
    /// </summary>
    public sealed partial class DebugApiService
    {
        // ══ create-asset wiring ═══════════════════════════════════════════════
        //
        // ⭐⭐ A DELEGATE, for the reason slice 3 already established for save/reload: the create path is
        //    host-composition-specific *(per-kind INewAssetService, the Blueprint source-root override,
        //    the per-contributor Refresh, the catalog rebuild)*. ⛔ Taking those types here would point
        //    this API at ONE host's composition.

        /// <summary>Creates an asset of <c>kind</c> named <c>name</c> under <c>relPath</c>.</summary>
        /// <returns>The minted asset id once it is in the catalog, plus a human status either way.</returns>
        public delegate (Guid? AssetId, string Status) CreateAssetDelegate(
            string kind, string name, string relPath);

        private CreateAssetDelegate? _createAsset;

        /// <summary>⭐ Hands this service the host's create-asset action. Called from the composition root.</summary>
        public void AttachAssetAuthoring(CreateAssetDelegate createAsset)
            => _createAsset = createAsset ?? throw new ArgumentNullException(nameof(createAsset));

        /// <summary>⭐ Exposed for the forwarding rail — ⛔ a rail must reach the CONSTRUCTED object.</summary>
        internal bool HasAssetAuthoring => _createAsset != null;

        private const string NoAssetAuthoring =
            "This host wires no AI-asset CREATE path. Creating an asset needs the per-kind "
          + "INewAssetService registry, the source-root override and the catalog refresh, all of which "
          + "are composed per host; the composition root must call AttachAssetAuthoring(...). Editing an "
          + "asset that ALREADY exists does not need this — open it and use /assets/{id}/graph/*.";

        // ══ resolution ════════════════════════════════════════════════════════

        /// <summary>
        /// ⭐ The one way in for every route below: an OPEN document, and the live
        /// <see cref="GraphView"/> the canvas is rendering for it.
        /// </summary>
        /// <remarks>
        /// ⭐⭐ <b>Editing requires an OPEN document, and that is a feature, not a limitation.</b> 📐 The
        /// in-memory graph model — the id space these routes edit by — is built by the per-kind DOCUMENT
        /// FACTORY when the document opens. ⇒ ⛔ there is no graph to address before that, and a route
        /// that silently opened one would hide from the caller that it is now editing something it never
        /// asked to see.
        /// </remarks>
        private (AiDocument? Doc, GraphView? View, string? Error) ResolveGraph(string? assetId)
        {
            if (_documents == null) return (null, null, NoAssetAccess);

            var (doc, error) = ResolveOpenDocument(assetId);
            if (error != null) return (null, null, error);

            if (doc!.ViewState is not AiCanvasContext ctx)
                return (null, null,
                        $"Asset {assetId} ('{doc.Asset.Name}', kind {doc.Kind}) is open but carries no graph "
                      + "view. Only the graph-document kinds — BTree, HSM, Blueprint — have one; a Scenario "
                      + "or Blackboard asset is not authored through this surface.");

            return (doc, ctx.View, null);
        }

        /// <summary>⭐ The canvas context, for the routes that need the editor-command dispatcher too.</summary>
        private AiCanvasContext? ContextOf(AiDocument doc) => doc.ViewState as AiCanvasContext;

        // ══ ① the read ════════════════════════════════════════════════════════

        /// <summary>
        /// <c>GET /assets/{assetId}/graph</c> — the whole graph as JSON, by IN-MEMORY guid.
        /// </summary>
        /// <remarks>
        /// ⭐⭐⭐ <b>This is the primitive that makes read-then-edit-by-guid work</b> *(`Q56-D`)*: the agent
        /// never PREDICTS an id, it reads the ones the edit commands accept. 📄 §3 explains why these are
        /// deliberately NOT the ids in the saved file.
        /// </remarks>
        public (JsonNode? Result, string? Error, string? HintCategory) ReadGraph(string? assetId)
        {
            var (doc, view, error) = ResolveGraph(assetId);
            if (error != null) return (null, error, DebugApiHints.Panel);

            var result = InMemoryGraphSerializer.ToJson(view!.Model);
            result["assetId"] = doc!.Asset.AssetId.ToString();
            result["name"]    = doc.Asset.Name;
            result["kind"]    = doc.Kind.ToString();
            result["note"]    = "every id here is the IN-MEMORY guid the edit routes take. It is NOT the id "
                              + "written to the asset file — the save path rewrites link endpoints to "
                              + "deterministic name-derived ids. Re-read after each edit rather than "
                              + "caching, and never copy an id out of the .json on disk.";
            return (result, null, null);
        }

        /// <summary>
        /// <c>GET /assets/{assetId}/graph/catalog</c> — the node kinds this graph can actually add.
        /// </summary>
        /// <remarks>
        /// ⚠⚠ <b>Not in the design's item list, and building the edit routes without it is what showed why
        /// it has to exist.</b> 📐 <c>POST …/graph/nodes</c> takes a <c>kind</c> STRING, and the host sink
        /// answers an unknown kind by simply not creating the node. ⇒ ⛔ without a discovery route the
        /// agent must GUESS a kind id, and a wrong guess is a silent no-op — 📌 exactly the
        /// <i>"advertised but unreachable"</i> shape `CE-009` §4c caught. ⭐ The catalog is
        /// <see cref="INodeCatalog"/>, which the view already carries; this is a projection, not a
        /// registry. *(Folded into the design — obligation ⑤.)*
        /// </remarks>
        public (JsonNode? Result, string? Error, string? HintCategory) ListGraphNodeKinds(
            string? assetId, string? filter)
        {
            var (_, view, error) = ResolveGraph(assetId);
            if (error != null) return (null, error, DebugApiHints.Panel);

            var entries = view!.Catalog.All;
            var arr     = new JsonArray();

            foreach (var e in entries)
            {
                if (!string.IsNullOrWhiteSpace(filter)
                 && e.Kind.Id.IndexOf(filter!, StringComparison.OrdinalIgnoreCase) < 0
                 && e.DisplayName.IndexOf(filter!, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                arr.Add(new JsonObject
                {
                    ["kind"]         = e.Kind.Id,
                    ["displayName"]  = e.DisplayName,
                    ["category"]     = e.CategoryPath,
                    ["description"]  = e.Description,
                    ["isDeprecated"] = e.IsDeprecated,
                    ["inputs"]       = new JsonArray(e.Inputs.Select(PinSig).ToArray()),
                    ["outputs"]      = new JsonArray(e.Outputs.Select(PinSig).ToArray()),
                });
            }

            return (new JsonObject
            {
                ["count"] = arr.Count,
                ["total"] = entries.Count,
                ["kinds"] = arr,
                ["note"]  = "pass a 'kind' verbatim to POST /assets/{id}/graph/nodes. An unknown kind is "
                          + "refused rather than silently ignored, but only this list is guaranteed valid "
                          + "for THIS graph.",
            }, null, null);

            static JsonNode PinSig(PinSignature p) => new JsonObject
            {
                ["label"] = p.Label,
                ["kind"]  = p.Kind.ToString(),
                ["type"]  = p.Type?.Id,
            };
        }

        // ══ ② the edits ═══════════════════════════════════════════════════════

        /// <summary>
        /// <c>POST /assets/{assetId}/graph/nodes {"kind": "...", "x": 0, "y": 0}</c> — add a node.
        /// </summary>
        /// <remarks>
        /// ⭐⭐ <b>It RETURNS the new guid</b>, which is the half of `Q56-D` that makes determinism a
        /// non-problem: the agent does not need to predict an id, only to receive one.
        /// <para>⚠⚠ <b>A rejected kind is REPORTED, and finding that out costs a lookup rather than trust.</b>
        /// 📐 <c>IGraphCommandSink.Apply</c> can answer <c>Success</c> for a kind the host does not build —
        /// <c>AuthoringPath.AddNode</c> documents exactly that. ⇒ this route re-reads the model and 400s if
        /// the node is not there, ⛔ rather than handing back a guid that addresses nothing.</para>
        /// </remarks>
        public (JsonNode? Result, string? Error, string? HintCategory) AddGraphNode(
            string? assetId, JsonNode? body)
        {
            var (doc, view, error) = ResolveGraph(assetId);
            if (error != null) return (null, error, DebugApiHints.Panel);

            var kind = body?["kind"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(kind))
                return (null,
                        "Body must be {\"kind\": \"<node kind id>\", \"x\": <float>, \"y\": <float>}. "
                      + "Call GET /assets/{assetId}/graph/catalog for the kinds this graph accepts.",
                        DebugApiHints.Panel);

            var pos = new Vector2(ReadFloat(body, "x"), ReadFloat(body, "y"));

            var (fwd, inv) = new CommandBuilder(view!.Model).AddNode(new NodeKindKey(kind!), pos, null);
            var newId      = ((GraphCommand.AddNode)fwd).AssignedId;

            var result = view.Execute(fwd, inv, "Add Node (MCP)");
            if (!result.Success)
                return (null, $"The editor refused to add '{kind}': {result.Message}", DebugApiHints.Panel);

            // ⛔ Trust the MODEL, not the verdict — see the remarks.
            var node = view.Model.FindNode(newId);
            if (node == null)
                return (null,
                        $"'{kind}' produced no node. The host sink accepted the command but built nothing, "
                      + "which is what an unknown kind id looks like from here. Call "
                      + $"GET /assets/{assetId}/graph/catalog and use a 'kind' from that list verbatim.",
                        DebugApiHints.Panel);

            MarkEdited(doc!);

            return (new JsonObject
            {
                ["nodeId"] = newId.Value.ToString(),
                ["kind"]   = node.Kind.Id,
                ["title"]  = node.Title,
                ["pins"]   = new JsonArray(node.Pins.Select(p => (JsonNode)new JsonObject
                {
                    ["pinId"]     = p.Id.Value.ToString(),
                    ["label"]     = p.Label,
                    ["direction"] = p.Direction.ToString(),
                    ["kind"]      = p.Kind.ToString(),
                    ["type"]      = p.Type?.Id,
                }).ToArray()),
                ["note"] = "the pins are returned with the node because linking needs them and a second "
                         + "round-trip to re-read the whole graph would be the common case.",
            }, null, null);
        }

        /// <summary>
        /// <c>POST /assets/{assetId}/graph/links {"fromPin": "guid", "toPin": "guid"}</c> — connect two pins.
        /// </summary>
        /// <remarks>
        /// ⭐⭐⭐ <b>THE VALIDATOR RUNS FIRST — this is design item ⑤, at the level it actually exists.</b>
        /// 📐 <see cref="ILinkValidator"/> is the host's own wire rule, the one the canvas consults while
        /// dragging. ⇒ an MCP wire is refused for the same reason, with the same words, as a wire a
        /// designer drags. ⛔ Skipping it would make MCP the one path that can author an illegal graph.
        /// </remarks>
        public (JsonNode? Result, string? Error, string? HintCategory) AddGraphLink(
            string? assetId, JsonNode? body)
        {
            var (doc, view, error) = ResolveGraph(assetId);
            if (error != null) return (null, error, DebugApiHints.Panel);

            if (!TryPin(body, "fromPin", out var from, out var pinError)
             || !TryPin(body, "toPin",   out var to,   out pinError))
                return (null, pinError, DebugApiHints.Panel);

            var fromPin = view!.Model.FindPin(from);
            var toPin   = view.Model.FindPin(to);
            if (fromPin == null || toPin == null)
                return (null,
                        $"{(fromPin == null ? "fromPin" : "toPin")} is not a pin in this graph. Pin ids come "
                      + $"from GET /assets/{assetId}/graph (or from the add-node response) and are the "
                      + "IN-MEMORY ids — an id copied out of the saved .json addresses nothing here.",
                        DebugApiHints.Panel);

            var verdict = view.Validator.Validate(from, to);
            if (verdict.Verdict == LinkValidity.Invalid)
                return (null,
                        $"The editor refuses {fromPin.Label}({fromPin.Direction}) -> "
                      + $"{toPin.Label}({toPin.Direction}): {verdict.Reason ?? "no reason given"}. "
                      + "This is the SAME validator the canvas consults while dragging a wire.",
                        DebugApiHints.Panel);

            var (fwd, inv) = new CommandBuilder(view.Model).AddLink(from, to);
            var newId      = ((GraphCommand.AddLink)fwd).AssignedId;

            var result = view.Execute(fwd, inv, "Connect Pins (MCP)");
            if (!result.Success)
                return (null, $"The host sink refused the link: {result.Message}", DebugApiHints.Panel);

            MarkEdited(doc!);

            return (new JsonObject
            {
                ["linkId"]       = newId.Value.ToString(),
                ["fromPin"]      = from.Value.ToString(),
                ["toPin"]        = to.Value.ToString(),
                ["requiresCast"] = verdict.RequiresCast,
                ["note"]         = verdict.Verdict == LinkValidity.ValidWithCast
                                 ? "the validator classed this ValidWithCast: the canvas would auto-insert a "
                                 + "cast node. This route connected the pins directly — re-read the graph and "
                                 + "check the host did what you expected."
                                 : "connected through the same command sink and undo stack as a dragged wire.",
            }, null, null);
        }

        /// <summary>
        /// <c>POST /assets/{assetId}/graph/params {"pinId": "guid", "value": &lt;json&gt;}</c> — set a pin's
        /// literal default.
        /// </summary>
        /// <remarks>
        /// ⭐⭐ <b>A PIN default, ⛔ not a free-form node property, and that is a measured choice.</b> 📐
        /// <see cref="CommandBuilder"/> offers <c>SetPinDefault</c> with a genuine inverse *(it reads the
        /// old value off the model)*; it offers NO <c>SetNodeProperty</c> builder, and
        /// <see cref="INodeModel"/> exposes no property bag ⇒ an inverse for one could not be built, so a
        /// node-property route would be the single un-undoable edit in the set. ⚠ Filed rather than faked.
        /// </remarks>
        public (JsonNode? Result, string? Error, string? HintCategory) SetGraphParam(
            string? assetId, JsonNode? body)
        {
            var (doc, view, error) = ResolveGraph(assetId);
            if (error != null) return (null, error, DebugApiHints.Panel);

            if (!TryPin(body, "pinId", out var pinId, out var pinError))
                return (null, pinError, DebugApiHints.Panel);

            var pin = view!.Model.FindPin(pinId);
            if (pin == null)
                return (null, $"No pin {pinId.Value} in this graph. Call GET /assets/{assetId}/graph.",
                        DebugApiHints.Panel);

            if (pin.Direction != PinDirection.Input || pin.Kind != PinKind.Data)
                return (null,
                        $"Pin '{pin.Label}' is {pin.Direction}/{pin.Kind}. A default value belongs to an "
                      + "INPUT DATA pin — an exec pin has no value, and an output's value is computed.",
                        DebugApiHints.Panel);

            var (value, convertError) = CoerceToPinDefault(body?["value"], pin);
            if (convertError != null) return (null, convertError, DebugApiHints.Panel);

            var oldValue   = pin.Default?.Value;
            var (fwd, inv) = new CommandBuilder(view.Model).SetPinDefault(pinId, value);

            var result = view.Execute(fwd, inv, "Set Pin Default (MCP)");
            if (!result.Success)
                return (null, $"The host sink refused the value: {result.Message}", DebugApiHints.Panel);

            MarkEdited(doc!);

            return (new JsonObject
            {
                ["pinId"]         = pinId.Value.ToString(),
                ["label"]         = pin.Label,
                ["previousValue"] = oldValue?.ToString(),
                ["value"]         = view.Model.FindPin(pinId)?.Default?.Value?.ToString(),
                ["note"]          = "'value' is re-read from the model after the edit, so it shows what the "
                                  + "host actually stored rather than what was sent.",
            }, null, null);
        }

        /// <summary>
        /// <c>POST /assets/{assetId}/graph/remove {"nodes": ["guid"], "links": ["guid"]}</c> — delete.
        /// </summary>
        /// <remarks>
        /// ⭐⭐⭐ <b>This route SELECTS and then invokes the editor's own Delete command</b>
        /// *(<see cref="CommandCatalog.DeleteSelection"/>)* — ⛔ it does not build a <c>RemoveNodes</c>
        /// batch of its own. 📐 That command already handles what a hand-rolled removal gets wrong: the
        /// IMPLICITLY deleted links incident to a removed node, the reroute waypoints, the attachments,
        /// and an inverse whose steps are reversed so nodes are restored before the links that reference
        /// them. ⇒ ⭐ reusing it is the difference between undo working and undo appearing to work.
        /// <para>⚠ The selection is a genuine side effect — an MCP delete leaves the canvas selection
        /// cleared, exactly as a human delete does. Stated because it is observable.</para>
        /// </remarks>
        public (JsonNode? Result, string? Error, string? HintCategory) RemoveGraphElements(
            string? assetId, JsonNode? body)
        {
            var (doc, view, error) = ResolveGraph(assetId);
            if (error != null) return (null, error, DebugApiHints.Panel);

            var commands = ContextOf(doc!)?.Commands;
            if (commands == null)
                return (null,
                        "This document carries no editor-command dispatcher, so the shared Delete command "
                      + "cannot be invoked. Removal deliberately has no second implementation here — see "
                      + "the route docs.",
                        DebugApiHints.Panel);

            var nodes = ReadGuidList(body, "nodes");
            var links = ReadGuidList(body, "links");
            if (nodes.Count == 0 && links.Count == 0)
                return (null,
                        "Body must name what to remove: {\"nodes\": [\"guid\", …]} and/or "
                      + "{\"links\": [\"guid\", …]}. Removing nothing is refused rather than answered 200.",
                        DebugApiHints.Panel);

            var missing = new List<string>();
            foreach (var g in nodes) if (view!.Model.FindNode(new NodeId(g)) == null) missing.Add($"node {g}");
            foreach (var g in links) if (view!.Model.FindLink(new LinkId(g)) == null) missing.Add($"link {g}");
            if (missing.Count > 0)
                return (null,
                        $"Not in this graph: {string.Join(", ", missing)}. Nothing was removed — a partial "
                      + $"delete would be worse than a refusal. Re-read GET /assets/{assetId}/graph.",
                        DebugApiHints.Panel);

            var nodeCountBefore = view!.Model.Nodes.Count;
            var linkCountBefore = view.Model.Links.Count;

            view.Selection.Clear();
            foreach (var g in nodes) view.Selection.Add(SelectionEntry.OfNode(new NodeId(g)));
            foreach (var g in links) view.Selection.Add(SelectionEntry.OfLink(new LinkId(g)));

            var invoked = commands.Invoke(CommandCatalog.DeleteSelection);
            if (!invoked.Success)
                return (null, $"The editor's Delete command refused: {invoked.Message}", DebugApiHints.Panel);

            MarkEdited(doc!);

            return (new JsonObject
            {
                ["removedNodes"] = nodeCountBefore - view.Model.Nodes.Count,
                ["removedLinks"] = linkCountBefore - view.Model.Links.Count,
                ["nodeCount"]    = view.Model.Nodes.Count,
                ["linkCount"]    = view.Model.Links.Count,
                ["note"]         = "removedLinks counts the links deleted IMPLICITLY with their nodes too, so "
                                 + "it is usually larger than the 'links' you named. The canvas selection is "
                                 + "left cleared, as after a human delete.",
            }, null, null);
        }

        // ══ ③ create ══════════════════════════════════════════════════════════

        /// <summary>
        /// <c>POST /assets {"kind": "BTree", "name": "Patrol", "path": "sub/folder"}</c> — create an asset.
        /// </summary>
        /// <remarks>
        /// ⭐ Delegates to the host's own New-Asset path *(per-kind <c>INewAssetService</c> + the file write
        /// + the contributor refresh)*, so an MCP-created asset is indistinguishable from a dialog-created
        /// one and appears in <c>GET /assets</c> by the same rebuild.
        /// </remarks>
        public (JsonNode? Result, string? Error, string? HintCategory) CreateAsset(JsonNode? body)
        {
            if (_createAsset == null) return (null, NoAssetAuthoring, DebugApiHints.Panel);

            var kind = body?["kind"]?.GetValue<string>();
            var name = body?["name"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(name))
                return (null,
                        "Body must be {\"kind\": \"BTree|Hsm|Blueprint\", \"name\": \"<asset name>\", "
                      + "\"path\": \"<optional subfolder>\"}.",
                        DebugApiHints.Panel);

            var relPath = body?["path"]?.GetValue<string>() ?? string.Empty;

            var (assetId, status) = _createAsset(kind!, name!, relPath);

            if (assetId == null)
                return (null, status, DebugApiHints.Panel);

            var result = new JsonObject
            {
                ["assetId"] = assetId.Value.ToString(),
                ["name"]    = name,
                ["kind"]    = kind,
                ["status"]  = status,
                ["note"]    = "the asset is in the catalog (GET /assets) and open as a document. Author it "
                            + "with /assets/{assetId}/graph/*, then save and reload.",
            };

            // ⭐ Re-read from the catalog rather than echoing the request — if the two disagree, the
            //   caller should see the CATALOG's answer, which is what every other route reports.
            var catalogued = _assets?.FindByAssetId(assetId.Value);
            if (catalogued != null)
                result["sourceFilePath"] = (catalogued.SourceFilePath ?? string.Empty).Replace('\\', '/');

            return (result, null, null);
        }

        // ══ ④ scenario authoring — the world-manipulation gap ═════════════════

        /// <summary>
        /// <c>DELETE /entities/{networkId}</c> — remove an entity from the world.
        /// </summary>
        /// <remarks>
        /// ⭐⭐⭐ <b>Scenario authoring is WORLD MANIPULATION, not file editing</b> *(`Q56-C`, user: the
        /// scenario file is a reduced SNAPSHOT of the world at save time)*. ⇒ the authoring vocabulary is
        /// the EXISTING <c>/entities/*</c> ops, and 📐 measuring them found exactly one gap: <b>place</b>
        /// had <c>POST /entities/spawn</c>, <b>configure</b> had <c>/attribute</c> and <c>/component</c>,
        /// <b>assign</b> had <c>/attach-blueprint</c> — and <b>delete had nothing.</b> ⭐ This is that one
        /// route, and it publishes the same <see cref="Fdp.Toolkit.NetworkSpawning.Events.DestroyEntityCommand"/>
        /// the CGF canvas delete and the cluster's <c>DeleteEntityRequestSystem</c> publish.
        /// <para>⚠ <b>Queued, like spawn.</b> ELM teardown happens on a later tick — step before asserting
        /// the entity is gone. ⛔ Answering as though it were synchronous would be a lie the caller then
        /// writes a flaky test against.</para>
        /// </remarks>
        public (JsonNode? Result, string? Error, string? HintCategory) DeleteEntity(long networkId)
        {
            if (!_entityMap.TryGetEntity(networkId, out _))
                return (null,
                        $"Entity {networkId} not found. List them with GET /entities — a delete of an "
                      + "entity that is not here is refused rather than queued against nothing.",
                        DebugApiHints.Entity);

            _world.Bus.PublishManaged(new Fdp.Toolkit.NetworkSpawning.Events.DestroyEntityCommand
            {
                NetworkId = networkId,
                Reason    = "mcp-authoring-delete",
            });

            return (new JsonObject
            {
                ["networkId"] = networkId,
                ["queued"]    = true,
                ["note"]      = "teardown runs through the ELM lifecycle on a later tick — call step, then "
                              + "GET /entities to confirm. scenario/save snapshots the world AFTER the "
                              + "teardown lands, not before.",
            }, null, null);
        }

        // ══ helpers ═══════════════════════════════════════════════════════════

        /// <summary>
        /// ⭐⭐ Marks the document dirty so the shared Save-All command includes it.
        /// </summary>
        /// <remarks>
        /// ⭐⭐ <b>A GUARD, and the probe that tried to remove it is why the argument is written down.</b>
        /// 📐 <c>SaveAllAiDocumentsCommand.Execute</c> skips a document whose <c>IsDirty</c> is false, and
        /// <c>AiDocument.MarkDirty</c> had exactly ONE production caller in the repo: the EDITOR's
        /// <c>DocumentOpened</c> factory, which subscribes <c>doc.Asset.Changed</c> — ⛔ and only when a
        /// regeneration scheduler exists. ⚠⚠ <b>CGF never subscribed at all</b>, which made its save a
        /// silent no-op after any edit (<c>MA-003</c> adds the subscription).
        /// <para>📐 <b>Measured by revert probe B:</b> with this line commented out the create/edit/save
        /// rail stayed GREEN — because on the EDITOR the <c>Asset.Changed</c> subscription already marks
        /// the document. ⇒ ⛔ <b>this line is REDUNDANT on a correctly-wired host, and that is exactly
        /// what it defends against.</b> ⭐ The subscription is per-host COMPOSITION, which a host can forget
        /// and did; this line is in the code path that performs the edit, which it cannot.</para>
        /// <para>⚠ <b>Stated honestly: no rail covers it</b> — pinning it needs a host whose composition
        /// lacks the subscription, and the one that did (CGF) now has it. Filed as <c>MA-009</c>.</para>
        /// </remarks>
        private static void MarkEdited(AiDocument doc) => doc.MarkDirty();

        private static float ReadFloat(JsonNode? body, string key)
        {
            var node = body?[key];
            if (node is null) return 0f;
            try { return node.GetValue<float>(); } catch { return 0f; }
        }

        private static bool TryPin(JsonNode? body, string key, out PinId pin, out string? error)
        {
            pin = default;
            var raw = body?[key]?.GetValue<string>();
            if (!Guid.TryParse(raw, out var g))
            {
                error = $"'{key}' must be a pin GUID from GET /assets/{{assetId}}/graph; got '{raw ?? "null"}'.";
                return false;
            }
            pin   = new PinId(g);
            error = null;
            return true;
        }

        private static List<Guid> ReadGuidList(JsonNode? body, string key)
        {
            var list = new List<Guid>();
            if (body?[key] is JsonArray arr)
                foreach (var item in arr)
                {
                    if (item is null) continue;
                    string? raw;
                    try { raw = item.GetValue<string>(); } catch { continue; }
                    if (Guid.TryParse(raw, out var g)) list.Add(g);
                }
            return list;
        }

        /// <summary>
        /// ⭐ Converts the JSON <c>value</c> to the CLR type the pin's CURRENT default already holds.
        /// </summary>
        /// <remarks>
        /// ⚠ <b>Why the existing default is the type authority.</b> <see cref="IPinModel.Type"/> is a host
        /// <c>TypeKey</c> string, not a CLR type, and this assembly has no business mapping one to the
        /// other — that is the host type system's job. ⇒ the boxed default the host itself produced IS the
        /// answer, and when there is none the JSON primitive is passed through for the host to interpret.
        /// </remarks>
        private static (object? Value, string? Error) CoerceToPinDefault(JsonNode? value, IPinModel pin)
        {
            if (value is null) return (null, null);   // an explicit null clears the default

            object? raw;
            try
            {
                raw = value.GetValueKind() switch
                {
                    System.Text.Json.JsonValueKind.True   => true,
                    System.Text.Json.JsonValueKind.False  => false,
                    System.Text.Json.JsonValueKind.String => value.GetValue<string>(),
                    System.Text.Json.JsonValueKind.Number => value.GetValue<double>(),
                    _ => value.ToJsonString(),
                };
            }
            catch (Exception ex)
            {
                return (null, $"Could not read 'value': {ex.Message}");
            }

            var target = pin.Default?.Value?.GetType();
            if (target == null || raw == null || target.IsInstanceOfType(raw))
                return (raw, null);

            try
            {
                if (target.IsEnum)
                    return (Enum.Parse(target, raw.ToString() ?? string.Empty, ignoreCase: true), null);

                return (Convert.ChangeType(raw, target, System.Globalization.CultureInfo.InvariantCulture), null);
            }
            catch (Exception ex)
            {
                return (null,
                        $"Pin '{pin.Label}' currently holds a {target.Name}; '{raw}' does not convert to it "
                      + $"({ex.GetType().Name}). Send a value of that type.");
            }
        }
    }
}
