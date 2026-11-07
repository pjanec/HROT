using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Transforms;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Terrain;

namespace Hrot.Tools.ContentMigrate;

/// <summary>
/// ⭐ S0 item 9 (<c>docs/DESIGN_Scenario_Rules.md</c> §9.8.11; handoff row 9; CE-4153 S10 "no backward compatibility") — moves authored
/// behaviour params to the S0 types, ONCE, in the files themselves. Idempotent: a second run changes nothing.
/// <para>
/// ⭐ Driven by the ENGINE'S type plan, not by patterns: each params blob's contract comes from the behaviour registry every host builds
/// (<see cref="Hrot.CGF.Configuration.CgfBehaviorSetup.LoadFromAiAssembly"/>), and the places in it are the <see cref="PointLink"/> leaves
/// the one walker finds (<see cref="EntityRefRemap.VisitLeaves"/>) — so a <c>"Location"</c> in a behaviour whose contract has no place is never
/// touched (the Python pass of <c>DESIGN_Scenario_Entity_Keys.md</c> §0a ③ had two false hits of exactly that kind).
/// </para>
/// <para>What moves (the item-5 retypes whose JSON changed; every other retype kept its JSON — a number is an <see cref="EntityLink"/>, a
/// <c>[x, y, z]</c> a <see cref="PointLink"/>, a number a <see cref="Fdp.Core.Bearing"/>):</para>
/// <list type="bullet">
/// <item>a <see cref="PointLink"/> leaf holding the retired <c>PickableGeoPoint</c> form <c>[lat, lon]</c> → <c>[x, y, 0]</c> local metres through
///   the scenario terrain's geo origin (<see cref="WGS84Transform"/>, as <c>OrderGeo.ToLocal</c> did); a pair that is not a valid latitude /
///   longitude is the old no-transform fallback (<c>x = second, y = first</c>), also as <c>OrderGeo.ToLocal</c> did;</item>
/// <item><c>MoveToLocation</c>: <c>targetLat</c> / <c>targetLon</c> (or the local <c>x</c> / <c>y</c> fallback) → <c>target</c>;</item>
/// <item><c>DefendArea</c>: <c>CenterLat</c> / <c>CenterLon</c> → <c>Center</c>.</item>
/// </list>
/// Usage: <c>hrot-content-migrate [--check] [--repo &lt;root&gt;] [path …]</c> (default: <see cref="ParamsMigrator.ContentRoots"/> — the
/// scenarios and the recipes; <c>test-data/scenario-corpus</c> holds no behaviour params and is versioned format data, so it is not a default).
/// <c>--check</c> writes nothing and exits 1 when anything would move. Every run also replays each blob through the scenario LOAD's reader
/// (<see cref="ParamsMigrator.LoadRefusal"/>) and exits 1 on any blob that would still fail to load.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        bool check = args.Contains("--check");
        string? repo = null;
        var paths = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--check") continue;
            if (args[i] == "--repo") { repo = args[++i]; continue; }
            paths.Add(args[i]);
        }
        repo ??= ContentLayout.TryFindSourceTree() ?? Directory.GetCurrentDirectory();
        if (paths.Count == 0) paths.AddRange(ParamsMigrator.ContentRoots(repo));

        var migrator = ParamsMigrator.ForRepository(repo);

        int files = 0, changedFiles = 0, blobs = 0, refused = 0;
        {
            foreach (var file in ParamsMigrator.JsonFiles(paths.Select(p => Path.IsPathRooted(p) ? p : Path.Combine(repo, p))))
            {
                files++;
                var r = migrator.MigrateFile(file, write: !check);
                foreach (var x in r.Refusals) Console.WriteLine($"{Path.GetRelativePath(repo, file)}: LOAD WOULD FAIL — {x}");
                refused += r.Refusals.Count;
                if (r.Changes.Count == 0) continue;
                changedFiles++; blobs += r.Changes.Count;
                foreach (var c in r.Changes) Console.WriteLine($"{Path.GetRelativePath(repo, file)}: {c}");
            }
        }
        Console.WriteLine($"{(check ? "would migrate" : "migrated")} {blobs} params blob(s) in {changedFiles} of {files} file(s); {refused} would still fail to load.");
        return refused > 0 || (check && blobs > 0) ? 1 : 0;
    }
}

