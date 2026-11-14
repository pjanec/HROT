using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.World;
using Xunit;

namespace Fdp.Toolkit.Terrain.Tests
{
    /// <summary>
    /// ⭐ CE-4392 (docs/DESIGN_Map_3D_Mode.md §6aj X-1 … X-4) — the 3-D chunks DRAW the replicated damage: a breach is an opening in its wall (a ray through it meets nothing, from
    /// either side; a ray beside it still meets the wall); a collapsed building's pieces are gone and its rubble is a HEAP; over a muscle's rebaked world the drawing is the same
    /// (nothing cut twice, one heap); the touched chunks are the building's; the world is never changed. 🔴 Red-proof (compiles): <c>DamagedBuildings.Keeps</c> always true ⇒ the
    /// breached wall still stands and the collapsed walls are still drawn.
    /// </summary>
    public class TerrainChunkDamageTests
    {
        private static (Vector3[] V, int[] I, string?[] M) Objects(TerrainWorld w, object? damage)
        {
            ITerrainChunkedGeometry q = new TerrainWorldQuery(w);
            var l = q.Chunks; var verts = new List<Vector3>(); var idx = new List<int>(); var mats = new List<string?>();
            for (int cy = 0; cy < l.Ny; cy++)
                for (int cx = 0; cx < l.Nx; cx++)
                {
                    q.BuildChunk(cx, cy, 0, TerrainChunkPart.ChunkObjects, damage, out var v, out var i, out _, out var m);
                    int b = verts.Count; verts.AddRange(v); idx.AddRange(i.Select(x => x + b)); mats.AddRange(m);
                }
            return (verts.ToArray(), idx.ToArray(), mats.ToArray());
        }

        // the first triangle a segment meets (Möller–Trumbore), or null
        private static (float T, string? Material)? Hit((Vector3[] V, int[] I, string?[] M) mesh, Vector3 from, Vector3 to)
        {
            var d = to - from; (float T, string? M)? best = null;
            for (int t = 0; t < mesh.I.Length / 3; t++)
            {
                var a = mesh.V[mesh.I[3 * t]]; var b = mesh.V[mesh.I[3 * t + 1]]; var c = mesh.V[mesh.I[3 * t + 2]];
                var e1 = b - a; var e2 = c - a; var p = Vector3.Cross(d, e2); float det = Vector3.Dot(e1, p);
                if (MathF.Abs(det) < 1e-9f) continue;
                float inv = 1f / det; var s = from - a; float u = Vector3.Dot(s, p) * inv; if (u < 0 || u > 1) continue;
                var q = Vector3.Cross(s, e1); float v = Vector3.Dot(d, q) * inv; if (v < 0 || u + v > 1) continue;
                float tt = Vector3.Dot(e2, q) * inv;
                if (tt >= 0 && tt <= 1 && (best == null || tt < best.Value.T)) best = (tt, mesh.M[t]);
            }
            return best;
        }

        private static BuildingDamageTable Table(TerrainWorld w, Action<EntityRepository> damage)
        {
            var repo = BreachFixtures.World(w);
            damage(repo);
            return DoorStates.Of(repo, w).Damage!;
        }

        // the south wall (y = 20, x 20..30): a hole x 24..26, z 0.8..2.2
        private static readonly (Vector3 From, Vector3 To) Through = (new Vector3(25f, 17f, 1.5f), new Vector3(25f, 22f, 1.5f));
        private static readonly (Vector3 From, Vector3 To) Inside = (new Vector3(25f, 22f, 1.5f), new Vector3(25f, 17f, 1.5f));
        private static readonly (Vector3 From, Vector3 To) Beside = (new Vector3(22f, 17f, 1.5f), new Vector3(22f, 22f, 1.5f));

        [Fact]
        public void ABreach_IsAnOpeningInItsWall_FromBothSides()
        {
            var w = BreachFixtures.House();
            int south = BreachFixtures.SouthWall(w);
            var table = Table(w, r => BreachFixtures.Damage(r, w, 0, new WallBreach(south, 4f, 6f, 0.8f, 2.2f, BreachKind.Hole)));
            var plain = Objects(w, null); var damaged = Objects(w, table);
            Assert.NotNull(Hit(plain, Through.From, Through.To));        // before: the wall
            Assert.Null(Hit(damaged, Through.From, Through.To));          // after: the gap, from outside …
            Assert.Null(Hit(damaged, Inside.From, Inside.To));            // … and from inside
            Assert.NotNull(Hit(damaged, Beside.From, Beside.To));         // the wall beside it stands
            Assert.NotNull(Hit(damaged, new Vector3(25f, 17f, 0.4f), new Vector3(25f, 22f, 0.4f)));   // under the hole's bottom (0.8 m)
            Assert.NotNull(Hit(damaged, new Vector3(25f, 17f, 2.6f), new Vector3(25f, 22f, 2.6f)));   // over its top (2.2 m)
            Assert.Contains(plain.M, m => m == "brick");
            Assert.True(w.SegmentBlocked(Through.From, Through.To), "the world itself is unchanged (R-4196)");
        }

