using System;
using System.Collections.Generic;
using System.Numerics;
using Bagira.Map.Common;
using Bagira.SimHost.Integration.Tests.Infrastructure;
using CarKinem.Core;
using Fdp.Kernel;
using Xunit;

namespace Bagira.SimHost.Integration.Tests
{
    /// <summary>
    /// TASK-S6.3 — Performance Testing.
    ///
    /// Verifies that the SimHost simulation loop can sustain 60 Hz with 100 active tank
    /// entities running vehicle physics, spatial hashing, and geographic egress every frame:
    ///
    ///   1. 100 Tank entities are spawned, each with a unique spawn position and a distant
    ///      navigation target so physics are active throughout the run.
    ///   2. Performance metrics collection is enabled.
    ///   3. 3 600 ticks (60 seconds at 60 Hz) are run.
    ///   4. Measured average FPS must be ≥ 58; measured minimum FPS must be ≥ 55.
    ///
    /// The soft lower bound (55 FPS min) accommodates JIT warm-up and scheduler jitter in
    /// CI environments while still catching regressions that would degrade real-time fidelity.
    /// </summary>
    public sealed class PerformanceTests : IDisposable
    {
        private const int EntityCount         = 100;
        private const int TickCount           = 3600;      // 60 seconds @ 60 Hz
        private const float MinAverageFPS     = 58f;
        private const float MinFPS            = 55f;

        private readonly SimHostInstance _host;

        public PerformanceTests() => _host = new SimHostInstance();

        public void Dispose() => _host.Dispose();

        // ── Tests ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 100 entities, 60 seconds of simulation at 60 Hz.
        /// Average FPS must be ≥ 58; minimum FPS per frame must be ≥ 55.
        /// </summary>
        [Fact]
        public void Performance_100Entities_Maintains60Hz()
        {
            // ── Spawn 100 entities spread in a 500 × 500 m grid ──────────────────────────
            var entityIds = new List<int>(EntityCount);
            const float spacing = 50f;          // 50 m between entities
            int sideLength = (int)MathF.Ceiling(MathF.Sqrt(EntityCount));  // 10

            int spawned = 0;
            for (int row = 0; row < sideLength && spawned < EntityCount; row++)
            {
                for (int col = 0; col < sideLength && spawned < EntityCount; col++, spawned++)
                {
                    var ack = _host.CreateEntity(TkbEntityTypes.Tank_M1Abrams);
                    Assert.Equal(0, ack.ErrorCode);
                    entityIds.Add(ack.NewEntityId);

                    // Place each tank at a distinct position and point it toward a distant target.
                    if (_host.EntityMap.TryGetEntity(ack.NewEntityId, out var entity))
                    {
                        // Spawn position: grid layout
                        var pos = new Vector3(col * spacing, row * spacing, 0f);
                        var tf  = _host.World.GetComponent<SimTransform>(entity);
                        tf = new SimTransform
                        {
                            Position = pos,
                            Rotation = System.Numerics.Quaternion.Identity,
                        };
                        _host.World.SetComponent(entity, tf);

                        // Navigation target: 2 km north-east so vehicles are always moving.
                        var nav = _host.World.GetComponent<NavState>(entity);
                        nav.Mode             = NavigationMode.Direct;
                        nav.FinalDestination = new Vector2(pos.X + 2000f, pos.Y + 2000f);
                        nav.TargetSpeed      = 15.0f;
                        nav.ArrivalRadius    = 5.0f;
                        nav.HasArrived       = 0;
                        _host.World.SetComponent(entity, nav);
                    }
                }
            }

            Assert.Equal(EntityCount, entityIds.Count);

            // ── Enable metrics and warm up the JIT (avoid measuring cold startup) ─────────
            _host.RunForTicks(60);   // 1-second warm-up at 60 Hz (not measured)
            _host.EnablePerformanceMetrics();

            // ── Run 60 seconds at 60 Hz ───────────────────────────────────────────────────
            _host.RunForTicks(TickCount);

            // ── Assert frame-rate stability ───────────────────────────────────────────────
            var metrics = _host.GetPerformanceMetrics();

            Assert.True(
                metrics.FrameCount >= TickCount,
                $"Expected {TickCount} measured frames but got {metrics.FrameCount}.");

            Assert.True(
                metrics.AverageFPS >= MinAverageFPS,
                $"Average FPS {metrics.AverageFPS:F1} is below the required {MinAverageFPS} FPS " +
                $"(min={metrics.MinFPS:F1}, max={metrics.MaxFPS:F1}, frames={metrics.FrameCount}).");

            Assert.True(
                metrics.MinFPS >= MinFPS,
                $"Minimum FPS {metrics.MinFPS:F1} dropped below {MinFPS} FPS. " +
                $"(avg={metrics.AverageFPS:F1}, max={metrics.MaxFPS:F1}, frames={metrics.FrameCount}).");
        }

        /// <summary>
        /// Smoke test — single entity still achieves ≥ 58 FPS average over 1 000 ticks
        /// (~16 seconds).  Confirms that the test infrastructure overhead is not itself
        /// the bottleneck.
        /// </summary>
        [Fact]
        public void Performance_SingleEntity_OverheadIsNegligible()
        {
            var ack = _host.CreateEntity(TkbEntityTypes.Tank_M1Abrams);
            Assert.Equal(0, ack.ErrorCode);

            if (_host.EntityMap.TryGetEntity(ack.NewEntityId, out var entity))
            {
                var nav = _host.World.GetComponent<NavState>(entity);
                nav.Mode             = NavigationMode.Direct;
                nav.FinalDestination = new Vector2(5000f, 5000f);
                nav.TargetSpeed      = 15.0f;
                nav.ArrivalRadius    = 5.0f;
                nav.HasArrived       = 0;
                _host.World.SetComponent(entity, nav);
            }

            _host.RunForTicks(60);   // JIT warm-up
            _host.EnablePerformanceMetrics();
            _host.RunForTicks(1000);

            var metrics = _host.GetPerformanceMetrics();

            Assert.True(
                metrics.AverageFPS >= MinAverageFPS,
                $"Single-entity average FPS {metrics.AverageFPS:F1} < {MinAverageFPS}.");
        }
    }
}
