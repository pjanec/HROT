using System;
using System.Numerics;
using CarKinem.Spatial;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Events;
using Fdp.Toolkit.Perception.Systems;
using Fdp.Core;
using Fdp.Core.Collections;
using Fdp.ModuleHost.Abstractions;
using Xunit;

namespace Fdp.Toolkit.Perception.Tests
{
    /// <summary>
    /// Unit tests for <see cref="VisionBroadphase"/> — what a unit can look at (range, FOV cone, other forces, the
    /// nearest <see cref="VisionBroadphase.MaxCandidatesPerObserver"/>), read from the perception grid.
    /// <para>⭐ <c>CE-3052</c> — re-homed from the retired <c>VisionBroadphaseSystem</c> onto the class the visual sensor
    /// calls (<c>VisualSensorGenerator</c>); the scenes and the assertions are unchanged, "a LOS request for X" is now
    /// "X is selected".</para>
    ///
    /// <b>Forward convention (critical):</b>
    ///   Forward direction is derived from <c>Vector3.Transform(Vector3.UnitX, tf.Rotation)</c>.
    ///   <c>Quaternion.Identity</c> â†’ yaw=0 â†’ facing east (+X). A target placed at (obsX+d, obsY, 0)
    ///   is directly ahead; a target at (obsX, obsY+d, 0) is 90Â° off-axis.
    /// </summary>
    public class VisionBroadphaseTests
    {
        // â”€â”€ Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// Creates a small spatial grid (100Ă—100 cells, 5 m/cell) suitable for unit tests.
        /// Caller is responsible for disposing.
        /// </summary>
        private static SpatialHashGrid CreateTestGrid() =>
            SpatialHashGrid.Create(100, 100, 5f, 1000, Allocator.Persistent);

        /// <summary>The targets the broadphase selects for <paramref name="observer"/> (its receptor's range and cone).</summary>
        private static System.Collections.Generic.List<Entity> Selected(ISimulationView view, SpatialHashGrid grid, Entity observer)
        {
            var broadphase = new VisionBroadphase();
            broadphase.Rebuild(view, grid);
            ref readonly var r = ref view.GetComponentRO<PerceptionReceptor>(observer);
            var list = new System.Collections.Generic.List<Entity>();
            foreach (var (_, target) in broadphase.Select(view, observer, r.VisionRange, r.FieldOfViewCos)) list.Add(target);
            return list;
        }

        // â”€â”€ Test 1 â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        [Fact]
        public void VisionBroadphase_EmitsLosCheckRequest_ForEnemyInFOV()
        {
            // Arrange
            var world  = PerceptionTestWorldFactory.Create();
            var view   = (ISimulationView)world;
            var grid   = CreateTestGrid();

            // Observer: Blue, at origin, facing east (Identity = yaw 0 = east).
            // FOV half-cosine = cos(30Â°) â‰ 0.866 â†’ 60Â° full FOV.
            var observer = world.CreateEntity();
            world.AddComponent(observer, new SimTransform
            {
                Position = Vector3.Zero,
                Rotation = Quaternion.Identity, // facing east
            });
            world.AddComponent(observer, new EntityInfo    { ForceId = ForceId.Friend }); // Blue
            world.AddComponent(observer, new PerceptionReceptor
            {
                VisionRange    = 200f,
                HearingRange   = 50f,
                FieldOfViewCos = MathF.Cos(MathF.PI / 6f), // cos 30Â° â‰ 0.866
            });
            world.AddComponent(observer, new TargetMemory());

            // Target: Red, directly east â€” exactly in the forward cone.
            var target = world.CreateEntity();
            world.AddComponent(target, new SimTransform
            {
                Position = new Vector3(100f, 0f, 0f), // due east of observer
                Rotation = Quaternion.Identity,
            });
            world.AddComponent(target, new EntityInfo { ForceId = ForceId.Hostile }); // Red

            // Add target to the local grid at its position.
            grid.Clear();
            grid.Add(target, new Vector2(100f, 0f));

            var selected = Selected(view, grid, observer);

            // Assert â€” exactly one LOS request emitted.
            
            Assert.Single(selected);
            Assert.Equal(target,   selected[0]);

            grid.Dispose();
        }

