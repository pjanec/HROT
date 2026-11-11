using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Terrain;
using Hrot.Editor.AiShared.Browser;
using Hrot.UI.Common.TerrainSave;

namespace Hrot.Editor.TerrainSave;

/// <summary>
/// ⭐ Terrain authoring U-5 / E-2 (docs/DESIGN_Terrain_Authoring.md §14; mockup 4) — <i>Edit terrain…</i>: what a terrain's SOURCE is (read from its <c>terrain.json</c>: <c>source.scenario</c> + <c>source.base.name</c>, E-1), whether it can be opened now, and opening it:
/// the source folder is COPIED to the NAS <c>opened/terrain/&lt;terrain&gt;</c> (<see cref="TerrainSourceNames"/>; the scenario door opens by name only) and opened through the editor's one door. The ground it is built on must still be in the catalogue (the source
/// scenario names it); ⛔ a terrain whose ground is gone cannot be opened until the catalogue resolves <c>&lt;terrain&gt;/source/base</c> (backend: <c>docs/blueprints/batches/NOTE_Ui_To_Backend_Edit_Terrain.md</c>). A terrain with NO source (an import, an older one) is E-3 — greyed with that reason.
/// </summary>
public sealed class EditorTerrainSourceService
{
    public const string NoSourceReason = "This terrain keeps no source scenario (an imported or older terrain); making one over an existing terrain is E-3 — not built yet.";

    private readonly Func<TerrainCatalog> _catalog;
    private readonly string _nasRoot;
    private readonly Action<string> _open;

    /// <param name="catalog">The terrain catalogue (the master's, the NAS); read per call.</param>
    /// <param name="open">Opens the staged scenario by name (the editor's guarded scenario door).</param>
    /// <param name="nasRoot">The content root (null ⇒ <see cref="ContentLayout.NasRoot"/>); a rail passes a temp tree.</param>
    public EditorTerrainSourceService(Func<TerrainCatalog> catalog, Action<string> open, string? nasRoot = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _open = open ?? throw new ArgumentNullException(nameof(open));
        _nasRoot = nasRoot ?? ContentLayout.NasRoot;
    }

    /// <summary>The source of <paramref name="terrain"/> as its <c>terrain.json</c> names it: the scenario folder and the base terrain's name; null when it keeps none.</summary>
    public (string ScenarioDir, string BaseName)? SourceOf(string terrain)
    {
        if (_catalog().ResolveDefinition(terrain) is not { } def) return null;
        try
        {
            if (JsonNode.Parse(File.ReadAllText(def)) is not JsonObject o || o["source"] is not JsonObject src) return null;
            string? rel = (string?)src["scenario"];
            string? baseName = (string?)(src["base"] as JsonObject)?["name"];
            if (string.IsNullOrWhiteSpace(rel) || string.IsNullOrWhiteSpace(baseName)) return null;
            string dir = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(def)!, rel));
            return File.Exists(Path.Combine(dir, ContentLayout.ScenarioFileName)) ? (dir, baseName) : null;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or InvalidCastException) { return null; }
    }

    /// <summary>Why <i>Edit terrain…</i> cannot run on <paramref name="terrain"/> now, or null when it can.</summary>
    public string? EditDisabledReason(string terrain)
    {
        if (SourceOf(terrain) is not { } s) return NoSourceReason;
        if (_catalog().ResolveDefinition(s.BaseName) == null)
            return $"Its ground '{s.BaseName}' is no longer in the catalogue; opening a source over the ground kept in '{terrain}/source/base' needs the catalogue to resolve it (backend — see NOTE_Ui_To_Backend_Edit_Terrain).";
        return null;
    }

    /// <summary>The offer the terrain picker shows for <paramref name="terrain"/>.</summary>
    public TerrainSourceOffer Offer(string terrain) => new(SourceOf(terrain) != null, EditDisabledReason(terrain), () => Edit(terrain));

    /// <summary>Stages the source (a fresh copy each time: what is saved in the terrain is what opens) and opens it; returns the scenario name. Throws when it cannot (the reason of <see cref="EditDisabledReason"/>).</summary>
    public string Edit(string terrain)
    {
        if (EditDisabledReason(terrain) is { } why) throw new InvalidOperationException(why);
        var source = SourceOf(terrain)!.Value;
        string name = TerrainSourceNames.NameOf(terrain);
        string dest = ContentLayout.ResolveScenarioDirectory(_nasRoot, name);
        if (Directory.Exists(dest)) Directory.Delete(dest, true);
        CopyTree(source.ScenarioDir, dest);
        _open(name);
        return name;
    }

    private static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var d in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, d)));
        foreach (var f in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories)) File.Copy(f, Path.Combine(to, Path.GetRelativePath(from, f)), overwrite: true);
    }
}
