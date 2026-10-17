using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Hrot.SimHost.Tests;

/// <summary>
/// ⭐⭐ CE-4013 — the bootstrap pieces every host shares are written ONCE (🔒 user, 2026-10-17: "We have to share and unify, not
/// duplicate"). 📄 docs/DESIGN_Subsystem_Composition_Unification.md §4.1ae.
/// </summary>
/// <remarks>
/// ⚠ A scan, because "there is one implementation" is a claim about the repository no constructed object can be asked. The behaviour
/// half (every hook runs, a skipped one throws) is railed on the constructed bootstrapper in SharedApplicationBootstrapperTests.
/// </remarks>
public class OneBootstrapImplementationRails
{
    /// <summary>No production root loops over its capabilities' hooks itself: <c>NodeCapabilityWalk</c> is the one walk.</summary>
    [Fact]
    public void NoProductionRoot_WalksItsCapabilitiesByHand()
        => Assert.Empty(Scan(new Regex(@"\b(capability|cap|c)\.(PopulateSystems|ProvideModules|Register)\("),
                             allowed: "Hrot/Engine/Hrot.Common/Infrastructure/NodeCapability.cs"));

    /// <summary>No production root builds a terrain residency itself: <c>HrotUniversalLoadParts</c> does (the player's catalogue
    /// residency has no load chain and is the one exception).</summary>
    [Fact]
    public void NoProductionRoot_BuildsItsOwnTerrainResidency()
        => Assert.Empty(Scan(new Regex(@"new\s+(Hrot\.Map\.Common\.Services\.)?TerrainResidency\("),
                             allowed: "Hrot/Engine/Hrot.Core/Services/LoadPhase/HrotUniversalLoadParts.cs",
                             alsoAllowed: "Hrot/Subsystems/Hrot.ReplayBrowser/ReplayBrowserSubsystem.cs"));

    /// <summary>⭐ CE-4013 D6 — no production root schedules the entity-creation pack by hand: <c>EntityCreation.RegisterOn</c> does.</summary>
    [Fact]
    public void NoProductionRoot_SchedulesTheCreationPackByHand()
        => Assert.Empty(Scan(new Regex(@"RegisterGlobalSystem\(\s*creation\.|SingleSystemModule\(\s*""NetworkSpawning"""),
                             allowed: "Hrot/Engine/Hrot.Common/EntityCreation/EntityCreation.cs"));

    /// <summary>⭐ CE-4013 D11 — the genesis tail is composed through <c>GenesisTail.RegisterOn</c> only (IG and traffic had none).</summary>
    [Fact]
    public void NoProductionRoot_ComposesTheGenesisTailByHand()
        => Assert.Empty(Scan(new Regex(@"new\s+(Hrot\.SimHost\.Systems\.)?(GenesisMaterializationSystem|InitialBrainMaterializationSystem)\("),
                             allowed: "Hrot/Engine/Hrot.EcsNode/Systems/GenesisTail.cs"));

