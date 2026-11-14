using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Hrot.Editor.DebugApi;
using Xunit;

namespace Hrot.Editor.Tests;

/// <summary>
/// ⭐⭐ CE-4524 / CE-4525 (H-2 / H-3; docs/DESIGN_Debug_Api_Http_Only.md §5 ①) — the agent guide: <c>GET /help</c> names every documented route
/// with its method, path and where each param goes; <c>--help-api</c> is the same text minus availability; it is language-neutral (R-4221);
/// its prose is HTTP-worded (no MCP server, no tool names).
/// </summary>
public sealed class AgentGuideTests
{
    private static string Help() => DebugApiHost.HelpText(_ => false);   // a host that can serve nothing: every classified route marked
    private static string HelpApi() => DebugApiHost.HelpText(null);

    [Fact]
    public void Help_NamesEveryDocumentedRoute_WithMethodAndPath()
    {
        string help = HelpApi();
        var missing = DebugApiHost.EnumerateRouteTemplates()
            .Where(r => DebugApiRouteDocs.ByRoute.ContainsKey((r.Method, r.Template)))
            .Where(r => !help.Contains($"**`{r.Method} {r.Template}`**", StringComparison.Ordinal))
            .Select(r => $"{r.Method} {r.Template}").ToArray();
        Assert.True(missing.Length == 0, "routes missing from /help: " + string.Join(", ", missing));
        Assert.Contains("- `networkId` (path, number, required)", help);   // a path param says so
        Assert.Contains("(query,", help);                                  // a GET's params are query
        Assert.Contains("(body,", help);                                   // a POST's params are body
    }

    [Fact]
    public void HelpApi_IsHelp_MinusAvailability()
    {
        string help = Help();
        Assert.Contains(AgentGuide.NotHereMarker, help);   // the host-less marks really are there to strip
        string stripped = string.Join("\n", help.Split('\n')
            .Where(l => !l.StartsWith("A route this host cannot serve right now", StringComparison.Ordinal))
            .Select(l => { int i = l.IndexOf(AgentGuide.NotHereMarker, StringComparison.Ordinal); return i < 0 ? l : l[..i]; }));
        Assert.Equal(HelpApi(), stripped);
    }

    [Fact]
    public void Help_IsTheSameUnderACzechCulture()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture; string invariant = HelpApi();
            CultureInfo.CurrentCulture = new CultureInfo("cs-CZ"); string czech = HelpApi();
            Assert.Equal(invariant, czech);
        }
        finally { CultureInfo.CurrentCulture = before; }
    }

    [Fact]
    public void TheProse_IsEmbedded_AndHttpWorded()
    {
        var tools = DebugApiRouteDocs.ByRoute.Values.Select(d => d.Tool).ToHashSet(StringComparer.Ordinal);
        foreach (var part in AgentGuide.Parts)
        {
            string text = AgentGuide.ReadPart(part);
            Assert.False(string.IsNullOrWhiteSpace(text), part);
            Assert.DoesNotMatch(new Regex(@"\bMCP\b"), text);
            var named = Regex.Matches(text, @"\b[a-z][a-z0-9]*(?:_[a-z0-9]+)+\b").Select(m => m.Value).Where(tools.Contains).Distinct().ToArray();
            Assert.True(named.Length == 0, $"{part} names tool(s) {string.Join(", ", named)} — write METHOD /path");
        }
    }
}
