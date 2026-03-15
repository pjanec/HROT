using System;
using System.Numerics;
using Fdp.Kernel;
using Fdp.Kernel.Collections;
using Fdp.Modules.Geographic.Components;
using Fdp.Modules.Geographic.Systems;
using ModuleHost.Core.Abstractions;
using Xunit;

namespace Fdp.Modules.Geographic.Tests.Systems
{
    /// <summary>
    /// Unit tests for <see cref="TerrainQueryResolutionSystem"/>.
    /// </summary>
    public sealed class TerrainQueryResolutionSystemTests : IDisposable
    {
        private readonly EntityRepository _world;
        private Entity _entity;

        public TerrainQueryResolutionSystemTests()
        {
            _world = new EntityRepository();
            _world.RegisterComponent<GroundClampingState>();

            _entity = _world.CreateEntity();
            _world.AddComponent(_entity, new GroundClampingState
            {
                TargetZOffset       = 0f,
                CurrentZOffset      = 0f,
                LastValidIgAltitude = 10f, // seed so jump-rejection threshold applies
            });

            _world.SetSingleton(new TerrainQueryBatchData
            {
                Requests = new NativeArray<TerrainQueryRequest>(TerrainQueryBatchData.DefaultCapacity, Allocator.Persistent),
                Results  = new NativeArray<TerrainQueryResult>(TerrainQueryBatchData.DefaultCapacity,  Allocator.Persistent),
                Count    = 0,
            });
        }

        public void Dispose()
        {
            if (_world.HasSingleton<TerrainQueryBatchData>())
            {
                ref var b = ref _world.GetSingleton<TerrainQueryBatchData>();
                if (b.Requests.IsCreated) b.Requests.Dispose();
                if (b.Results.IsCreated)  b.Results.Dispose();
            }
            _world.Dispose();
        }

        private static void PlaybackCommands(EntityRepository repo)
        {
            var view = (ISimulationView)repo;
            if (view.GetCommandBuffer() is EntityCommandBuffer ecb)
                ecb.Playback(repo);
        }

        private void SetBatchEntry(Entity entity, float hitZ, float referenceSimZ, bool hasHit = true)
        {
            ref var batch = ref _world.GetSingleton<TerrainQueryBatchData>();
            batch.Count = 1;
            batch.Requests[0] = new TerrainQueryRequest
            {
                Entity        = entity,
                QueryX        = 0f,
                QueryY        = 0f,
                ReferenceSimZ = referenceSimZ,
            };
            batch.Results[0] = new TerrainQueryResult
            {
                HitZ   = hitZ,
                HasHit = hasHit,
            };
        }

        /// <summary>
        /// Jump-rejection: |16 − 10| = 6 > 5 → result must be discarded.
        /// </summary>
        [Fact]
        public void Execute_RejectsJump_WhenDeltaGreaterThan5m()
        {
            // LastValidIgAltitude = 10, HitZ = 16 → delta = 6 → reject
            SetBatchEntry(_entity, hitZ: 16f, referenceSimZ: 0f);

            var system = new TerrainQueryResolutionSystem();
            system.Execute((ISimulationView)_world, 0.016f);
            PlaybackCommands(_world);

            var state = _world.GetComponent<GroundClampingState>(_entity);
            Assert.Equal(0f, state.TargetZOffset);          // unchanged
            Assert.Equal(10f, state.LastValidIgAltitude);   // unchanged
        }

        /// <summary>
        /// Hit within threshold: |13 − 10| = 3 ≤ 5 → result accepted.
        /// TargetZOffset = HitZ(13) − ReferenceSimZ(10) = 3.
        /// </summary>
        [Fact]
        public void Execute_AcceptsHit_WhenWithin5m()
        {
            // LastValidIgAltitude = 10, HitZ = 13 → delta = 3 ≤ 5 → accept
            SetBatchEntry(_entity, hitZ: 13f, referenceSimZ: 10f);

            var system = new TerrainQueryResolutionSystem();
            system.Execute((ISimulationView)_world, 0.016f);
            PlaybackCommands(_world);

            var state = _world.GetComponent<GroundClampingState>(_entity);
            Assert.Equal(3f, state.TargetZOffset);          // 13 - 10 = 3
            Assert.Equal(13f, state.LastValidIgAltitude);   // updated to new hit
        }

        /// <summary>
        /// First-frame bootstrap: when <c>LastValidIgAltitude == 0</c> any hit should be
        /// accepted regardless of the jump-rejection threshold.
        /// </summary>
        [Fact]
        public void Execute_AcceptsFirstHit_WhenLastAltitudeIsZero()
        {
            // Override entity to have LastValidIgAltitude = 0 (first frame)
            _world.SetComponent(_entity, new GroundClampingState
            {
                TargetZOffset       = 0f,
                CurrentZOffset      = 0f,
                LastValidIgAltitude = 0f,
            });

            // HitZ = 50, would normally fail threshold check against LastValidIgAltitude = 0
            SetBatchEntry(_entity, hitZ: 50f, referenceSimZ: 45f);

            var system = new TerrainQueryResolutionSystem();
            system.Execute((ISimulationView)_world, 0.016f);
            PlaybackCommands(_world);

            var state = _world.GetComponent<GroundClampingState>(_entity);
            Assert.Equal(5f, state.TargetZOffset);          // 50 - 45 = 5
            Assert.Equal(50f, state.LastValidIgAltitude);
        }

        /// <summary>
        /// When the result has <c>HasHit = false</c> it must be ignored.
        /// </summary>
        [Fact]
        public void Execute_IgnoresMissedHit()
        {
            SetBatchEntry(_entity, hitZ: 10f, referenceSimZ: 0f, hasHit: false);

            var system = new TerrainQueryResolutionSystem();
            system.Execute((ISimulationView)_world, 0.016f);
            PlaybackCommands(_world);

            var state = _world.GetComponent<GroundClampingState>(_entity);
            Assert.Equal(0f, state.TargetZOffset);       // unchanged
            Assert.Equal(10f, state.LastValidIgAltitude); // unchanged
        }

        /// <summary>
        /// When no singleton exists the system should not throw.
        /// </summary>
        [Fact]
        public void Execute_NoThrow_WhenSingletonAbsent()
        {
            // Remove singleton
            if (_world.HasSingleton<TerrainQueryBatchData>())
            {
                ref var b = ref _world.GetSingleton<TerrainQueryBatchData>();
                if (b.Requests.IsCreated) b.Requests.Dispose();
                if (b.Results.IsCreated)  b.Results.Dispose();
            }

            var system = new TerrainQueryResolutionSystem();
            var ex = Record.Exception(() => system.Execute((ISimulationView)_world, 0.016f));
            Assert.Null(ex);
        }
    }
}
