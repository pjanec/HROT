using System;
using System.Globalization;
using System.Text.Json.Nodes;
using NodeEditor.Core.Interfaces;

namespace Hrot.Editor.DebugApi
{
    /// <summary>
    /// ⭐⭐⭐ <b>The AUTHORING READ — an IN-MEMORY-FAITHFUL projection of a graph.</b>
    /// 📄 <c>docs/DESIGN_Mcp_Authoring.md</c> §3 *(the read-shape caution)* · §5 *(the classDiagram)*.
    ///
    /// <para>⛔⛔ <b>Why this is NOT the save serialization, restated at the point of use.</b> 📐 Measured:
    /// <c>SaveActiveBlueprintCommand</c> rewrites every link endpoint to a <b>deterministic name-derived</b>
    /// pin guid and STRIPS the in-memory pins before writing. ⇒ ⭐⭐ <b>there are TWO ID SPACES</b>, and only
    /// the in-memory one is the space <see cref="NodeEditor.Core.Commands.GraphCommand"/> edits by. An agent
    /// handed the on-disk ids would address nothing.</para>
    ///
    /// <para>⭐⭐⭐ <b>Why it projects <see cref="IGraphModel"/> and not any host's asset model.</b>
    /// ⚠ The design named <c>BlueprintClipboard</c> as the closest analog to reuse. 📐 Measured, and it is
    /// the WRONG shape: the clipboard round-trips <c>Hrot.Blueprints.Core.Assets.Node</c> — the ASSET model,
    /// Blueprint-only, and its own header says the vendored NodeEdit tree *"knows nothing about"* it. It
    /// carries no <c>PinId</c> at all. ⭐ <see cref="IGraphModel"/>/<see cref="INodeModel"/>/
    /// <see cref="IPinModel"/>/<see cref="ILinkModel"/> ARE the in-memory view, they expose exactly the
    /// <c>NodeId</c>/<c>PinId</c>/<c>LinkId</c> the command sink takes, and they are <b>host-agnostic</b> ⇒
    /// ⭐⭐ <b>one serializer covers BTree, HSM and Blueprint</b> instead of three. *(Deviation argued in the
    /// report and folded into the design — obligation ③/⑤.)*</para>
    ///
    /// <para>⚠ <b>It is a PROJECTION, not a persistence format.</b> ⛔ Nothing reads it back; the edit routes
    /// take ids, not documents. So a value it cannot represent losslessly is rendered as text with its CLR
    /// type named, rather than dropped silently.</para>
    /// </summary>
    internal static class InMemoryGraphSerializer
    {
        /// <summary>⭐ The whole graph: nodes with their pins, links, comments — all by in-memory guid.</summary>
        public static JsonObject ToJson(IGraphModel model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));

            var nodes = new JsonArray();
            foreach (var n in model.Nodes) nodes.Add(NodeToJson(model, n));

            var links = new JsonArray();
            foreach (var l in model.Links)
            {
                var lo = new JsonObject
                {
                    ["linkId"]  = l.Id.Value.ToString(),
                    ["fromPin"] = l.FromPin.Value.ToString(),
                    ["toPin"]   = l.ToPin.Value.ToString(),
                    // ⭐ The owning nodes, resolved here rather than left to the caller: an agent reading
                    //   "which node feeds which" should not have to build a pin→node index itself.
                    ["fromNode"] = model.FindPin(l.FromPin)?.OwnerNodeId.Value.ToString(),
                    ["toNode"]   = model.FindPin(l.ToPin)?.OwnerNodeId.Value.ToString(),
                    ["style"]    = l.Style.ToString(),
                };

                // ⭐ MA-011 — REROUTE WAYPOINTS. `InsertReroute`/`MoveReroute`/`RemoveReroute` are three
                //   variants of the union; without the waypoints in the read they are unprovable.
                if (l.Waypoints.Count > 0)
                {
                    var wp = new JsonArray();
                    foreach (var w in l.Waypoints) wp.Add(Vec(w.X, w.Y));
                    lo["waypoints"] = wp;
                }

                links.Add(lo);
            }

