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
    /// Unit tests for <see cref="TerrainQuerySubmitSystem"/>.
    /// </summary>
    public sealed class TerrainQuerySubmitSystemTests : IDisposable
    {
        private readonly EntityRepository _world;

        public TerrainQuerySubmitSystemTests()
        {
            _world = new EntityRepository();
            _world.RegisterComponent<SimTransform>();
            _world.RegisterComponent<SimVelocity>();
            _world.RegisterComponent<GroundClampingConfig>();
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

        private void SetupSingleton()
        {
            _world.SetSingleton(new TerrainQueryBatchData
            {
                Requests = new NativeArray<TerrainQueryRequest>(TerrainQueryBatchData.DefaultCapacity, Allocator.Persistent),
                Results  = new NativeArray<TerrainQueryResult>(TerrainQueryBatchData.DefaultCapacity,  Allocator.Persistent),
                Count    = 0,
            });
        }

        /// <summary>
        /// Entity with <c>Mode = ForceOff</c> must NOT produce a query entry.
        /// </summary>
        [Fact]
        public void Execute_SkipsEntity_WhenClampingInactive_ForceOff()
        {
            SetupSingleton();

            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new SimTransform { Position = new Vector3(10, 20, 5) });
            _world.AddComponent(entity, new GroundClampingConfig
            {
                Mode                 = EClampingMode.ForceOff,
                BaseRequiresClamping = 1, // would be clamped by Default, but ForceOff overrides
            });

            var system = new TerrainQuerySubmitSystem();
            system.Execute((ISimulationView)_world, 0.016f);

            ref readonly var batch = ref _world.GetSingleton<TerrainQueryBatchData>();
            Assert.Equal(0, batch.Count);
        }

        /// <summary>
        /// Entity with <c>Mode = Default</c> and <c>BaseRequiresClamping = 0</c> (aircraft)
        /// must NOT produce a query entry.
        /// </summary>
        [Fact]
        public void Execute_SkipsEntity_WhenClampingInactive_DefaultNonGrounded()
        {
            SetupSingleton();

            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new SimTransform { Position = new Vector3(10, 20, 5) });
            _world.AddComponent(entity, new GroundClampingConfig
            {
                Mode                 = EClampingMode.Auto,
                BaseRequiresClamping = 0, // aircraft
            });

            var system = new TerrainQuerySubmitSystem();
            system.Execute((ISimulationView)_world, 0.016f);

            ref readonly var batch = ref _world.GetSingleton<TerrainQueryBatchData>();
            Assert.Equal(0, batch.Count);
        }

        /// <summary>
        /// Entity with <c>Mode = ForceOn</c> must produce exactly one query entry.
        /// </summary>
        [Fact]
        public void Execute_AddsRequest_WhenClampingActive()
        {
            SetupSingleton();

            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new SimTransform { Position = new Vector3(10f, 20f, 5f) });
            _world.AddComponent(entity, new GroundClampingConfig
            {
                Mode                 = EClampingMode.ForceOn,
                BaseRequiresClamping = 0,
            });

            var system = new TerrainQuerySubmitSystem();
            system.Execute((ISimulationView)_world, 0.016f);

            ref readonly var batch = ref _world.GetSingleton<TerrainQueryBatchData>();
            Assert.Equal(1, batch.Count);
            Assert.Equal(entity, batch.Requests[0].Entity);
            Assert.Equal(5f, batch.Requests[0].ReferenceSimZ);
        }

        /// <summary>
        /// When no singleton exists the system should not throw.
        /// </summary>
        [Fact]
        public void Execute_NoThrow_WhenSingletonAbsent()
        {
            var system = new TerrainQuerySubmitSystem();
            var ex = Record.Exception(() => system.Execute((ISimulationView)_world, 0.016f));
            Assert.Null(ex);
        }
    }
}
