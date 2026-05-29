using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Core.Collections;
using Fdp.Modules.Geographic.Components;
using Fdp.Modules.Geographic.Systems;
using Fdp.ModuleHost.Abstractions;
using Xunit;

namespace Fdp.Modules.Geographic.Tests.Systems
{
    /// <summary>
    /// Unit tests for <see cref="TerrainQueryResolutionSystem"/> after the 3D Cognitive Spatial
    /// Awareness promotion (P3D-102): an accepted terrain hit writes <c>HitZ</c> into the
    /// authoritative <c>SimTransform.Position.Z</c> (X/Y/rotation preserved) and advances the
    /// <see cref="TerrainClampBaseline"/> jump-rejection baseline. No visual offset is computed.
    /// </summary>
    public sealed class TerrainQueryResolutionSystemTests : IDisposable
    {
        private const float InitialX = 3f;
        private const float InitialY = 4f;
        private const float InitialZ = 1f;

        private readonly EntityRepository _world;
        private Entity _entity;

        public TerrainQueryResolutionSystemTests()
        {
            _world = new EntityRepository();
            _world.RegisterComponent<TerrainClampBaseline>();
            _world.RegisterComponent<SimTransform>();

            _entity = _world.CreateEntity();
            _world.AddComponent(_entity, new TerrainClampBaseline
            {
                LastValidIgAltitude = 10f, // seed so jump-rejection threshold applies
                IgAltitudeBaselineEstablished = 1,
            });
            _world.AddComponent(_entity, new SimTransform
            {
                Position = new Vector3(InitialX, InitialY, InitialZ),
                Rotation = Quaternion.Identity,
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

        private void RunOnce()
        {
            var system = new TerrainQueryResolutionSystem();
            system.Execute((ISimulationView)_world, 0.016f);
            PlaybackCommands(_world);
        }

        /// <summary>Jump-rejection: |16 − 10| = 6 > 5 → discarded; Z and baseline unchanged.</summary>
        [Fact]
        public void Execute_RejectsJump_WhenDeltaGreaterThan5m()
        {
            SetBatchEntry(_entity, hitZ: 16f, referenceSimZ: 0f);
            RunOnce();

            var tf = _world.GetComponent<SimTransform>(_entity);
            var state = _world.GetComponent<TerrainClampBaseline>(_entity);
            Assert.Equal(InitialZ, tf.Position.Z);        // Z unchanged on rejection
            Assert.Equal(10f, state.LastValidIgAltitude); // baseline unchanged
        }

        /// <summary>Hit within threshold: |13 − 10| = 3 ≤ 5 → accepted; Z := 13, X/Y unchanged.</summary>
        [Fact]
        public void Execute_AcceptsHit_WhenWithin5m_WritesAuthoritativeZ()
        {
            SetBatchEntry(_entity, hitZ: 13f, referenceSimZ: 10f);
            RunOnce();

            var tf = _world.GetComponent<SimTransform>(_entity);
            var state = _world.GetComponent<TerrainClampBaseline>(_entity);
            Assert.Equal(13f, tf.Position.Z);             // authoritative altitude written
            Assert.Equal(InitialX, tf.Position.X);        // X preserved
            Assert.Equal(InitialY, tf.Position.Y);        // Y preserved
            Assert.Equal(13f, state.LastValidIgAltitude); // baseline advanced
            Assert.Equal((byte)1, state.IgAltitudeBaselineEstablished);
        }

        /// <summary>First accepted hit (bootstrap): any magnitude accepted while baseline unset.</summary>
        [Fact]
        public void Execute_AcceptsFirstHit_RegardlessOfMagnitude()
        {
            _world.SetComponent(_entity, new TerrainClampBaseline
            {
                LastValidIgAltitude = 0f,
                IgAltitudeBaselineEstablished = 0, // bootstrap
            });

            // HitZ = 50 would fail the ±5 m threshold against baseline 0, but bootstrap accepts it.
            SetBatchEntry(_entity, hitZ: 50f, referenceSimZ: 45f);
            RunOnce();

            var tf = _world.GetComponent<SimTransform>(_entity);
            var state = _world.GetComponent<TerrainClampBaseline>(_entity);
            Assert.Equal(50f, tf.Position.Z);
            Assert.Equal(50f, state.LastValidIgAltitude);
            Assert.Equal((byte)1, state.IgAltitudeBaselineEstablished);
        }

        /// <summary>Two-step: bootstrap then a within-threshold hit accepted and Z updated.</summary>
        [Fact]
        public void Execute_SecondHitWithinThreshold_UpdatesZ()
        {
            _world.SetComponent(_entity, new TerrainClampBaseline
            {
                LastValidIgAltitude = 0f,
                IgAltitudeBaselineEstablished = 0,
            });

            SetBatchEntry(_entity, hitZ: 20f, referenceSimZ: 0f); // bootstrap accept
            RunOnce();
            Assert.Equal(20f, _world.GetComponent<SimTransform>(_entity).Position.Z);

            SetBatchEntry(_entity, hitZ: 23f, referenceSimZ: 0f); // |23-20|=3 ≤ 5 accept
            RunOnce();
            Assert.Equal(23f, _world.GetComponent<SimTransform>(_entity).Position.Z);
            Assert.Equal(23f, _world.GetComponent<TerrainClampBaseline>(_entity).LastValidIgAltitude);
        }

        /// <summary>When the result has <c>HasHit = false</c> it must be ignored.</summary>
        [Fact]
        public void Execute_IgnoresMissedHit()
        {
            SetBatchEntry(_entity, hitZ: 10f, referenceSimZ: 0f, hasHit: false);
            RunOnce();

            var tf = _world.GetComponent<SimTransform>(_entity);
            var state = _world.GetComponent<TerrainClampBaseline>(_entity);
            Assert.Equal(InitialZ, tf.Position.Z);        // unchanged
            Assert.Equal(10f, state.LastValidIgAltitude); // unchanged
        }

        /// <summary>When no singleton exists the system should not throw.</summary>
        [Fact]
        public void Execute_NoThrow_WhenSingletonAbsent()
        {
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