            var comments = new JsonArray();
            foreach (var c in model.Comments)
                comments.Add(new JsonObject
                {
                    ["commentId"] = c.Id.Value.ToString(),
                    ["text"]      = c.Text,
                    ["position"]  = Vec(c.Position.X, c.Position.Y),
                });

            // ⭐⭐⭐ MA-011 — the ASSET-LEVEL attachment list, in addition to the per-node one.
            // ⛔ Both, deliberately: the per-node list is how a caller asks *"what decorates THIS node"*,
            //    and the flat list is how it asks *"what attachments exist at all"* — which is the one a
            //    coverage rail needs after an `AddAttachment` whose host it must not assume.
            var attachments = new JsonArray();
            foreach (var a in model.Attachments) attachments.Add(AttachmentToJson(a));

            return new JsonObject
            {
                ["graphId"]         = model.Id.Value.ToString(),
                ["displayName"]     = model.DisplayName,
                ["graphKind"]       = model.Kind.Id,
                ["nodeCount"]       = nodes.Count,
                ["linkCount"]       = links.Count,
                ["attachmentCount"] = attachments.Count,
                ["nodes"]           = nodes,
                ["links"]           = links,
                ["comments"]        = comments,
                ["attachments"]     = attachments,
            };
        }

        /// <summary>
        /// ⭐⭐ One attachment — a <b>BTree decorator / condition pill</b> or an <c>HsmAttachment</c>.
        /// </summary>
        /// <remarks>
        /// ⭐⭐⭐ <b>Why this had to be added</b> *(design §11.3)*: the four typed verbs could not express an
        /// attachment, so nothing read one back either. ⇒ ⛔ an `AddAttachment` over the union route would
        /// have been <b>unprovable</b> — the round-trip rail has nothing to assert against. 📌 The read and
        /// the write must widen TOGETHER, or the wider write is untestable.
        /// </remarks>
        private static JsonObject AttachmentToJson(IAttachmentModel a)
        {
            var o = new JsonObject
            {
                ["attachmentId"] = a.Id.Value.ToString(),
                ["hostNodeId"]   = a.HostNodeId.Value.ToString(),
                ["category"]     = a.Category.ToString(),
                ["stackIndex"]   = a.StackIndex,
            };

            if (!string.IsNullOrEmpty(a.Glyph))   o["glyph"]   = a.Glyph;
            if (!string.IsNullOrEmpty(a.Label))   o["label"]   = a.Label;
            if (!string.IsNullOrEmpty(a.Tooltip)) o["tooltip"] = a.Tooltip;

            // ⭐ The host's restore data — the generic delete-undo path rebuilds the attachment from it,
            //   so an agent that reads it can reconstruct the attachment through the union route too.
            if (a.HostProperties is { Count: > 0 } props)
            {
                var p = new JsonObject();
                foreach (var kv in props) p[kv.Key] = kv.Value is null ? null : ValueToJson(kv.Value);
                o["hostProperties"] = p;
            }

            return o;
        }

        private static JsonObject NodeToJson(IGraphModel model, INodeModel n)
        {
            var pins = new JsonArray();
            foreach (var p in n.Pins) pins.Add(PinToJson(p));

            var o = new JsonObject
            {
                ["nodeId"]   = n.Id.Value.ToString(),
                ["kind"]     = n.Kind.Id,
                ["title"]    = n.Title,
                ["subtitle"] = n.Subtitle,
                ["category"] = n.Category.ToString(),
                ["position"] = Vec(n.Position.X, n.Position.Y),
                ["pins"]     = pins,
            };

            if (n.ParentContainerId is { } parent)
                o["parentContainerId"] = parent.Value.ToString();

            // ⚠ Emitted only when TRUE / non-default — a payload of mostly-false booleans is harder to
            //   read than one that states only what is unusual.
            if (n.IsCollapsed)      o["isCollapsed"]     = true;
            if (n.ShowAdvancedPins) o["showAdvancedPins"] = true;

            // ⭐⭐⭐ MA-011 — CONTAINERS AND REGIONS: the HSM parallel-region structure.
            // 📄 design §11.3. ⛔ Without this an `AddRegion`/`ReorderRegions` edit lands in the model and
            //    the read cannot see it ⇒ the host specific the union route exists for is unprovable.
            if (n is IContainerNodeModel { IsContainer: true } container)
            {
                var regions = new JsonArray();
                foreach (var r in container.Regions)
                    regions.Add(new JsonObject
                    {
                        ["index"]    = r.Index,
                        ["name"]     = r.Name,
                        ["priority"] = r.Priority,
                    });

                var children = new JsonArray();
                foreach (var childId in container.ChildNodeIds)
                    children.Add(new JsonObject
                    {
                        ["nodeId"]      = childId.Value.ToString(),
                        // ⭐ The region a child sits in — the thing a reparent edit changes, and the only
                        //   way to tell "moved between regions" from "moved on the canvas".
                        ["regionIndex"] = container.GetRegionIndexForChild(childId),
                    });

                o["isContainer"]       = true;
                o["regionOrientation"] = container.RegionOrientation.ToString();
                o["regions"]           = regions;
                o["children"]          = children;
            }

            // ⭐ The per-node attachment stack, in StackIndex order — how a caller asks
            //   "what decorates THIS node" without scanning the asset-level list.
            var nodeAttachments = model.GetAttachmentsForNode(n.Id);
            if (nodeAttachments.Count > 0)
            {
                var arr = new JsonArray();
                foreach (var a in nodeAttachments) arr.Add(AttachmentToJson(a));
                o["attachments"] = arr;
            }

            return o;
        }

        private static JsonObject PinToJson(IPinModel p)
        {
            var o = new JsonObject
            {
                ["pinId"]     = p.Id.Value.ToString(),
                ["label"]     = p.Label,
                ["direction"] = p.Direction.ToString(),
                ["kind"]      = p.Kind.ToString(),
                ["type"]      = p.Type?.Id,
            };

            // ⚠ Optional flags are emitted only when TRUE — the common pin carries neither, and a payload
            //   of mostly-false booleans is harder to read than one that states only what is unusual.
            if (p.IsAdvanced) o["isAdvanced"] = true;
            if (p.IsOptional) o["isOptional"] = true;

            var def = p.Default?.Value;
            if (def != null)
            {
                o["default"]     = ValueToJson(def);
                o["defaultType"] = def.GetType().Name;
            }

            return o;
        }

        /// <summary>
        /// ⭐ Renders a boxed pin default. ⛔ Anything not a JSON primitive becomes its <c>ToString()</c>
        /// with <c>defaultType</c> naming the CLR type — ⚠ stated as text rather than dropped, so an agent
        /// can see that a value EXISTS even when it cannot round-trip it.
        /// </summary>
        private static JsonNode? ValueToJson(object v) => v switch
        {
            bool b            => JsonValue.Create(b),
            string s          => JsonValue.Create(s),
            int i             => JsonValue.Create(i),
            long l            => JsonValue.Create(l),
            float f           => JsonValue.Create(f),
            double d          => JsonValue.Create(d),
            decimal m         => JsonValue.Create(m),
            Guid g            => JsonValue.Create(g.ToString()),
            IFormattable fmt  => JsonValue.Create(fmt.ToString(null, CultureInfo.InvariantCulture)),
            _                 => JsonValue.Create(v.ToString()),
        };

        private static JsonObject Vec(float x, float y) => new() { ["x"] = x, ["y"] = y };
    }
}
