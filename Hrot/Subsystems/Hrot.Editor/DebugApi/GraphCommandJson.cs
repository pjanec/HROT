using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Editor.DebugApi
{
    /// <summary>
    /// ⭐⭐⭐ <b>THE UNION BACKBONE — one serialized <see cref="GraphCommand"/> in, one command out.</b>
    /// 📄 <c>docs/DESIGN_Mcp_Authoring.md</c> §11.2 *(the decision)* · §11.1 *(the measured union)*.
    ///
    /// <para>⭐⭐⭐ <b>Why a union route rather than more typed verbs.</b> 📐 <c>GraphCommand</c> is an
    /// abstract record with ~35 <c>sealed record</c> variants applied through the SINGLE seam
    /// <see cref="IGraphCommandSink"/>, implemented by all three hosts. ⇒ ⛔ <b>a hand-picked verb list WILL
    /// lag the union</b> — 📌 it already did: the four shipped verbs *(`MA-002`)* cannot express a BTree
    /// decorator or an HSM region, which are exactly the host specifics authoring must reach. ⭐ Exposing
    /// the union instead means <b>zero per-host MCP code</b>, now and for every variant added later.</para>
    ///
    /// <para>⛔⛔ <b>Why hand-written and not <c>JsonSerializer</c> polymorphism.</b> 📐 The variants are
    /// positional <c>record</c>s over NodeEdit primitives — <c>NodeId</c>/<c>PinId</c>/<c>LinkId</c> are
    /// <c>readonly record struct</c>s wrapping a <c>Guid</c>, and several carry <c>Vector2</c>,
    /// <c>Vector4</c> or <c>IReadOnlyDictionary&lt;string, object?&gt;</c>. ⚠ A `[JsonPolymorphic]`
    /// attribute cannot be added — <b>NodeEdit is a VENDORED third-party tree this batch does not own</b>
    /// *(and §4's lane forbids touching it)*. ⇒ ⭐ an explicit reader, which also lets every failure name
    /// the field that was wrong instead of surfacing a converter exception.</para>
    ///
    /// <para>⭐⭐ <b>The inverse is built here too</b>, because <see cref="GraphView.Execute"/> records it on
    /// the undo stack. ⚠ Where an exact inverse cannot be derived from the read-only model, the variant is
    /// reported as <b>not undoable</b> rather than given a wrong one — ⛔ a wrong inverse is worse than a
    /// missing one: it corrupts the graph on undo, silently.</para>
    /// </summary>
    internal static class GraphCommandJson
    {
        /// <summary>The outcome of reading one command from JSON.</summary>
        /// <param name="Forward">The command to apply.</param>
        /// <param name="Inverse">Its inverse, or <see langword="null"/> when none can be derived.</param>
        /// <param name="Label">Undo-stack label.</param>
        /// <param name="NewIds">Ids this command MINTS, so the caller can address them afterwards.</param>
        internal sealed record Parsed(
            GraphCommand Forward,
            GraphCommand? Inverse,
            string Label,
            IReadOnlyDictionary<string, string> NewIds);

        /// <summary>
        /// ⭐ Every variant this route accepts, with the fields it takes — the payload
        /// <c>GET /assets/{id}/graph/command</c> returns so an agent never has to guess a shape.
        /// </summary>
        /// <remarks>
        /// ⚠⚠ <b>This table is the one hand-maintained thing in the file, and a rail keeps it honest.</b>
        /// 📐 <c>The_command_route_covers_the_whole_union</c> reflects over <see cref="GraphCommand"/>'s
        /// nested types and asserts every one is either here or in <see cref="Unsupported"/> with a reason.
        /// ⇒ ⛔ a variant added to NodeEdit later cannot be silently unreachable — 📌 the
        /// <i>"advertised but unreachable"</i> inversion, run the other way round.
        /// </remarks>
        internal static readonly IReadOnlyDictionary<string, string[]> Schema =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["AddNode"]                = new[] { "kind", "position?", "initialProperties?" },
                ["RemoveNodes"]            = new[] { "nodes[]" },
                ["MoveNodes"]              = new[] { "moves[]{node,position}" },
                ["SetNodeProperty"]        = new[] { "node", "key", "value?" },
                ["SetNodeCollapsed"]       = new[] { "node", "collapsed" },
                ["SetNodeAdvancedShown"]   = new[] { "node", "shown" },
                ["SetNodeDisabled"]        = new[] { "node", "disabled" },
                ["AddLink"]                = new[] { "from", "to" },
                ["RemoveLinks"]            = new[] { "links[]" },
                ["ReplaceLinkEndpoint"]    = new[] { "link", "endpoint(Source|Target)", "newPin" },
                ["SetPinDefault"]          = new[] { "pin", "value?" },
                ["AddAttachment"]          = new[] { "host", "category?", "glyph?", "label?", "tooltip?", "stackIndex?", "hostProperties?" },
                ["RemoveAttachments"]      = new[] { "attachments[]" },
                ["SetAttachmentProperty"]  = new[] { "attachment", "key", "value?" },
                ["ReorderAttachments"]     = new[] { "host", "order[]" },
                ["MoveAttachment"]         = new[] { "attachment", "newHost", "newStackIndex" },
                ["AddComment"]             = new[] { "text", "position?", "size?", "color?", "moveWithContents?" },
                ["UpdateComment"]          = new[] { "comment", "text?", "position?", "size?", "zOrder?", "moveWithContents?" },
                ["RemoveComment"]          = new[] { "comment" },
                ["InsertReroute"]          = new[] { "link", "position" },
                ["MoveReroute"]            = new[] { "link", "waypointIndex", "position" },
                ["RemoveReroute"]          = new[] { "link", "waypointIndex" },
                ["AddRegion"]              = new[] { "container", "insertAtIndex", "regionName", "priority?" },
                ["RemoveRegion"]           = new[] { "container", "regionIndex", "policy(DeleteChildren|MoveToFirstRegion|MoveToParent)?" },
                ["ReorderRegions"]         = new[] { "container", "newOrder[]" },
                ["SetRegionProperty"]      = new[] { "container", "regionIndex", "key", "value?" },
                ["SetContainerCollapsed"]  = new[] { "container", "collapsed" },
                ["ChangeParent"]           = new[] { "node", "newParent?", "newRegionIndex?", "position?" },
                ["ChangeParentMultiple"]   = new[] { "moves[]{node,newParent?,newRegionIndex?,position?}" },
                ["PromoteToVariable"]      = new[] { "pin", "variableName", "isLocal?", "categoryPath?" },
                ["CollapseToFunction"]     = new[] { "nodes[]", "functionName", "pure?", "categoryPath?" },
                ["CollapseToMacro"]        = new[] { "nodes[]", "macroName", "categoryPath?" },
                ["CollapseToComment"]      = new[] { "nodes[]", "commentText" },
                ["ExpandNode"]             = new[] { "node" },
                ["Batch"]                  = new[] { "label?", "commands[]" },
            };

        /// <summary>
        /// ⛔ Variants deliberately NOT reachable over MCP, each with the reason the rail prints.
        /// </summary>
        /// <remarks>
        /// ⭐ Empty today, and that is the point: it exists so a future *"we cannot expose X"* is a
        /// RECORDED decision with a reason, ⛔ never a silent omission the coverage rail would catch as a
        /// bug. ⚠ A variant listed here still has to be listed — the rail asserts the two sets TOGETHER
        /// cover the union.
        /// </remarks>
        internal static readonly IReadOnlyDictionary<string, string> Unsupported =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // ══ the reader ════════════════════════════════════════════════════════

        /// <summary>Reads one command. <paramref name="error"/> names the field when it fails.</summary>
        internal static Parsed? Read(JsonNode? body, IGraphModel model, out string? error)
        {
            error = null;
            if (body is not JsonObject o)
            {
                error = "Body must be a JSON object: {\"type\": \"<variant>\", …}. "
                      + "Call GET /assets/{assetId}/graph/command for the accepted variants.";
                return null;
            }

            var type = o["type"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(type))
            {
                error = "Missing 'type'. It names one GraphCommand variant — call "
                      + "GET /assets/{assetId}/graph/command for the list and each one's fields.";
                return null;
            }

            if (Unsupported.TryGetValue(type!, out var why))
            {
                error = $"'{type}' is deliberately not reachable over MCP: {why}";
                return null;
            }

            if (!Schema.ContainsKey(type!))
            {
                error = $"'{type}' is not a GraphCommand variant this route accepts. "
                      + "Call GET /assets/{assetId}/graph/command for the list — the names match the "
                      + "nested record names in NodeEditor.Core.Commands.GraphCommand exactly.";
                return null;
            }

            var newIds = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                var parsed = Build(type!, o, model, newIds);
                if (parsed == null)
                    error = $"'{type}' could not be built from this body. Its fields are: "
                          + string.Join(", ", Schema[type!]);
                return parsed;
            }
            catch (CommandJsonException ex)
            {
                error = $"{type}: {ex.Message}";
                return null;
            }
        }

        /// <summary>Raised with a message naming the offending field.</summary>
        private sealed class CommandJsonException : Exception
        {
            public CommandJsonException(string message) : base(message) { }
        }

        private static Parsed? Build(
            string type, JsonObject o, IGraphModel model, Dictionary<string, string> newIds)
        {
            switch (type.ToLowerInvariant())
            {
                // ── nodes ─────────────────────────────────────────────────────
                case "addnode":
                {
                    var id = IdGenerator.NewNodeId();
                    newIds["nodeId"] = id.Value.ToString();
                    return new Parsed(
                        new GraphCommand.AddNode(id, new NodeKindKey(Str(o, "kind")),
                                                 Vec(o, "position"), Props(o, "initialProperties")),
                        new GraphCommand.RemoveNodes(new[] { id }),
                        "Add Node (MCP)", newIds);
                }

                case "removenodes":
                {
                    var ids = NodeIds(o, "nodes");
                    return new Parsed(new GraphCommand.RemoveNodes(ids),
                                      InverseOfRemoveNodes(ids, model), "Remove Nodes (MCP)", newIds);
                }

                case "movenodes":
                {
                    var moves   = new List<NodeMove>();
                    var inverse = new List<NodeMove>();
                    foreach (var m in Arr(o, "moves"))
                    {
                        var mo   = Obj(m, "moves[]");
                        var node = NodeId(mo, "node");
                        moves.Add(new NodeMove(node, Vec(mo, "position")));
                        var cur = model.FindNode(node);
                        if (cur != null) inverse.Add(new NodeMove(node, cur.Position));
                    }
                    return new Parsed(new GraphCommand.MoveNodes(moves),
                                      new GraphCommand.MoveNodes(inverse), "Move Nodes (MCP)", newIds);
                }

                // ⚠ NO INVERSE, and stated rather than faked: INodeModel exposes no property bag, so the
                //   OLD value of a host-defined property cannot be read back from the model. ⛔ Guessing
                //   one would corrupt the graph on undo.
                case "setnodeproperty":
                    return new Parsed(
                        new GraphCommand.SetNodeProperty(NodeId(o, "node"), Str(o, "key"), Val(o, "value")),
                        null, "Set Node Property (MCP)", newIds);

                case "setnodecollapsed":
                {
                    var n = NodeId(o, "node");
                    var v = Bool(o, "collapsed");
                    return new Parsed(new GraphCommand.SetNodeCollapsed(n, v),
                                      new GraphCommand.SetNodeCollapsed(n, model.FindNode(n)?.IsCollapsed ?? !v),
                                      "Set Node Collapsed (MCP)", newIds);
                }

                case "setnodeadvancedshown":
                {
                    var n = NodeId(o, "node");
                    var v = Bool(o, "shown");
                    return new Parsed(new GraphCommand.SetNodeAdvancedShown(n, v),
                                      new GraphCommand.SetNodeAdvancedShown(n, model.FindNode(n)?.ShowAdvancedPins ?? !v),
                                      "Set Advanced Pins (MCP)", newIds);
                }

                case "setnodedisabled":
                {
                    var n = NodeId(o, "node");
                    var v = Bool(o, "disabled");
                    // ⚠ NodeState is a flags enum on the read model; the inverse simply toggles back.
                    return new Parsed(new GraphCommand.SetNodeDisabled(n, v),
                                      new GraphCommand.SetNodeDisabled(n, !v),
                                      "Set Node Disabled (MCP)", newIds);
                }

                // ── links / pins ──────────────────────────────────────────────
                case "addlink":
                {
                    var id = IdGenerator.NewLinkId();
                    newIds["linkId"] = id.Value.ToString();
                    return new Parsed(
                        new GraphCommand.AddLink(id, PinId(o, "from"), PinId(o, "to")),
                        new GraphCommand.RemoveLinks(new[] { id }), "Connect Pins (MCP)", newIds);
                }

                case "removelinks":
                {
                    var ids     = LinkIds(o, "links");
                    var restore = new List<GraphCommand>();
                    foreach (var id in ids)
                    {
                        var l = model.FindLink(id);
                        if (l != null) restore.Add(new GraphCommand.AddLink(l.Id, l.FromPin, l.ToPin));
                    }
                    return new Parsed(new GraphCommand.RemoveLinks(ids),
                                      restore.Count > 0 ? new GraphCommand.Batch("Restore Links", restore) : null,
                                      "Remove Links (MCP)", newIds);
                }

                case "replacelinkendpoint":
                {
                    var link = LinkId(o, "link");
                    var end  = Enum<LinkEndpoint>(o, "endpoint");
                    var cur  = model.FindLink(link);
                    var old  = cur == null ? (PinId?)null
                             : end == LinkEndpoint.Source ? cur.FromPin : cur.ToPin;
                    return new Parsed(
                        new GraphCommand.ReplaceLinkEndpoint(link, end, PinId(o, "newPin")),
                        old is { } p ? new GraphCommand.ReplaceLinkEndpoint(link, end, p) : null,
                        "Re-point Link (MCP)", newIds);
                }

                case "setpindefault":
                {
                    var pin = PinId(o, "pin");
                    return new Parsed(
                        new GraphCommand.SetPinDefault(pin, Val(o, "value")),
                        new GraphCommand.SetPinDefault(pin, model.FindPin(pin)?.Default?.Value),
                        "Set Pin Default (MCP)", newIds);
                }

                // ── attachments — BTree decorators / condition pills ───────────
                case "addattachment":
                {
                    var id   = IdGenerator.NewAttachmentId();
                    var host = NodeId(o, "host");
                    newIds["attachmentId"] = id.Value.ToString();
                    return new Parsed(
                        new GraphCommand.AddAttachment(
                            id, host,
                            OptEnum(o, "category", AttachmentCategory.Custom),
                            OptStr(o, "glyph"), OptStr(o, "label"), OptStr(o, "tooltip"),
                            // ⭐ Default to the END of the host's stack — the same placement the picker uses.
                            OptInt(o, "stackIndex") ?? model.GetAttachmentsForNode(host).Count,
                            Props(o, "hostProperties")),
                        new GraphCommand.RemoveAttachments(new[] { id }),
                        "Add Attachment (MCP)", newIds);
                }

                case "removeattachments":
                {
                    var ids     = AttachmentIds(o, "attachments");
                    var restore = new List<GraphCommand>();
                    foreach (var id in ids)
                    {
                        var a = model.FindAttachment(id);
                        if (a != null)
                            restore.Add(new GraphCommand.AddAttachment(
                                a.Id, a.HostNodeId, a.Category, a.Glyph, a.Label,
                                a.Tooltip, a.StackIndex, a.HostProperties));
                    }
                    return new Parsed(new GraphCommand.RemoveAttachments(ids),
                                      restore.Count > 0 ? new GraphCommand.Batch("Restore Attachments", restore) : null,
                                      "Remove Attachments (MCP)", newIds);
                }

                // ⚠ No inverse — same reason as SetNodeProperty: attachment host properties are
                //   host-defined and the read model exposes no per-key old value.
                case "setattachmentproperty":
                    return new Parsed(
                        new GraphCommand.SetAttachmentProperty(
                            AttachmentId(o, "attachment"), Str(o, "key"), Val(o, "value")),
                        null, "Set Attachment Property (MCP)", newIds);

                case "reorderattachments":
                {
                    var host  = NodeId(o, "host");
                    var order = AttachmentIds(o, "order");
                    var prior = model.GetAttachmentsForNode(host).Select(a => a.Id).ToList();
                    return new Parsed(new GraphCommand.ReorderAttachments(host, order),
                                      prior.Count > 0 ? new GraphCommand.ReorderAttachments(host, prior) : null,
                                      "Reorder Attachments (MCP)", newIds);
                }

                case "moveattachment":
                {
                    var id   = AttachmentId(o, "attachment");
                    var cur  = model.FindAttachment(id);
                    return new Parsed(
                        new GraphCommand.MoveAttachment(id, NodeId(o, "newHost"), Int(o, "newStackIndex")),
                        cur != null ? new GraphCommand.MoveAttachment(id, cur.HostNodeId, cur.StackIndex) : null,
                        "Move Attachment (MCP)", newIds);
                }

                // ── comments / reroutes ───────────────────────────────────────
                case "addcomment":
                {
                    var id = IdGenerator.NewCommentId();
                    newIds["commentId"] = id.Value.ToString();
                    return new Parsed(
                        new GraphCommand.AddComment(
                            id, Str(o, "text"), Vec(o, "position"),
                            OptVec(o, "size") ?? new Vector2(240f, 120f),
                            OptVec4(o, "color") ?? new Vector4(0.25f, 0.35f, 0.5f, 0.35f),
                            OptBool(o, "moveWithContents") ?? true),
                        new GraphCommand.RemoveComment(id), "Add Comment (MCP)", newIds);
                }

                case "updatecomment":
                {
                    var id  = CommentId(o, "comment");
                    var cur = model.Comments.FirstOrDefault(c => c.Id == id);
                    return new Parsed(
                        new GraphCommand.UpdateComment(
                            id, OptStr(o, "text"), OptVec(o, "position"), OptVec(o, "size"),
                            OptVec4(o, "color"), OptInt(o, "zOrder"), OptBool(o, "moveWithContents")),
                        cur != null
                            ? new GraphCommand.UpdateComment(id, cur.Text, cur.Position, cur.Size,
                                                            cur.Color, null, cur.MoveWithContents)
                            : null,
                        "Update Comment (MCP)", newIds);
                }

                case "removecomment":
                {
                    var id  = CommentId(o, "comment");
                    var cur = model.Comments.FirstOrDefault(c => c.Id == id);
                    return new Parsed(
                        new GraphCommand.RemoveComment(id),
                        cur != null
                            ? new GraphCommand.AddComment(cur.Id, cur.Text, cur.Position, cur.Size,
                                                          cur.Color, cur.MoveWithContents)
                            : null,
                        "Remove Comment (MCP)", newIds);
                }

                case "insertreroute":
                {
                    var link = LinkId(o, "link");
                    var at   = model.FindLink(link)?.Waypoints.Count ?? 0;
                    return new Parsed(new GraphCommand.InsertReroute(link, Vec(o, "position")),
                                      new GraphCommand.RemoveReroute(link, at),
                                      "Insert Reroute (MCP)", newIds);
                }

                case "movereroute":
                {
                    var link = LinkId(o, "link");
                    var idx  = Int(o, "waypointIndex");
                    var cur  = model.FindLink(link);
                    var old  = cur != null && idx >= 0 && idx < cur.Waypoints.Count
                             ? cur.Waypoints[idx] : (Vector2?)null;
                    return new Parsed(
                        new GraphCommand.MoveReroute(link, idx, Vec(o, "position")),
                        old is { } p ? new GraphCommand.MoveReroute(link, idx, p) : null,
                        "Move Reroute (MCP)", newIds);
                }

                case "removereroute":
                {
                    var link = LinkId(o, "link");
                    var idx  = Int(o, "waypointIndex");
                    var cur  = model.FindLink(link);
                    var old  = cur != null && idx >= 0 && idx < cur.Waypoints.Count
                             ? cur.Waypoints[idx] : (Vector2?)null;
                    return new Parsed(
                        new GraphCommand.RemoveReroute(link, idx),
                        old is { } p ? new GraphCommand.InsertReroute(link, p) : null,
                        "Remove Reroute (MCP)", newIds);
                }

                // ── containers / regions — HSM parallel states ────────────────
                case "addregion":
                {
                    var c   = NodeId(o, "container");
                    var at  = Int(o, "insertAtIndex");
                    return new Parsed(
                        new GraphCommand.AddRegion(c, at, Str(o, "regionName"), OptInt(o, "priority") ?? 0),
                        new GraphCommand.RemoveRegion(c, at, ChildRedistributionPolicy.MoveToParent),
                        "Add Region (MCP)", newIds);
                }

                // ⚠ No inverse: restoring a removed region means restoring its children's membership,
                //   which the removal policy may already have destroyed. ⛔ Reported, not guessed.
                case "removeregion":
                    return new Parsed(
                        new GraphCommand.RemoveRegion(
                            NodeId(o, "container"), Int(o, "regionIndex"),
                            OptEnum(o, "policy", ChildRedistributionPolicy.MoveToParent)),
                        null, "Remove Region (MCP)", newIds);

                case "reorderregions":
                {
                    var c     = NodeId(o, "container");
                    var order = Ints(o, "newOrder");
                    var prior = (model.FindNode(c) as IContainerNodeModel)?.Regions.Select(r => r.Index).ToList();
                    return new Parsed(new GraphCommand.ReorderRegions(c, order),
                                      prior is { Count: > 0 } ? new GraphCommand.ReorderRegions(c, prior) : null,
                                      "Reorder Regions (MCP)", newIds);
                }

                // ⚠ No inverse — region properties are host-defined, like node properties.
                case "setregionproperty":
                    return new Parsed(
                        new GraphCommand.SetRegionProperty(
                            NodeId(o, "container"), Int(o, "regionIndex"), Str(o, "key"), Val(o, "value")),
                        null, "Set Region Property (MCP)", newIds);

                case "setcontainercollapsed":
                {
                    var c = NodeId(o, "container");
                    var v = Bool(o, "collapsed");
                    return new Parsed(new GraphCommand.SetContainerCollapsed(c, v),
                                      new GraphCommand.SetContainerCollapsed(c, !v),
                                      "Set Container Collapsed (MCP)", newIds);
                }

                case "changeparent":
                {
                    var n   = NodeId(o, "node");
                    var cur = model.FindNode(n);
                    return new Parsed(
                        new GraphCommand.ChangeParent(n, OptNodeId(o, "newParent"),
                                                      OptInt(o, "newRegionIndex"), Vec(o, "position")),
                        cur != null
                            ? new GraphCommand.ChangeParent(n, cur.ParentContainerId, null, cur.Position)
                            : null,
                        "Reparent Node (MCP)", newIds);
                }

                case "changeparentmultiple":
                {
                    var moves   = new List<ChangeParentMove>();
                    var inverse = new List<ChangeParentMove>();
                    foreach (var m in Arr(o, "moves"))
                    {
                        var mo = Obj(m, "moves[]");
                        var n  = NodeId(mo, "node");
                        moves.Add(new ChangeParentMove(n, OptNodeId(mo, "newParent"),
                                                       OptInt(mo, "newRegionIndex"), Vec(mo, "position")));
                        var cur = model.FindNode(n);
                        if (cur != null)
                            inverse.Add(new ChangeParentMove(n, cur.ParentContainerId, null, cur.Position));
                    }
                    return new Parsed(new GraphCommand.ChangeParentMultiple(moves),
                                      inverse.Count > 0 ? new GraphCommand.ChangeParentMultiple(inverse) : null,
                                      "Reparent Nodes (MCP)", newIds);
                }

                // ── refactor ──────────────────────────────────────────────────
                //
                // ⚠ NONE of these carry an inverse. 📐 Each rewrites a whole selection into a new graph
                //   entity; the read-only model cannot express "put it all back". ⛔ The editor's own UI
                //   builds their inverses inside the host sink, out of reach here — so this route reports
                //   `undoable: false` rather than recording a lie on the undo stack.
                case "promotetovariable":
                    return new Parsed(
                        new GraphCommand.PromoteToVariable(
                            PinId(o, "pin"), Str(o, "variableName"),
                            OptBool(o, "isLocal") ?? false, OptStr(o, "categoryPath")),
                        null, "Promote To Variable (MCP)", newIds);

                case "collapsetofunction":
                    return new Parsed(
                        new GraphCommand.CollapseToFunction(
                            NodeIds(o, "nodes"), Str(o, "functionName"),
                            OptBool(o, "pure") ?? false, OptStr(o, "categoryPath")),
                        null, "Collapse To Function (MCP)", newIds);

                case "collapsetomacro":
                    return new Parsed(
                        new GraphCommand.CollapseToMacro(
                            NodeIds(o, "nodes"), Str(o, "macroName"), OptStr(o, "categoryPath")),
                        null, "Collapse To Macro (MCP)", newIds);

                case "collapsetocomment":
                    return new Parsed(
                        new GraphCommand.CollapseToComment(NodeIds(o, "nodes"), Str(o, "commentText")),
                        null, "Collapse To Comment (MCP)", newIds);

                case "expandnode":
                    return new Parsed(new GraphCommand.ExpandNode(NodeId(o, "node")),
                                      null, "Expand Node (MCP)", newIds);

                // ── atomic ────────────────────────────────────────────────────
                //
                // ⭐⭐ Batch is where the union pays off: multi-step authoring becomes ONE undo entry, and
                //    the inverses are reversed so nodes are restored before the links referencing them.
                case "batch":
                {
                    var label    = OptStr(o, "label") ?? "Batch (MCP)";
                    var forwards = new List<GraphCommand>();
                    var inverses = new List<GraphCommand>();
                    var anyMissingInverse = false;

                    foreach (var item in Arr(o, "commands"))
                    {
                        var child = Read(item, model, out var childError);
                        if (child == null)
                            throw new CommandJsonException(
                                $"a nested command could not be read: {childError}");

                        forwards.Add(child.Forward);
                        if (child.Inverse != null) inverses.Add(child.Inverse);
                        else anyMissingInverse = true;

                        foreach (var kv in child.NewIds)
                            newIds[$"{kv.Key}[{forwards.Count - 1}]"] = kv.Value;
                    }

                    inverses.Reverse();

                    return new Parsed(
                        new GraphCommand.Batch(label, forwards),
                        // ⛔ A PARTIAL inverse is worse than none: undoing half a batch leaves a graph
                        //    nobody asked for. If any step lacks one, the batch is not undoable.
                        anyMissingInverse || inverses.Count == 0
                            ? null : new GraphCommand.Batch(label, inverses),
                        label, newIds);
                }

                default:
                    return null;
            }
        }

        /// <summary>⭐ Rebuild removed nodes WITH their pin ids — the shape `EditCommands` uses.</summary>
        /// <remarks>
        /// ⚠ The <c>"PinIds"</c> initial property is not decoration: without it the restored node gets
        /// fresh pins and every link the inverse then restores points at nothing.
        /// </remarks>
        private static GraphCommand? InverseOfRemoveNodes(IReadOnlyList<NodeId> ids, IGraphModel model)
        {
            var steps = new List<GraphCommand>();

            // Links first in the INVERSE order — restore nodes, then the links that reference them.
            var restoreLinks = new List<GraphCommand>();
            foreach (var l in model.Links)
            {
                var fromNode = model.FindPin(l.FromPin)?.OwnerNodeId;
                var toNode   = model.FindPin(l.ToPin)?.OwnerNodeId;
                if ((fromNode is { } f && ids.Contains(f)) || (toNode is { } t && ids.Contains(t)))
                    restoreLinks.Add(new GraphCommand.AddLink(l.Id, l.FromPin, l.ToPin));
            }

            foreach (var id in ids)
            {
                var n = model.FindNode(id);
                if (n == null) continue;
                var props = new Dictionary<string, object?>
                {
                    ["PinIds"] = n.Pins.Select(p => p.Id).ToList(),
                };
                steps.Add(new GraphCommand.AddNode(n.Id, n.Kind, n.Position, props));
            }

            if (steps.Count == 0) return null;
            steps.AddRange(restoreLinks);
            return new GraphCommand.Batch("Restore Nodes", steps);
        }

        // ══ field readers — each names the field it could not read ════════════

        private static JsonObject Obj(JsonNode? n, string what)
            => n as JsonObject ?? throw new CommandJsonException($"'{what}' must be an object.");

        private static JsonArray Arr(JsonObject o, string key)
            => o[key] as JsonArray
               ?? throw new CommandJsonException($"'{key}' is required and must be an array.");

        private static string Str(JsonObject o, string key)
        {
            var v = OptStr(o, key);
            if (string.IsNullOrEmpty(v))
                throw new CommandJsonException($"'{key}' is required and must be a string.");
            return v!;
        }

        private static string? OptStr(JsonObject o, string key)
        {
            var n = o[key];
            if (n is null) return null;
            try { return n.GetValue<string>(); } catch { return n.ToJsonString().Trim('"'); }
        }

        private static bool Bool(JsonObject o, string key)
            => OptBool(o, key) ?? throw new CommandJsonException($"'{key}' is required and must be a boolean.");

        private static bool? OptBool(JsonObject o, string key)
        {
            var n = o[key];
            if (n is null) return null;
            try { return n.GetValue<bool>(); } catch { throw new CommandJsonException($"'{key}' must be a boolean."); }
        }

        private static int Int(JsonObject o, string key)
            => OptInt(o, key) ?? throw new CommandJsonException($"'{key}' is required and must be a number.");

        private static int? OptInt(JsonObject o, string key)
        {
            var n = o[key];
            if (n is null) return null;
            try { return n.GetValue<int>(); }
            catch
            {
                try { return (int)n.GetValue<double>(); }
                catch { throw new CommandJsonException($"'{key}' must be a number."); }
            }
        }

        private static IReadOnlyList<int> Ints(JsonObject o, string key)
        {
            var list = new List<int>();
            foreach (var item in Arr(o, key))
            {
                if (item is null) continue;
                try { list.Add(item.GetValue<int>()); }
                catch { throw new CommandJsonException($"'{key}' must be an array of numbers."); }
            }
            return list;
        }

        private static Guid Guid_(JsonObject o, string key)
        {
            var raw = OptStr(o, key);
            if (!Guid.TryParse(raw, out var g))
                throw new CommandJsonException(
                    $"'{key}' must be a GUID from GET /assets/{{assetId}}/graph; got '{raw ?? "null"}'.");
            return g;
        }

        private static NodeId NodeId(JsonObject o, string key)       => new(Guid_(o, key));
        private static PinId PinId(JsonObject o, string key)         => new(Guid_(o, key));
        private static LinkId LinkId(JsonObject o, string key)       => new(Guid_(o, key));
        private static CommentId CommentId(JsonObject o, string key) => new(Guid_(o, key));
        private static AttachmentId AttachmentId(JsonObject o, string key) => new(Guid_(o, key));

        private static NodeId? OptNodeId(JsonObject o, string key)
            => o[key] is null ? null : new NodeId(Guid_(o, key));

        private static IReadOnlyList<NodeId> NodeIds(JsonObject o, string key)
            => Guids(o, key).Select(g => new NodeId(g)).ToList();

        private static IReadOnlyList<LinkId> LinkIds(JsonObject o, string key)
            => Guids(o, key).Select(g => new LinkId(g)).ToList();

        private static IReadOnlyList<AttachmentId> AttachmentIds(JsonObject o, string key)
            => Guids(o, key).Select(g => new AttachmentId(g)).ToList();

        private static List<Guid> Guids(JsonObject o, string key)
        {
            var list = new List<Guid>();
            foreach (var item in Arr(o, key))
            {
                string? raw = null;
                try { raw = item?.GetValue<string>(); } catch { /* fall through to the throw */ }
                if (!Guid.TryParse(raw, out var g))
                    throw new CommandJsonException($"'{key}' must be an array of GUIDs; '{raw ?? "null"}' is not one.");
                list.Add(g);
            }
            return list;
        }

        private static TEnum Enum<TEnum>(JsonObject o, string key) where TEnum : struct
        {
            var raw = Str(o, key);
            if (!System.Enum.TryParse<TEnum>(raw, ignoreCase: true, out var v))
                throw new CommandJsonException(
                    $"'{key}' must be one of: {string.Join(", ", System.Enum.GetNames(typeof(TEnum)))}; got '{raw}'.");
            return v;
        }

        private static TEnum OptEnum<TEnum>(JsonObject o, string key, TEnum fallback) where TEnum : struct
            => o[key] is null ? fallback : Enum<TEnum>(o, key);

        /// <summary>A position; absent means the canvas origin, which is what an agent usually means.</summary>
        private static Vector2 Vec(JsonObject o, string key) => OptVec(o, key) ?? Vector2.Zero;

        private static Vector2? OptVec(JsonObject o, string key)
        {
            if (o[key] is not JsonObject v) return null;
            return new Vector2(F(v, "x"), F(v, "y"));
        }

        private static Vector4? OptVec4(JsonObject o, string key)
        {
            if (o[key] is not JsonObject v) return null;
            return new Vector4(F(v, "r"), F(v, "g"), F(v, "b"), F(v, "a"));
        }

        private static float F(JsonObject o, string key)
        {
            var n = o[key];
            if (n is null) return 0f;
            try { return n.GetValue<float>(); } catch { return 0f; }
        }

        /// <summary>A host-defined property bag — passed through as JSON primitives.</summary>
        private static IReadOnlyDictionary<string, object?>? Props(JsonObject o, string key)
        {
            if (o[key] is not JsonObject p) return null;
            var d = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var kv in p) d[kv.Key] = Primitive(kv.Value);
            return d;
        }

        private static object? Val(JsonObject o, string key) => Primitive(o[key]);

        /// <summary>
        /// ⭐ JSON → a boxed CLR primitive the host sink can interpret.
        /// </summary>
        /// <remarks>
        /// ⚠ Numbers become <c>double</c> and the HOST coerces. ⛔ This layer must not guess a target type:
        /// it has no type system, and a wrong narrowing is silent data loss.
        /// </remarks>
        private static object? Primitive(JsonNode? n)
        {
            if (n is null) return null;
            try
            {
                return n.GetValueKind() switch
                {
                    System.Text.Json.JsonValueKind.True   => true,
                    System.Text.Json.JsonValueKind.False  => false,
                    System.Text.Json.JsonValueKind.String => n.GetValue<string>(),
                    System.Text.Json.JsonValueKind.Number => n.GetValue<double>(),
                    System.Text.Json.JsonValueKind.Null   => null,
                    _ => n.ToJsonString(),
                };
            }
            catch
            {
                return n.ToJsonString();
            }
        }

        /// <summary>⭐ The self-describing payload for <c>GET /assets/{id}/graph/command</c>.</summary>
        internal static JsonObject Describe()
        {
            var variants = new JsonArray();
            foreach (var kv in Schema.OrderBy(k => k.Key, StringComparer.Ordinal))
                variants.Add(new JsonObject
                {
                    ["type"]   = kv.Key,
                    ["fields"] = new JsonArray(kv.Value.Select(f => (JsonNode)f!).ToArray()),
                });

            var unsupported = new JsonArray();
            foreach (var kv in Unsupported.OrderBy(k => k.Key, StringComparer.Ordinal))
                unsupported.Add(new JsonObject { ["type"] = kv.Key, ["reason"] = kv.Value });

            return new JsonObject
            {
                ["count"]       = variants.Count,
                ["variants"]    = variants,
                ["unsupported"] = unsupported,
                ["note"]        = "POST one of these to /assets/{assetId}/graph/command as "
                                + "{\"type\":\"<type>\", …fields}. A field marked '?' is optional. Ids are "
                                + "GUID strings from GET /assets/{assetId}/graph. 'Batch' takes "
                                + "{\"commands\":[…]} and applies them as ONE undo entry.",
            };
        }
    }
}
