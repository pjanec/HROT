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
                             allowed: "Hrot/Subsystems/Hrot.SimHost/Systems/GenesisTail.cs"));

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
