using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Hrot.UI.Common.Map3D.Director;

/// <summary>A take or a reel read from its file, and what is wrong with it (empty = nothing).</summary>
public sealed record Loaded<T>(T? Value, IReadOnlyList<string> Problems) where T : class
{
    public bool Ok => Value is not null && Problems.Count == 0;
}

/// <summary>⭐ <c>CE-1304</c> P4 — the scenario a store beside a recording belongs to, and that scenario's OWN takes (the scenarios root's store).</summary>
public sealed record ScenarioTakesSource(Func<string?> Scenario, Func<TakeStore?> Store);

/// <summary>
/// ⭐ <c>CE-4038</c> (docs/DESIGN_Film_Director.md §2, §4, D-2) — where takes and reels live: a take BESIDE its scenario
/// (<c>&lt;scenarios&gt;/&lt;scenario&gt;/takes/&lt;take&gt;.take.json</c>, so a variant carries its own and "Save As" copies them), a reel under
/// <c>films/&lt;reel&gt;.reel.json</c>. ⭐ The scenarios root is the editor's working one (the curated scenarios are seeded into it with their
/// <c>takes/</c>, and "Save to git" copies them back).
/// </summary>
public sealed class TakeStore
{
    public const string TakesFolder = "takes";
    public const string TakeSuffix = ".take.json";
    public const string ReelSuffix = ".reel.json";

    /// <summary>Indented, and ⭐ text kept readable (a take is edited by hand: no <c>\u2014</c> for a dash, no <c>\u0027</c> for a quote).</summary>
    //   (A DOM is written, so only the indent and the encoder matter — the registry's variant, R-2005.)
    private static readonly JsonSerializerOptions Indented = Fdp.Core.Serialization.FdpJsonOptionsRegistry.Derive(
        o => o.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, indented: true);

    private readonly Func<string> _scenariosRoot;
    private readonly Func<string?> _filmsRoot;
    private readonly Func<string, string?>? _scenarioHash;

    /// <param name="scenarioHash">⭐ <c>CE-1303</c> P2a (docs/DESIGN_Director_Timeline_Editor.md §11m D8) — where a scenario's hash comes from when
    /// this store holds no scenario file: the Replay Browser's store sits BESIDE A RECORDING (TE-1), whose meta names the scenario's hash
    /// (<c>RecordingMetadata.ScenarioHash</c>, the one rule <c>ScenarioDescription.HashOf</c>). Null, or a null answer: the file under the root.</param>
    /// <param name="scenarioTakes">⭐ <c>CE-1304</c> P4 (docs/DESIGN_Director_Timeline_Editor.md §11s D7; TE-1 "the reverse needs no operation") — a store BESIDE A
    /// RECORDING also shows its scenario's OWN takes (a promoted take plays on every later recording of it): read where this store has no take of that
    /// name; ⛔ never written — a save writes beside the recording (an adjustment on a recording is a recording take until promoted).</param>
    public TakeStore(Func<string> scenariosRoot, Func<string?> filmsRoot, Func<string, string?>? scenarioHash = null, ScenarioTakesSource? scenarioTakes = null)
    {
        _scenariosRoot = scenariosRoot ?? throw new ArgumentNullException(nameof(scenariosRoot));
        _filmsRoot = filmsRoot ?? throw new ArgumentNullException(nameof(filmsRoot));
        _scenarioHash = scenarioHash;
        _scenarioTakes = scenarioTakes;
    }

    private readonly ScenarioTakesSource? _scenarioTakes;

    /// <summary>⭐ CE-1304 — the scenario's own store for <paramref name="scenario"/> when this store reads through to it (the recording's scenario only).</summary>
    private TakeStore? ScenarioStoreFor(string scenario)
        => _scenarioTakes is { } s && s.Scenario() is { } name && string.Equals(name, scenario, StringComparison.Ordinal) && s.Store() is { } st
           && !string.Equals(Path.GetFullPath(st.ScenariosRoot), Path.GetFullPath(ScenariosRoot), StringComparison.Ordinal) ? st : null;

