using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Hrot.Editor.DebugApi;
using Xunit;

namespace Hrot.Editor.Tests;

/// <summary>
/// ⭐⭐⭐ <b>THE ENFORCEMENT THAT MAKES THE GENERATED CATALOG TRUSTWORTHY.</b>
/// 📄 <c>MCP_Integration.md</c> § *"Follow-up — GENERATE `tool-catalog.mjs` from the routes"* *(`HN-030`)*.
///
/// <para>⛔⛔ <b>What went wrong before, and why generation alone would not have prevented it.</b>
/// <c>tool-catalog.mjs</c> was a hand-maintained mirror of the routes, and it drifted: <c>HN-025/026/027</c>
/// shipped <c>/capabilities</c>, <c>/perspectives</c> and <c>/perspective</c> with no catalog entry, and
/// <c>HN-029</c>'s skill prose then instructed agents to call a <c>switch_perspective</c> tool that <b>did not
/// exist</b>. ⚠ Nobody forgot a step; there was simply nothing that could fail.</para>
///
/// <para>⭐⭐ <b>Generation moves the docs next to the routes; THIS is what stops a route shipping without
/// them.</b> Adding an endpoint and not documenting it now fails here, by name. ⇒ the drift class above is
/// structurally impossible rather than merely detectable.</para>
///
/// <para>⭐ Cheap and hermetic: <see cref="DebugApiHost.EnumerateRouteTemplates"/> builds the table without
/// binding a port, booting a world or touching DDS — <c>BuildRoutes</c> only creates closures.</para>
/// </summary>
public sealed class EveryRouteIsDocumentedTests
{
    /// <summary>
    /// 🔴 <b>Every route carries a <see cref="RouteDoc"/>.</b> ⛔ An undocumented route is invisible to
    /// every agent using the MCP surface — it exists, it works, and nothing tells anyone it is there.
    /// </summary>
    [Fact]
    public void EveryRouteCarriesADoc()
    {
        var missing = DebugApiHost.EnumerateRouteTemplates()
            .Where(r => !DebugApiRouteDocs.ByRoute.ContainsKey((r.Method, r.Template)))
            .Select(r => $"{r.Method} {r.Template}")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();

        Assert.True(missing.Length == 0,
            $"{missing.Length} route(s) have no RouteDoc, so the generated tool catalog cannot describe them "
          + $"and no agent will ever discover them:\n  " + string.Join("\n  ", missing)
          + "\n⭐ Add an entry in Hrot.Editor/DebugApi/DebugApiRouteDocs.cs. ⛔ If the endpoint is "
          + "deliberately not an MCP tool, document it anyway and set NotATool: true — that is a reviewed "
          + "decision, not an omission.");
    }

    /// <summary>
    /// ⭐⭐ <b>THE CONTROL IN THE OTHER DIRECTION: no doc for a route that does not exist.</b>
    /// ⛔ Without this the table is an append-only pile, and a renamed or deleted endpoint leaves a doc
    /// behind that the generator would happily turn into a tool pointing at a 404.
    /// 📌 The same inversion as the conformance suite's *"a declared divergence that stopped diverging is
    /// deleted"*.
    /// </summary>
    [Fact]
    public void NoDocDescribesARouteThatIsGone()
    {
        var live = DebugApiHost.EnumerateRouteTemplates()
                              .Select(r => (r.Method, r.Template))
                              .ToHashSet();

        var orphans = DebugApiRouteDocs.ByRoute.Keys
            .Where(k => !live.Contains((k.Method, k.Path)))
            .Select(k => $"{k.Method} {k.Path}")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();

        Assert.True(orphans.Length == 0,
            $"{orphans.Length} RouteDoc(s) describe endpoints the host does not serve, so the generated "
          + $"catalog would advertise tools that 404:\n  " + string.Join("\n  ", orphans)
          + "\n⭐ Delete the entry, or fix its (method, path) key if the route was renamed.");
    }

