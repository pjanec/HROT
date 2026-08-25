using System;
using System.Collections.Generic;
using System.Reflection;
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
        /// <param name="recipe">
        /// ⭐⭐ <b><c>MA-021</c></b> — the name of a recipe from <c>GET /assets/recipes</c>, or
        /// <see langword="null"/> for the kind's blank template. ⛔ An unmatched name must be REFUSED by
        /// the host, never silently downgraded to the blank template.
        /// </param>
        /// <returns>The minted asset id once it is in the catalog, plus a human status either way.</returns>
        public delegate (Guid? AssetId, string Status) CreateAssetDelegate(
            string kind, string name, string relPath, string? recipe);

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

        // ══ MA-020: recipe discovery ══════════════════════════════════════════
        //
        // ⭐⭐⭐ AQ57's whole finding: this registry ALREADY EXISTS. `RecipePickerSource` (AiShared) projects
        //    `INewAssetService.AvailableRecipes()` per kind, and it is what the New-Asset picker draws from.
        //    ⛔ So this route BUILDS NOTHING — it takes the same dictionary the picker takes and reports
        //    the same projection over HTTP. A parallel "recipe registry" would be ruling 9's duplicate.

        private IReadOnlyDictionary<Hrot.Editor.AiShared.AssetKind,
                                    Hrot.Editor.AiShared.Recipes.INewAssetService>? _newAssetServices;

        private Func<Hrot.Editor.AiShared.IEditableAsset, string?>? _recipeDescribe;
        private Func<Hrot.Editor.AiShared.IEditableAsset, string?>? _recipeCategory;

        /// <summary>
        /// ⭐ Hands this service the host's per-kind new-asset registry, so recipes can be listed.
        /// </summary>
        /// <param name="services">The same dictionary the host's New-Asset picker is built from.</param>
        /// <param name="describe">
        /// ⭐⭐ Resolves a recipe's DESCRIPTION. ⚠ Supplied by the composition root because only it sees the
        /// concrete per-kind adapters that carry <c>RecipeMetadata</c> — ⛔ this service must not learn
        /// one host's asset types. A <see langword="null"/> describe reports <c>description: null</c>,
        /// which the payload distinguishes from "not looked up".
        /// </param>
        /// <param name="recipeCategory">Resolves a recipe's sub-category, same reasoning.</param>
        public void AttachRecipes(
            IReadOnlyDictionary<Hrot.Editor.AiShared.AssetKind,
                                Hrot.Editor.AiShared.Recipes.INewAssetService> services,
            Func<Hrot.Editor.AiShared.IEditableAsset, string?>? describe = null,
            Func<Hrot.Editor.AiShared.IEditableAsset, string?>? recipeCategory = null)
        {
            _newAssetServices = services ?? throw new ArgumentNullException(nameof(services));
            _recipeDescribe   = describe;
            _recipeCategory   = recipeCategory;
        }

        /// <summary>⭐ Exposed for the forwarding rail — ⛔ a rail must reach the CONSTRUCTED object.</summary>
        internal bool HasRecipes => _newAssetServices != null;

        private const string NoRecipes =
            "This host wires no per-kind INewAssetService registry, so it can offer no recipes. The "
          + "composition root must call AttachRecipes(...) with the same dictionary its New-Asset picker "
          + "uses. A host with no registry also cannot create (POST /assets) — the two share it.";

        /// <summary>
        /// <c>GET /assets/recipes[?kind=BTree]</c> — the templates <c>POST /assets</c> can create from.
        /// </summary>
        /// <remarks>
        /// ⭐⭐ <b>The recipe analog of <c>MA-013</c>'s node-kind discovery</b>: an agent that cannot see the
        /// recipes can only ever create blanks. ⇒ every name here is accepted verbatim as
        /// <c>POST /assets {"recipe": "..."}</c>.
        /// <para>⚠ <c>AvailableRecipes()</c> is called LIVE rather than snapshotted at attach time — the
        /// Blueprint service discovers recipes from disk, so a cached list would go stale silently.</para>
        /// </remarks>
        public (JsonNode? Result, string? Error, string? HintCategory) ListRecipes(string? kindFilter)
        {
            if (_newAssetServices == null) return (null, NoRecipes, DebugApiHints.Panel);

            Hrot.Editor.AiShared.AssetKind? only = null;
            if (!string.IsNullOrWhiteSpace(kindFilter))
            {
                if (!Enum.TryParse<Hrot.Editor.AiShared.AssetKind>(kindFilter, ignoreCase: true, out var k))
                    return (null,
                            $"'{kindFilter}' is not an AssetKind. This host offers: "
                          + string.Join(", ", _newAssetServices.Keys) + ".",
                            DebugApiHints.Panel);
                only = k;
            }

            // ⭐ The SHARED projection — the picker's own ToEntry, so a recipe reads the same over HTTP as
            //   it does in the New-Asset tree (id, name, description, category).
            var source = new Hrot.Editor.AiShared.Browser.RecipePickerSource(
                _newAssetServices, _recipeDescribe, _recipeCategory);

            var recipes = new JsonArray();
            foreach (var choice in source.Query(string.Empty, null))
            {
                if (only != null && choice.Kind != only) continue;
                if (!_newAssetServices.TryGetValue(choice.Kind, out var service)) continue;

                var entry = source.ToEntry(choice);
                recipes.Add(new JsonObject
                {
                    ["id"]              = entry.Id,             // "Kind:Name" — the picker's own key
                    ["kind"]            = choice.Kind.ToString(),
                    ["name"]            = choice.Recipe.Name,   // ⭐ what POST /assets takes as "recipe"
                    ["description"]     = entry.Description,
                    ["category"]        = entry.Category,
                    // ⭐⭐ A BLANK TEMPLATE seeds an empty asset; a CONTENT recipe clones a real one. ⛔ The
                    //   two are not interchangeable and the caller cannot tell them apart from the name.
                    ["isBlankTemplate"] = service.IsBlankTemplate(choice.Recipe),
                    ["sourceFilePath"]  = (choice.Recipe.SourceFilePath ?? string.Empty).Replace('\\', '/'),
                });
            }

            return (new JsonObject
            {
                ["kinds"]   = new JsonArray(_newAssetServices.Keys.Select(k => (JsonNode)k.ToString()!).ToArray()),
                ["recipes"] = recipes,
                ["note"]    = "create with POST /assets {\"kind\": ..., \"name\": ..., \"recipe\": \"<name>\"}. "
                            + "Omit \"recipe\" for the kind's blank template. Descriptions are null when the "
                            + "recipe carries no RecipeMetadata (the synthetic Empty/Starter entries do not).",
            }, null, null);
        }

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

                // ⭐⭐ MA-013 — ONE projection, shared with GET .../catalog/{kind}. ⛔ The list used to
                //    build its own object literal; two projections of one concept drift, and the drift
                //    shows up as a caller trusting the wrong one.
                arr.Add(DescribeKind(e, includeDoc: false));
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

        // ══ ②b THE UNION BACKBONE (MA-012) ════════════════════════════════════
        //
        // ⭐⭐⭐ 📄 DESIGN_Mcp_Authoring.md §11.2. ONE route carrying ONE serialized GraphCommand.
        // ⛔⛔ Why this exists ALONGSIDE the four typed verbs above rather than replacing them:
        //    §11.2 allows typed sugar "so long as they are never a parallel model", and they are not —
        //    they build the same union and apply it the same way. ⭐ What the union route adds is the ~31
        //    variants a curated verb list cannot express, the BTree decorators and HSM regions among them.

        /// <summary>
        /// <c>GET /assets/{assetId}/graph/command</c> — the variants this host accepts, with their fields.
        /// </summary>
        /// <remarks>
        /// ⭐⭐ <b>Self-describing, for the same reason every DebugApi route is</b> *(`R-133`/`HN-030`)*: an
        /// agent must never guess a payload shape. ⛔ Without this the union route would be exactly the
        /// *"advertised but unreachable"* hazard — 35 variants and no way to learn any of their fields.
        /// </remarks>
        public (JsonNode? Result, string? Error, string? HintCategory) DescribeGraphCommands(string? assetId)
        {
            // ⚠ The document still has to be OPEN and a graph document — the variant list is useless to a
            //   caller that cannot then apply one, and answering it anyway would invite a 404 they do not
            //   expect one step later.
            var (_, _, error) = ResolveGraph(assetId);
            if (error != null) return (null, error, DebugApiHints.Panel);

            return (GraphCommandJson.Describe(), null, null);
        }

        /// <summary>
        /// <c>POST /assets/{assetId}/graph/command</c> — apply ONE <c>GraphCommand</c> from JSON.
        /// </summary>
        /// <remarks>
        /// ⭐⭐⭐ <b>The parity guarantee, in one line of code:</b> the deserialized command goes through
        /// <see cref="GraphView.Execute"/> — the SAME undo stack and the SAME host sink a canvas gesture
        /// uses. ⇒ ⛔ there is no MCP-only mutation path that could accept what the editor rejects, for any
        /// variant, on any of the three hosts, with **zero per-host code here**.
        ///
        /// <para>⭐⭐ <b>`Batch` is atomic for free</b> and its inverses are reversed, so a multi-step
        /// authoring sequence is ONE undo entry.</para>
        ///
        /// <para>⚠⚠ <b>`undoable: false` is REPORTED, never faked.</b> A handful of variants — the refactor
        /// ops, `SetNodeProperty`, `RemoveRegion` — cannot have an inverse derived from the read-only model.
        /// ⛔ Recording a wrong one would corrupt the graph on undo, silently; the response says so instead,
        /// and the command still applies.</para>
        /// </remarks>
        public (JsonNode? Result, string? Error, string? HintCategory) ApplyGraphCommand(
            string? assetId, JsonNode? body)
        {
            var (doc, view, error) = ResolveGraph(assetId);
            if (error != null) return (null, error, DebugApiHints.Panel);

            var parsed = GraphCommandJson.Read(body, view!.Model, out var readError);
            if (parsed == null) return (null, readError, DebugApiHints.Panel);

            var nodesBefore = view.Model.Nodes.Count;
            var linksBefore = view.Model.Links.Count;

            GraphCommandResult result;
            try
            {
                // ⭐ Execute records the inverse; with none, apply through the sink directly so the caller
                //   still gets the edit — ⛔ refusing to apply an un-undoable variant would make the whole
                //   refactor group unreachable, which is a bigger loss than one missing undo entry.
                result = parsed.Inverse != null
                    ? view.Execute(parsed.Forward, parsed.Inverse, parsed.Label)
                    : view.Commands.Apply(parsed.Forward);
            }
            catch (Exception ex)
            {
                // ⚠ A host sink CAN throw on a shape it does not expect. ⛔ Letting that become a 500 would
                //   read as "the API broke"; it is the HOST refusing, and the caller needs to see which.
                return (null,
                        $"The host sink threw applying '{body?["type"]}': {ex.GetType().Name}: {ex.Message}. "
                      + "This is the host's own reaction to the command, not a transport failure.",
                        DebugApiHints.Panel);
            }

            if (!result.Success)
                return (null,
                        $"The host refused '{body?["type"]}': {result.Message ?? "no reason given"}.",
                        DebugApiHints.Panel);

            // ⭐⭐⭐ THE POST-CONDITION — generalised from `MA-004`'s add-node lesson to the WHOLE union.
            // 🔴 MEASURED `2026-08-25`: `AddAttachment` on a BLUEPRINT returns Success with no message and
            //    builds NOTHING — attachments are a BTree/HSM concept (decorators, condition pills) and
            //    `BlueprintCommandSink` has no arm for them. ⇒ ⛔ trusting `GraphCommandResult.Success`
            //    would hand back an attachmentId that addresses nothing, which is the exact silent-wrong
            //    answer this surface exists to prevent.
            // ⭐ So: every id the command MINTED must resolve in the model afterwards. A host that cannot
            //   serve a variant now says so, instead of appearing to.
            var unresolved = UnresolvedMintedIds(parsed.NewIds, view.Model);
            if (unresolved.Count > 0)
                return (null,
                        $"The host ACCEPTED '{body?["type"]}' and built nothing: "
                      + string.Join(", ", unresolved)
                      + $" cannot be found in the graph afterwards. This host's sink has no arm for that "
                      + "variant — attachments are a BTree/HSM concept and regions an HSM one, so a "
                      + "Blueprint refuses them silently. Call GET /assets/{assetId}/graph/command for the "
                      + "variants, and apply host-specific ones to an asset of that kind.",
                        DebugApiHints.Panel);

            MarkEdited(doc!);

            var newIds = new JsonObject();
            foreach (var kv in parsed.NewIds) newIds[kv.Key] = kv.Value;

            return (new JsonObject
            {
                ["type"]        = body?["type"]?.GetValue<string>(),
                ["applied"]     = true,
                ["undoable"]    = parsed.Inverse != null,
                ["message"]     = result.Message,
                ["newIds"]      = newIds,
                ["nodeCount"]   = view.Model.Nodes.Count,
                ["linkCount"]   = view.Model.Links.Count,
                ["nodeDelta"]   = view.Model.Nodes.Count - nodesBefore,
                ["linkDelta"]   = view.Model.Links.Count - linksBefore,
                ["note"]        = "re-read GET /assets/{assetId}/graph to see the result — one command can "
                                + "change more than it names (removing a node takes its links with it). "
                                + "`undoable:false` means no inverse could be derived from the read-only "
                                + "model, so the edit applied but the undo stack has no entry for it.",
            }, null, null);
        }

        /// <summary>
        /// ⭐⭐ Which of the ids a command MINTED cannot be found in the model afterwards.
        /// </summary>
        /// <remarks>
        /// ⚠ Keys are the ones <c>GraphCommandJson</c> mints — <c>nodeId</c> · <c>linkId</c> ·
        /// <c>attachmentId</c> · <c>commentId</c>, and the <c>key[i]</c> form a <c>Batch</c> produces.
        /// ⛔ An unrecognised key is IGNORED rather than guessed at: a false "built nothing" would be
        /// worse than the silence it is meant to catch.
        /// </remarks>
        private static List<string> UnresolvedMintedIds(
            IReadOnlyDictionary<string, string> minted, IGraphModel model)
        {
            var missing = new List<string>();

            foreach (var kv in minted)
            {
                if (!Guid.TryParse(kv.Value, out var g)) continue;

                // A Batch reports "nodeId[0]" etc; the prefix before '[' is the kind of id.
                var bracket = kv.Key.IndexOf('[');
                var key     = bracket < 0 ? kv.Key : kv.Key[..bracket];

                var found = key switch
                {
                    "nodeId"       => model.FindNode(new NodeId(g)) != null,
                    "linkId"       => model.FindLink(new LinkId(g)) != null,
                    "attachmentId" => model.FindAttachment(new AttachmentId(g)) != null,
                    "commentId"    => model.Comments.Any(c => c.Id.Value == g),
                    _              => true,   // not an id shape this check understands
                };

                if (!found) missing.Add($"{kv.Key} {g}");
            }

            return missing;
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
                      + "\"path\": \"<optional subfolder>\", \"recipe\": \"<optional recipe name from "
                      + "GET /assets/recipes>\"}.",
                        DebugApiHints.Panel);

            var relPath = body?["path"]?.GetValue<string>() ?? string.Empty;

            // ⭐⭐ MA-021 — the recipe, by name, from GET /assets/recipes. ⛔ Without this the create route
            //   could only ever produce BLANKS, which would make recipe discovery a list of things the
            //   agent cannot actually ask for — 📌 the same "reports a capability it does not have" shape
            //   MA-017 caught on the union route.
            var recipe = body?["recipe"]?.GetValue<string>();

            var (assetId, status) = _createAsset(kind!, name!, relPath, recipe);

            if (assetId == null)
                return (null, status, DebugApiHints.Panel);

            var result = new JsonObject
            {
                ["assetId"] = assetId.Value.ToString(),
                ["name"]    = name,
                ["kind"]    = kind,
                ["recipe"]  = recipe,
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

        // ══ ⑦ DISCOVERY (MA-013 · MA-014) ═════════════════════════════════════
        //
        // ⭐⭐⭐ 📄 DESIGN_Mcp_Authoring.md §10. "What the user can SEE and change, the MCP can too."
        // ⛔⛔ §10.2 ① proposed a parallel `GET /assets/{id}/nodetypes`. ⭐ NOT built — the handoff §1
        //    overrides it, and rightly: `GET /assets/{id}/graph/catalog` shipped in MA-004 and answers
        //    the same question. ⇒ this EXTENDS it and hangs the per-kind schema off it.

        private Hrot.Editor.AiShared.Blackboard.IActionSchemaExporter? _schemaExporter;

        /// <summary>
        /// ⭐ Hands this service the host's action-schema exporter, for the DTO-field half of a schema.
        /// </summary>
        /// <remarks>
        /// ⚠ <b>OPTIONAL, and the routes degrade honestly without it</b> — they report
        /// <c>paramsSource: "none"</c> and say why, ⛔ rather than looking like a kind that has no params.
        /// 📌 The silent-default rule applies in the other direction too: a host that HAS one must pass it,
        /// and a forwarding rail asserts that on the constructed object.
        /// </remarks>
        public void AttachSchemaExporter(Hrot.Editor.AiShared.Blackboard.IActionSchemaExporter exporter)
            => _schemaExporter = exporter ?? throw new ArgumentNullException(nameof(exporter));

        /// <summary>⭐ Exposed for the forwarding rail — ⛔ a rail must reach the CONSTRUCTED object.</summary>
        internal bool HasSchemaExporter => _schemaExporter != null;

        /// <summary>
        /// <c>GET /assets/{assetId}/graph/catalog/{kind}</c> — one kind's full schema and documentation.
        /// </summary>
        /// <remarks>
        /// ⭐⭐⭐ <b>MEASURED from the registries, never authored</b> *(`R-133`'s discipline applied to node
        /// kinds)*: pins come from <see cref="NodeCatalogEntry"/>, params from the action-schema exporter's
        /// reflected DTO fields, docs from the entry's own description plus the StructEdit
        /// <c>Edit*</c>/<c>EditDoc</c> attribute family. ⛔ A hand-written kind table would rot the moment a
        /// node kind is added — and nothing would fail.
        ///
        /// <para>⚠⚠ <b>TWO measured limits, stated in the payload rather than papered over.</b>
        /// <list type="number">
        ///   <item>⛔ <b>The catalog cannot say whether a kind is a CONTAINER.</b> 📐 Container-ness is
        ///   <see cref="IContainerNodeModel"/>, implemented by an INSTANCE; <c>NodeCatalogEntry</c> has no
        ///   such flag. ⇒ the kind payload reports what the catalog DOES know — <c>paletteAction</c> and
        ///   <c>attachmentCategory</c>, i.e. whether picking it makes a node or an ATTACHMENT — and
        ///   container/region structure is reported per NODE by <c>GET /assets/{id}/graph</c>.</item>
        ///   <item>⛔ <b>The action-schema exporter is keyed by action FQN, which belongs to a node
        ///   INSTANCE, not to a kind.</b> 📐 Every production caller reads the fqn off the selected node.
        ///   ⇒ this route tries the kind id as an fqn and falls back to a suffix match, and says which
        ///   worked in <c>paramsSource</c> — ⛔ it does not pretend a kind always has DTO params.</item>
        /// </list></para>
        /// </remarks>
        public (JsonNode? Result, string? Error, string? HintCategory) GetNodeKindSchema(
            string? assetId, string? kind)
        {
            var (_, view, error) = ResolveGraph(assetId);
            if (error != null) return (null, error, DebugApiHints.Panel);

            if (string.IsNullOrWhiteSpace(kind))
                return (null, "Path must be /assets/{assetId}/graph/catalog/{kind}.", DebugApiHints.Panel);

            NodeCatalogEntry? entry = null;
            foreach (var e in view!.Catalog.All)
                if (string.Equals(e.Kind.Id, kind, StringComparison.OrdinalIgnoreCase)) { entry = e; break; }

            if (entry == null)
                return (null,
                        $"'{kind}' is not a kind this graph's catalog offers. Call "
                      + $"GET /assets/{assetId}/graph/catalog for the list — the catalog is PER GRAPH, so a "
                      + "kind valid in a BTree is not necessarily valid in a Blueprint.",
                        DebugApiHints.Panel);

            var result = DescribeKind(entry, includeDoc: true);
            result["assetId"] = assetId;
            result["note"]    = "pins and flags come from the host's own INodeCatalog; params come from the "
                              + "reflected action DTO when this kind resolves to one (see paramsSource). "
                              + "Container/region structure is a property of a NODE, not of a kind — read it "
                              + "from GET /assets/{assetId}/graph.";
            return (result, null, null);
        }

        /// <summary>
        /// <c>GET /assets/{assetId}/graph/nodes/{nodeId}/properties</c> — one node's editable properties
        /// with their CURRENT values, as the Details panel shows them.
        /// </summary>
        /// <remarks>
        /// ⭐⭐ <b>This is the read half of *"read/set the properties shown in the detail panel"*</b>
        /// *(§10.2 ③)*; the SET half is <c>POST …/graph/params</c> and the union route's
        /// <c>SetPinDefault</c>/<c>SetNodeProperty</c>. ⇒ together they close the loop:
        /// <i>list kinds → read a kind's schema → add a node → read its properties → set one → save+reload.</i>
        ///
        /// <para>⭐ <b>Values come from the MODEL, schema from the CATALOG</b>, joined here — so a value is
        /// never reported without the type and constraints needed to change it correctly.</para>
        /// </remarks>
        public (JsonNode? Result, string? Error, string? HintCategory) GetNodeProperties(
            string? assetId, string? nodeId)
        {
            var (_, view, error) = ResolveGraph(assetId);
            if (error != null) return (null, error, DebugApiHints.Panel);

            if (!Guid.TryParse(nodeId, out var g))
                return (null,
                        $"'{nodeId}' is not a node GUID. They come from GET /assets/{assetId}/graph.",
                        DebugApiHints.Panel);

            var node = view!.Model.FindNode(new NodeId(g));
            if (node == null)
                return (null,
                        $"No node {g} in this graph. Re-read GET /assets/{assetId}/graph — a node id from a "
                      + "stale read may have been removed, and ids are never reused.",
                        DebugApiHints.Panel);

            // ⭐ The EDITABLE properties are the input DATA pins: an exec pin has no value and an output's
            //   is computed. ⛔ Listing all pins as "properties" would invite a set that must be refused.
            var props = new JsonArray();
            foreach (var p in node.Pins)
            {
                if (p.Direction != PinDirection.Input || p.Kind != PinKind.Data) continue;

                var po = new JsonObject
                {
                    ["pinId"]      = p.Id.Value.ToString(),
                    ["name"]       = p.Label,
                    ["type"]       = p.Type?.Id,
                    ["isAdvanced"] = p.IsAdvanced,
                    ["isOptional"] = p.IsOptional,
                    ["value"]      = p.Default?.Value?.ToString(),
                    ["hasValue"]   = p.Default?.Value != null,
                };

                if (!string.IsNullOrEmpty(p.Tooltip)) po["doc"] = p.Tooltip;

                // ⭐ The constraint metadata the Details editor itself reads — range, unit, placeholder.
                //   ⛔ Not re-derived here: it is carried on the pin's own default descriptor.
                if (p.Default?.Metadata is { } m)
                {
                    if (m.RangeMin is { } lo) po["rangeMin"] = lo;
                    if (m.RangeMax is { } hi) po["rangeMax"] = hi;
                    if (m.Step is { } st)     po["step"]     = st;
                    if (!string.IsNullOrEmpty(m.Units))           po["unit"]        = m.Units;
                    if (!string.IsNullOrEmpty(m.PlaceholderText)) po["placeholder"] = m.PlaceholderText;
                    if (!string.IsNullOrEmpty(m.PickerSourceKey)) po["picker"]      = m.PickerSourceKey;
                    if (m.ClampToRange) po["clampToRange"] = true;
                }

                props.Add(po);
            }

            var kindEntry = view.Catalog.All.FirstOrDefault(e => e.Kind.Id == node.Kind.Id);

            return (new JsonObject
            {
                ["assetId"]    = assetId,
                ["nodeId"]     = node.Id.Value.ToString(),
                ["kind"]       = node.Kind.Id,
                ["title"]      = node.Title,
                ["doc"]        = kindEntry?.Description,
                ["count"]      = props.Count,
                ["properties"] = props,
                ["note"]       = "these are the INPUT DATA pins — the editable properties. Set one with "
                               + "POST /assets/{assetId}/graph/params, or via the union route's "
                               + "SetPinDefault. An exec pin has no value and an output's is computed, so "
                               + "neither appears here.",
            }, null, null);
        }

        /// <summary>
        /// ⭐⭐ ONE projection of a node kind, so the list route and the per-kind route cannot disagree.
        /// </summary>
        /// <remarks>
        /// 📌 The same reasoning as <c>Describe(IEditableAsset)</c> in the assets file: two projections of
        /// one concept drift, and the drift shows up as a caller trusting the wrong one.
        /// </remarks>
        private JsonObject DescribeKind(NodeCatalogEntry e, bool includeDoc)
        {
            var o = new JsonObject
            {
                ["kind"]          = e.Kind.Id,
                ["displayName"]   = e.DisplayName,
                ["category"]      = e.CategoryPath,
                ["isPure"]        = e.IsPure,
                ["isLatent"]      = e.IsLatent,
                ["isDeprecated"]  = e.IsDeprecated,
                // ⭐ CreateNode vs AttachToSelected — the catalog's own answer to "does picking this make a
                //   node, or an ATTACHMENT on the selected node?". This is the kind-level structure fact
                //   the catalog genuinely has (container-ness is not — see the route's remarks).
                ["paletteAction"] = e.PaletteAction.ToString(),
                ["inputs"]        = new JsonArray(e.Inputs.Select(PinSigJson).ToArray()),
                ["outputs"]       = new JsonArray(e.Outputs.Select(PinSigJson).ToArray()),
            };

            if (e.AttachmentCategory is { } cat)
            {
                o["isAttachmentKind"]   = true;
                o["attachmentCategory"] = cat.ToString();
            }

            if (e.Keywords is { Count: > 0 })
                o["keywords"] = new JsonArray(e.Keywords.Select(k => (JsonNode)k!).ToArray());

            if (includeDoc)
            {
                o["doc"] = e.Description;
                var (paramsArr, source) = DescribeKindParams(e);
                o["paramsSource"] = source;
                o["params"]       = paramsArr;
            }
            else if (!string.IsNullOrEmpty(e.Description))
            {
                o["doc"] = e.Description;
            }

            return o;
        }

        private static JsonNode PinSigJson(PinSignature p) => new JsonObject
        {
            ["label"]      = p.Label,
            ["kind"]       = p.Kind.ToString(),
            ["type"]       = p.Type?.Id,
            ["isWildcard"] = p.IsWildcard,
        };

        /// <summary>
        /// ⭐ The DTO-field half of a kind's schema, when the kind resolves to a reflected action.
        /// </summary>
        /// <remarks>
        /// ⚠ Returns the SOURCE alongside the fields — <c>"exporter:exact"</c>, <c>"exporter:suffix"</c>,
        /// <c>"none:not-an-action"</c> or <c>"none:no-exporter-wired"</c>. ⛔ An empty list with no
        /// explanation reads as *"this kind has no params"*, which is a different and often false claim.
        /// </remarks>
        private (JsonArray Fields, string Source) DescribeKindParams(NodeCatalogEntry e)
        {
            var arr = new JsonArray();

            if (_schemaExporter == null)
                return (arr, "none:no-exporter-wired");

            var entry  = _schemaExporter.Lookup(e.Kind.Id);
            var source = "exporter:exact";

            if (entry == null)
            {
                // ⚠ The exporter is keyed by action FQN and a kind id is not always one. A suffix match at
                //   a dot boundary is the honest widening — ⛔ and it is REPORTED as a suffix match, so a
                //   caller can tell a certain answer from a probable one.
                foreach (var kv in _schemaExporter.All)
                {
                    if (kv.Key.EndsWith("." + e.Kind.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        entry  = kv.Value;
                        source = "exporter:suffix";
                        break;
                    }
                }
            }

            if (entry is not { } resolved) return (arr, "none:not-an-action");

            // ⚠⚠ `DtoFields` is NULLABLE on the record, and the two cases mean different things:
            //    null = the entry was built without reflecting its DTO; empty = reflected and it has no
            //    editable fields. ⛔ Collapsing them would report "no params" for a kind whose params were
            //    simply never enumerated — the false-negative shape this whole surface exists to avoid.
            if (resolved.DtoFields is not { } fields)
                return (arr, "none:dto-fields-not-reflected");

            foreach (var f in fields)
                arr.Add(DescribeDtoField(resolved.DtoType, f));

            return (arr, source);
        }

        /// <summary>
        /// ⭐⭐⭐ <b>THE DOC HARVEST (`MA-016`) — a param's documentation read off the SAME attributes the
        /// Details editor reads.</b> 📄 <c>DESIGN_Mcp_Authoring.md</c> §10.6.
        /// </summary>
        /// <remarks>
        /// ⭐⭐ <b>"As elsewhere" means the <c>RouteDoc</c> pattern at FIELD granularity</b>: a colocated
        /// descriptor, harvested at runtime. ⛔ Never a parallel hand-written doc table — 📌 that is the rot
        /// <c>RouteDoc</c> was built to avoid; it goes stale the first time a field is renamed and nothing
        /// fails when it does.
        ///
        /// <para>📐 <b>The sources, measured:</b> <c>EditDisplayName</c> *(the label)* ·
        /// <c>EditRange</c> *(min/max)* · <c>EditUnit</c> · <c>EditReadOnly</c> ·
        /// <c>InlineArrayHint</c>/<c>FixedBufferHint</c> *(buffer shape)* — and ⭐ <c>EditDoc</c>, added by
        /// this slice for the free-text half §10.6 measured as absent from every attribute.</para>
        ///
        /// <para>⚠ <b>Reflection is wrapped</b>: a DTO type that fails to reflect *(a generic parameter, a
        /// ref struct)* must degrade to name+type, ⛔ not take the whole schema route down with it.</para>
        /// </remarks>
        private static JsonObject DescribeDtoField(
            Type dtoType, Hrot.Editor.AiShared.Blackboard.DtoFieldDescriptor f)
        {
            var o = new JsonObject
            {
                ["name"] = f.Name,
                ["type"] = f.FieldType.Name,
            };

            try
            {
                var member = (System.Reflection.MemberInfo?)dtoType.GetField(f.Name)
                          ?? dtoType.GetProperty(f.Name);
                if (member == null) return o;

                if (member.GetCustomAttribute<StructEdit.Core.Attributes.EditDisplayNameAttribute>()
                    is { } dn) o["displayName"] = dn.Name;

                if (member.GetCustomAttribute<StructEdit.Core.Attributes.EditDocAttribute>()
                    is { } doc) o["doc"] = doc.Summary;

                if (member.GetCustomAttribute<StructEdit.Core.Attributes.EditRangeAttribute>()
                    is { } r) { o["rangeMin"] = r.Min; o["rangeMax"] = r.Max; }

                if (member.GetCustomAttribute<StructEdit.Core.Attributes.EditUnitAttribute>()
                    is { } u) o["unit"] = u.Unit;

                if (member.GetCustomAttribute<StructEdit.Core.Attributes.EditReadOnlyAttribute>() != null)
                    o["readOnly"] = true;

                if (member.GetCustomAttribute<StructEdit.Core.Attributes.InlineArrayHintAttribute>()
                    is not null) o["isInlineArray"] = true;

                if (member.GetCustomAttribute<StructEdit.Core.Attributes.FixedBufferHintAttribute>()
                    is { } fb) { o["isFixedBuffer"] = true; o["bufferLength"] = fb.Length; }

                // ⭐ An enum's legal values, so an agent setting this param never has to guess one.
                if (f.FieldType.IsEnum)
                    o["enumValues"] = new JsonArray(
                        Enum.GetNames(f.FieldType).Select(n => (JsonNode)n!).ToArray());
            }
            catch (Exception ex)
            {
                // ⚠ Reported on the field, not thrown: one un-reflectable field must not cost the caller
                //   the whole schema.
                o["docHarvestError"] = ex.Message;
            }

            return o;
        }

        // ══ ⑧ UI-COMMAND ACTIONS (MA-015) ═════════════════════════════════════
        //
        // ⭐⭐⭐ 📄 DESIGN_Mcp_Authoring.md §10.7. The editor command bus, over MCP — the SAME
        //    discover → describe → invoke shape as the graph surface, so the slice now covers THREE
        //    invoke surfaces through one pattern: the graph-command union · the entity/world ops ·
        //    the editor command bus.
        //
        // ⛔⛔ THE ROUTE PREFIX IS `/editor/commands`, NOT `/commands` (DECISION D1).
        //    📐 `GET /commands` has existed since Group F as `list_commands` — "enumerate publishable FDP
        //    event types with field schemas" — and `send_entity_command` depends on it. ⇒ taking that path
        //    would break a shipped tool. ⭐ The prefix is also the honest name: this is the EDITOR command
        //    bus, ⛔ not the FDP event bus.
        //
        // ⛔ `GlobalActionRegistry` is deliberately OUT (§10.7): int-keyed, no descriptor, no display name.
        //    It needs an author-a-descriptor pass and belongs to the entity-action / Axis-B track.

        private Func<NodeEditor.Core.Action.IEditorCommands?>? _editorCommands;

        /// <summary>
        /// ⭐ Hands this service a way to reach the ACTIVE document's editor-command dispatcher.
        /// </summary>
        /// <remarks>
        /// ⚠⚠ <b>A <see cref="Func{TResult}"/>, not the object</b>, and the reason is measured: the command
        /// set is <b>per document</b> — it is built by the per-kind document factory and hangs off
        /// <see cref="AiCanvasContext.Commands"/>. ⇒ ⛔ capturing one instance would pin this API to
        /// whichever document happened to be open when the composition root ran, and every later invoke
        /// would target the wrong graph.
        /// <para>⭐ Left unwired, the routes fall back to the ACTIVE document's own dispatcher, which is
        /// what a caller means by *"the editor's commands"* anyway. The seam exists so a host with a
        /// SHELL-level command set can supply it.</para>
        /// </remarks>
        public void AttachEditorCommands(Func<NodeEditor.Core.Action.IEditorCommands?> commands)
            => _editorCommands = commands ?? throw new ArgumentNullException(nameof(commands));

        /// <summary>⭐ Exposed for the forwarding rail — ⛔ a rail must reach the CONSTRUCTED object.</summary>
        internal bool HasEditorCommands => ResolveEditorCommands() != null;

        /// <summary>⭐ The explicitly-attached set if there is one, else the ACTIVE document's.</summary>
        private NodeEditor.Core.Action.IEditorCommands? ResolveEditorCommands()
        {
            if (_editorCommands?.Invoke() is { } attached) return attached;
            var active = _documents?.Active;
            return active == null ? null : ContextOf(active)?.Commands;
        }

        private const string NoEditorCommands =
            "No editor-command dispatcher is reachable. The command set is built PER DOCUMENT by the "
          + "per-kind factory, so open an AI asset first (POST /assets/{assetId}/open) — or the host must "
          + "call AttachEditorCommands(...) with a shell-level set.";

        /// <summary>
        /// <c>GET /editor/commands</c> — every editor command, with its live enabled/checked state.
        /// </summary>
        /// <remarks>
        /// ⭐⭐⭐ <b>Self-documenting for free, and that is why this bundles with the graph surface at all.</b>
        /// 📐 <c>EditorCommandDescriptor</c> already carries <c>DisplayName</c>, <c>Category</c>,
        /// <c>Description</c> and <c>DefaultKey</c> INLINE — ⛔ no reflection and no attribute harvest is
        /// needed, unlike node kinds. ⇒ the doc-coverage rail can assert a non-empty description on every
        /// one of them the day it is written.
        ///
        /// <para>⚠ <c>isEnabled</c> is EVALUATED at read time — it is a <c>Func&lt;bool&gt;</c> over live
        /// editor state *(is there a selection? is the undo stack empty?)*. ⇒ ⭐ it is a snapshot, and a
        /// caller that acts on a stale one gets a refusal from <c>invoke</c>, not a silent no-op.</para>
        /// </remarks>
        public (JsonNode? Result, string? Error, string? HintCategory) ListEditorCommands(string? category)
        {
            var commands = ResolveEditorCommands();
            if (commands == null) return (null, NoEditorCommands, DebugApiHints.Panel);

            var arr = new JsonArray();
            foreach (var d in commands.All)
            {
                if (!string.IsNullOrWhiteSpace(category)
                 && !string.Equals(d.Category, category, StringComparison.OrdinalIgnoreCase))
                    continue;
                arr.Add(DescribeEditorCommand(d));
            }

            return (new JsonObject
            {
                ["count"]    = arr.Count,
                ["total"]    = commands.All.Count,
                ["commands"] = arr,
                ["note"]     = "isEnabled/isChecked are evaluated NOW over live editor state, so they are a "
                             + "snapshot. The command set is per OPEN DOCUMENT — opening a different asset "
                             + "kind changes it. These are the EDITOR's commands; GET /commands is a "
                             + "different surface (publishable FDP event types).",
            }, null, null);
        }

        /// <summary><c>GET /editor/commands/{commandId}</c> — describe one command.</summary>
        public (JsonNode? Result, string? Error, string? HintCategory) DescribeEditorCommandById(string? id)
        {
            var commands = ResolveEditorCommands();
            if (commands == null) return (null, NoEditorCommands, DebugApiHints.Panel);

            if (string.IsNullOrWhiteSpace(id))
                return (null, "Path must be /editor/commands/{commandId}.", DebugApiHints.Panel);

            var d = commands.Get(id!);
            if (d == null)
                return (null,
                        $"No editor command '{id}'. Call GET /editor/commands — ids look like "
                      + "'editor.delete-selection', and the set depends on which document kind is open.",
                        DebugApiHints.Panel);

            return (DescribeEditorCommand(d), null, null);
        }

        /// <summary>
        /// <c>POST /editor/commands/{commandId}/invoke</c> — run an editor command.
        /// </summary>
        /// <remarks>
        /// ⭐⭐ <b>ONE seam, exactly like the graph union.</b> <c>IEditorCommands.Invoke(id, ctx)</c> is what
        /// the toolbar, the menu and the hotkey all call ⇒ an MCP invocation is the same action a click is.
        ///
        /// <para>⚠⚠ <b>The DISABLED check is made here, before invoking, and it is not redundant.</b> 📐
        /// <c>EditorCommandsImpl</c> is free to run a handler whose <c>IsEnabled</c> is false — the UI
        /// simply never offers it. ⇒ ⛔ without this, MCP could run a command the editor greys out, which
        /// is precisely the *"a path that accepts what the editor would refuse"* hazard the whole surface
        /// is built to avoid.</para>
        ///
        /// <para>⚠ Ruling 53: a headless origin never pre-flights a confirmation — the command runs
        /// directly and the ORIGIN-SIDE LOG is the safety net. The response carries what ran.</para>
        /// </remarks>
        public (JsonNode? Result, string? Error, string? HintCategory) InvokeEditorCommand(
            string? id, JsonNode? body)
        {
            var commands = ResolveEditorCommands();
            if (commands == null) return (null, NoEditorCommands, DebugApiHints.Panel);

            if (string.IsNullOrWhiteSpace(id))
                return (null, "Path must be /editor/commands/{commandId}/invoke.", DebugApiHints.Panel);

            var d = commands.Get(id!);
            if (d == null)
                return (null,
                        $"No editor command '{id}'. Call GET /editor/commands for the ids this host offers.",
                        DebugApiHints.Panel);

            bool enabled;
            try { enabled = d.IsEnabled(); }
            catch (Exception ex)
            {
                return (null,
                        $"'{id}' could not report whether it is enabled: {ex.GetType().Name}: {ex.Message}. "
                      + "That is the command's own predicate throwing, not a transport failure.",
                        DebugApiHints.Panel);
            }

            if (!enabled)
                return (null,
                        $"'{d.DisplayName}' ({id}) is DISABLED right now. The editor greys it out for the "
                      + "same reason — usually an empty selection or an empty undo stack. Read "
                      + "GET /editor/commands to see the live state, and set up the precondition first.",
                        DebugApiHints.Panel);

            var ctx = BuildCommandContext(body);

            NodeEditor.Core.Action.EditorCommandResult result;
            try
            {
                result = commands.Invoke(id!, ctx);
            }
            catch (Exception ex)
            {
                return (null,
                        $"'{id}' threw: {ex.GetType().Name}: {ex.Message}. This is the command's own "
                      + "handler failing — the MCP call reached it.",
                        DebugApiHints.Panel);
            }

            // ⭐ Ruling 53's requirement: the ORIGIN logs what it dispatched, because a headless origin
            //   never pre-flights. ⛔ The log is the whole safety net, so it is a requirement not a nicety.
            Fdp.Core.Logging.FdpLog<DebugApiService>.Info(
                "[MCP] editor command '{0}' ({1}) invoked over the debug API — success={2}",
                id, d.DisplayName, result.Success);

            return (new JsonObject
            {
                ["commandId"]   = id,
                ["displayName"] = d.DisplayName,
                ["invoked"]     = true,
                ["success"]     = result.Success,
                ["message"]     = result.Message,
                ["note"]        = "this ran through IEditorCommands.Invoke — the same seam the toolbar, the "
                                + "menu and the hotkey use. Effects that redraw appear on the NEXT frame, "
                                + "so step a tick before reading GET /panels.",
            }, null, null);
        }

        /// <summary>⭐ ONE projection of a command, so list and describe cannot disagree.</summary>
        private static JsonObject DescribeEditorCommand(
            NodeEditor.Core.Action.EditorCommandDescriptor d)
        {
            var o = new JsonObject
            {
                ["id"]          = d.Id,
                ["displayName"] = d.DynamicDisplayName?.Invoke() ?? d.DisplayName,
                ["category"]    = d.Category,
                ["doc"]         = d.Description,
            };

            if (d.DefaultKey is { } key) o["defaultKey"] = key.ToString();

            // ⚠ Both predicates are host code and CAN throw; a broken predicate must not take the whole
            //   listing down, so each is reported as unknown rather than propagated.
            try { o["isEnabled"] = d.IsEnabled(); }
            catch (Exception ex) { o["isEnabled"] = null; o["isEnabledError"] = ex.Message; }

            if (d.IsChecked != null)
            {
                try { o["isChecked"] = d.IsChecked(); }
                catch (Exception ex) { o["isChecked"] = null; o["isCheckedError"] = ex.Message; }
            }

            return o;
        }

        /// <summary>⭐ The MCP body → <c>EditorCommandContext</c>: the args bag plus optional positions.</summary>
        private static NodeEditor.Core.Action.EditorCommandContext BuildCommandContext(JsonNode? body)
        {
            Vector2? canvasPos = null, screenPos = null;

            if (body?["canvasPos"] is JsonObject cp)
                canvasPos = new Vector2(ReadFloat(cp, "x"), ReadFloat(cp, "y"));
            if (body?["screenPos"] is JsonObject sp)
                screenPos = new Vector2(ReadFloat(sp, "x"), ReadFloat(sp, "y"));

            Dictionary<string, object?>? args = null;
            if (body?["args"] is JsonObject a)
            {
                args = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var kv in a)
                {
                    var node = kv.Value;
                    if (node is null) { args[kv.Key] = null; continue; }
                    try
                    {
                        args[kv.Key] = node.GetValueKind() switch
                        {
                            System.Text.Json.JsonValueKind.True   => true,
                            System.Text.Json.JsonValueKind.False  => false,
                            System.Text.Json.JsonValueKind.String => node.GetValue<string>(),
                            System.Text.Json.JsonValueKind.Number => node.GetValue<double>(),
                            _ => node.ToJsonString(),
                        };
                    }
                    catch { args[kv.Key] = node.ToJsonString(); }
                }
            }

            return new NodeEditor.Core.Action.EditorCommandContext(screenPos, canvasPos, args);
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