        /// <summary>⭐ X-7 (user: "black explosion marks all around the hole") — just beside the opening, on both faces, the first thing a ray meets is soot; far from it, the brick.
        /// 🔴 Red-proof (compiles): the <c>AddScorch</c> call dropped ⇒ the ray meets brick.</summary>
        [Fact]
        public void ABreach_IsRingedWithSoot_OnBothFaces()
        {
            var w = BreachFixtures.House();
            int south = BreachFixtures.SouthWall(w);
            var table = Table(w, r => BreachFixtures.Damage(r, w, 0, new WallBreach(south, 4f, 6f, 0.8f, 2.2f, BreachKind.Hole)));
            var mesh = Objects(w, table);
            // x 26.1: 10 cm past the opening's east edge (x 26), at its middle height — inside every jittered rim (≥ 0.55 × the 0.7 m margin)
            Assert.Equal(TerrainWorldMesh.ScorchMaterial, Hit(mesh, new Vector3(26.1f, 17f, 1.5f), new Vector3(26.1f, 22f, 1.5f))!.Value.Material);   // the outer face
            Assert.Equal(TerrainWorldMesh.ScorchMaterial, Hit(mesh, new Vector3(26.1f, 23f, 1.5f), new Vector3(26.1f, 19f, 1.5f))!.Value.Material);   // the inner face
            Assert.Equal("brick", Hit(mesh, new Vector3(28.5f, 17f, 1.5f), new Vector3(28.5f, 22f, 1.5f))!.Value.Material);                           // far from it: the wall
            Assert.Null(Hit(mesh, Through.From, Through.To));                                                                                          // the opening itself stays open
            Assert.Equal(Objects(w, table).I.Length, mesh.I.Length);                                                                                    // the same marks every build
        }

        /// <summary>⭐ X-8 (user: "teeth, like missing bricks or bricks extending into the hole") — the rim is broken: brick stubs reach into the opening along its edges,
        /// irregularly (not every slot), never past <see cref="TerrainWorldMesh.ToothDepthShare"/> of its smaller side, so the middle stays open; the same every build.
        /// 🔴 Red-proof (compiles): the <c>AddBrokenRim</c> call dropped ⇒ no stub inside the opening.</summary>
        [Fact]
        public void ABreachsRim_IsBroken_BricksReachInButTheMiddleStaysOpen()
        {
            var w = BreachFixtures.House();
            int south = BreachFixtures.SouthWall(w);
            var table = Table(w, r => BreachFixtures.Damage(r, w, 0, new WallBreach(south, 4f, 6f, 0.8f, 2.2f, BreachKind.Hole)));
            var mesh = Objects(w, table);
            // the opening: x 24..26, z 0.8..2.2, y ≈ 20; the brick vertices inside it are the stubs
            Assert.All(Enumerable.Range(0, mesh.I.Length / 3).Where(tri => mesh.M[tri] is "brick" && Inside(tri)), tri => Assert.Fail("a stub in the opening is not burnt"));
            var inside = Enumerable.Range(0, mesh.I.Length / 3).Where(tri => mesh.M[tri] is "brick" + TerrainWorldMesh.ScorchedSuffix or "brick" + TerrainWorldMesh.SingedSuffix)
                .SelectMany(tri => new[] { mesh.V[mesh.I[3 * tri]], mesh.V[mesh.I[3 * tri + 1]], mesh.V[mesh.I[3 * tri + 2]] })
                .Where(p => p.X > 24.01f && p.X < 25.99f && p.Z > 0.81f && p.Z < 2.19f && MathF.Abs(p.Y - 20f) < 0.3f).ToList();
            Assert.True(inside.Count > 40, $"burnt brick stubs reach into the opening ({inside.Count} vertices)");
            bool Inside(int tri) { var c = (mesh.V[mesh.I[3 * tri]] + mesh.V[mesh.I[3 * tri + 1]] + mesh.V[mesh.I[3 * tri + 2]]) / 3f; return c.X > 24.05f && c.X < 25.95f && c.Z > 0.85f && c.Z < 2.15f && MathF.Abs(c.Y - 20f) < 0.3f; }
            float maxDepth = 1.4f * TerrainWorldMesh.ToothDepthShare;   // the opening's smaller side is 1.4 m
            Assert.All(inside, p => Assert.True(p.X <= 24f + maxDepth + 0.01f || p.X >= 26f - maxDepth - 0.01f || p.Z <= 0.8f + maxDepth + 0.01f || p.Z >= 2.2f - maxDepth - 0.01f,
                $"a stub at ({p.X:F2}, {p.Z:F2}) reaches past the rim's depth"));
            // irregular: along the top edge, the stubs' lowest points are not all the same depth
            var tops = inside.Where(p => p.Z > 2.2f - maxDepth - 0.01f && p.X > 24f + maxDepth && p.X < 26f - maxDepth).Select(p => MathF.Round(p.Z, 2)).Distinct().Count();
            Assert.True(tops > 3, "the top edge is ragged");
            Assert.Null(Hit(mesh, Through.From, Through.To));        // the middle stays open
            Assert.Equal(Objects(w, table).I.Length, mesh.I.Length);   // the same every build
        }

