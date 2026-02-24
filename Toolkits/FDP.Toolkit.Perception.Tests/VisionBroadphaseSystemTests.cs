using System;
using System.Numerics;
using CarKinem.Spatial;
using FDP.Toolkit.Perception.Components;
using FDP.Toolkit.Perception.Events;
using FDP.Toolkit.Perception.Systems;
using Fdp.Kernel;
using Fdp.Kernel.Collections;
using ModuleHost.Core.Abstractions;
using Xunit;

namespace FDP.Toolkit.Perception.Tests
{
    /// <summary>
    /// Unit tests for <see cref="VisionBroadphaseSystem"/>.
    ///
    /// Test pattern (IModuleSystem):
    ///   1. Build world via <see cref="PerceptionTestWorldFactory"/>.
    ///   2. Create a <see cref="SpatialHashGrid"/>, populate it with target entities.
    ///   3. Cast world to <see cref="ISimulationView"/> â€” EntityRepository implements it natively.
    ///   4. <c>sys.Execute(view, dt)</c>.
    ///   5. Flush the ECB: <c>((EntityCommandBuffer)view.GetCommandBuffer()).Playback(world)</c>.
    ///   6. Swap buffers to move published events to the readable slot.
    ///   7. Assert <c>world.Bus.Consume&lt;LosCheckRequestEvent&gt;()</c>.
    ///   8. Dispose the grid.
    ///
    /// <b>Forward convention (critical):</b>
    ///   Forward direction is derived from <c>Vector3.Transform(Vector3.UnitX, tf.Rotation)</c>.
    ///   <c>Quaternion.Identity</c> â†’ yaw=0 â†’ facing east (+X). A target placed at (obsX+d, obsY, 0)
    ///   is directly ahead; a target at (obsX, obsY+d, 0) is 90Â° off-axis.
    /// </summary>
    public class VisionBroadphaseSystemTests
    {
        // â”€â”€ Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// Creates a small spatial grid (100Ă—100 cells, 5 m/cell) suitable for unit tests.
        /// Caller is responsible for disposing.
        /// </summary>
        private static SpatialHashGrid CreateTestGrid() =>
            SpatialHashGrid.Create(100, 100, 5f, 1000, Allocator.Persistent);

        /// <summary>
        /// Flushes the ECB written by <see cref="IModuleSystem.Execute"/> back to
        /// the live world, then swaps event buffers so <c>Bus.Consume</c> can read them.
        /// Must be called on the same thread as Execute.
        /// </summary>
        private static void FlushEcbAndSwap(ISimulationView view, EntityRepository world)
        {
            var ecb = (EntityCommandBuffer)view.GetCommandBuffer();
            ecb.Playback(world);
            world.Bus.SwapBuffers();
        }

