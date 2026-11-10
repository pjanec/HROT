using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Vis3D;
using Fdp.Toolkit.World;
using Xunit;
using Xunit.Abstractions;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐ CE-3423 / CE-4377 — the NUMBERS for the status file (handoff D-1: "numbers before / after"): scale-1 (5.5 km, 1 m) loaded the production way, then what the chunked
/// 3-D terrain builds — the coarse picture (the load's "built"), and the levels wanted at three cameras (near, mid, far; no frustum culling — an upper bound). CPU only (the
/// vertex streams the GPU would get). ⭐ Opt-in: <c>HROT_MEASURE=1</c> and <c>scripts/terrain-scale.py --seed 1 --trees</c> run first; <c>HROT_MEASURE_SOUP=1</c> also builds the
/// old whole soup (the "before" — run it alone: its peak is the point).
/// </summary>
public sealed class TheLargeTerrainNumbers
{
    private readonly ITestOutputHelper _out;
    public TheLargeTerrainNumbers(ITestOutputHelper output) => _out = output;

    private static string? Folder()
    {
        var root = ContentLayout.TryFindSourceTree();
        var f = root == null ? null : Path.Combine(ContentLayout.TerrainAssets(root), "scale-1");
        return f != null && Directory.Exists(f) && Directory.GetFiles(f, "*.world.geojson").Length == 1 ? f : null;
    }

    [SkippableFact]
    public void ScaleOne_TheChunkedTerrainsNumbers()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("HROT_MEASURE") == "1", "opt-in: HROT_MEASURE=1");
        var folder = Folder();
        Skip.If(folder == null, "assets/terrain/scale-1 is not generated (scripts/terrain-scale.py --seed 1 --trees)");

        var sw = Stopwatch.StartNew();
        var world = TerrainWorldParser.Parse(File.ReadAllText(Directory.GetFiles(folder!, "*.world.geojson").Single()), "scale-1", TerrainAssets.ForFolder(folder!));
        _out.WriteLine($"parse: {sw.Elapsed.TotalSeconds:F1} s, {world.Prisms.Count} prisms, heights {(world.Height != null ? world.Height.CellSize + " m" : "none")}");

        if (Environment.GetEnvironmentVariable("HROT_MEASURE_SOUP") == "1")
        {
            sw.Restart();
            new TerrainWorldQuery(world).BuildTagged(out var sv, out var si, out _, out _);
            _out.WriteLine($"BEFORE — the whole tagged soup: {sw.Elapsed.TotalSeconds:F1} s, {si.Length / 3:N0} triangles, {(long)si.Length * 28 / (1 << 20):N0} MB of drawn vertices, peak {Peak()}");
            return;
        }

        var q = new TerrainWorldQuery(world);
        sw.Restart();
        var l = q.Chunks;
        _out.WriteLine($"layout: {l.Nx} × {l.Ny} chunks of {l.ChunkSize} m, cell {l.Cell} m, {l.Levels} levels, z {l.MinZ:F0}…{l.MaxZ:F0} ({sw.ElapsedMilliseconds} ms)");

        // the load's "built": every chunk's coarsest ground + its objects + the outside, built on the workers the layer uses
        int coarse = l.Levels - 1;
        long tris = 0, objTris = 0;
        sw.Restart();
        Parallel.For(0, l.Nx * l.Ny, new ParallelOptions { MaxDegreeOfParallelism = TerrainChunks3D.MaxInFlight }, k =>
        {
            q.BuildChunk(k % l.Nx, k / l.Nx, coarse, TerrainChunkPart.ChunkGround, out var v, out var i, out var kk, out var m);
            TerrainLayer3D.BuildVertexData(v, i, kk, m, out _, out _, out _);
            System.Threading.Interlocked.Add(ref tris, i.Length / 3);
            q.BuildChunk(k % l.Nx, k / l.Nx, 0, TerrainChunkPart.ChunkObjects, out v, out i, out kk, out m);
            TerrainLayer3D.BuildVertexData(v, i, kk, m, out _, out _, out _);
            System.Threading.Interlocked.Add(ref objTris, i.Length / 3);
        });
        q.BuildOutside(out _, out var oi, out _, out _);
        _out.WriteLine($"AFTER — the coarse picture (load 'built'): {sw.Elapsed.TotalSeconds:F1} s on {TerrainChunks3D.MaxInFlight} workers; ground {tris:N0} + objects {objTris:N0} + outside {oi.Length / 3:N0} triangles = {(tris + objTris + oi.Length / 3) * 3 * 28 / (1 << 20):N0} MB of vertices; peak {Peak()}");

        var c = (l.ChunkMin(0, 0) + l.ChunkMax(l.Nx - 1, l.Ny - 1)) * 0.5f;
        float gz = world.GroundHeightAt(c.X, c.Y);
        foreach (var (name, eye) in new[] { ("near (40 m up)", new Vector3(c, gz + 40f)), ("mid (400 m up)", new Vector3(c, gz + 400f)), ("far (3 000 m up)", new Vector3(c, gz + 3000f)) })
        {
            long wanted = 0; int[] perLevel = new int[l.Levels];
            sw.Restart();
            Parallel.For(0, l.Nx * l.Ny, new ParallelOptions { MaxDegreeOfParallelism = TerrainChunks3D.MaxInFlight }, k =>
            {
                int level = TerrainChunks3D.LevelFor(l, k % l.Nx, k / l.Nx, eye);
                System.Threading.Interlocked.Increment(ref perLevel[level]);
                q.BuildChunk(k % l.Nx, k / l.Nx, level, TerrainChunkPart.ChunkGround, out _, out var i, out _, out _);
                System.Threading.Interlocked.Add(ref wanted, i.Length / 3);
            });
            _out.WriteLine($"camera {name}: chunks per level [{string.Join(", ", perLevel)}]; ground triangles at the wanted levels {wanted:N0} (+ objects {objTris:N0}); "
                         + $"built in {sw.Elapsed.TotalSeconds:F1} s; ≤ {(2 * l.Nx * l.Ny) + 1} draw calls before culling; peak {Peak()}");
        }
    }

    private static string Peak() => $"{Process.GetCurrentProcess().PeakWorkingSet64 / (1 << 20):N0} MB working set";
}
