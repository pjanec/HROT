using Fdp.Testing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Hrot.Editor.DebugApi;
using Xunit;

namespace Hrot.Editor.Tests;

/// <summary>
/// ⭐⭐⭐ <b>Every route carries its doc, and every doc a live route</b> — the one source <c>GET /capabilities</c> and <c>GET /help</c> render
/// (docs/DESIGN_Debug_Api_Http_Only.md §4; R-4223 supersedes R-1030's regeneration chore: nothing is generated and committed any more).
/// ⛔ HISTORY: this rail first guarded the MCP server's generated <c>tool-catalog.mjs</c> (HN-030), which had drifted from the routes; the
/// server is removed (CE-4526), the guard stays.
/// <para>⭐ Cheap and hermetic: <see cref="DebugApiHost.EnumerateRouteTemplates"/> builds the table without binding a port, booting a world or
/// touching DDS — <c>BuildRoutes</c> only creates closures.</para>
/// </summary>
public sealed class EveryRouteIsDocumentedTests
{
    /// <summary>
    /// 🔴 <b>Every route carries a <see cref="RouteDoc"/>.</b> ⛔ An undocumented route is invisible to
    /// every agent reading GET /help — it exists, it works, and nothing tells anyone it is there.
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
            $"{missing.Length} route(s) have no RouteDoc, so GET /help cannot describe them "
          + $"and no agent will ever discover them:\n  " + string.Join("\n  ", missing)
          + "\n⭐ Add an entry in Hrot.Editor/DebugApi/DebugApiRouteDocs.cs — GET /help renders it.");
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
          + $"guide would advertise routes that 404:\n  " + string.Join("\n  ", orphans)
          + "\n⭐ Delete the entry, or fix its (method, path) key if the route was renamed.");
    }

    /// <summary>
    /// ⭐ Each doc is <b>usable</b>: the fields /help and /capabilities render must not be blank.
    /// ⛔ An empty summary or hint passes the two rails above and still ships a tool an agent cannot read.
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

            if (string.IsNullOrWhiteSpace(doc.Tool))
                problems.Add($"{where}: empty Tool name (the route's short unique id)");

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
    /// ⭐⭐ <b>Tool names (each route's short id) are unique.</b> ⛔ Two routes claiming one id means a reference to it has
    /// an ambiguous target — the caller reaches an
    /// endpoint it did not ask for.
    /// </summary>
    [Fact]
    public void ToolNamesAreUnique()
    {
        var dupes = DebugApiRouteDocs.ByRoute
            .GroupBy(kv => kv.Value.Tool, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} <- [{string.Join(", ", g.Select(kv => kv.Key.Method + " " + kv.Key.Path))}]")
            .ToArray();

        Assert.True(dupes.Length == 0,
            $"{dupes.Length} tool name(s) are claimed by more than one route:\n  " + string.Join("\n  ", dupes));
    }


}
