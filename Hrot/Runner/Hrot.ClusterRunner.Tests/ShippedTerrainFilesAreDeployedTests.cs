using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Terrain;
using Hrot.ScenarioEditor.Services;
using Xunit;

namespace Hrot.ClusterRunner.Tests;

/// <summary>
/// ⭐ Every file a terrain's <c>terrain.json</c> names must be in the NAS a dev start SEEDS from the repository.
///
/// <para>📌 Measured <c>2026-10-17</c> on a five-process cluster: <c>demo-city</c>'s <c>terrain.json</c> names
/// <c>demo-city.net.xml</c> (AQ93 §11.3), but a <c>Content</c> glob in <c>Hrot.AI.Behaviors.csproj</c> copied only
/// <c>*.json</c>/<c>*.geojson</c>/<c>*.asc</c> ⇒ every node's PrepareLive threw <c>FileNotFoundException</c> and the load never reached
/// <c>OperatingLive</c>. ⭐ <c>CE-4089</c> (R-4097) REWROTE this rail for the new pattern: there is no exe-folder copy and no csproj glob any more —
/// <c>assets/terrain</c> at the repository root is copied WHOLE into the NAS by <c>CuratedScenarios.SeedTreeFrom</c> — so the rail seeds a temp NAS from
/// the source tree and reads it exactly as a node does (<c>TerrainCatalog.ForNode</c>, the NAS's <c>assets/buildings</c>). A file reference is
/// any string value shaped like a relative file name (<c>name.ext</c>), outside <c>$</c>-prefixed keys.</para>
/// </summary>
public sealed class ShippedTerrainFilesAreDeployedTests : IDisposable
{
    private static readonly Regex FileName = new(@"^[\w.\-/]+\.[A-Za-z][A-Za-z0-9]{1,7}$", RegexOptions.Compiled);

    private readonly string _nas = Path.Combine(Path.GetTempPath(), "ce4089-seed-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_nas, recursive: true); } catch (IOException) { /* best effort */ }
    }

    /// <summary>⭐ The NAS after a dev start: the repository's scenarios/, assets/ and recipes/ copied in.</summary>
    private string SeededNas()
    {
        var tree = ContentLayout.TryFindSourceTree();
        Assert.NotNull(tree);
        CuratedScenarios.SeedTreeFrom(tree!, _nas);
        return _nas;
    }

    [Fact]
    public void EveryTerrain_HasEveryFileItNames_InTheSeededNas()
    {
        var catalog = TerrainCatalog.ForNode(localStagingRoot: null, nasRoot: SeededNas());
        var names = catalog.List();
        Assert.NotEmpty(names);   // the rail would prove nothing on an empty NAS

        var missing = new List<string>();
        foreach (var name in names)
        {
            var folder = catalog.ResolveFolder(name)!;
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, TerrainCatalog.DefinitionFileName)));
            foreach (var file in FileReferences(doc.RootElement))
                if (!File.Exists(Path.Combine(folder, file)))
                    missing.Add($"{name}/{file}");
        }

        Assert.True(missing.Count == 0,
            "terrain files named in terrain.json but not in the seeded NAS (is the file outside assets/terrain, or skipped by the seed?): "
          + string.Join(", ", missing));
    }

    /// <summary>
    /// ⭐ UI-1 (HANDOFF_Behaviors_To_Ui_Platoon_Demos) — every terrain's WORLD parses from the seeded NAS, so every building
    /// template it places is there: its own <c>buildings/</c> or the shared <c>assets/buildings/</c> of the NAS
    /// (<c>ContentLayout.BuildingAssets</c> = <c>TerrainAssets.DefaultSharedBuildingsRoot</c>'s tree).
    /// </summary>
    [Fact]
    public void EveryTerrainWorld_LoadsWithItsBuildingTemplates_FromTheSeededNas()
    {
        var nas = SeededNas();
        var shared = ContentLayout.BuildingAssets(nas);
        Assert.True(File.Exists(Path.Combine(shared, "house-2f.building.json")), $"the shared house-2f is not in the seeded NAS at '{shared}'.");

        var catalog = TerrainCatalog.ForNode(localStagingRoot: null, nasRoot: nas);
        var failures = new List<string>();
        int buildings = 0;
        foreach (var name in catalog.List())
        {
            var folder = catalog.ResolveFolder(name)!;
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, TerrainCatalog.DefinitionFileName)));
            if (!doc.RootElement.TryGetProperty("world", out var w) || w.GetString() is not string world) continue;
            try
            {
                var parsed = TerrainWorldParser.Parse(File.ReadAllText(Path.Combine(folder, world)), Path.GetFileName(folder),
                    TerrainAssets.ForFolder(folder, shared));
                buildings += parsed.Buildings.Count;
            }
            catch (Exception ex) { failures.Add($"{name}: {ex.Message}"); }
        }
        Assert.True(failures.Count == 0, "terrain worlds that do not load from the seeded NAS: " + string.Join(" | ", failures));
        Assert.True(buildings > 0, "no terrain placed a building — the rail would prove nothing.");
    }

    /// <summary>
    /// ⭐ The seed is the ONLY delivery (L4): a second seed with the same bytes writes nothing, and the seeded NAS holds the same terrain
    /// files as the repository (so a file a future glob would have missed is carried too).
    /// </summary>
    [Fact]
    public void TheSeedCarriesEveryTerrainFileOfTheRepository()
    {
        var nas = SeededNas();
        var tree = ContentLayout.TryFindSourceTree()!;

        var inRepo = Directory.GetFiles(ContentLayout.TerrainAssets(tree), "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(ContentLayout.TerrainAssets(tree), f)).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var inNas = Directory.GetFiles(ContentLayout.TerrainAssets(nas), "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(ContentLayout.TerrainAssets(nas), f)).OrderBy(x => x, StringComparer.Ordinal).ToList();

        Assert.NotEmpty(inRepo);
        Assert.Equal(inRepo, inNas);
    }

    /// <summary>⭐ The rail can fail: the known SUMO net reference is found by the walk.</summary>
    [Fact]
    public void TheWalk_FindsTheSumoNetReference()
    {
        using var doc = JsonDocument.Parse("""{"$comment":"x.y","world":"a.world.geojson","sumoNet":{"file":"a.net.xml","anchor":[1.0,2.0]}}""");
        Assert.Equal(new[] { "a.world.geojson", "a.net.xml" }, FileReferences(doc.RootElement).ToArray());
    }

    private static IEnumerable<string> FileReferences(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in element.EnumerateObject())
                {
                    if (p.Name.StartsWith('$')) continue;
                    foreach (var f in FileReferences(p.Value)) yield return f;
                }
                break;
            case JsonValueKind.Array:
                foreach (var e in element.EnumerateArray())
                    foreach (var f in FileReferences(e)) yield return f;
                break;
            case JsonValueKind.String:
                var s = element.GetString()!;
                if (FileName.IsMatch(s)) yield return s;
                break;
        }
    }
}