        // â”€â”€ Test 2 â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        [Fact]
        public void VisionBroadphase_ExcludesSameFaction_EmitsNoEvent()
        {
            // Arrange
            var world  = PerceptionTestWorldFactory.Create();
            var view   = (ISimulationView)world;
            var grid   = CreateTestGrid();

            // Observer and target both Blue â†’ same faction â†’ excluded.
            var observer = world.CreateEntity();
            world.AddComponent(observer, new SimTransform
            {
                Position = Vector3.Zero,
                Rotation = Quaternion.Identity,
            });
            world.AddComponent(observer, new EntityInfo    { ForceId = ForceId.Friend });
            world.AddComponent(observer, new PerceptionReceptor
            {
                VisionRange    = 200f,
                HearingRange   = 50f,
                FieldOfViewCos = MathF.Cos(MathF.PI / 6f), // cos(30Â°) â€” 60Â° full FOV
            });
            world.AddComponent(observer, new TargetMemory());

            var target = world.CreateEntity();
            world.AddComponent(target, new SimTransform
            {
                Position = new Vector3(50f, 0f, 0f),
                Rotation = Quaternion.Identity,
            });
            world.AddComponent(target, new EntityInfo { ForceId = ForceId.Friend }); // also Blue

            // Add target to grid (it's in the world but same faction â€” should be excluded).
            grid.Clear();
            grid.Add(target, new Vector2(50f, 0f));

            var selected = Selected(view, grid, observer);

            // Assert â€” no event because target is friendly.
            
            Assert.Empty(selected);

            grid.Dispose();
        }

        // â”€â”€ Test 3 â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        [Fact]
        public void VisionBroadphase_ExcludesTarget_BeyondVisionRange()
        {
            // Arrange
            var world  = PerceptionTestWorldFactory.Create();
            var view   = (ISimulationView)world;
            var grid   = CreateTestGrid();

            var observer = world.CreateEntity();
            world.AddComponent(observer, new SimTransform
            {
                Position = Vector3.Zero,
                Rotation = Quaternion.Identity,
            });
            world.AddComponent(observer, new EntityInfo    { ForceId = ForceId.Friend });
            world.AddComponent(observer, new PerceptionReceptor
            {
                VisionRange    = 50f,    // target is at 100 m â€” outside range
                HearingRange   = 50f,
                FieldOfViewCos = MathF.Cos(MathF.PI / 6f), // cos(30Â°) â€” 60Â° full FOV
            });
            world.AddComponent(observer, new TargetMemory());

            var target = world.CreateEntity();
            world.AddComponent(target, new SimTransform
            {
                Position = new Vector3(100f, 0f, 0f), // 100 m east â€” beyond 50 m range
                Rotation = Quaternion.Identity,
            });
            world.AddComponent(target, new EntityInfo { ForceId = ForceId.Hostile });

            // Target is in the grid at 100 m. QueryNeighbors with radius 50 m won't return it.
            grid.Clear();
            grid.Add(target, new Vector2(100f, 0f));

            var selected = Selected(view, grid, observer);

            // Assert â€” no event because target is beyond VisionRange.
            
            Assert.Empty(selected);

            grid.Dispose();
        }

        // â”€â”€ Test 4 â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        [Fact]
        public void VisionBroadphase_ExcludesTarget_OutsideFOVCone()
        {
            // Arrange
            var world  = PerceptionTestWorldFactory.Create();
            var view   = (ISimulationView)world;
            var grid   = CreateTestGrid();

            // Observer facing east (Identity). FieldOfViewCos = cos(30Â°) â‰ 0.866.
            // A target due north has dot(forward=(1,0), dir=(0,1)) = 0 < 0.866 â†’ outside cone.
            var observer = world.CreateEntity();
            world.AddComponent(observer, new SimTransform
            {
                Position = Vector3.Zero,
                Rotation = Quaternion.Identity, // yaw=0 â†’ east
            });
            world.AddComponent(observer, new EntityInfo    { ForceId = ForceId.Friend });
            world.AddComponent(observer, new PerceptionReceptor
            {
                VisionRange    = 200f,
                HearingRange   = 50f,
                FieldOfViewCos = MathF.Cos(MathF.PI / 6f), // 30Â° â†’ 60Â° full FOV
            });
            world.AddComponent(observer, new TargetMemory());

            // Target directly north â€” 90Â° off observer's forward axis.
            var target = world.CreateEntity();
            world.AddComponent(target, new SimTransform
            {
                Position = new Vector3(0f, 100f, 0f), // due north
                Rotation = Quaternion.Identity,
            });
            world.AddComponent(target, new EntityInfo { ForceId = ForceId.Hostile });

            // Add target to grid at its position (within VisionRange, but outside FOV cone).
            grid.Clear();
            grid.Add(target, new Vector2(0f, 100f));

            var selected = Selected(view, grid, observer);

            // Assert â€” dot(east, north) = 0 < 0.866 â†’ outside FOV â†’ no event.
            
            Assert.Empty(selected);

            grid.Dispose();
        }

