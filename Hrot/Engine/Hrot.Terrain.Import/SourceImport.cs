using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Fdp.Toolkit.Terrain;
using Hrot.Terrain.InputSet;

namespace Hrot.Terrain.Import;

/// <summary>
/// ⭐ CE-11037 (docs/DESIGN_Terrain_Import_UTM.md §13) — the terrain catalog's import-on-first-use hook: finds the committed input set of a terrain NAME
/// (<c>assets/terrain-input/&lt;name&gt;/</c>, beside every catalog root's <c>terrain</c> folder and in the repository), takes a machine-wide lock file beside
/// the target, and runs <see cref="TerrainImporter"/> into it. F-3 (revised, user 2026-11-22): the import PREFILLS the navmesh (<see cref="DefaultImporter"/>). F-4: the folder's
/// <c>terrain.json</c> records the sha256 of the input's <c>manifest.json</c>; a differing manifest re-imports (the importer's <c>versions/</c> swap).
/// ⭐ A terrain folder with NO recorded sha (hand-made, or imported before CE-11037) is never touched. Registered once per process
/// (<see cref="TerrainSources.Import"/>) where an executable starts.
/// </summary>
public sealed class SourceImport : ITerrainSourceImport
{
    /// <summary>The terrain.json key holding the input manifest's sha256.</summary>
    public const string RecordedShaKey = "inputManifestSha256";

    /// <summary>The folder (beside a root's <c>terrain/</c>, and under the repository's <c>assets/</c>) holding input sets by name.</summary>
    public const string InputDirectoryName = "terrain-input";

    private readonly Func<string, TerrainImporter> _importerFor;
    private readonly Action<string>? _log;
    private readonly string? _repoRoot;
    // up-to-date verdicts: definition path → (its stamp, the manifest's stamp) when last found current — a resolve of a current terrain costs two stats
    private readonly ConcurrentDictionary<string, (long Def, long Manifest)> _current = new();

    /// <param name="importerFor">the importer for a target root (tests count imports through it); default <c>new TerrainImporter(root)</c></param>
    /// <param name="log">one line at the start and end of an import (a first load waits minutes); default the console</param>
    /// <param name="repoRoot">the repository whose <c>assets/terrain-input</c> is searched last; default found from the working directory (null in a deployment)</param>
    public SourceImport(Func<string, TerrainImporter>? importerFor = null, Action<string>? log = null, string? repoRoot = null)
    {
        _importerFor = importerFor ?? DefaultImporter;
        _log = log ?? Console.WriteLine;
        _repoRoot = repoRoot ?? Netconvert.RepoRoot();
    }

    /// <summary>⭐ F-3 (user, 2026-11-22) — the importer a first use runs: the whole navmesh is prefilled into <c>navtiles/</c> INSIDE the import (the same lock, the same cache), so the first load bakes nothing.</summary>
    public static TerrainImporter DefaultImporter(string targetRoot) => new(targetRoot) { NavmeshPrefill = new NavmeshPrefill() };

    public string? TryImport(string terrainName, string? existingDefinition, string targetRoot, IReadOnlyList<string> catalogRoots, CancellationToken cancellation)
    {
        if (!TerrainName.IsValid(terrainName)) return null;   // a sub-folder name cannot be imported (the importer refuses it)
        string? set = FindSet(terrainName, targetRoot, catalogRoots);
        if (set == null) return null;
        string manifest = Path.Combine(set, InputSetLayout.Manifest);

        if (existingDefinition != null)
        {
            long stamp = Stamp(existingDefinition), mstamp = Stamp(manifest);
            if (_current.TryGetValue(existingDefinition, out var seen) && seen == (stamp, mstamp)) return null;
            string? recorded = RecordedSha(existingDefinition);
            if (recorded == null) return null;   // not imported from an input: never overwrite it
            if (string.Equals(recorded, Manifest.Sha256(manifest), StringComparison.Ordinal)) { _current[existingDefinition] = (stamp, mstamp); return null; }
        }

        Directory.CreateDirectory(targetRoot);
        using (AcquireLock(Path.Combine(targetRoot, "." + terrainName + ".import.lock"), cancellation))
        {
            // inside the lock: another thread or process may have imported it while this one waited
            string definition = Path.Combine(targetRoot, terrainName, TerrainCatalog.DefinitionFileName);
            string sha = Manifest.Sha256(manifest);
            bool exists = File.Exists(definition);
            if (exists)
            {
                string? recorded = RecordedSha(definition);
                if (recorded == null || string.Equals(recorded, sha, StringComparison.Ordinal)) return definition;
            }
            var sw = Stopwatch.StartNew();
            _log?.Invoke($"[TerrainImport] importing '{terrainName}' from {set} into {targetRoot} (first use{(exists ? ", the input changed" : "")})");
            var importer = _importerFor(targetRoot);
            string folder = importer.ImportAsync(set, terrainName, Directory.Exists(Path.Combine(targetRoot, terrainName)), null, cancellation).GetAwaiter().GetResult();
            _log?.Invoke($"[TerrainImport] '{terrainName}' imported in {sw.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)} s");
            return Path.Combine(folder, TerrainCatalog.DefinitionFileName);
        }
    }

    /// <summary>The sha256 a terrain.json records for its input, or null (none recorded, unreadable).</summary>
    public static string? RecordedSha(string definitionPath)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(definitionPath));
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty(RecordedShaKey, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        }
        catch (Exception e) when (e is IOException or JsonException) { return null; }
    }

    /// <summary>F-6 — the input set of <paramref name="name"/>: <c>&lt;root&gt;/../terrain-input/&lt;name&gt;/</c> for the target and every catalog root, then the repository's. Null when none holds a manifest.</summary>
    private string? FindSet(string name, string targetRoot, IReadOnlyList<string> catalogRoots)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in new[] { targetRoot }.Concat(catalogRoots))
        {
            string? parent = Path.GetDirectoryName(Path.GetFullPath(root.TrimEnd('/', '\\')));
            if (parent != null) { string c = Path.Combine(parent, InputDirectoryName, name); if (seen.Add(c) && File.Exists(Path.Combine(c, InputSetLayout.Manifest))) return c; }
        }
        if (_repoRoot != null)
        {
            string c = Path.Combine(_repoRoot, "assets", InputDirectoryName, name);
            if (File.Exists(Path.Combine(c, InputSetLayout.Manifest))) return c;
        }
        return null;
    }

    private static long Stamp(string file) { var i = new FileInfo(file); return i.LastWriteTimeUtc.Ticks ^ (i.Length * 397); }

    /// <summary>F-5 — one lock file per terrain beside the target; a second thread or process waits for it, then finds the folder. (The file stays: deleting a lock file is racy.)</summary>
    private static FileStream AcquireLock(string lockPath, CancellationToken ct)
    {
        for (; ; )
        {
            ct.ThrowIfCancellationRequested();
            try { return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.None); }
            catch (IOException) { Thread.Sleep(50); }
        }
    }
}
