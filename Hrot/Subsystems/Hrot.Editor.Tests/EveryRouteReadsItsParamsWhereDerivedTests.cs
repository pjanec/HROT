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
/// ⭐⭐ CE-4523 (H-1; docs/DESIGN_Debug_Api_Http_Only.md §4) — a route's parameter LOCATION is derived, never authored: a <c>{name}</c> in the
/// template ⇒ path; otherwise GET / DELETE ⇒ query, POST / PUT / PATCH ⇒ body. ⭐ So <c>/help</c> can say where every key goes without a
/// word written per route — and this rail keeps that true: it reads every route handler in <c>DebugApiHost*.cs</c> and fails when one reads
/// a key from another place than derived, reads a path value its template does not name, or reads a key its <see cref="RouteDoc"/> does not
/// document.
/// <para>⭐ A SOURCE scan, not a recording context (as-built deviation, design §4): it sees EVERY branch of every handler without a world — a
/// recorded call sees one branch and stops at the first throw. ⚠ Its reach is the handler's own <c>ctx.Query("k")</c> /
/// <c>ctx.Body?["k"]</c> / <c>ctx.RouteValue("k")</c>; a key read deeper (the body handed to a service method) is that method's, and is
/// not checked here.</para>
/// <para>🔴 Red-proof: the invoke route's <c>scope</c> read from the query again ⇒ red (it was, until CE-4523).</para>
/// </summary>
public sealed class EveryRouteReadsItsParamsWhereDerivedTests
{
    private static readonly string[] Sources =
    {
        "Hrot/Subsystems/Hrot.Editor/DebugApi/DebugApiHost.cs",
        "Hrot/Subsystems/Hrot.Editor/DebugApi/DebugApiHost.Director.cs",
    };

    internal sealed record Reads(string Method, string Template, HashSet<string> Query, HashSet<string> Body, HashSet<string> Path);

    private static readonly Regex RouteStart = new(@"_routes\.Add\(new\(""([A-Z]+)"",\s*""([^""]+)""", RegexOptions.Compiled);
    private static readonly Regex QueryRead = new(@"ctx\.Query\(""([^""]+)""\)", RegexOptions.Compiled);
    private static readonly Regex BodyRead = new(@"ctx\.Body\??\[""([^""]+)""\]", RegexOptions.Compiled);
    private static readonly Regex PathRead = new(@"ctx\.RouteValue\(""([^""]+)""\)", RegexOptions.Compiled);
    private static readonly Regex TemplateParam = new(@"\{(\w+)\}", RegexOptions.Compiled);

    /// <summary>Every <c>_routes.Add(new("M", "/t", handler))</c> in <paramref name="source"/> with the keys its handler reads.</summary>
    internal static List<Reads> Scan(string source)
    {
        var all = new List<Reads>();
        foreach (Match m in RouteStart.Matches(source))
        {
            int open = source.IndexOf('(', m.Index);
            string body = source.Substring(open, End(source, open) - open);
            all.Add(new Reads(m.Groups[1].Value, m.Groups[2].Value, Keys(QueryRead, body), Keys(BodyRead, body), Keys(PathRead, body)));
        }
        return all;
    }

    private static HashSet<string> Keys(Regex r, string text) => r.Matches(text).Select(x => x.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

    /// <summary>The index of the parenthesis closing the one at <paramref name="open"/> — strings, chars and line comments skipped.</summary>
    private static int End(string s, int open)
    {
        int depth = 0;
        for (int i = open; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '/') { i = s.IndexOf('\n', i); if (i < 0) return s.Length; continue; }
            if (c == '"' || c == '\'')
            {
                bool verbatim = c == '"' && i > 0 && s[i - 1] == '@';
                for (i++; i < s.Length && s[i] != c; i++)
                    if (!verbatim && s[i] == '\\') i++;
                continue;
            }
            if (c == '(') depth++;
            else if (c == ')' && --depth == 0) return i;
        }
        throw new InvalidOperationException("unbalanced route registration at " + open);
    }

    /// <summary>The findings for one route: wrong place, a path value not in the template, an undocumented key.</summary>
    internal static IEnumerable<string> Findings(Reads r, RouteDoc? doc)
    {
        bool readsQuery = ParamLocations.ForMethod(r.Method) == ParamLocations.Query;   // the ONE derivation (AgentGuide renders it too)
        var path = TemplateParam.Matches(r.Template).Select(x => x.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        string at = $"{r.Method} {r.Template}";
        if (readsQuery && r.Body.Count > 0) yield return $"{at}: reads body key(s) {string.Join(", ", r.Body)} — a {r.Method} takes the query";
        if (!readsQuery && r.Query.Count > 0) yield return $"{at}: reads query key(s) {string.Join(", ", r.Query)} — a {r.Method} takes the body";
        foreach (var k in r.Path.Where(k => !path.Contains(k))) yield return $"{at}: reads path value '{k}' its template does not name";
        if (doc is null) yield break;   // EveryRouteIsDocumentedTests names it
        var documented = (doc.Params ?? Array.Empty<RouteParam>()).Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var k in r.Query.Concat(r.Body).Concat(r.Path).Distinct().Where(k => !documented.Contains(k)))
            yield return $"{at}: reads '{k}', which its RouteDoc does not document";
    }

    [Fact]
    public void EveryHandler_ReadsEachKeyWhereTheRouteSays_AndOnlyDocumentedKeys()
    {
        var routes = Sources.SelectMany(f => Scan(File.ReadAllText(Path.Combine(TestRepo.Root, f)))).ToList();
        var live = DebugApiHost.EnumerateRouteTemplates().Select(r => (r.Method, r.Template)).ToHashSet();
        Assert.Equal(live.Count, routes.Select(r => (r.Method, r.Template)).Distinct().Count(k => live.Contains(k)));   // the scan saw every route

        var findings = routes
            .SelectMany(r => Findings(r, DebugApiRouteDocs.ByRoute.TryGetValue((r.Method, r.Template), out var d) ? d : null))
            .OrderBy(s => s, StringComparer.Ordinal).ToArray();
        Assert.True(findings.Length == 0,
            $"{findings.Length} parameter location finding(s) — a path value is {{name}} in the template, GET / DELETE read the query, "
          + "POST / PUT / PATCH the body (DESIGN_Debug_Api_Http_Only.md §4):\n  " + string.Join("\n  ", findings));
    }

    /// <summary>🔴 The rail's own control: a POST that reads its query, a GET that reads its body, an undocumented key — each is found.</summary>
    [Fact]
    public void TheScan_FindsAQueryReadOnAPost_ABodyReadOnAGet_AndAnUndocumentedKey()
    {
        const string src = """
            _routes.Add(new("POST", "/a/{id}", ctx => Run(s => s.Do(ctx.RouteValue("id"), ctx.Query("scope"), ")"))));
            _routes.Add(new("GET", "/b", ctx => Run(s => s.Get(ctx.Body?["k"]))));
            """;
        var scan = Scan(src);
        Assert.Equal(2, scan.Count);
        var doc = new RouteDoc("t", "g", "s", "r", "h", new[] { new RouteParam("id", "string", true, "x") });
        Assert.Contains(Findings(scan[0], doc), f => f.Contains("query key(s) scope"));
        Assert.Contains(Findings(scan[0], doc), f => f.Contains("'scope', which its RouteDoc does not document"));
        Assert.Contains(Findings(scan[1], null), f => f.Contains("body key(s) k"));
    }
}
