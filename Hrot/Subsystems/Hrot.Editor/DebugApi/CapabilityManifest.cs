using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Hrot.Presentation.DebugApi;

namespace Hrot.Editor.DebugApi;

/// <summary>
/// ⭐⭐⭐ <b><c>GET /capabilities</c> — DESCRIPTION × AVAILABILITY, and NEITHER half is hand-authored.</b>
/// 📄 <c>Architect_Question_54</c> § *"The manifest is DESCRIPTION × AVAILABILITY"* *(RESOLVED, user-approved
/// FULL from day one)* · charter <c>D4</c> *(the manifest is a teaching surface that makes absence
/// assertable)*.
///
/// <para>🔒 <b>User, `2026-08-24`:</b> <i>"all endpoints are known… The manifest could likely be describing
/// all the apis, implemented internally just with some matrix what works where — and the matrix will change
/// but not the manifest itself."</i></para>
///
/// <para>⭐⭐ <b>Two layers that change at different rates, with two different sources of truth:</b>
/// <list type="number">
/// <item>⭐ <b>DESCRIPTION</b> — every endpoint this host serves, <b>enumerated from the live route
/// table</b>. ⇒ ⛔ it cannot drift: adding a route grows the manifest by construction, and there is no list
/// to remember to update.</item>
/// <item>⭐⭐ <b>AVAILABILITY</b> — <b>measured</b> from what is actually wired *(a provider reports
/// <c>time.drive</c> present because its facade is non-null, not because a table said so)*. 📌 Q54's one
/// real risk is a hand-authored matrix: it stays green while the code drifts — `CLAUDE.md` §M's
/// "the ledger may not assert what the code is".</item>
/// </list></para>
///
/// <para>⛔⛔ <b>The known-absent BASELINE lives in the HARNESS, not here.</b> ⭐ This endpoint reports what the
/// host measures RIGHT NOW; the committed baseline of *"cells legitimately absent during migration"* is the
/// GOLDEN it is compared against, and a golden belongs with the test that asserts it. 📌 Same split as the
/// panel goldens: live dump here, reviewed expectation there.</para>
/// </summary>
public static class CapabilityManifest
{
    /// <summary>
    /// ⭐⭐⭐ <b>The capability a route needs, derived from its PATH — one table, and a rail keeps it
    /// complete.</b>
    ///
    /// <para>⭐ Prefix-derived rather than per-route annotated, so a new endpoint under an existing prefix is
    /// classified automatically. ⛔ A new PREFIX yields <see langword="null"/> *(unclassified)* — and
    /// <c>CapabilityManifestRails</c> reddens on that, naming the route. ⇒ ⭐⭐ classification cannot be
    /// forgotten, only made deliberately. 📌 The same inversion the panel-golden ignore-list uses.</para>
    /// </summary>
    public static string? CapabilityFor(string path)
    {
        // ⚠ Order matters: the most specific prefix first.
        if (path.StartsWith("/panels", StringComparison.Ordinal))
            return path.Contains("_gizmo", StringComparison.Ordinal)
                ? DebugCapabilities.GizmoFrame : DebugCapabilities.Panels;

        if (path.StartsWith("/preview", StringComparison.Ordinal))    return DebugCapabilities.Preview;
        if (path.StartsWith("/sim", StringComparison.Ordinal))         return DebugCapabilities.TimeDrive;
        // ⭐⭐ HN-029: the LOAD routes are their OWN capability, not `editor.authoring`.
        //    📌 While `/scenario/load` was hardwired to IEditorLogic, a cluster refusal read "authoring is
        //    absent here" — true of the editor's DRIVER, but easily misread as "a cluster cannot load
        //    scenarios", which is false: it loads them all the time, through 2PC. ⛔ Ordered BEFORE the
        //    /scenario prefix below, which still covers save/list.
        if (path.StartsWith("/scenario/load", StringComparison.Ordinal)) return DebugCapabilities.ScenarioLoad;
        if (path.StartsWith("/scenario", StringComparison.Ordinal))    return DebugCapabilities.EditorAuthoring;
        if (path.StartsWith("/scenarios", StringComparison.Ordinal))   return DebugCapabilities.EditorAuthoring;
        if (path.StartsWith("/recording", StringComparison.Ordinal))   return DebugCapabilities.EditorAuthoring;
        if (path.StartsWith("/replay", StringComparison.Ordinal))      return DebugCapabilities.EditorAuthoring;
        if (path.StartsWith("/checkpoint", StringComparison.Ordinal))  return DebugCapabilities.WorldRead;
        if (path.StartsWith("/diff", StringComparison.Ordinal))        return DebugCapabilities.WorldRead;
        if (path.StartsWith("/entities", StringComparison.Ordinal))    return DebugCapabilities.WorldRead;
        if (path.StartsWith("/breakpoints", StringComparison.Ordinal)) return DebugCapabilities.EditorAuthoring;
        if (path.StartsWith("/breakpoint-types", StringComparison.Ordinal)) return DebugCapabilities.EditorAuthoring;
        if (path.StartsWith("/blueprints", StringComparison.Ordinal))  return DebugCapabilities.EditorAuthoring;
        if (path.StartsWith("/behaviors", StringComparison.Ordinal))   return DebugCapabilities.WorldRead;
        if (path.StartsWith("/annotations", StringComparison.Ordinal)) return DebugCapabilities.WorldRead;
        if (path.StartsWith("/trace", StringComparison.Ordinal))       return DebugCapabilities.WorldRead;
        if (path.StartsWith("/attributes", StringComparison.Ordinal))  return DebugCapabilities.WorldRead;
        if (path.StartsWith("/world", StringComparison.Ordinal))       return DebugCapabilities.WorldRead;
        if (path.StartsWith("/perspective", StringComparison.Ordinal)) return "perspective.switch";
        if (path.StartsWith("/tkb", StringComparison.Ordinal))         return "tkb.read";
        if (path.StartsWith("/components", StringComparison.Ordinal))  return "registry.read";
        if (path.StartsWith("/commands", StringComparison.Ordinal))    return "registry.read";
        if (path.StartsWith("/events", StringComparison.Ordinal))      return "events.read";
        if (path.StartsWith("/logs", StringComparison.Ordinal))        return "logs.read";

        // ⭐ Always available, by construction: they describe or end the host itself.
        if (path is "/" or "/status" or "/shutdown" or "/capabilities") return "host";

        return null;   // ⛔ unclassified — the rail names it
    }

