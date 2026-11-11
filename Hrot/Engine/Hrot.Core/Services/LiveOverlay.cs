using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Lifecycle.Events;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Common.Components;

namespace Hrot.Map.Common.Services;

/// <summary>One placed terrain block / feature as the live overlay reads it from the world (a snapshot, taken only when the set changed).</summary>
public readonly record struct OverlaySource(string Key, byte Kind, string Name, uint Seed, float HeadingDeg, byte Fit, float Pad, Vector2 At, Vector2[]? Points = null)
{
    /// <summary><see cref="Kind"/> of a block (a recipe expansion); a feature uses its <see cref="TerrainFeature"/> kind (1 and up). <see cref="Points"/> are a line / area feature's vertices in WORLD coordinates (T-2).</summary>
    public const byte BlockKind = 0;

    /// <summary>What the expansion depends on, at 1 cm — a source with the same hash expands to the same parts.</summary>
    internal ulong ParamsHash()
    {
        ulong h = 1469598103934665603UL;
        void Mix(long v) { h ^= (ulong)v; h *= 1099511628211UL; }
        void MixF(float f) => Mix((long)MathF.Round(f * 100f));
        Mix(Kind); Mix(Fdp.Core.StableHash.Fnv1a32(Name)); Mix(Seed); MixF(HeadingDeg); Mix(Fit); MixF(Pad); MixF(At.X); MixF(At.Y);
        if (Points != null) foreach (var p in Points) { MixF(p.X); MixF(p.Y); }
        return h;
    }
}

/// <summary>One expanded part of the live overlay (a house, a wood): its stable key (<c>&lt;guid&gt;/&lt;i&gt;</c>), a hash of its geometry and the ground rectangle it spans.</summary>
public readonly record struct OverlayPart(string Key, ulong Hash, Vector2 Min, Vector2 Max);

/// <summary>
/// What <see cref="LiveOverlay.Expand"/> made: the overlay as a <see cref="TerrainWorld"/> parsed in memory, its parts, and the base it was expanded against (the bake uses THAT base, so an overlay
/// never meets a base it was not made for).
/// </summary>
public sealed class LiveOverlayResult
{
    internal LiveOverlayResult(TerrainWorld world, IReadOnlyList<OverlayPart> parts, TerrainWorld @base, int baseVersion)
    { World = world; Parts = parts; Base = @base; BaseVersion = baseVersion; }

    public TerrainWorld World { get; }
    public IReadOnlyList<OverlayPart> Parts { get; }
    public TerrainWorld Base { get; }
    public int BaseVersion { get; }
}

/// <summary>
/// ⭐⭐ O-7 B (CE-4418, docs/DESIGN_Terrain_Authoring.md §18e-2) — the EDITOR's live block expansion: the <see cref="TerrainBlock"/> / <see cref="TerrainFeature"/> entities of the world, expanded in memory by the
/// SAME code the scenario save uses (<see cref="OverlayExpansion"/>), so a block shows as terrain (and in the navmesh) without a save. Held only by the editor's <see cref="StaticObstacleBakeSystem"/>;
/// on every other host a block is inert data (the compiled overlay reaches those at load).
/// <para>Expansion is per entity and CACHED by key + its parameters (recipe, seed, heading, fit, position at 1 cm): only a changed entity re-expands (B-2). The cache is dropped when the base terrain
/// changes. A source that cannot expand (a missing recipe or template) is logged loudly and skipped — it must not stop the obstacles from baking.</para>
/// <para>HOT PATH: <see cref="SignatureOfWorld"/> runs every frame and allocates nothing (a cached query, no strings); <see cref="Snapshot"/> and <see cref="Expand"/> run only when it changed —
/// <see cref="Expand"/> on the bake's background task.</para>
/// </summary>
public sealed class LiveOverlay
{
    private const ulong Empty = 1469598103934665603UL;

    private sealed class Entry
    {
        public ulong Params;
        public string[] Files = Array.Empty<string>();   // ⭐ CE-4427 R-7 — the recipe / template files the expansion read…
        public ulong Stamp;                              // …and their (length, mtime) when it did
        public readonly List<JsonObject> Features = new();
        public readonly List<OverlayPart> Parts = new();
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _cache = new(StringComparer.Ordinal);
    private TerrainWorld? _cacheBase;
    private string? _cacheFolder;
    private TerrainAssets? _assets;
    private IEnumerable<string?>? _roots;