        // â”€â”€ Test 1 â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        [Fact]
        public void VisionBroadphase_EmitsLosCheckRequest_ForEnemyInFOV()
        {
            // Arrange
            var world  = PerceptionTestWorldFactory.Create();
            var view   = (ISimulationView)world;
            var grid   = CreateTestGrid();
            var sys    = new VisionBroadphaseSystem(grid);

            // Observer: Blue, at origin, facing east (Identity = yaw 0 = east).
            // FOV half-cosine = cos(30Â°) â‰ 0.866 â†’ 60Â° full FOV.
            var observer = world.CreateEntity();
            world.AddComponent(observer, new SimTransform
            {
                Position = Vector3.Zero,
                Rotation = Quaternion.Identity, // facing east
            });
            world.AddComponent(observer, new Faction    { FactionId = 1 }); // Blue
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
            world.AddComponent(target, new Faction { FactionId = 2 }); // Red

            // Add target to the local grid at its position.
            grid.Clear();
            grid.Add(target, new Vector2(100f, 0f));

            // Act
            sys.Execute(view, 0.1f);
            FlushEcbAndSwap(view, world);

            // Assert â€” exactly one LOS request emitted.
            var events = world.Bus.Consume<LosCheckRequestEvent>();
            Assert.Equal(1, events.Length);
            Assert.Equal(observer.Index, events[0].ObserverEntityIndex);
            Assert.Equal(target.Index,   events[0].TargetEntityIndex);

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
            var sys    = new VisionBroadphaseSystem(grid);

            // Observer and target both Blue â†’ same faction â†’ excluded.
            var observer = world.CreateEntity();
            world.AddComponent(observer, new SimTransform
            {
                Position = Vector3.Zero,
                Rotation = Quaternion.Identity,
            });
            world.AddComponent(observer, new Faction    { FactionId = 1 });
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
            world.AddComponent(target, new Faction { FactionId = 1 }); // also Blue

            // Add target to grid (it's in the world but same faction â€” should be excluded).
            grid.Clear();
            grid.Add(target, new Vector2(50f, 0f));

            // Act
            sys.Execute(view, 0.1f);
            FlushEcbAndSwap(view, world);

            // Assert â€” no event because target is friendly.
            var events = world.Bus.Consume<LosCheckRequestEvent>();
            Assert.Equal(0, events.Length);

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
            var sys    = new VisionBroadphaseSystem(grid);

            var observer = world.CreateEntity();
            world.AddComponent(observer, new SimTransform
            {
                Position = Vector3.Zero,
                Rotation = Quaternion.Identity,
            });
            world.AddComponent(observer, new Faction    { FactionId = 1 });
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
            world.AddComponent(target, new Faction { FactionId = 2 });

            // Target is in the grid at 100 m. QueryNeighbors with radius 50 m won't return it.
            grid.Clear();
            grid.Add(target, new Vector2(100f, 0f));

            // Act
            sys.Execute(view, 0.1f);
            FlushEcbAndSwap(view, world);

            // Assert â€” no event because target is beyond VisionRange.
            var events = world.Bus.Consume<LosCheckRequestEvent>();
            Assert.Equal(0, events.Length);

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
            var sys    = new VisionBroadphaseSystem(grid);

            // Observer facing east (Identity). FieldOfViewCos = cos(30Â°) â‰ 0.866.
            // A target due north has dot(forward=(1,0), dir=(0,1)) = 0 < 0.866 â†’ outside cone.
            var observer = world.CreateEntity();
            world.AddComponent(observer, new SimTransform
            {
                Position = Vector3.Zero,
                Rotation = Quaternion.Identity, // yaw=0 â†’ east
            });
            world.AddComponent(observer, new Faction    { FactionId = 1 });
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
            world.AddComponent(target, new Faction { FactionId = 2 });

            // Add target to grid at its position (within VisionRange, but outside FOV cone).
            grid.Clear();
            grid.Add(target, new Vector2(0f, 100f));

            // Act
            sys.Execute(view, 0.1f);
            FlushEcbAndSwap(view, world);

            // Assert â€” dot(east, north) = 0 < 0.866 â†’ outside FOV â†’ no event.
            var events = world.Bus.Consume<LosCheckRequestEvent>();
            Assert.Equal(0, events.Length);

            grid.Dispose();
        }

        // â”€â”€ Test 5 (DEBT-011 isolation proof) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// DEBT-011: Proves that <see cref="VisionBroadphaseSystem"/> queries the injected
        /// local grid and does <b>not</b> perform a brute-force world scan.
        /// <para>
        /// Setup: two enemy entities are both within VisionRange and inside the FOV cone.
        /// The local grid contains only one of them. The assertion is that exactly one
        /// <see cref="LosCheckRequestEvent"/> is emitted â€” for the entity that is in the grid.
        /// If the system were scanning the world it would emit two events.
        /// </para>
        /// </summary>
        [Fact]
        public void VisionBroadphase_UsesLocalGrid_DoesNotBruteForce()
        {
            // Arrange
            var world  = PerceptionTestWorldFactory.Create();
            var view   = (ISimulationView)world;
            var grid   = CreateTestGrid();
            var sys    = new VisionBroadphaseSystem(grid);

            // Observer: Blue, at origin, facing east.
            var observer = world.CreateEntity();
            world.AddComponent(observer, new SimTransform
            {
                Position = Vector3.Zero,
                Rotation = Quaternion.Identity,
            });
            world.AddComponent(observer, new Faction { FactionId = 1 });
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
            world.AddComponent(targetA, new Faction { FactionId = 2 }); // Red

            // Target B: in the world but NOT in the grid.
            var targetB = world.CreateEntity();
            world.AddComponent(targetB, new SimTransform
            {
                Position = new Vector3(80f, 0f, 0f), // also due east â€” inside FOV
                Rotation = Quaternion.Identity,
            });
            world.AddComponent(targetB, new Faction { FactionId = 2 }); // Red

            // Local grid contains only Target A.
            grid.Clear();
            grid.Add(targetA, new Vector2(50f, 0f));
            // Target B is intentionally NOT added to the grid.

            // Act
            sys.Execute(view, 0.1f);
            FlushEcbAndSwap(view, world);

            // Assert: only one LosCheckRequest â€” for Target A.
            // If the system were scanning the world it would emit two events (one per enemy).
            var events = world.Bus.Consume<LosCheckRequestEvent>();
            Assert.Equal(1, events.Length);
            Assert.Equal(observer.Index, events[0].ObserverEntityIndex);
            Assert.Equal(targetA.Index,  events[0].TargetEntityIndex);

            grid.Dispose();
        }
    }
}
