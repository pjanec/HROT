using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Vis3D;
using Fdp.Toolkit.World;
using Hrot.Editor.UiFrameRail;
using Hrot.Map.Common.Services;
using Hrot.UI.Common.Map3D;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐ Terrain authoring U-6 (docs/DESIGN_Terrain_Authoring.md §15a; §18e-2 B-5) — the 3-D view follows the live bake BY AREA: a new world over the same chunk grid whose change is known (the bake's dirty rectangles) is ADOPTED — every chunk keeps its
/// meshes, only the chunks under the rectangles rebuild (all their slots, off the frame thread); an unknown change rebuilds everything as before. Measured on demo-city (a chunked terrain). Display rail (Xvfb; a skip is not a pass).
/// 🔴 Red-proof (compiles): <c>TerrainChunks3D.Adopt</c> marking EVERY chunk stale ⇒ the "only the touched chunk" cell reds.
/// </summary>
[Collection(Hrot.Presentation.Tests.Windows.PanelSnapshotTestCollection.Name)]
public sealed class LiveTerrainChunkRebuildTests
{
    private static TerrainWorld Load(string name)
    {
        var folder = Path.Combine(ContentLayout.TerrainAssets(ContentLayout.TryFindSourceTree()!), name);
        var world = Directory.GetFiles(folder, "*.world.geojson").Single();
        return TerrainWorldParser.Parse(File.ReadAllText(world), name, TerrainAssets.ForFolder(folder));
    }

    /// <summary>The terrain's chunked geometry under ANOTHER identity (as a newly committed world is), counting the chunk builds it is asked for.</summary>
    private sealed class Counting : ITerrainChunkedGeometry
    {
        private readonly ITerrainChunkedGeometry _inner;
        public readonly object Id = new();
        public readonly List<(int Cx, int Cy, int Level, TerrainChunkPart Part)> Builds = new();
        public Counting(ITerrainChunkedGeometry inner) => _inner = inner;
        public object Identity => Id;
        public TerrainChunkLayout Chunks => _inner.Chunks;
        public void Build(out Vector3[] vertices, out int[] indices, out TerrainSurfaceKind[] kinds) => _inner.Build(out vertices, out indices, out kinds);
        public void BuildChunk(int cx, int cy, int level, TerrainChunkPart part, out Vector3[] vertices, out int[] indices, out TerrainSurfaceKind[] kinds, out string?[] materials)
        {
            lock (Builds) Builds.Add((cx, cy, level, part));
            _inner.BuildChunk(cx, cy, level, part, out vertices, out indices, out kinds, out materials);
        }
        public void BuildOutside(out Vector3[] vertices, out int[] indices, out TerrainSurfaceKind[] kinds, out string?[] materials) => _inner.BuildOutside(out vertices, out indices, out kinds, out materials);
    }

    private static void PumpUntil(UiFrameSession frame, TerrainChunks3D chunks, Func<bool> done)
    {
        var sw = Stopwatch.StartNew();
        int quiet = 0;
        while (sw.Elapsed < TimeSpan.FromSeconds(120))
        {
            frame.Step(chunks.Pump);
            if (done() && chunks.Pending == 0) { if (++quiet >= 5) return; } else quiet = 0;
            Thread.Sleep(5);
        }
        Assert.Fail("the chunks did not settle");
    }

    [SkippableFact]
    public void U6_AMovedFeature_RebuildsOnlyTheChunksUnderItsDirtyRectangle_NotEveryChunk()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(800, 600);
        LitShader.Reset();
        var terrain = Load("demo-city");
        var world = Map3DTests.WorldWith(terrain);
        var inner = (ITerrainChunkedGeometry)WorldQuery.RenderGeometryOf(world)!;
        var first = new Counting(inner);
        using var chunks = new TerrainChunks3D(first);
        PumpUntil(frame, chunks, () => chunks.CoarseBuilt);
        int n = chunks.Layout.Nx * chunks.Layout.Ny;
        Assert.True(n >= 4, $"demo-city is chunked ({n} chunks)");
        int builtAtLoad = first.Builds.Count;
        Assert.True(builtAtLoad >= 2 * n, $"every chunk was built at load ({builtAtLoad} chunk builds for {n} chunks)");

        // ⭐ a new world (new identity), changed inside ONE rectangle: chunk (0, 0)'s middle
        var l = chunks.Layout;
        var rectCentre = l.ChunkMin(0, 0) + new Vector2(l.ChunkSize * 0.5f);
        var second = new Counting(inner);
        Assert.True(chunks.CanAdopt(second));
        Assert.True(chunks.Adopt(second, new[] { (rectCentre - new Vector2(5f), rectCentre + new Vector2(5f)) }));
        Assert.Equal(1, chunks.AdoptedChunks);   // the rectangle (with its margin) meets one chunk
        PumpUntil(frame, chunks, () => second.Builds.Count >= chunks.AdoptedSlots);

        Console.WriteLine($"U6 measured on demo-city: {n} chunks, {builtAtLoad} chunk builds at load, one rectangle moved => {second.Builds.Count} chunk builds ({chunks.AdoptedChunks} chunk, {chunks.AdoptedSlots} slots stale)");
        Assert.NotEmpty(second.Builds);
        Assert.All(second.Builds, b => Assert.Equal((0, 0), (b.Cx, b.Cy)));   // ONLY the touched chunk was asked to rebuild
        Assert.True(second.Builds.Count < builtAtLoad / 2, $"the rebuild ({second.Builds.Count} chunk builds) is a fraction of the load ({builtAtLoad})");
        Assert.Same(second.Id, chunks.Identity);

        // a grid that differs cannot be adopted: the caller then builds a fresh set
        Assert.False(chunks.Adopt(new Counting(new Shifted(inner)), new[] { (Vector2.Zero, Vector2.One) }));
    }

    /// <summary>The same terrain on another chunk grid (shifted origin).</summary>
    private sealed class Shifted : ITerrainChunkedGeometry
    {
        private readonly ITerrainChunkedGeometry _inner;
        public Shifted(ITerrainChunkedGeometry inner) => _inner = inner;
        public object Identity => _inner.Identity;
        public TerrainChunkLayout Chunks => _inner.Chunks with { Origin = _inner.Chunks.Origin + new Vector2(1f) };
        public void Build(out Vector3[] v, out int[] i, out TerrainSurfaceKind[] k) => _inner.Build(out v, out i, out k);
        public void BuildChunk(int cx, int cy, int level, TerrainChunkPart part, out Vector3[] v, out int[] i, out TerrainSurfaceKind[] k, out string?[] m) => _inner.BuildChunk(cx, cy, level, part, out v, out i, out k, out m);
        public void BuildOutside(out Vector3[] v, out int[] i, out TerrainSurfaceKind[] k, out string?[] m) => _inner.BuildOutside(out v, out i, out k, out m);
    }

    [Fact]
    public void U6_TheChangeIsKnownOnlyForTheNextLiveBake_ElseTheLayerRebuildsEverything()
    {
        var repo = new EntityRepository();
        var changes = new LiveTerrainChanges(() => repo);
        Assert.Null(changes.Take(new object()));   // no live bake published: unknown

        repo.RegisterManagedComponent<LiveTerrainBakeInfo>();
        var rects = new[] { (new Vector2(0), new Vector2(10)) };
        repo.SetSingletonManaged(LiveTerrainBakeInfo.From(1, Array.Empty<OverlayPart>(), rects));
        Assert.Same(rects, changes.Take(new object()));   // the very next bake (0 → 1): its rectangles

        repo.SetSingletonManaged(LiveTerrainBakeInfo.From(1, Array.Empty<OverlayPart>(), rects));
        Assert.Null(changes.Take(new object()));          // the same bake again (a terrain switch, no new bake): unknown

        repo.SetSingletonManaged(LiveTerrainBakeInfo.From(3, Array.Empty<OverlayPart>(), rects));
        Assert.Null(changes.Take(new object()));          // a bake the layer never saw (1 → 3): unknown

        repo.SetSingletonManaged(LiveTerrainBakeInfo.From(4, Array.Empty<OverlayPart>(), null));
        Assert.Null(changes.Take(new object()));          // the bake did not know where it changed
    }
}
