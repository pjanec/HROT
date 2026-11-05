using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Spatial;
using Xunit;

namespace Fdp.Toolkits.Tests.Spatial
{
    /// <summary>
    /// ⭐ <c>CE-2411</c> — the shared per-frame entity grid (<see cref="FrameEntityGrid"/>) the "who is near / on this line" rules walk: every
    /// entity within the walk's reach is a candidate (circle, and the cells ALONG a segment — negative coordinates, lines across many
    /// cells, a zero-length line); it rebuilds when the frame moves; an entity that joins within the frame (a destroy and a create recycling one index included) is found from the late list; a move does not rebuild; it allocates nothing
    /// once grown. 🔴 Red-proofs: the segment walk's per-cell reach without the half-diagonal ⇒ entities near the line are missed; the late list not walked ⇒ the late entity is missed.
    /// </summary>
    public sealed class FrameEntityGridTests : IDisposable
    {
        private readonly EntityRepository _w = new();
        public FrameEntityGridTests() => _w.RegisterComponent<SimTransform>();
        public void Dispose() => _w.Dispose();

        private static readonly Func<EntityRepository, Entity, bool> All = static (_, _) => true;

        private Entity At(float x, float y)
        {
            var e = _w.CreateEntity();
            _w.AddComponent(e, new SimTransform { Position = new Vector3(x, y, 5f), Rotation = Quaternion.Identity });
            return e;
        }

        private static float SegDist2(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a; float l2 = ab.LengthSquared();
            var q = l2 > 1e-12f ? a + ab * Math.Clamp(Vector2.Dot(p - a, ab) / l2, 0f, 1f) : a;
            return Vector2.DistanceSquared(p, q);
        }

        [Fact]
        public void CE2411_TheWalks_FindEveryEntityWithinReach_AndRebuildOnlyWhenTheFrameOrTheSetChanges()
        {
            var rng = new Random(2411);
            var all = new Entity[500];
            for (int i = 0; i < all.Length; i++) all[i] = At(rng.NextSingle() * 800f - 400f, rng.NextSingle() * 800f - 400f);
            var query = _w.Query().With<SimTransform>().Build();
            var grid = new FrameEntityGrid();
            Assert.True(grid.EnsureFresh(_w, query, All));
            Assert.Equal(500, grid.Count);

            var hit = new bool[_w.MaxEntityIndex + 1];
            for (int q = 0; q < 400; q++)
            {
                var a = new Vector2(rng.NextSingle() * 800f - 400f, rng.NextSingle() * 800f - 400f);
                var b = q % 10 == 0 ? a : new Vector2(rng.NextSingle() * 800f - 400f, rng.NextSingle() * 800f - 400f);
                float pad = rng.NextSingle() * 30f;
                Array.Clear(hit);
                grid.Segment(a, b, pad);
                foreach (int k in grid.Found) hit[grid.EntityAt(k).Index] = true;
                foreach (var e in all)
                {
                    var p = _w.GetComponentRO<SimTransform>(e).Position;
                    bool within = SegDist2(new Vector2(p.X, p.Y), a, b) <= pad * pad;
                    Assert.Equal(within, hit[e.Index]);
                }
                bool any = false;   // ⭐ CE-2413 — AnyWithin agrees with the scan
                foreach (var e in all)
                {
                    var p = _w.GetComponentRO<SimTransform>(e).Position;
                    if (Vector2.DistanceSquared(new Vector2(p.X, p.Y), a) <= pad * pad) { any = true; break; }
                }
                Assert.Equal(any, grid.AnyWithin(a, pad));
                Array.Clear(hit);
                grid.Circle(a, pad);
                foreach (int k in grid.Found) hit[grid.EntityAt(k).Index] = true;
                foreach (var e in all)
                {
                    var p = _w.GetComponentRO<SimTransform>(e).Position;
                    Assert.Equal(Vector2.DistanceSquared(new Vector2(p.X, p.Y), a) <= pad * pad, hit[e.Index]);
                }
            }

            _w.GetComponentRW<SimTransform>(all[0]).Position = new Vector3(9000f, 0f, 0f);
            Assert.False(grid.EnsureFresh(_w, query, All));                 // a move within the frame: the frame's cells stand
            _w.DestroyEntity(all[1]);
            var late = At(1000f, 1000f);                                   // recycles all[1]'s index: the count does not move
            Assert.Equal(all[1].Index, late.Index);
            Assert.False(grid.EnsureFresh(_w, query, All));                // no rebuild: it joins the late list
            Assert.Equal(1, grid.LateCount);
            grid.Circle(new Vector2(1000f, 1000f), 1f);
            Assert.Equal(1, grid.Found.Length);
            Assert.Equal(late, grid.EntityAt(grid.Found[0]));
            grid.Segment(new Vector2(990f, 999f), new Vector2(1010f, 1001f), 2f);
            Assert.Equal(1, grid.Found.Length);
            _w.Tick();
            Assert.True(grid.EnsureFresh(_w, query, All));                  // a new frame: rebuilt

            grid.Segment(new Vector2(-400f, -400f), new Vector2(400f, 400f), 20f);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
            {
                grid.Segment(new Vector2(-400f, i), new Vector2(400f, -i), 20f);
                grid.Circle(new Vector2(i, i), 25f);
            }
            _w.Tick();
            grid.EnsureFresh(_w, query, All);
            Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        }
    }
}