        // â”€â”€ Test 5 (DEBT-011 isolation proof) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// DEBT-011: Proves that <see cref="VisionBroadphase"/> queries the injected
        /// local grid and does <b>not</b> perform a brute-force world scan.
        /// <para>
        /// Setup: two enemy entities are both within VisionRange and inside the FOV cone.
        /// The local grid contains only one of them. The assertion is that exactly one
        /// target is selected â€” for the entity that is in the grid.
        /// If it were scanning the world it would select both.
        /// </para>
        /// </summary>
        [Fact]
        public void VisionBroadphase_UsesLocalGrid_DoesNotBruteForce()
        {
            // Arrange
            var world  = PerceptionTestWorldFactory.Create();
            var view   = (ISimulationView)world;
            var grid   = CreateTestGrid();

            // Observer: Blue, at origin, facing east.
            var observer = world.CreateEntity();
            world.AddComponent(observer, new SimTransform
            {
                Position = Vector3.Zero,
                Rotation = Quaternion.Identity,
            });
            world.AddComponent(observer, new EntityInfo { ForceId = ForceId.Friend });
            world.AddComponent(observer, new PerceptionReceptor
            {
                VisionRange    = 200f,
                HearingRange   = 50f,
                FieldOfViewCos = MathF.Cos(MathF.PI / 6f), // cos(30Â°) â€” 60Â° full FOV
            });
            world.AddComponent(observer, new TargetMemory());

            // Target A: in the world AND in the grid.
            var targetA = world.CreateEntity();
            world.AddComponent(targetA, new SimTransform
            {
                Position = new Vector3(50f, 0f, 0f), // due east â€” inside FOV
                Rotation = Quaternion.Identity,
            });
            world.AddComponent(targetA, new EntityInfo { ForceId = ForceId.Hostile }); // Red

            // Target B: in the world but NOT in the grid.
            var targetB = world.CreateEntity();
            world.AddComponent(targetB, new SimTransform
            {
                Position = new Vector3(80f, 0f, 0f), // also due east â€” inside FOV
                Rotation = Quaternion.Identity,
            });
            world.AddComponent(targetB, new EntityInfo { ForceId = ForceId.Hostile }); // Red

            // Local grid contains only Target A.
            grid.Clear();
            grid.Add(targetA, new Vector2(50f, 0f));
            // Target B is intentionally NOT added to the grid.

            var selected = Selected(view, grid, observer);

            // Assert: only one LosCheckRequest â€” for Target A.
            // If it were scanning the world it would select both enemies.
            
            Assert.Single(selected);
            Assert.Equal(targetA,  selected[0]);

            grid.Dispose();
        }
        // ── CE-3032 ─────────────────────────────────────────────────────────────

        /// <summary>
        /// ⭐ CE-3032 — over the cap the NEAREST hostiles win, and friendlies never take a slot. 🔴 The broadphase used
        /// to keep the first 256 neighbours the cell scan reached, friendlies included, so a near enemy could be lost
        /// to a far one. 40 hostiles + 40 friendlies, all in range ⇒ exactly the 32 nearest hostiles are checked.
        /// </summary>
        [Fact]
        public void VisionBroadphase_OverTheCap_ChecksTheNearestHostiles_FriendliesTakeNoSlot_CE3032()
        {
            var world = PerceptionTestWorldFactory.Create();
            var view  = (ISimulationView)world;
            var grid  = CreateTestGrid();
            grid.Clear();

            var observer = world.CreateEntity();
            world.AddComponent(observer, new SimTransform { Position = new Vector3(250f, 250f, 0f), Rotation = Quaternion.Identity });
            world.AddComponent(observer, new EntityInfo { ForceId = ForceId.Friend });
            world.AddComponent(observer, new PerceptionReceptor { VisionRange = 240f, FieldOfViewCos = -1f });

            var hostiles = new System.Collections.Generic.List<(Entity E, float D)>();
            for (int i = 0; i < 40; i++)
            {
                // Hostiles on a spiral, distances 10..205 m; friendlies placed CLOSER than every hostile.
                float d = 10f + i * 5f, a = i * 0.7f;
                var h = world.CreateEntity();
                var hp = new Vector2(250f + d * MathF.Cos(a), 250f + d * MathF.Sin(a));
                world.AddComponent(h, new SimTransform { Position = new Vector3(hp, 0f), Rotation = Quaternion.Identity });
                world.AddComponent(h, new EntityInfo { ForceId = ForceId.Hostile });
                grid.Add(h, hp);
                hostiles.Add((h, d));

                var f = world.CreateEntity();
                var fp = new Vector2(250f + 2f * MathF.Cos(a), 250f + 2f * MathF.Sin(a));
                world.AddComponent(f, new SimTransform { Position = new Vector3(fp, 0f), Rotation = Quaternion.Identity });
                world.AddComponent(f, new EntityInfo { ForceId = ForceId.Friend });
                grid.Add(f, fp);
            }

            var selected = Selected(view, grid, observer);

            
            Assert.Equal(VisionBroadphase.MaxCandidatesPerObserver, selected.Count);
            var expected = new System.Collections.Generic.HashSet<Entity>();
            for (int i = 0; i < VisionBroadphase.MaxCandidatesPerObserver; i++) expected.Add(hostiles[i].E);
            foreach (var e in selected) Assert.Contains(e, expected);

            grid.Dispose();
        }