    // ⭐ CE-4427 R-7 — the files of the live cache, polled ONCE A SECOND by wall time (a stat each, no per-frame IO): an edit moves the epoch, which joins the signature, which re-bakes; the re-expansion then finds the stamp changed
    private string[] _watchPaths = Array.Empty<string>();
    private DateTime[] _watchTimes = Array.Empty<DateTime>();
    private double _polledAt = double.MinValue;
    private long _fileEpoch;

    private EntityRepository? _for;
    private EntityQuery? _blocks, _features;

    /// <summary>Block / feature expansions made (a cache miss each) — diagnostics and rails.</summary>
    public int Expansions { get; private set; }

    /// <summary>Per-entity cache hits — an entity whose parameters did not change.</summary>
    public int CacheHits { get; private set; }

    /// <summary>Sources that failed to expand (logged).</summary>
    public int Failures { get; private set; }

    private void Bind(EntityRepository world)
    {
        if (ReferenceEquals(world, _for) && _blocks != null) return;
        _blocks = world.IsComponentTypeRegistered<TerrainBlock>()
            ? world.Query().With<TerrainBlock>().With<SimTransform>().WithLifecycle(EntityLifecycle.All).Build() : null;
        _features = world.IsComponentTypeRegistered<TerrainFeature>()
            ? world.Query().With<TerrainFeature>().With<SimTransform>().WithLifecycle(EntityLifecycle.All).Build() : null;
        _for = world;
    }

    private static bool Live(EntityRepository world, Entity e)
    {
        var state = world.GetLifecycleState(e);
        return state == EntityLifecycle.Active || state == EntityLifecycle.Constructing;
    }

    /// <summary>
    /// The hash of every block / feature as the expansion sees it, read straight from the components, allocating nothing (HOT PATH — every frame). Equals the empty value when the world holds none.
    /// </summary>
    public ulong SignatureOfWorld(EntityRepository world)
    {
        Bind(world);
        ISimulationView view = world;
        ulong h = Empty;
        void Mix(long v) { h ^= (ulong)v; h *= 1099511628211UL; }
        void MixF(float f) => Mix((long)MathF.Round(f * 100f));
        if (_blocks != null)
            foreach (var e in _blocks)
            {
                if (!Live(world, e)) continue;
                ref readonly var t = ref world.GetComponentRO<SimTransform>(e);
                ref readonly var b = ref world.GetComponentRO<TerrainBlock>(e);
                Mix(1); Mix(GuidHash(world, e)); Mix(b.Recipe.GetHashCode()); Mix(b.Seed); MixF(b.HeadingDeg); Mix(b.Fit); MixF(t.Position.X); MixF(t.Position.Y);
            }
        if (_features != null)
            foreach (var e in _features)
            {
                if (!Live(world, e)) continue;
                ref readonly var t = ref world.GetComponentRO<SimTransform>(e);
                ref readonly var f = ref world.GetComponentRO<TerrainFeature>(e);
                Mix(2); Mix(GuidHash(world, e)); Mix(f.Kind); Mix(f.Template.GetHashCode()); MixF(f.HeadingDeg); MixF(f.Pad); Mix(f.Seed); MixF(t.Position.X); MixF(t.Position.Y);
                // ⭐ T-2 — a line / area feature's shape (a managed immutable record, read by index: no enumerator, no allocation)
                if (world.HasManagedComponent<EditablePolyline>(e) && view.GetManagedComponentRO<EditablePolyline>(e) is { } poly)
                {
                    var pts = poly.Points;
                    for (int i = 0; i < pts.Count; i++) { MixF(pts[i].X); MixF(pts[i].Y); }
                }
            }
        if (_fileEpoch != 0 && h != Empty) Mix(_fileEpoch);   // ⭐ CE-4427 R-7
        return h;
    }

    /// <summary>
    /// ⭐ CE-4427 R-7 — called every frame with the wall clock; does anything only once a second: a recipe or template file the cache read was edited ⇒ the file epoch moves (and the next bake re-expands what read it).
    /// No IO on the other frames.
    /// </summary>
    public void PollFiles(double wallSeconds)
    {
        if (wallSeconds - _polledAt < 1.0) return;
        _polledAt = wallSeconds;
        var paths = _watchPaths; var times = _watchTimes;
        for (int i = 0; i < paths.Length; i++)
        {
            var t = File.GetLastWriteTimeUtc(paths[i]);
            if (t != times[i]) { times[i] = t; _fileEpoch++; }
        }
    }