/// <summary>One file's outcome: a line per migrated params blob.</summary>
public sealed record FileResult(List<string> Changes, List<string> Refusals);

public sealed class ParamsMigrator
{
    private static readonly string[] ParamsKeys = { "Params", "behaviorParams", "BehaviorParams" };
    private static readonly string[] NameKeys = { "Name", "behaviorName", "BehaviorName" };

    private readonly BehaviorRegistry _registry;
    private readonly TerrainCatalog _terrains;

    public ParamsMigrator(BehaviorRegistry registry, TerrainCatalog terrains) { _registry = registry; _terrains = terrains; }

    /// <summary>The migrator every host's behaviour registry implies, with <paramref name="repo"/>'s terrains (their geo origins).</summary>
    public static ParamsMigrator ForRepository(string repo)
    {
        var registry = new BehaviorRegistry();
        Hrot.CGF.Configuration.CgfBehaviorSetup.LoadFromAiAssembly(registry);
        return new ParamsMigrator(registry, new TerrainCatalog(new[] { ContentLayout.TerrainAssets(repo) }));
    }

    /// <summary>The authored content that carries behaviour params: the scenarios and the recipes (paths through <see cref="ContentLayout"/>).</summary>
    public static IReadOnlyList<string> ContentRoots(string repo) => new[] { ContentLayout.Scenarios(repo), ContentLayout.Recipes(repo) };

    /// <summary>Every <c>*.json</c> under <paramref name="roots"/> (a file stands for itself), in ordinal order.</summary>
    public static IEnumerable<string> JsonFiles(IEnumerable<string> roots)
        => roots.SelectMany(r => File.Exists(r) ? new[] { r }
                               : Directory.Exists(r) ? Directory.EnumerateFiles(r, "*.json", SearchOption.AllDirectories) : Array.Empty<string>())
                .OrderBy(f => f, StringComparer.Ordinal);

    /// <summary>Migrates every behaviour params string in <paramref name="path"/>; the bytes outside the migrated strings are kept as they are.</summary>
    public FileResult MigrateFile(string path, bool write)
    {
        var result = new FileResult(new List<string>(), new List<string>());
        byte[] bytes = File.ReadAllBytes(path);
        var sites = ParamsSites(bytes);
        var ids = NetworkIdsToKeys(bytes);   // S9': this file's entity network id -> its file key (the GUID)
        var holds = ids.Count == 0 ? new List<(int Start, int Length, long Value)>() : HoldUnitSites(bytes);

        WGS84Transform? geo = null;
        string? terrain = TerrainName(bytes);
        var edits = new List<(int Start, int Length, byte[] Text)>();
        foreach (var site in sites)
        {
            string? migrated = MigrateParams(site.Behavior, site.Value, () => geo ??= GeoFor(terrain, path), out string? what, ids);
            if (LoadRefusal(site.Behavior, migrated ?? site.Value) is { } refusal) result.Refusals.Add($"{site.Behavior}: {refusal}");
            if (migrated == null) continue;
            string raw = Encoding.UTF8.GetString(bytes, site.Start, site.Length);
            edits.Add((site.Start, site.Length, Encoding.UTF8.GetBytes(EncodeLike(raw, migrated))));
            result.Changes.Add($"{site.Behavior}: {what}");
        }
        // S9' — a numeric RoutePlan waypoint HoldUnit that is some entity's network id in this file becomes that entity's GUID (the Exact form)
        foreach (var h in holds)
            if (ids.TryGetValue(h.Value, out var key))
            {
                edits.Add((h.Start, h.Length, Encoding.UTF8.GetBytes("\"" + key + "\"")));
                result.Changes.Add($"HoldUnit {h.Value} -> {key}");
            }

        byte[] output = bytes;
        if (edits.Count > 0)
        {
            using var ms = new MemoryStream(bytes.Length + 256);
            int at = 0;
            foreach (var e in edits.OrderBy(e => e.Start))
            {
                ms.Write(bytes, at, e.Start - at);
                ms.Write(e.Text);
                at = e.Start + e.Length;
            }
            ms.Write(bytes, at, bytes.Length - at);
            output = ms.ToArray();
        }
        // S8 - a scenario file carries no network id: every entity's NetworkIdentity is removed (after the references that used it became GUIDs above)
        var stripped = StripNetworkIdentity(output, out int removed);
        if (removed > 0) { output = stripped; result.Changes.Add($"NetworkIdentity removed from {removed} entit{(removed == 1 ? "y" : "ies")}"); }
        if (write && !ReferenceEquals(output, bytes)) File.WriteAllBytes(path, output);
        return result;
    }