        [Fact]
        public void ACollapse_HidesTheBuilding_AndDrawsAHeap()
        {
            var w = BreachFixtures.House();
            var table = Table(w, r => BreachFixtures.Collapsed(r, w, 0));
            var damaged = Objects(w, table);
            Assert.DoesNotContain(damaged.M, m => m == "brick");          // walls gone
            var top = Hit(damaged, new Vector3(25f, 24f, 10f), new Vector3(25f, 24f, -1f));
            Assert.NotNull(top);
            Assert.Equal(BuildingDamageTable.RubbleMaterial, top!.Value.Material);
            float z = 10f - (top.Value.T * 11f);
            float heapTop = 3f * BuildingDamageTable.RubbleHeightShare;   // a third of the 3 m storey
            Assert.InRange(z, heapTop - 0.01f, heapTop + 0.9f);           // the heap's top, or a broken block lying on it (≤ 0.9 m high, sunk 40 %)
            // a heap, not a box: near the footprint's edge the ray meets the slope, lower than the top
            var edge = Hit(damaged, new Vector3(20.6f, 24f, 10f), new Vector3(20.6f, 24f, -1f));
            Assert.NotNull(edge);
            Assert.True(10f - (edge!.Value.T * 11f) < z - 0.2f, "the side slopes down to the footprint");
            Assert.NotNull(Hit(Objects(w, null), new Vector3(25f, 17f, 1.5f), new Vector3(25f, 22f, 1.5f)));   // the plain build still has the house
        }

        [Fact]
        public void OverAMusclesRebakedWorld_TheDrawingIsTheSame_NothingTwice()
        {
            var w = BreachFixtures.House();
            int south = BreachFixtures.SouthWall(w);
            var passage = Table(w, r => BreachFixtures.Damage(r, w, 0, new WallBreach(south, 4f, 6f, 0f, 2.2f, BreachKind.Passage)));
            var rebaked = TerrainObstacles.With(w, Array.Empty<TerrainPrism>(), passage);   // the muscle's derived world (the passage already cut)
            var table = Table(rebaked, r => BreachFixtures.Damage(r, rebaked, 0, new WallBreach(south, 4f, 6f, 0f, 2.2f, BreachKind.Passage)));
            var a = Objects(w, passage); var b = Objects(rebaked, table);
            Assert.Equal(a.I.Length, b.I.Length);                         // the same pieces: the rebake's own cut replaced, not cut again
            Assert.Null(Hit(b, Through.From, Through.To));

            var collapse = Table(w, r => BreachFixtures.Collapsed(r, w, 0));
            var rebakedCollapse = TerrainObstacles.With(w, Array.Empty<TerrainPrism>(), collapse);
            Assert.Contains(rebakedCollapse.Prisms, p => DamagedBuildings.IsResidentRubble(p));
            var c = Table(rebakedCollapse, r => BreachFixtures.Collapsed(r, rebakedCollapse, 0));
            var heap = Objects(rebakedCollapse, c);
            var plainHeap = Objects(w, collapse);
            Assert.Equal(plainHeap.I.Length, heap.I.Length);              // one heap, not the resident box AND the heap
        }

        [Fact]
        public void TheTouchedChunks_AreTheBuildings_AndNoDamageTouchesNothing()
        {
            var w = BreachFixtures.House();
            ITerrainChunkedGeometry q = new TerrainWorldQuery(w);
            var table = Table(w, r => BreachFixtures.Collapsed(r, w, 0));
            var touched = new List<int>();
            q.ChunksTouchedBy(null, table, touched);
            Assert.NotEmpty(touched);
            Assert.Equal(TerrainWorldMesh.ChunksOfBuilding(w, 0).OrderBy(x => x), touched.OrderBy(x => x));
            var none = new List<int>();
            q.ChunksTouchedBy(null, null, none);
            Assert.Empty(none);
            Assert.Null(q.Damage);                                        // a query with no doors has no damage
            // the render source's query carries the view's damage (WorldQuery.RenderGeometryOf → ForView)
            var repo = BreachFixtures.World(w); BreachFixtures.Collapsed(repo, w, 0);
            var viewed = (ITerrainChunkedGeometry)WorldQuery.RenderGeometryOf(repo)!;
            var drawn = Assert.IsType<BuildingDamageTable>(viewed.Damage);
            Assert.Equal(1, drawn.CollapsedCount);
            Assert.Same(viewed.Damage, ((ITerrainChunkedGeometry)WorldQuery.RenderGeometryOf(repo)!).Damage);   // unchanged ⇒ the same instance (nothing rebuilt)
        }
    }
}