    /// <summary>Whether <paramref name="take"/> of <paramref name="scenario"/> is a file of THIS store (not one read through from the scenario's own takes).</summary>
    public bool HasOwn(string scenario, string take) => File.Exists(TakePath(scenario, take));

    public string ScenariosRoot => _scenariosRoot();
    public string? FilmsRoot => _filmsRoot();

    /// <summary>The repository's <c>films/</c>, found by walking up from the binaries (as the curated scenarios are); null when none.</summary>
    public static string? FindFilmsDirectory()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && dir != null; i++)
        {
            var candidate = Path.Combine(dir, "films");
            if (Directory.Exists(candidate) && Directory.EnumerateFiles(candidate, "*" + ReelSuffix).Any()) return candidate;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return null;
    }

    public string TakePath(string scenario, string take) => Path.Combine(ScenariosRoot, scenario, TakesFolder, take + TakeSuffix);

    /// <summary>The takes of <paramref name="scenario"/> (names, sorted, <c>main</c> first).</summary>
    public IReadOnlyList<string> TakesOf(string scenario)
    {
        var dir = Path.Combine(ScenariosRoot, scenario, TakesFolder);
        var own = Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, "*" + TakeSuffix).Select(f => Path.GetFileName(f)[..^TakeSuffix.Length])
            : Enumerable.Empty<string>();
        var shared = ScenarioStoreFor(scenario)?.TakesOf(scenario) ?? Array.Empty<string>();   // ⭐ CE-1304 — the scenario's own takes too
        return own.Concat(shared).Distinct(StringComparer.Ordinal)
            .OrderBy(n => n == "main" ? 0 : 1).ThenBy(n => n, StringComparer.Ordinal).ToList();
    }

    /// <summary>Every scenario with a take, and its takes (scenario names are paths relative to the root, '/'-separated).</summary>
    public IReadOnlyList<(string Scenario, IReadOnlyList<string> Takes)> AllTakes()
    {
        var root = ScenariosRoot;
        var list = new List<(string, IReadOnlyList<string>)>();
        if (Directory.Exists(root))
            foreach (var dir in Directory.EnumerateDirectories(root, TakesFolder, SearchOption.AllDirectories))
            {
                var scenario = Path.GetRelativePath(root, Path.GetDirectoryName(dir)!).Replace('\\', '/');
                var takes = TakesOf(scenario);
                if (takes.Count > 0) list.Add((scenario, takes));
            }
        // ⭐ CE-1304 — the recording's scenario, when only its own (promoted) takes exist
        if (_scenarioTakes?.Scenario() is { } named && !list.Any(x => x.Item1 == named) && TakesOf(named) is { Count: > 0 } sharedOnly)
            list.Add((named, sharedOnly));
        return list.OrderBy(x => x.Item1, StringComparer.Ordinal).ToList();
    }

    public Loaded<Take> Load(string scenario, string take)
    {
        var path = TakePath(scenario, take);
        if (!File.Exists(path) && ScenarioStoreFor(scenario) is { } shared && shared.HasOwn(scenario, take)) return shared.Load(scenario, take);   // ⭐ CE-1304 (TE-1)
        if (!File.Exists(path)) return new Loaded<Take>(null, new[] { $"no take '{take}' for scenario '{scenario}' ({path})" });
        try
        {
            if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject json)
                return new Loaded<Take>(null, new[] { $"{path} is not a JSON object" });
            var problems = new List<string>();
            return new Loaded<Take>(TakeJson.ParseTake(json, scenario, take, problems), problems);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return new Loaded<Take>(null, new[] { $"{path}: {ex.Message}" });
        }
    }

    /// <summary>Writes the take to its file (creating <c>takes/</c>).</summary>
    public string Save(Take take)
    {
        var path = TakePath(take.Scenario, take.Name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, TakeJson.WriteTake(take).ToJsonString(Indented) + "\n");
        return path;
    }

    public IReadOnlyList<string> ReelNames()
    {
        var root = FilmsRoot;
        if (root == null || !Directory.Exists(root)) return Array.Empty<string>();
        return Directory.EnumerateFiles(root, "*" + ReelSuffix).Select(f => Path.GetFileName(f)[..^ReelSuffix.Length])
            .OrderBy(n => n, StringComparer.Ordinal).ToList();
    }

    public Loaded<Reel> LoadReel(string name)
    {
        var root = FilmsRoot;
        var path = root == null ? null : Path.Combine(root, name + ReelSuffix);
        if (path == null || !File.Exists(path)) return new Loaded<Reel>(null, new[] { $"no reel '{name}' ({root ?? "no films/ folder found"})" });
        try
        {
            if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject json) return new Loaded<Reel>(null, new[] { $"{path} is not a JSON object" });
            var problems = new List<string>();
            return new Loaded<Reel>(TakeJson.ParseReel(json, name, problems), problems);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return new Loaded<Reel>(null, new[] { $"{path}: {ex.Message}" });
        }
    }

    /// <summary>⭐ <c>CE-4042</c> (docs/DESIGN_Scenario_Regression.md §4) — a scenario's own JSON beside it (<c>checks.json</c>, <c>golden.json</c>).</summary>
    public string ScenarioFilePath(string scenario, string file) => Path.Combine(ScenariosRoot, scenario, file);

    /// <summary>The scenarios that carry <paramref name="file"/> beside them (<c>checks.json</c> — the ones that can be verified).</summary>
    public IReadOnlyList<string> ScenariosWith(string file)
    {
        var root = ScenariosRoot;
        if (!Directory.Exists(root)) return Array.Empty<string>();
        return Directory.EnumerateDirectories(root).Where(d => File.Exists(Path.Combine(d, file)))
            .Select(Path.GetFileName).OfType<string>().OrderBy(n => n, StringComparer.Ordinal).ToList();
    }

    /// <summary>A scenario's own JSON file; null with the reason when there is none or it does not parse.</summary>
    public Loaded<JsonObject> LoadScenarioJson(string scenario, string file)
    {
        var path = ScenarioFilePath(scenario, file);
        if (!File.Exists(path)) return new Loaded<JsonObject>(null, new[] { $"no {file} for scenario '{scenario}' ({path})" });
        try
        {
            return JsonNode.Parse(File.ReadAllText(path)) is JsonObject json
                ? new Loaded<JsonObject>(json, Array.Empty<string>())
                : new Loaded<JsonObject>(null, new[] { $"{path} is not a JSON object" });
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return new Loaded<JsonObject>(null, new[] { $"{path}: {ex.Message}" });
        }
    }

    /// <summary>
    /// Writes a scenario's own JSON file to the working root AND, when the scenario is one of the source tree's
    /// (<see cref="Hrot.ScenarioEditor.Services.CuratedScenarios.TryFindSourceScenariosDirectory"/>), to the source tree too — ⛔ never only
    /// the working copy, which the next launch re-seeds over (the take-bake trap, PLAN_Feature_Films §6). Returns every path written.
    /// </summary>
    public IReadOnlyList<string> SaveScenarioJson(string scenario, string file, JsonNode json)
    {
        var text = json.ToJsonString(Indented) + "\n";
        var written = new List<string>();
        var path = ScenarioFilePath(scenario, file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        written.Add(path);
        if (Hrot.ScenarioEditor.Services.CuratedScenarios.TryFindSourceScenariosDirectory() is { } source
            && Directory.Exists(Path.Combine(source, scenario)))
        {
            var src = Path.GetFullPath(Path.Combine(source, scenario, file));
            if (!string.Equals(src, Path.GetFullPath(path), StringComparison.Ordinal))
            {
                File.WriteAllText(src, text);
                written.Add(src);
            }
        }
        return written;
    }

    /// <summary>The SHA-256 of the scenario's file (the scout's bake records it; a changed scenario re-scouts). Empty when there is no file.</summary>
    public string ScenarioHash(string scenario)
    {
        if (_scenarioHash?.Invoke(scenario) is { Length: > 0 } known) return known;   // ⭐ CE-1303 D8 — a recording's
        var path = Path.Combine(ScenariosRoot, scenario, "scenario.json");
        if (!File.Exists(path)) return "";
        return Fdp.Toolkit.Scenario.ScenarioDescription.HashOf(File.ReadAllText(path));   // ⭐ CE-4058 ② — the ONE rule, a recording's meta too
    }

    /// <summary>
    /// ⭐ D-2 — copies <paramref name="from"/>'s takes to <paramref name="to"/> ("Save As" and "new from" keep a scenario's takes). ⛔ Never
    /// over a scenario that has takes of its own. Returns the files copied.
    /// </summary>
    public static int CopyTakes(string scenariosRoot, string? from, string to)
    {
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to) || string.Equals(from, to, StringComparison.Ordinal)) return 0;
        var src = Path.Combine(scenariosRoot, from, TakesFolder);
        var dst = Path.Combine(scenariosRoot, to, TakesFolder);
        if (!Directory.Exists(src) || (Directory.Exists(dst) && Directory.EnumerateFiles(dst).Any())) return 0;
        Directory.CreateDirectory(dst);
        int n = 0;
        foreach (var file in Directory.EnumerateFiles(src, "*" + TakeSuffix))
        {
            File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), overwrite: false);
            n++;
        }
        return n;
    }

    /// <summary>
    /// ⭐ B1 — what in <paramref name="take"/> the loaded world cannot honour: each unit name no unit carries (a <c>"*part"</c> needs at least
    /// one), and each anchor a shot or the end names that the take does not define. Empty = playable.
    /// </summary>
    public static IReadOnlyList<string> Validate(Take take, IShotWorld world)
    {
        var problems = new List<string>();
        var names = world.Names;
        void Check(string who, string where)
        {
            if (who.StartsWith('*'))
            {
                var part = who[1..];
                if (!names.Any(n => n.Contains(part, StringComparison.Ordinal))) problems.Add($"{where}: no unit's name contains '{part}'");
            }
            else if (world.Resolve(who) is null)
            {
                var near = names.Where(n => n.Contains(who, StringComparison.OrdinalIgnoreCase) || who.Contains(n, StringComparison.OrdinalIgnoreCase)
                                            || Distance(n.ToLowerInvariant(), who.ToLowerInvariant()) <= Math.Max(1, who.Length / 4))
                                .OrderBy(n => Distance(n.ToLowerInvariant(), who.ToLowerInvariant())).Take(3).ToList();
                problems.Add($"{where}: no unit named '{who}'" + (near.Count > 0 ? $" (did you mean {string.Join(", ", near.Select(n => $"'{n}'"))}?)" : ""));
            }
        }
        for (int i = 0; i < take.Shots.Count; i++)
        {
            var s = take.Shots[i];
            foreach (var who in s.Camera.Names()) Check(who, $"shot {i + 1}");
            if (s.At.Anchor is { } a && !take.Anchors.ContainsKey(a)) problems.Add($"shot {i + 1}: anchor '{a}' is not defined");
        }
        foreach (var (key, anchor) in take.Anchors)
            foreach (var who in anchor.Names()) Check(who, $"anchor '{key}'");
        if (take.EndAt?.Anchor is { } end && !take.Anchors.ContainsKey(end)) problems.Add($"endAt: anchor '{end}' is not defined");
        return problems;
    }

    /// <summary>The edit distance of two names (a misspelt name's neighbours).</summary>
    private static int Distance(string a, string b)
    {
        var row = Enumerable.Range(0, b.Length + 1).ToArray();
        for (int i = 1; i <= a.Length; i++)
        {
            int prev = row[0];
            row[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cur = row[j];
                row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), prev + (a[i - 1] == b[j - 1] ? 0 : 1));
                prev = cur;
            }
        }
        return row[b.Length];
    }
}