    /// <summary>The signature of a world with no block and no feature.</summary>
    public static ulong EmptySignature => Empty;

    private static long GuidHash(EntityRepository world, Entity e)
        => world.IsComponentTypeRegistered<EntityGuid>() && world.HasComponent<EntityGuid>(e) ? world.GetComponentRO<EntityGuid>(e).Value.GetHashCode() : e.Index;

    private static string KeyOf(EntityRepository world, Entity e)
        => world.IsComponentTypeRegistered<EntityGuid>() && world.HasComponent<EntityGuid>(e) ? world.GetComponentRO<EntityGuid>(e).Value.ToString("D") : "e" + e.Index;

    /// <summary>Frame thread, only when <see cref="SignatureOfWorld"/> moved: the blocks and features as plain values (the key is the entity's scenario guid, as the compiled overlay's is).</summary>
    public List<OverlaySource> Snapshot(EntityRepository world)
    {
        Bind(world);
        var list = new List<OverlaySource>();
        if (_blocks != null)
            foreach (var e in _blocks)
            {
                if (!Live(world, e)) continue;
                ref readonly var t = ref world.GetComponentRO<SimTransform>(e);
                ref readonly var b = ref world.GetComponentRO<TerrainBlock>(e);
                list.Add(new OverlaySource(KeyOf(world, e), OverlaySource.BlockKind, b.Recipe.ToString() ?? "", b.Seed, b.HeadingDeg, b.Fit, 0f, new Vector2(t.Position.X, t.Position.Y)));
            }
        if (_features != null)
            foreach (var e in _features)
            {
                if (!Live(world, e)) continue;
                ref readonly var t = ref world.GetComponentRO<SimTransform>(e);
                ref readonly var f = ref world.GetComponentRO<TerrainFeature>(e);
                if (!FeatureExpander.IsKnown(f.Kind)) continue;   // 0 and the unknown are ignored, as the compiler does
                var at = new Vector2(t.Position.X, t.Position.Y);
                Vector2[]? points = null;
                if (FeatureExpander.NeedsShape(f.Kind))
                {
                    // the polyline is RELATIVE to the entity: world = position + vertex (the compiler adds it the same way)
                    var poly = world.HasManagedComponent<EditablePolyline>(e) ? ((ISimulationView)world).GetManagedComponentRO<EditablePolyline>(e) : null;
                    if (poly == null) { FdpLog<LiveOverlay>.Warn($"[LiveOverlay] Feature {KeyOf(world, e)} (kind {f.Kind}) has no EditablePolyline: left out of the live terrain."); continue; }
                    points = new Vector2[poly.Points.Count];
                    for (int i = 0; i < points.Length; i++) points[i] = at + poly.Points[i];
                }
                list.Add(new OverlaySource(KeyOf(world, e), f.Kind, f.Template.ToString() ?? "", f.Seed, f.HeadingDeg, 0, f.Pad, at, points));
            }
        return list;
    }

    /// <summary>
    /// Background thread: expands the changed <paramref name="sources"/> (the rest come from the cache) over <paramref name="baseWorld"/> and parses the overlay world in memory. Null when there is nothing to expand
    /// (no source, or the node holds no terrain folder to place them on).
    /// </summary>
    public LiveOverlayResult? Expand(IReadOnlyList<OverlaySource> sources, TerrainWorld baseWorld, int baseVersion, string? terrainFolder)
    {
        if (sources.Count == 0 || string.IsNullOrEmpty(terrainFolder)) return null;
        lock (_gate)
        {
            if (!ReferenceEquals(baseWorld, _cacheBase) || !string.Equals(terrainFolder, _cacheFolder, StringComparison.Ordinal))
            {
                _cache.Clear(); _cacheBase = baseWorld; _cacheFolder = terrainFolder;
                _assets = TerrainAssets.ForFolder(terrainFolder);
                _roots = new List<string?>(TerrainRecipe.RootsFor(terrainFolder));
            }

            var features = new List<JsonObject>();
            var parts = new List<OverlayPart>();
            var live = new HashSet<string>(StringComparer.Ordinal);
            foreach (var s in sources)
            {
                live.Add(s.Key);
                ulong p = s.ParamsHash();
                if (!_cache.TryGetValue(s.Key, out var entry) || entry.Params != p || StampOf(entry.Files) != entry.Stamp)
                {
                    entry = Make(s, p, baseWorld);
                    if (entry == null) { _cache.Remove(s.Key); continue; }
                    _cache[s.Key] = entry;
                    Expansions++;
                }
                else CacheHits++;
                features.AddRange(entry.Features);
                parts.AddRange(entry.Parts);
            }
            foreach (var key in new List<string>(_cache.Keys)) if (!live.Contains(key)) _cache.Remove(key);
            RefreshWatch();
            if (features.Count == 0) return null;

            OverlayExpansion.SortByLabel(features);
            parts.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            string json = OverlayExpansion.WorldDocument(baseWorld, features).ToJsonString();
            var world = TerrainWorldParser.Parse(json, baseWorld.Name, _assets);
            return new LiveOverlayResult(world, parts, baseWorld, baseVersion);
        }
    }