    /// <summary>The migrated params JSON, or null when nothing in it is in a retired form (or the behaviour / JSON is unknown).</summary>
    public string? MigrateParams(string behavior, string? json, Func<IGeographicTransform> geo, out string? what,
                               IReadOnlyDictionary<long, string>? ids = null)
    {
        what = null;
        if (string.IsNullOrWhiteSpace(json)) return null;
        if (!_registry.TryGetId(behavior, out int id) || !_registry.TryGetDefinition(id, out var def) || def.JsonParamsDtoType is not { } contract)
            return null;
        JsonObject? obj;
        try { obj = JsonNode.Parse(json) as JsonObject; }
        catch (JsonException) { return null; }
        if (obj == null) return null;

        var moved = new List<string>();
        EntityRefRemap.VisitLeaves(contract, obj,
            (leafType, node, key) =>
            {
                // S9' — a NUMERIC EntityLink naming an entity of this file is written as that entity's GUID (the Exact form)
                if (leafType == typeof(EntityLink) && ids != null && node is JsonValue jv && jv.TryGetValue<long>(out long n) && n != 0 && ids.TryGetValue(n, out var guid))
                {
                    moved.Add($"{key} {n} -> {guid}");
                    return JsonValue.Create(guid);
                }
                if (leafType != typeof(PointLink) || node is not JsonArray a || a.Count != 2) return null;
                moved.Add($"{key} [lat, lon] -> local");
                return Point(GeoPair(a[0]!.GetValue<double>(), a[1]!.GetValue<double>(), geo));
            },
            (type, o, path) => Retired(type, o, geo, moved, path));   // renamed keys, at any depth (a blueprint nests MoveToLocation params)
        if (moved.Count == 0) return null;
        what = string.Join(", ", moved);
        return obj.ToJsonString(new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    /// <summary>⭐ The blob as the scenario LOAD reads it — <see cref="RefResolve"/>, the applier that refuses old forms (CE-4153 S10) — with a
    /// scope that answers every reference (this checks FORMS, not keys): the refusal, or null when it loads.</summary>
    public string? LoadRefusal(string behavior, string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        if (!_registry.TryGetId(behavior, out int id) || !_registry.TryGetDefinition(id, out var def) || def.JsonParamsDtoType is not { } contract)
            return null;
        try { EntityRefRemap.WalkJson(contract, json, new RefResolve(AnyScope.Instance)); return null; }
        catch (JsonException ex) { return ex.Message; }
    }

    private sealed class AnyScope : IRefScope
    {
        public static readonly AnyScope Instance = new();
        public bool TryResolve(in Fdp.Core.RefSpec spec, out EntityRef entity) { entity = new EntityRef(1); return true; }
        public bool TryPositionOf(EntityRef entity, out Fdp.Core.WorldPoint position) { position = default; return true; }
        public string Describe => "content check";
    }

    /// <summary>The item-5 retypes that RENAMED keys — found by the contract TYPE, never by a key alone.</summary>
    private static void Retired(Type contract, JsonObject obj, Func<IGeographicTransform> geo, List<string> moved, string path)
    {
        if (contract == typeof(Hrot.Map.Definitions.Behavior.MoveToLocationParamsJsonDto))
        {
            double lat = Num(obj, "targetLat"), lon = Num(obj, "targetLon");
            bool any = Has(obj, "targetLat") || Has(obj, "targetLon") || Has(obj, "x") || Has(obj, "y");
            if (!any) return;
            // the old resolver: a non-zero lat/lon pair through the geo transform, else the local x / y
            var at = (lat != 0 || lon != 0) ? GeoPair(lat, lon, geo) : (Num(obj, "x"), Num(obj, "y"));
            foreach (var k in new[] { "targetLat", "targetLon", "x", "y" }) Remove(obj, k);
            if (!Has(obj, "target")) obj["target"] = Point(at);
            moved.Add($"{At(path)}targetLat/targetLon|x/y -> target");
        }
        else if (contract == typeof(Hrot.Map.Definitions.Behavior.Intents.DefendAreaIntentDto))
        {
            if (!Has(obj, "CenterLat") && !Has(obj, "CenterLon")) return;
            var at = GeoPair(Num(obj, "CenterLat"), Num(obj, "CenterLon"), geo);
            Remove(obj, "CenterLat"); Remove(obj, "CenterLon");
            if (!Has(obj, "Center")) obj["Center"] = Point(at);
            moved.Add($"{At(path)}CenterLat/CenterLon -> Center");
        }
    }

    /// <summary>A retired <c>[lat, lon]</c> pair as local metres — exactly <c>OrderGeo.ToLocal</c>: through the transform, or (not a valid
    /// latitude / longitude — the editor's no-transform fallback wrote local metres swapped) <c>x = second, y = first</c>.</summary>
    private static (double X, double Y) GeoPair(double first, double second, Func<IGeographicTransform> geo)
    {
        if (Math.Abs(first) > 90 || Math.Abs(second) > 180) return (second, first);
        var v = geo().ToCartesian(first, second, 0.0);
        return (v.X, v.Y);
    }

    private static JsonArray Point((double X, double Y) p) => new(JsonValue.Create(Math.Round(p.X, 2)), JsonValue.Create(Math.Round(p.Y, 2)), JsonValue.Create(0));

    private WGS84Transform GeoFor(string? terrain, string path)
    {
        var origin = terrain != null ? _terrains.ReadDefinition(terrain)?.Origin : null;
        if (origin is not { } o)
            throw new InvalidOperationException($"{path}: a [lat, lon] value needs the terrain's geo origin, and terrain '{terrain ?? "(none)"}' declares none.");
        return new WGS84Transform(o.Lat, o.Lon, o.Alt);
    }

    private static string At(string path) => path.Length == 0 ? "" : path + ".";

    private static bool Has(JsonObject o, string k) => o.Any(p => p.Key.Equals(k, StringComparison.OrdinalIgnoreCase));
    private static void Remove(JsonObject o, string k) { foreach (var key in o.Where(p => p.Key.Equals(k, StringComparison.OrdinalIgnoreCase)).Select(p => p.Key).ToList()) o.Remove(key); }
    private static double Num(JsonObject o, string k)
        => o.FirstOrDefault(p => p.Key.Equals(k, StringComparison.OrdinalIgnoreCase)).Value is JsonValue v && v.TryGetValue(out double d) ? d : 0;

    /// <summary>Writes <paramref name="json"/> in the form of <paramref name="rawToken"/>: as itself when that was an object / array (CE-4295), else as a JSON string token in the escaping style of <paramref name="rawToken"/> (<c>"</c> or <c>\"</c>).</summary>
    private static string EncodeLike(string rawToken, string json)
    {
        if (rawToken.Length > 0 && rawToken[0] is '{' or '[') return json;   // ⭐ CE-4295 — an object site stays an object
        string relaxed = JsonSerializer.Serialize(json, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        return rawToken.Contains("\\u0022", StringComparison.Ordinal) ? relaxed.Replace("\\\"", "\\u0022") : relaxed;
    }

    private static string? TerrainName(byte[] bytes)
    {
        try
        {
            using var doc = JsonDocument.Parse(bytes);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("header", out var h) && h.ValueKind == JsonValueKind.Object
                && h.TryGetProperty("terrainName", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    private sealed record Site(string Behavior, string Value, int Start, int Length);

    /// <summary>Every <c>{Name|behaviorName|BehaviorName, Params|behaviorParams|BehaviorParams}</c> pair: the params STRING token's byte range.</summary>
    /// <summary>S9' — <c>entities.&lt;key&gt;.NetworkIdentity.Value</c> -> <c>key</c> for a scenario / recipe file (empty for any other shape).</summary>
    public static Dictionary<long, string> NetworkIdsToKeys(byte[] bytes)
    {
        var map = new Dictionary<long, string>();
        try
        {
            if (JsonNode.Parse(bytes, null, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) is not JsonObject root
                || root["entities"] is not JsonObject entities) return map;
            foreach (var (key, node) in entities)
                if (node is JsonObject e && e["NetworkIdentity"] is JsonObject ni && ni["Value"] is JsonValue v && v.TryGetValue<long>(out long id) && id != 0)
                    map[id] = key;
        }
        catch (JsonException) { }
        return map;
    }

    /// <summary>S9' — every numeric <c>"HoldUnit"</c> member (RoutePlan waypoints) as raw byte ranges.</summary>
    private static List<(int Start, int Length, long Value)> HoldUnitSites(byte[] bytes)
    {
        var sites = new List<(int, int, long)>();
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        string? property = null;
        try
        {
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.PropertyName) property = reader.GetString();
                else
                {
                    if (reader.TokenType == JsonTokenType.Number && property == "HoldUnit" && reader.TryGetInt64(out long v))
                        sites.Add(((int)reader.TokenStartIndex, reader.ValueSpan.Length, v));
                    property = null;
                }
            }
        }
        catch (JsonException) { return new(); }
        return sites;
    }

    /// <summary>S8 - removes <c>entities.&lt;key&gt;.NetworkIdentity</c> (and its comma) from the raw bytes, leaving every other byte as it was.</summary>
    public static byte[] StripNetworkIdentity(byte[] bytes, out int removed)
    {
        removed = 0;
        var cuts = new List<(int Start, int End)>();
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        try
        {
            while (reader.Read())
            {
                if (reader.TokenType != JsonTokenType.PropertyName || reader.CurrentDepth != 3 || !reader.ValueTextEquals("NetworkIdentity")) continue;
                int start = (int)reader.TokenStartIndex;
                reader.Read();
                reader.Skip();
                int end = (int)reader.BytesConsumed;
                int after = end;
                while (after < bytes.Length && (bytes[after] == ' ' || bytes[after] == '\t' || bytes[after] == '\r' || bytes[after] == '\n')) after++;
                if (after < bytes.Length && bytes[after] == ',')
                {
                    after++;
                    while (after < bytes.Length && (bytes[after] == ' ' || bytes[after] == '\t' || bytes[after] == '\r' || bytes[after] == '\n')) after++;
                    cuts.Add((start, after));
                }
                else
                {
                    int before = start;
                    while (before > 0 && (bytes[before - 1] == ' ' || bytes[before - 1] == '\t' || bytes[before - 1] == '\r' || bytes[before - 1] == '\n')) before--;
                    cuts.Add(before > 0 && bytes[before - 1] == ',' ? (before - 1, end) : (start, end));
                }
            }
        }
        catch (JsonException) { return bytes; }
        if (cuts.Count == 0) return bytes;
        removed = cuts.Count;
        using var ms = new MemoryStream(bytes.Length);
        int pos = 0;
        foreach (var (cs, ce) in cuts) { ms.Write(bytes, pos, cs - pos); pos = ce; }
        ms.Write(bytes, pos, bytes.Length - pos);
        return ms.ToArray();
    }

    private static List<Site> ParamsSites(byte[] bytes)
    {
        var sites = new List<Site>();
        var frames = new Stack<(string? Name, List<(string Value, int Start, int Length)> Params)>();
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        string? property = null;
        try
        {
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject or JsonTokenType.StartArray when property != null && frames.Count > 0 && ParamsKeys.Contains(property):
                    {
                        // ⭐ CE-4295 — params written as the object they are ([JsonText]): the site is the object's raw text
                        int start = (int)reader.TokenStartIndex;
                        reader.Skip();
                        int length = (int)reader.BytesConsumed - start;
                        frames.Peek().Params.Add((Encoding.UTF8.GetString(bytes, start, length), start, length));
                        property = null; break;
                    }
                    case JsonTokenType.StartObject: frames.Push((null, new())); property = null; break;
                    case JsonTokenType.EndObject:
                        var f = frames.Pop();
                        if (f.Name != null) foreach (var p in f.Params) sites.Add(new Site(f.Name, p.Value, p.Start, p.Length));
                        property = null; break;
                    case JsonTokenType.PropertyName: property = reader.GetString(); break;
                    case JsonTokenType.String when property != null && frames.Count > 0:
                        if (NameKeys.Contains(property)) { var top = frames.Pop(); frames.Push((reader.GetString(), top.Params)); }
                        else if (ParamsKeys.Contains(property))
                        {
                            int start = (int)reader.TokenStartIndex;
                            int length = reader.ValueSpan.Length + 2;   // the raw (escaped) value between the quotes
                            frames.Peek().Params.Add((reader.GetString()!, start, length));
                        }
                        property = null; break;
                    default: property = null; break;
                }
            }
        }
        catch (JsonException) { return new List<Site>(); }   // not JSON this tool understands: left alone
        return sites;
    }
}