    /// <summary>
    /// ⭐⭐ No host project references another host (🔒 user, 2026-10-17: "IG for sure cannot depend on sim host. The host must not
    /// be dependent on each other." — design: .dev/_DONE/modular-2/feedback-3.md, subsystems "must never depend on each other, but only
    /// point inward toward the shared domain"). ⭐ Shared host code lives in Hrot.EcsNode (CE-4015: NodeBootstrapper, the scenario
    /// serializer, the genesis tail — Hrot/Engine/Hrot.EcsNode).
    /// ⚠ A RATCHET: the lateral references that predate the rule are listed as debt (CE-4015). A NEW one fails, and so does a listed
    /// one that is gone — the list may only shrink, and must be shrunk in the commit that removes the reference.
    /// </summary>
    [Fact]
    public void NoHost_ReferencesAnotherHost_BeyondTheRecordedDebt()
    {
        string? root = RepoRoot();
        Assert.NotNull(root);
        var hosts = new Dictionary<string, string>
        {
            ["Hrot.SimHost"]          = "Hrot/Subsystems/Hrot.SimHost/Hrot.SimHost.csproj",
            ["Hrot.IG"]               = "Hrot/Subsystems/Hrot.IG/Hrot.IG.csproj",
            ["Hrot.CGF"]              = "Hrot/Subsystems/Hrot.CGF/Hrot.CGF.csproj",
            ["Hrot.Editor"]           = "Hrot/Subsystems/Hrot.Editor/Hrot.Editor.csproj",
            ["Hrot.TrafficNode"]      = "Hrot/Subsystems/Hrot.TrafficNode/Hrot.TrafficNode.csproj",
            ["Hrot.Orchestrator"]     = "Hrot/Subsystems/Hrot.Orchestrator/Hrot.Orchestrator.csproj",
            ["Hrot.ExCon"]            = "Hrot/Subsystems/Hrot.ExCon/Hrot.ExCon.csproj",
            ["Hrot.ReplayBrowser"]    = "Hrot/Subsystems/Hrot.ReplayBrowser/Hrot.ReplayBrowser.csproj",
            ["Hrot.NodeComposition"]  = "Hrot/Subsystems/Hrot.NodeComposition/Hrot.NodeComposition.csproj",
            ["HrotStrideApp.Game"]    = "Stride/HrotStrideApp.Game/HrotStrideApp.Game.csproj",
        };
        // ⛔ CE-4015 — the debt, as it stood on 2026-10-17. Remove a line in the commit that removes the reference.
        var debt = new HashSet<string>
        {
            "Hrot.CGF -> Hrot.SimHost",
            "Hrot.Editor -> Hrot.SimHost", "Hrot.Editor -> Hrot.CGF", "Hrot.Editor -> Hrot.Orchestrator", "Hrot.Editor -> Hrot.IG",
            "Hrot.ExCon -> Hrot.Orchestrator",
            "Hrot.NodeComposition -> Hrot.SimHost", "Hrot.NodeComposition -> Hrot.IG",
            "Hrot.ReplayBrowser -> Hrot.CGF", "Hrot.ReplayBrowser -> Hrot.SimHost",
            "HrotStrideApp.Game -> Hrot.NodeComposition", "HrotStrideApp.Game -> Hrot.Editor", "HrotStrideApp.Game -> Hrot.Orchestrator",
            "HrotStrideApp.Game -> Hrot.CGF", "HrotStrideApp.Game -> Hrot.SimHost",
        };

        var edges = new HashSet<string>();
        var reference = new Regex(@"<ProjectReference\s+Include=""[^""]*?([A-Za-z.]+)\.csproj""");
        foreach (var (host, rel) in hosts)
        {
            string path = Path.Combine(root!, rel);
            Assert.True(File.Exists(path), $"host project moved: {rel} — update the rail.");
            foreach (Match m in reference.Matches(File.ReadAllText(path)))
                if (hosts.ContainsKey(m.Groups[1].Value)) edges.Add($"{host} -> {m.Groups[1].Value}");
        }

        var added = new List<string>();
        foreach (var e in edges) if (!debt.Contains(e)) added.Add(e);
        var gone = new List<string>();
        foreach (var d in debt) if (!edges.Contains(d)) gone.Add(d);
        Assert.True(added.Count == 0, "a host references another host — move the shared code to Hrot.EcsNode instead: " + string.Join(", ", added));
        Assert.True(gone.Count == 0, "recorded debt no longer exists — remove it from the list (CE-4015): " + string.Join(", ", gone));
    }

    private static List<string> Scan(Regex pattern, string allowed, string? alsoAllowed = null)
    {
        string? root = RepoRoot();
        Assert.NotNull(root);
        var hits = new List<string>();
        foreach (string top in new[] { "Hrot", "Stride" })
        {
            string dir = Path.Combine(root!, top);
            if (!Directory.Exists(dir)) continue;
            foreach (string file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(root!, file).Replace('\\', '/');
                if (rel.Contains("/obj/") || rel.Contains("/bin/") || rel.Contains(".Tests/") || rel == allowed || rel == alsoAllowed) continue;
                int n = 0;
                foreach (string line in File.ReadLines(file))
                {
                    n++;
                    string t = line.TrimStart();
                    if (t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith("*", StringComparison.Ordinal)) continue;
                    if (pattern.IsMatch(line)) hits.Add($"{rel}:{n}: {t}");
                }
            }
        }
        return hits;
    }

    private static string? RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
        {
            if (File.Exists(Path.Combine(dir, "HROT.sln"))) return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return null;
    }
}
