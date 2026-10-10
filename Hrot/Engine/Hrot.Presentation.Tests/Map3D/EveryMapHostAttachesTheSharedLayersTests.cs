using System.Text.RegularExpressions;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐⭐⭐ CE-1033 — "not on every host" cannot happen BY CONSTRUCTION. 🔒 User, 2026-10-10: <i>"we should be unifying and sharing
/// from the day zero so something like 'not on all host' can not happen by construction."</i>
/// <para>① every host that builds the map machinery (<c>MapInteractionPack.Build(</c>) also attaches the shared layers
/// (<c>MapInteractionPack.AttachMapLayers(</c>) — which is where the gizmo layer, the 3-D layers and the 2-D ↔ 3-D switch come
/// from; ② nothing outside the pack builds those pieces itself, so a host cannot grow a private variant.</para>
/// 📄 docs/DESIGN_Map_3D_Mode.md §6b.
/// </summary>
public sealed class EveryMapHostAttachesTheSharedLayersTests
{
    private static readonly string[] ProductionTrees = { "Hrot", "Stride" };
    private const string PackFile = "Hrot/Engine/Hrot.Presentation/ScenarioEditor/Map/MapInteractionPack.cs";

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "HROT.sln"))) dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("repo root not found");
    }

    private static IEnumerable<(string Rel, string Text)> ProductionFiles()
    {
        var root = RepoRoot();
        foreach (var tree in ProductionTrees)
        {
            var dir = Path.Combine(root, tree);
            if (!Directory.Exists(dir)) continue;
            foreach (var f in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(root, f).Replace('\\', '/');
                if (rel.Contains("/obj/") || rel.Contains("/bin/") || rel.Contains(".Tests/") || rel.Contains(".Tests.")
                    || rel.Contains("/Examples/")) continue;
                // Code only: a doc comment that NAMES the pack is not a host building a map.
                var code = string.Join('\n', File.ReadLines(f).Where(l => !l.TrimStart().StartsWith("//")));
                yield return (rel, code);
            }
        }
    }

    /// <summary>The subsystem a file belongs to: <c>Hrot/Subsystems/Hrot.SimHost/…</c> ⇒ <c>Hrot/Subsystems/Hrot.SimHost</c>.</summary>
    private static string HostOf(string rel)
    {
        var parts = rel.Split('/');
        return parts.Length >= 3 ? string.Join('/', parts.Take(3)) : rel;
    }

    [Fact]
    public void EveryHostThatBuildsTheMap_AttachesTheSharedLayers()
    {
        var files = ProductionFiles().ToList();
        var builders = files.Where(f => f.Text.Contains("MapInteractionPack.Build(")).Select(f => HostOf(f.Rel)).Distinct().ToList();
        var attachers = files.Where(f => f.Text.Contains("MapInteractionPack.AttachMapLayers(")).Select(f => HostOf(f.Rel)).ToHashSet();

        Assert.True(builders.Count >= 5, "anti-vacuity: expected the five map hosts, found " + string.Join(", ", builders));
        var missing = builders.Where(h => !attachers.Contains(h)).ToList();
        Assert.True(missing.Count == 0, "map hosts that do not attach the shared layers (no 3-D, no shared gizmo layer): "
                                        + string.Join(", ", missing));
    }

    [Theory]
    [InlineData(@"MapInteractionPack\.BuildRenderLayer\(")]
    [InlineData(@"new\s+(Fdp\.Toolkit\.Vis3D\.)?TerrainLayer3D\(")]
    [InlineData(@"new\s+([\w.]+\.)?EntityBodyLayer3D\(")]
    [InlineData(@"new\s+(Fdp\.Toolkit\.Vis3D\.)?MapViewSwitch\(")]
    [InlineData(@"new\s+([\w.]+\.)?MapViewModeLayer\(")]
    public void NoHostBuildsAMapLayerOfItsOwn(string pattern)
    {
        var re = new Regex(pattern);
        var offenders = ProductionFiles().Where(f => f.Rel != PackFile && re.IsMatch(f.Text)).Select(f => f.Rel).ToList();
        Assert.True(offenders.Count == 0, $"built outside MapInteractionPack ({pattern}): " + string.Join(", ", offenders));
    }
}