    private Entry? Make(OverlaySource s, ulong paramsHash, TerrainWorld baseWorld)
    {
        try
        {
            var entry = new Entry { Params = paramsHash };
            var files = new List<string>();
            if (s.Kind == OverlaySource.BlockKind)
            {
                files.Add(RecipePath(s.Name));
                foreach (var t in TerrainRecipe.Find(s.Name, _roots!).Templates) if (_assets!.TemplatePath?.Invoke(t) is { } tp) files.Add(tp);
            }
            else if (s.Kind == TerrainFeature.House) { if (_assets!.TemplatePath?.Invoke(s.Name) is { } hp) files.Add(hp); }
            else if (s.Kind == TerrainFeature.Scatter)   // a scatter reads its recipe
                files.Add(RecipePath(s.Name));
            entry.Files = files.ToArray();
            entry.Stamp = StampOf(entry.Files);
            if (s.Kind == OverlaySource.BlockKind)
                entry.Features.AddRange(OverlayExpansion.Block(s.Key, s.Name, s.Seed, s.HeadingDeg, s.Fit, s.At, baseWorld, _assets!, _roots!).Features);
            else
                entry.Features.AddRange(OverlayExpansion.Feature(new OverlayExpansion.FeatureSpec(s.Key, s.Kind, s.Name, s.HeadingDeg, s.Pad, s.Seed, s.At, s.Points ?? Array.Empty<Vector2>()), baseWorld, _assets!, _roots!));
            foreach (var f in entry.Features)
            {
                var (min, max) = OverlayExpansion.BoundsOf(f);
                ulong h = Empty;
                foreach (char c in f.ToJsonString()) { h ^= c; h *= 1099511628211UL; }
                entry.Parts.Add(new OverlayPart(OverlayExpansion.Label(f), h, min, max));
            }
            return entry;
        }
        catch (Exception ex)
        {
            Failures++;
            FdpLog<LiveOverlay>.Error($"[LiveOverlay] Block / feature {s.Key} ('{s.Name}') could not be expanded and is left out of the live terrain: {ex.Message}");
            return null;
        }
    }

    /// <summary>The path of recipe <paramref name="id"/> in the first root that holds it (the one <see cref="TerrainRecipe.Find"/> reads).</summary>
    private string RecipePath(string id)
    {
        foreach (var root in _roots!)
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            var path = Path.Combine(root, id + TerrainRecipe.Suffix);
            if (File.Exists(path)) return path;
        }
        return id + TerrainRecipe.Suffix;
    }

    /// <summary>A hash of (path, length, mtime) of each file — what "the file was edited" means for the cache. Background thread only.</summary>
    private static ulong StampOf(string[] files)
    {
        ulong h = Empty;
        void Mix(long v) { h ^= (ulong)v; h *= 1099511628211UL; }
        foreach (var f in files)
        {
            foreach (char c in f) Mix(c);
            var i = new FileInfo(f);
            if (i.Exists) { Mix(i.Length); Mix(i.LastWriteTimeUtc.Ticks); } else Mix(-1);
        }
        return h;
    }

    /// <summary>After an expansion: the distinct files the cache read become the polled set (their mtimes as of now).</summary>
    private void RefreshWatch()
    {
        var set = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var e in _cache.Values) foreach (var f in e.Files) set.Add(f);
        var paths = new string[set.Count]; set.CopyTo(paths);
        var times = new DateTime[paths.Length];
        for (int i = 0; i < paths.Length; i++) times[i] = File.GetLastWriteTimeUtc(paths[i]);
        _watchTimes = times; _watchPaths = paths;
    }
}