    /// <summary>
    /// ⭐ Each doc is <b>usable</b>: the fields the generator projects into an MCP tool must not be blank.
    /// ⛔ An empty summary or hint passes the two rails above and still ships a tool an agent cannot read.
    /// ⚠ A <c>NotATool</c> entry is exempt from the tool-name check only — its prose still has to say what
    /// the endpoint is and what to call instead.
    /// </summary>
    [Fact]
    public void EveryDocIsUsable()
    {
        var problems = new List<string>();

        foreach (var (key, doc) in DebugApiRouteDocs.ByRoute.OrderBy(k => k.Key.Path, StringComparer.Ordinal))
        {
            var where = $"{key.Method} {key.Path}";
            if (string.IsNullOrWhiteSpace(doc.Summary)) problems.Add($"{where}: empty Summary");
            if (string.IsNullOrWhiteSpace(doc.Returns)) problems.Add($"{where}: empty Returns");
            if (string.IsNullOrWhiteSpace(doc.Hint))    problems.Add($"{where}: empty Hint");
            if (string.IsNullOrWhiteSpace(doc.Group))   problems.Add($"{where}: empty Group");

            if (!doc.NotATool && string.IsNullOrWhiteSpace(doc.Tool))
                problems.Add($"{where}: empty Tool name (set NotATool if that is deliberate)");

            foreach (var p in doc.Params ?? Array.Empty<RouteParam>())
            {
                if (string.IsNullOrWhiteSpace(p.Name))
                    problems.Add($"{where}: a param has no name");
                if (string.IsNullOrWhiteSpace(p.Description))
                    problems.Add($"{where}: param '{p.Name}' has no description");
            }
        }

        Assert.True(problems.Count == 0,
            $"{problems.Count} route doc problem(s) — each would ship a tool an agent cannot use:\n  "
          + string.Join("\n  ", problems));
    }

    /// <summary>
    /// ⭐⭐ <b>Tool names are unique.</b> ⛔ Two routes claiming one tool name means the generated catalog has
    /// a duplicate and whichever the MCP server registers second silently wins — the caller reaches an
    /// endpoint it did not ask for.
    /// </summary>
    [Fact]
    public void ToolNamesAreUnique()
    {
        var dupes = DebugApiRouteDocs.ByRoute
            .Where(kv => !kv.Value.NotATool)
            .GroupBy(kv => kv.Value.Tool, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} <- [{string.Join(", ", g.Select(kv => kv.Key.Method + " " + kv.Key.Path))}]")
            .ToArray();

        Assert.True(dupes.Length == 0,
            $"{dupes.Length} tool name(s) are claimed by more than one route:\n  " + string.Join("\n  ", dupes));
    }

    /// <summary>
    /// ⭐⭐⭐ <b>CE-1268 / R-1030 — every tool a RouteDoc names is IN THE GENERATED CATALOG.</b> 🔒 User,
    /// <c>2026-10-23</c>: <i>"regenerating of the docs must be part of every ai debug http change, no way as some
    /// time later"</i>. 📐 Measured that day: the ten <c>/director/*</c> routes had RouteDocs and no catalog entry —
    /// the rails above pass while the MCP server still cannot call them, because nothing ran
    /// <c>npm run gen:catalog</c>. ⭐ Hermetic: reads the committed <c>tool-catalog.mjs</c> as text, no node.
    /// 📄 <c>docs/DESIGN_Agent_Shared_View.md</c> AV-11.
    /// </summary>
    [Fact]
    public void EveryToolIsInTheGeneratedCatalog()
    {
        var catalog = Path.Combine(RepoRoot(), "tools", "ai-debug-mcp", "tool-catalog.mjs");
        var names = Regex.Matches(File.ReadAllText(catalog), "^    \"name\": \"([^\"]+)\"", RegexOptions.Multiline)
                         .Select(m => m.Groups[1].Value)
                         .ToHashSet(StringComparer.Ordinal);

        var missing = DebugApiRouteDocs.ByRoute
            .Where(kv => !kv.Value.NotATool && !names.Contains(kv.Value.Tool))
            .Select(kv => $"{kv.Value.Tool} <- {kv.Key.Method} {kv.Key.Path}")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();

        Assert.True(missing.Length == 0,
            $"{missing.Length} tool(s) have a RouteDoc but are missing from tools/ai-debug-mcp/tool-catalog.mjs, "
          + "so no agent can call them:\n  " + string.Join("\n  ", missing)
          + "\n⭐ Regenerate IN THE SAME COMMIT (R-1030): build Hrot.ClusterRunner, then in tools/ai-debug-mcp run "
          + "`npm run gen:catalog && npm run gen:skill`.");
    }

    private static string RepoRoot()
    {
        for (var dir = AppContext.BaseDirectory; dir != null; dir = Path.GetDirectoryName(dir))
            if (File.Exists(Path.Combine(dir, "HROT.sln"))) return dir;
        throw new InvalidOperationException("HROT.sln not found above " + AppContext.BaseDirectory);
    }
}
