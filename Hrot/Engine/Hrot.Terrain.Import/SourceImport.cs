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

    /// <summary>⭐ CE-5056 — the terrain.json key holding the importer revision that generated it (<see cref="TerrainImporter.Revision"/>; absent = 1).</summary>
    public const string RecordedRevisionKey = "importerRevision";

    /// <summary>⭐ CE-5056 — true when <paramref name="definitionPath"/> was imported from this input by THIS importer revision: the same manifest sha256 and revision.</summary>
    private static bool Current(string definitionPath, string recordedSha, string sha)
        => string.Equals(recordedSha, sha, StringComparison.Ordinal) && RecordedRevision(definitionPath) == TerrainImporter.Revision;

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

    public string? TryImport(string terrainName, string? existingDefinition, Func<string> targetRootOf, IReadOnlyList<string> catalogRoots, CancellationToken cancellation)
    {
        if (!TerrainName.IsValid(terrainName)) return null;   // a sub-folder name cannot be imported (the importer refuses it)
        // the root that holds the terrain (a hit: <root>/<name>/terrain.json — a valid name has no sub-folder), else the import target
        string holder = existingDefinition != null ? Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(existingDefinition)))! : targetRootOf();
        string? set = FindSet(terrainName, holder, catalogRoots);
        if (set == null) return null;
        string manifest = Path.Combine(set, InputSetLayout.Manifest);

        if (existingDefinition != null)
        {
            long stamp = Stamp(existingDefinition), mstamp = Stamp(manifest);
            if (_current.TryGetValue(existingDefinition, out var seen) && seen == (stamp, mstamp)) return null;
            string? recorded = RecordedSha(existingDefinition);
            if (recorded == null) return null;   // not imported from an input: never overwrite it
            if (Current(existingDefinition, recorded, Manifest.Sha256(manifest))) { _current[existingDefinition] = (stamp, mstamp); return null; }
        }

        // ⭐ CE-11061 (unreal-ig) / CE-5060 — asked only now (a write probe): a STALE hit goes to the machine-wide target when the catalog has one, so a stale copy
        //   seeded into a private NAS is replaced ONCE where every NAS is seeded from, not re-imported into the NAS on every run
        string targetRoot = existingDefinition != null ? targetRootOf() : holder;
        if (existingDefinition != null)
            _log?.Invoke($"[TerrainImport] '{terrainName}': the import at {Path.GetDirectoryName(existingDefinition)} is stale (importer revision {RecordedRevision(existingDefinition)}, now {TerrainImporter.Revision}; or its input changed)"
                         + (PathsEqual(holder, targetRoot) ? "" : $" — it is passed over for {Path.Combine(targetRoot, terrainName)}"));
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
                if (recorded == null || Current(definition, recorded, sha)) return definition;
            }
            var sw = Stopwatch.StartNew();
            _log?.Invoke($"[TerrainImport] importing '{terrainName}' from {set} into {targetRoot} (first use{(exists ? ", the input or the importer changed" : "")})");
            var importer = _importerFor(targetRoot);
            string folder = importer.ImportAsync(set, terrainName, Directory.Exists(Path.Combine(targetRoot, terrainName)), new LoadDetailProgress(), cancellation).GetAwaiter().GetResult();
            _log?.Invoke($"[TerrainImport] '{terrainName}' imported in {sw.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)} s");
            return Path.Combine(folder, TerrainCatalog.DefinitionFileName);
        }
    }

    /// <summary>
    /// ⭐ <c>CE-5060</c> (docs/DESIGN_Load_GeoReference_From_Nodes.md §2) — the definition this input set's import WRITES, as far as its origin goes (name,
    /// <c>origin</c>, <c>frame.utm</c> + <c>corner</c> — <see cref="TerrainFolderWriter.OriginOf"/>), read from <c>manifest.json</c> alone: never imports, never
    /// takes the lock. Null when there is no input set, or <paramref name="existingDefinition"/> records no input (a hand-made terrain keeps its own origin).
    /// </summary>
    public TerrainDefinition? TryReadInputDefinition(string terrainName, string? existingDefinition, IReadOnlyList<string> catalogRoots)
    {
        if (!TerrainName.IsValid(terrainName)) return null;
        if (existingDefinition != null && RecordedSha(existingDefinition) == null) return null;   // not imported from an input: its own file is the truth
        string where = existingDefinition != null ? Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(existingDefinition)))!
                                                  : (catalogRoots.Count > 0 ? catalogRoots[0] : Directory.GetCurrentDirectory());
        string? set = FindSet(terrainName, where, catalogRoots);
        if (set == null) return null;
        var m = InputSetReader.Open(set).Manifest;
        var (lat, lon) = TerrainFolderWriter.OriginOf(m.Frame, m.MinX, m.MinY);
        return new TerrainDefinition
        {
            Name = terrainName,
            Origin = new TerrainGeoOrigin(lat, lon, 0.0),
            Utm = new TerrainUtmFrame(m.Frame.Zone, m.Frame.South, m.Frame.E0 + m.MinX, m.Frame.N0 + m.MinY),
        };
    }

    /// <summary>
    /// ⭐ <c>CE-11040</c> (handoff ui → terrain, Stechovice showcase §2 row 4) — a first use runs the whole import INSIDE the node's load (minutes on a large
    /// terrain); each stage's fraction goes to <see cref="Fdp.Core.LoadProgress.ReportDetail"/> (<i>"import navmesh 412/1000"</i>), so the load step's text
    /// moves and the 30 s stall check (CE-11023) sees it advancing — as CE-11038 does for the navmesh bake of a load. Synchronous (no posting to a
    /// context); free when no load runs (no ambient progress, e.g. the CLI or the wizard).
    /// </summary>
    internal sealed class LoadDetailProgress : IProgress<ImportProgress>
    {
        private string? _stage, _what;

        public void Report(ImportProgress p)
        {
            if (!ReferenceEquals(p.Stage, _stage)) { _stage = p.Stage; _what = "import " + p.Stage; }   // once per stage
            Fdp.Core.LoadProgress.ReportDetail(_what!, (long)(Math.Clamp(p.Fraction, 0d, 1d) * 1000d), 1000);
        }
    }

    /// <summary>⭐ CE-5056 — the importer revision a terrain.json records; 1 when none is (an import from before revisions were recorded), 0 when unreadable.</summary>
    public static int RecordedRevision(string definitionPath)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(definitionPath));
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return 0;
            return doc.RootElement.TryGetProperty(RecordedRevisionKey, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int r) ? r : 1;
        }
        catch (Exception e) when (e is IOException or JsonException) { return 0; }
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

    private static bool PathsEqual(string a, string b) => string.Equals(Path.GetFullPath(a).TrimEnd('/', '\\'), Path.GetFullPath(b).TrimEnd('/', '\\'), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static long Stamp(string file) { var i = new FileInfo(file); return i.LastWriteTimeUtc.Ticks ^ (i.Length * 397); }

    /// <summary>F-5 — one lock file per terrain beside the target; a second thread or process waits for it, then finds the folder. (The file stays: deleting a lock file is racy.)</summary>
    private static FileStream AcquireLock(string lockPath, CancellationToken ct)
    {
        for (; ; )
        {
            ct.ThrowIfCancellationRequested();
            try { return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.None); }
            catch (IOException)
            {
                // ⭐ CE-5060 P2 — a node waiting for another node's (or process's) import of the same terrain is not hung: it says so (a detail report advances the load's count)
                Fdp.Core.LoadProgress.ReportDetail("import: waiting for another import", 0, 1);
                Thread.Sleep(50);
            }
        }
    }
}