    /// <summary>
    /// ⭐ Projects a <see cref="RouteDoc"/> into the manifest. ⛔ Deliberately flat and lossless: the JS
    /// generator's job is a rename, not an interpretation, so anything it needs has to survive this hop.
    /// <para>⚠ <c>exampleArgs</c> and a param's <c>default</c> are re-parsed from their JSON literals so the
    /// dump carries real JSON values rather than strings containing JSON — otherwise every consumer would
    /// have to parse them again.</para>
    /// </summary>
    private static JsonNode DocToJson(RouteDoc doc)
    {
        var o = new JsonObject
        {
            ["tool"]    = doc.Tool,
            ["group"]   = doc.Group,
            ["summary"] = doc.Summary,
            ["returns"] = doc.Returns,
            ["hint"]    = doc.Hint,
        };

        if (doc.NotATool)     o["notATool"]     = true;
        if (doc.ManualVerify) o["manualVerify"] = true;

        if (doc.Params is { Length: > 0 })
        {
            var ps = new JsonArray();
            foreach (var p in doc.Params)
            {
                var po = new JsonObject
                {
                    ["name"]        = p.Name,
                    ["required"]    = p.Required,
                    ["description"] = p.Description,
                };
                // ⭐ Omitted, not null, when the param deliberately accepts several shapes — see RouteParam.Type.
                if (p.Type is not null) po["type"] = p.Type;
                if (p.DefaultJson    is not null) po["default"]    = ParseOrString(p.DefaultJson);
                // ⚠ These three reach the agent's inputSchema. 📌 An earlier cut omitted them and the
                //   regenerated catalog silently lost five `enum` constraints, two `items` and one
                //   `properties` — tools that would then have accepted anything. The round-trip diff caught it.
                if (p.EnumJson       is not null) po["enum"]       = ParseOrString(p.EnumJson);
                if (p.ItemsJson      is not null) po["items"]      = ParseOrString(p.ItemsJson);
                if (p.PropertiesJson is not null) po["properties"] = ParseOrString(p.PropertiesJson);
                ps.Add(po);
            }
            o["params"] = ps;
        }

        if (doc.Notes is { Length: > 0 })
        {
            var ns = new JsonArray();
            foreach (var n in doc.Notes) ns.Add(n);
            o["notes"] = ns;
        }

        if (doc.ExampleArgsJson is not null || doc.ExampleGist is not null)
        {
            var ex = new JsonObject();
            if (doc.ExampleArgsJson is not null) ex["args"] = ParseOrString(doc.ExampleArgsJson);
            if (doc.ExampleGist is not null)     ex["gist"] = doc.ExampleGist;
            o["example"] = ex;
        }

        return o;
    }