        /// <summary>
        /// ⭐ CE-3032 — the coarse index finds exactly what the fine grid holds: a target just inside the range on a
        /// coarse-cell boundary is checked, one just outside is not, and an entity in the world but not in the grid is
        /// still not seen (the grid stays the source of truth).
        /// </summary>
        [Fact]
        public void VisionBroadphase_CoarseIndex_RangeEdgesAndGridMembership_CE3032()
        {
            var world = PerceptionTestWorldFactory.Create();
            var view  = (ISimulationView)world;
            var grid  = CreateTestGrid();
            grid.Clear();

            var observer = world.CreateEntity();
            world.AddComponent(observer, new SimTransform { Position = new Vector3(49.5f, 10f, 0f), Rotation = Quaternion.Identity });
            world.AddComponent(observer, new EntityInfo { ForceId = ForceId.Friend });
            world.AddComponent(observer, new PerceptionReceptor { VisionRange = 100f, FieldOfViewCos = -1f });

            Entity Hostile(float x, float y, bool inGrid)
            {
                var e = world.CreateEntity();
                world.AddComponent(e, new SimTransform { Position = new Vector3(x, y, 0f), Rotation = Quaternion.Identity });
                world.AddComponent(e, new EntityInfo { ForceId = ForceId.Hostile });
                if (inGrid) grid.Add(e, new Vector2(x, y));
                return e;
            }
            var inside   = Hostile(149.5f, 10f, true);   // exactly 100 m — inclusive, in the next coarse cells
            var outside  = Hostile(149.6f, 10f, true);   // 100.1 m
            var notInGrid = Hostile(60f, 10f, false);

            var selected = Selected(view, grid, observer);


            Assert.Single(selected);
            Assert.Equal(inside, selected[0]);
            _ = outside; _ = notInGrid;
            grid.Dispose();
        }

        // ── CE-3146 — the chain guard ────────────────────────────────────────

        /// <summary>
        /// ⭐⭐⭐ <c>CE-3146</c> — <b>a MALFORMED grid chain must not hang <see cref="VisionBroadphase.Rebuild"/>.</b>
        /// 📌 The user reported the editor "easily gets stuck, looping inside Rebuild"; the walk follows the perception
        /// grid's intrusive linked list and terminates only when that list does.
        /// <para>⚠ <b>This rail HANGS FOREVER without the guard</b> — that is the point, and it is why it carries an
        /// explicit timeout rather than just calling Rebuild: a plain call would wedge the whole test run instead of
        /// failing it. ⭐ The red-proof is to delete the `steps > slotCapacity` branch and watch this time out.</para>
        /// </summary>
        [Fact]
        public void CE3146_ASelfCyclingGridChain_DoesNotHangRebuild()
        {
            var world = PerceptionTestWorldFactory.Create();
            var view  = (ISimulationView)world;
            var grid  = CreateTestGrid();
            try
            {
                var target = world.CreateEntity();
                world.AddComponent(target, new SimTransform { Position = new Vector3(100f, 0f, 0f), Rotation = Quaternion.Identity });
                world.AddComponent(target, new EntityInfo  { ForceId = ForceId.Hostile });
                grid.Add(target, new Vector2(100f, 0f));

                // Corrupt the list exactly as a double-handed-out slot would: the cell's head points at itself.
                int corruptedCell = -1;
                for (int c = 0; c < grid.Width * grid.Height; c++)
                {
                    if (grid.GridHead[c] >= 0) { corruptedCell = c; break; }
                }
                Assert.True(corruptedCell >= 0, "the target was not inserted — the fixture, not the guard, is wrong");
                int head = grid.GridHead[corruptedCell];
                grid.GridNext[head] = head;          // 🔴 self-cycle

                var broadphase = new VisionBroadphase();
                var rebuilt = System.Threading.Tasks.Task.Run(() => broadphase.Rebuild(view, grid));

                Assert.True(rebuilt.Wait(TimeSpan.FromSeconds(10)),
                    "Rebuild did not return within 10 s on a self-cycling chain — the CE-3146 guard is missing or "
                  + "its bound is wrong, and the editor will hang here.");
                Assert.Null(rebuilt.Exception);      // ⛔ it must DEGRADE, not throw: this runs on a module thread
            }
            finally
            {
                grid.Dispose();
            }
        }
    }
}
