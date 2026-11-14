using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Toolkit.Navigation.Recast;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Terrain;
using Hrot.UI.Common.TerrainSave;

namespace Hrot.Editor.TerrainSave;

/// <summary>
/// ⭐ Terrain authoring U-5 (docs/DESIGN_Terrain_Authoring.md §18d / §18d-1) — the editor's <see cref="ITerrainSaveService"/>: the dialog's engine is backend's <see cref="TerrainBake.SaveAs"/> (the saved world = the base's features followed by the overlay's, the
/// source kept inside the terrain, the new scenario returned as a DOM) and <see cref="TerrainUsers.Of"/>. ⭐ This class only ARRANGES: where the source scenario is (the NAS scenarios root), where the new terrain goes (the NAS terrain root), and writes the
/// returned scenario DOM — ⛔ no terrain writing of its own. <see cref="Preview"/> runs the same save into a throw-away folder without the navmesh bake (seconds), so the summary the dialog shows is exactly what the save will make.
/// ⚠ It reads the scenario AS SAVED (<c>scenario.json</c> on the NAS): the dialog says so; <i>Save scenario</i> first when blocks were just placed.
/// </summary>
public sealed class EditorTerrainSaveService : ITerrainSaveService
{
    private readonly Func<string?> _loadedScenario;
    private readonly string _nasRoot;

    /// <param name="loadedScenario">The editor's loaded scenario name, or null.</param>
    /// <param name="nasRoot">The content root (null ⇒ <see cref="ContentLayout.NasRoot"/>); a rail passes a temp tree.</param>
    public EditorTerrainSaveService(Func<string?> loadedScenario, string? nasRoot = null)
    {
        _loadedScenario = loadedScenario ?? throw new ArgumentNullException(nameof(loadedScenario));
        _nasRoot = nasRoot ?? ContentLayout.NasRoot;
    }

    private string ScenariosRoot => ContentLayout.Scenarios(_nasRoot);
    private string TerrainRoot => ContentLayout.TerrainAssets(_nasRoot);
    private IEnumerable<string?> RecipeRoots => new[] { ContentLayout.TerrainRecipeAssets(_nasRoot) };

    /// <summary>The loaded scenario when its folder (through the ONE resolver — a saved scenario or a terrain's staged source, <c>opened/terrain/…</c>) holds a scenario.json.</summary>
    public string? SourceScenarioName => _loadedScenario() is { Length: > 0 } n && File.Exists(Path.Combine(ContentLayout.ResolveScenarioDirectory(_nasRoot, n), ContentLayout.ScenarioFileName)) ? n : null;

    public string? SourceTerrainName
    {
        get
        {
            if (SourceScenarioName is not { } n) return null;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(ContentLayout.ResolveScenarioDirectory(_nasRoot, n), ContentLayout.ScenarioFileName)));
                foreach (var p in doc.RootElement.EnumerateObject())
                    if (p.Name.Equals("header", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.Object)
                        foreach (var h in p.Value.EnumerateObject())
                            if (h.Name.Equals("terrainName", StringComparison.OrdinalIgnoreCase) && h.Value.ValueKind == JsonValueKind.String) return h.Value.GetString();
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
            return null;
        }
    }

    private string? Resolve(string terrainName) => new TerrainCatalog(new[] { TerrainRoot }).ResolveDefinition(terrainName);

    private string SourceDir => ContentLayout.ResolveScenarioDirectory(_nasRoot, SourceScenarioName ?? throw new InvalidOperationException("No scenario is loaded."));

    public TerrainSavePreview Preview(string newName)
    {
        string temp = Path.Combine(Path.GetTempPath(), "tsa-" + Guid.NewGuid().ToString("N")[..10]);
        try
        {
            var r = TerrainBake.SaveAs(SourceDir, Path.Combine(temp, newName), newName, Resolve, RecipeRoots, bakeNavtiles: false, bridgeNet: new Hrot.Traffic.BridgeNetBuilder(),
            reElevateNet: (net, ground) => Hrot.Terrain.InputSet.NetElevation.Apply(net, ground, force: true));   // ⭐ CE-4597 (G-20)
            return Of(r.Summary);
        }
        finally { try { Directory.Delete(temp, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    private static TerrainSavePreview Of(TerrainSaveSummary s)
        => new(s.Blocks.Count, s.Houses, s.Woods, s.Trees, s.WallKm, s.Bridges, s.UnitsNotBaked, s.AreasNotBaked, s.DamagedHousesNotBaked, s.TilesToBake, s.EstimateSeconds);

    public bool Exists(string name) => Directory.Exists(Path.Combine(TerrainRoot, name));

    public IReadOnlyList<string> UsersOf(string name)
        => TerrainUsers.Of(name, new[] { ScenariosRoot }).Select(p => Path.GetRelativePath(ScenariosRoot, p).Replace(Path.DirectorySeparatorChar, '/')).ToArray();

    public TerrainSaveOutcome Save(string newName, bool prebuildMesh, string? scenarioName)
    {
        string? scenarioDir = null;
        if (scenarioName is not null)
        {
            if (scenarioName.IndexOfAny(new[] { '/', '\\' }) >= 0 || scenarioName.Contains("..")) throw new ArgumentException($"'{scenarioName}' is not a scenario name.");
            scenarioDir = Path.Combine(ScenariosRoot, scenarioName);
            if (Directory.Exists(scenarioDir)) throw new IOException($"A scenario called '{scenarioName}' already exists — choose another name.");
        }
        var r = TerrainBake.SaveAs(SourceDir, Path.Combine(TerrainRoot, newName), newName, Resolve, RecipeRoots, prebuildMesh, bridgeNet: new Hrot.Traffic.BridgeNetBuilder(),
            reElevateNet: (net, ground) => Hrot.Terrain.InputSet.NetElevation.Apply(net, ground, force: true));   // ⭐ CE-4597 (G-20)
        if (scenarioDir is not null)
        {
            Directory.CreateDirectory(scenarioDir);
            File.WriteAllText(Path.Combine(scenarioDir, "scenario.json"), r.Scenario.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        return new TerrainSaveOutcome(r.Folder, scenarioName);
    }
}