    /// <summary>⭐ Parse a JSON literal; fall back to the raw text so a malformed literal is visible, not fatal.</summary>
    private static JsonNode? ParseOrString(string literal)
    {
        try { return JsonNode.Parse(literal); }
        catch (System.Text.Json.JsonException) { return JsonValue.Create(literal); }
    }

    /// <summary>
    /// ⭐⭐ Builds the manifest for this host.
    /// </summary>
    /// <param name="routes">⭐ The LIVE route table — the description's only source.</param>
    /// <param name="mode">How this process was started, e.g. <c>"editor"</c> or <c>"all"</c>.</param>
    /// <param name="dispatcher">⭐ Present in cluster mode: supplies the measured per-perspective matrix.</param>
    /// <param name="editorCapabilities">
    /// ⭐ Present in editor mode: the single-context measured cells. ⚠ The editor has one implicit
    /// "perspective" for capability purposes — its whole process is one node.
    /// </param>
    public static JsonNode Build(
        IEnumerable<(string Method, string Template)> routes,
        string mode,
        PerspectiveScopedDispatcher? dispatcher,
        IReadOnlyDictionary<string, bool>? editorCapabilities)
    {
        var endpoints = new JsonArray();
        var unclassified = new JsonArray();

        foreach (var (method, template) in routes.OrderBy(r => r.Template, StringComparer.Ordinal)
                                                 .ThenBy(r => r.Method, StringComparer.Ordinal))
        {
            var capability = CapabilityFor(template);
            var entry = new JsonObject
            {
                ["method"]     = method,
                ["path"]       = template,
                ["capability"] = capability,
            };

            // ⭐⭐⭐ HN-030: THE AGENT-FACING DOC, emitted from the SAME route table as the description above.
            //    ⇒ `tools/ai-debug-mcp/tool-catalog.mjs` is GENERATED from this dump instead of being a
            //    hand-maintained mirror of the same routes. 📄 MCP_Integration.md § Follow-up.
            // ⛔ Absent rather than empty when a route carries no doc: EveryRouteIsDocumentedTests fails in
            //    that case, so a null here is a defect the gate names — not a shape a consumer must handle.
            if (DebugApiRouteDocs.ByRoute.TryGetValue((method, template), out var doc))
                entry["doc"] = DocToJson(doc);

            endpoints.Add(entry);
            if (capability is null)
                unclassified.Add(template);
        }

        var matrix = new JsonObject();
        if (dispatcher is not null)
        {
            foreach (var (perspective, cells) in dispatcher.Matrix())
            {
                var row = new JsonObject();
                foreach (var (key, present) in cells) row[key] = present;
                // ⭐ Process-wide statics, reported once per row so a consumer does not have to know they
                //   are global. ⛔ Not claimed per-subsystem in the provider — see its remarks.
                row[DebugCapabilities.Panels]     = true;
                row[DebugCapabilities.GizmoFrame] = true;
                matrix[perspective] = row;
            }
        }
        else if (editorCapabilities is not null)
        {
            var row = new JsonObject();
            foreach (var (key, present) in editorCapabilities) row[key] = present;
            matrix["*"] = row;
        }

        return new JsonObject
        {
            ["mode"] = mode,
            ["host"] = new JsonObject
            {
                // ⭐ hasMaster answers "can a step be CONFIRMED cluster-wide here?" — the ack-gate's
                //   precondition (Q54: issue where the user is, confirm where the truth is).
                ["hasMaster"]            = dispatcher?.HasMaster ?? true,
                ["currentPerspective"]   = dispatcher?.CurrentPerspective,
                ["routablePerspectives"] = new JsonArray(
                    (dispatcher?.RoutablePerspectives ?? Array.Empty<string>())
                        .Select(p => (JsonNode?)p).ToArray()),
            },
            ["endpoints"] = endpoints,
            ["matrix"] = matrix,
            // ⭐⭐ Reported, not hidden: an unclassified route means CapabilityFor has no prefix for it, so
            //    the matrix cannot say whether it works here. ⛔ A rail fails on a non-empty list.
            ["unclassifiedRoutes"] = unclassified,
        };
    }
}
