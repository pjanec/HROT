using System;
using System.Numerics;
using BenchmarkDotNet.Attributes;
using CarKinem.Spatial;
using CarKinem.Systems;
using Fdp.Core;
using Fdp.Core.Collections;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Physics;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Physics.Systems;

namespace Fdp.ModuleHost.Benchmarks
{
    /// <summary>
    /// ⭐⭐ <c>CE-3209</c> (docs/designs/hit-location/DESIGN_Body_Parts_And_Hit_Location.md HL-2, S1a) — the crowd benchmark: 300 people in a
    /// 100 × 20 m street and a burst of 500 rounds through it, one solver pass. <c>Old</c> = the midpoint disc + the circle and band (before
    /// CE-3209); <c>CellWalk</c> = the new broad phase with the old narrow phase; <c>Parts</c> = the new path (cell walk + sphere + parts).
    /// Run: <c>dotnet run -c Release --project FDP/Engine/Fdp.ModuleHost.Benchmarks -- crowd</c>.
    /// </summary>
    [ShortRunJob]
    [MemoryDiagnoser]
    public class CrowdRaycastBenchmark
    {
        public enum Path { Old, CellWalk, Parts }

        [Params(Path.Old, Path.CellWalk, Path.Parts)]
        public Path Mode { get; set; }

        private EntityRepository _world = null!;
        private RaycastSolverSystem _solver = null!;
        private RaycastRequestEvent[] _burst = null!;

        [GlobalSetup]
        public void Setup()
        {
            _world = new EntityRepository();
            _world.RegisterComponent<SimTransform>();
            _world.RegisterComponent<SimVelocity>();
            _world.RegisterComponent<PhysicsCollider>();
            _world.RegisterComponent<SpatialGridData>();
            _world.RegisterComponent<global::CarKinem.Core.VehicleParams>();
            _world.RegisterEvent<RaycastRequestEvent>();
            _world.RegisterEvent<RaycastResultEvent>();
            _world.SetSingleton(new RaycastBatchData { Hits = new NativeArray<RaycastHit>(PhysicsConstants.RaycastBatchCapacity, Allocator.Persistent) });
            var rng = new Random(3209);
            for (int i = 0; i < 300; i++)
            {
                var e = _world.CreateEntity();
                _world.AddComponent(e, new SimTransform
                {
                    Position = new Vector3((float)rng.NextDouble() * 100f, (float)rng.NextDouble() * 20f, 0f),
                    Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)(rng.NextDouble() * Math.PI * 2)),
                });
                _world.AddComponent(e, new PhysicsCollider { Radius = 0.4f, CollisionLayer = 1, Height = 1.8f });
                _world.AddComponent(e, new global::CarKinem.Core.VehicleParams { Class = global::CarKinem.Core.VehicleClass.Pedestrian });
            }
            new SpatialHashSystem().Execute(_world, 0.016f);
            _burst = new RaycastRequestEvent[500];
            for (int i = 0; i < _burst.Length; i++)
            {
                float y = (float)rng.NextDouble() * 20f, z = 0.2f + (float)rng.NextDouble() * 1.6f;
                float x0 = (float)rng.NextDouble() * 60f;
                _burst[i] = new RaycastRequestEvent
                {
                    Start = new Vector3(x0, y, z), End = new Vector3(x0 + 40f, y + (float)(rng.NextDouble() - 0.5) * 4f, z),   // a 40 m step
                    RayId = PhysicsConstants.PackBulletRayId(i + 1), LayerMask = 1,
                };
            }
            _solver = new RaycastSolverSystem { PersonParts = Mode == Path.Parts, CellWalk = Mode != Path.Old };
        }

        [Benchmark]
        public void Burst()
        {
            foreach (var r in _burst) _world.Bus.Publish(r);
            _world.Bus.SwapBuffers();
            _solver.Execute(_world, 0.016f);
            var cmd = (EntityCommandBuffer)((ISimulationView)_world).GetCommandBuffer();
            cmd.Playback(_world);
            _world.Bus.SwapBuffers();
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            ref var b = ref _world.GetSingleton<RaycastBatchData>();
            if (b.Hits.IsCreated) b.Hits.Dispose();
            _world.Dispose();
        }
    }
}
