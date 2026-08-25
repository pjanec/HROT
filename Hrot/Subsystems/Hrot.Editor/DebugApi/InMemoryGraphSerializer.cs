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
                links.Add(new JsonObject
                {
                    ["linkId"]  = l.Id.Value.ToString(),
                    ["fromPin"] = l.FromPin.Value.ToString(),
                    ["toPin"]   = l.ToPin.Value.ToString(),
                    // ⭐ The owning nodes, resolved here rather than left to the caller: an agent reading
                    //   "which node feeds which" should not have to build a pin→node index itself.
                    ["fromNode"] = model.FindPin(l.FromPin)?.OwnerNodeId.Value.ToString(),
                    ["toNode"]   = model.FindPin(l.ToPin)?.OwnerNodeId.Value.ToString(),
                });

            var comments = new JsonArray();
            foreach (var c in model.Comments)
                comments.Add(new JsonObject
                {
                    ["commentId"] = c.Id.Value.ToString(),
                    ["text"]      = c.Text,
                    ["position"]  = Vec(c.Position.X, c.Position.Y),
                });

            return new JsonObject
            {
                ["graphId"]     = model.Id.Value.ToString(),
                ["displayName"] = model.DisplayName,
                ["graphKind"]   = model.Kind.Id,
                ["nodeCount"]   = nodes.Count,
                ["linkCount"]   = links.Count,
                ["nodes"]       = nodes,
                ["links"]       = links,
                ["comments"]    = comments,
            };
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
