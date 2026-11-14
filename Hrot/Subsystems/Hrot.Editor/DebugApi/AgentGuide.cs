using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Hrot.Editor.DebugApi
{
    /// <summary>
    /// ⭐ CE-4523 (docs/DESIGN_Debug_Api_Http_Only.md §4) — WHERE a route's parameter goes, derived, never authored: a <c>{name}</c> in the
    /// template ⇒ the path; otherwise GET / DELETE ⇒ the query, POST / PUT / PATCH ⇒ the JSON body. <c>EveryRouteReadsItsParamsWhereDerivedTests</c>
    /// keeps every handler true to it; <see cref="AgentGuide"/> renders it.
    /// </summary>
    public static class ParamLocations
    {
        public const string Path = "path", Query = "query", Body = "body";

        public static string Of(string method, string template, string name)
            => template.Contains("{" + name + "}", StringComparison.Ordinal) ? Path : ForMethod(method);

        /// <summary>Where a non-path key goes for <paramref name="method"/>.</summary>
        public static string ForMethod(string method) => method is "GET" or "DELETE" ? Query : Body;
    }

    /// <summary>
    /// ⭐⭐ CE-4525 (H-3; docs/DESIGN_Debug_Api_Http_Only.md §2.1) — the agent guide: the prose (embedded <c>DebugApi/Guide/*.md</c>, moved from
    /// the retired MCP server's skill parts, CE-4524) + the command reference RENDERED from <see cref="DebugApiRouteDocs"/> — the same table
    /// <c>GET /capabilities</c> serves. ⭐ It adds no fact of its own, so it can never describe a different build than the one running.
    /// Served as <c>GET /help</c>; <c>Hrot.ClusterRunner --help-api</c> prints the same text with no world started (no availability).
    /// ⭐ Language-neutral (R-4221): every number through the invariant culture.
    /// </summary>
    public static class AgentGuide
    {
        /// <summary>The prose parts, in order (embedded resources <c>Hrot.Editor.Guide.&lt;name&gt;</c>).</summary>
        public static readonly string[] Parts = { "00-intro.md", "10-mental-model.md", "20-lifecycle.md", "30-workflows.md", "50-gotchas.md", "60-discovery.md" };

        /// <summary>The availability marker a route gets on a host that cannot serve it now (stripped to compare with <c>--help-api</c>).</summary>
        public const string NotHereMarker = "  ⛔ not here: ";

        /// <summary>
        /// The guide. <paramref name="available"/>: a capability key ⇒ can THIS host serve it now (null: no host — <c>--help-api</c>, nothing
        /// marked). <paramref name="appendix"/>: sections rendered by other owners (the environment variables, CE-4586).
        /// </summary>
        public static string Render(IEnumerable<(string Method, string Template)> routes, Func<string, bool?>? available = null,
            IEnumerable<string>? appendix = null)
        {
            var sb = new StringBuilder(64 * 1024);
            foreach (var part in Parts) sb.Append(ReadPart(part)).Append("\n\n");
            sb.Append("---\n\n## 7. Command reference — every route\n\n")
              .Append("Generated at request time from the route docs (the same source as `GET /capabilities`). Parameters say where they go: ")
              .Append("`{path}` is in the URL, `?query` on the URL (GET / DELETE), `body` a JSON object (POST / PUT / PATCH).\n");
            if (available != null) sb.Append("A route this host cannot serve right now is marked `⛔ not here: <capability>` (switch perspective, or see `GET /capabilities`).\n");

            var list = routes.Distinct().ToList();
            var byGroup = list
                .Select(r => (r.Method, r.Template, Doc: DebugApiRouteDocs.ByRoute.TryGetValue((r.Method, r.Template), out var d) ? d : null))
                .GroupBy(r => r.Doc?.Group ?? "(undocumented)")
                .OrderBy(g => g.Key, StringComparer.Ordinal);
            foreach (var group in byGroup)
            {
                sb.Append("\n### ").Append(group.Key).Append("\n\n");
                foreach (var (method, template, doc) in group.OrderBy(r => r.Template, StringComparer.Ordinal).ThenBy(r => r.Method, StringComparer.Ordinal))
                    Route(sb, method, template, doc, available);
            }
            if (appendix != null) foreach (var a in appendix) sb.Append("\n").Append(a.TrimEnd()).Append('\n');
            return sb.ToString();
        }

        private static void Route(StringBuilder sb, string method, string template, RouteDoc? doc, Func<string, bool?>? available)
        {
            sb.Append("**`").Append(method).Append(' ').Append(template).Append("`**");
            if (available != null && CapabilityManifest.CapabilityFor(template) is { } cap && available(cap) == false)
                sb.Append(NotHereMarker).Append(cap);
            sb.Append('\n');
            if (doc is null) { sb.Append("  (no doc)\n\n"); return; }
            sb.Append("  ").Append(doc.Summary).Append('\n');
            foreach (var p in doc.Params ?? Array.Empty<RouteParam>())
            {
                string where = ParamLocations.Of(method, template, p.Name);
                sb.Append("  - `").Append(p.Name).Append("` (").Append(where);
                if (!string.IsNullOrEmpty(p.Type)) sb.Append(", ").Append(p.Type);
                sb.Append(p.Required ? ", required" : ", optional");
                if (p.DefaultJson != null) sb.Append(", default ").Append(p.DefaultJson);
                if (p.EnumJson != null) sb.Append(", one of ").Append(p.EnumJson);
                sb.Append(") — ").Append(p.Description).Append('\n');
            }
            if (!string.IsNullOrWhiteSpace(doc.Returns)) sb.Append("  Returns: ").Append(doc.Returns).Append('\n');
            if (!string.IsNullOrWhiteSpace(doc.ExampleArgsJson)) sb.Append("  Example: ").Append(Example(method, template, doc.ExampleArgsJson!)).Append('\n');
            foreach (var n in doc.Notes ?? Array.Empty<string>()) sb.Append("  ⚠ ").Append(n).Append('\n');
            sb.Append('\n');
        }

        /// <summary>The documented example args as a call: path values substituted, the rest as <c>?k=v</c> or a JSON body.</summary>
        private static string Example(string method, string template, string argsJson)
        {
            System.Text.Json.Nodes.JsonObject? args;
            try { args = System.Text.Json.Nodes.JsonNode.Parse(argsJson) as System.Text.Json.Nodes.JsonObject; }
            catch (System.Text.Json.JsonException) { return argsJson; }
            if (args is null) return argsJson;
            string path = template;
            var rest = new System.Text.Json.Nodes.JsonObject();
            foreach (var (k, v) in args)
            {
                if (path.Contains("{" + k + "}", StringComparison.Ordinal)) path = path.Replace("{" + k + "}", Scalar(v), StringComparison.Ordinal);
                else rest[k] = v?.DeepClone();
            }
            if (rest.Count == 0) return $"{method} {path}";
            if (ParamLocations.ForMethod(method) == ParamLocations.Query)
                return $"{method} {path}?" + string.Join("&", rest.Select(kv => kv.Key + "=" + Uri.EscapeDataString(Scalar(kv.Value))));
            return $"{method} {path} " + rest.ToJsonString();
        }

        private static string Scalar(System.Text.Json.Nodes.JsonNode? v)
            => v is System.Text.Json.Nodes.JsonValue jv && jv.TryGetValue<string>(out var s) ? s
             : v is System.Text.Json.Nodes.JsonValue nv && nv.TryGetValue<double>(out var d) ? d.ToString(CultureInfo.InvariantCulture)
             : v?.ToJsonString() ?? "";

        /// <summary>One embedded prose part.</summary>
        public static string ReadPart(string name)
        {
            using var s = typeof(AgentGuide).Assembly.GetManifestResourceStream("Hrot.Editor.Guide." + name)
                       ?? throw new InvalidOperationException($"the guide part '{name}' is not embedded (Hrot.Editor.csproj, DebugApi/Guide/*.md)");
            using var r = new StreamReader(s, Encoding.UTF8);
            return r.ReadToEnd().Trim();
        }
    }
}
