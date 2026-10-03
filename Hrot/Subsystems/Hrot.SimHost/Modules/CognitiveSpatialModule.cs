using System;
using CarKinem.Spatial;
using Fdp.Core;
using Fdp.Core.Collections;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception.Events;
using Fdp.Toolkit.Perception.Modules;
using Fdp.Toolkit.Perception.Systems;
using Hrot.SimHost.Systems;

namespace Hrot.SimHost.Modules
{
    public sealed class CognitiveSpatialModule : IEcsModule, IDisposable
    {
        public string Name => "CognitiveSpatial";

        public ExecutionPolicy Policy => ExecutionPolicy.SlowBackground(10);

        // B3 -- RECEIVED, not allocated. See the constructor.
        private readonly SpatialHashGrid _localGrid;
        private readonly PerceptionGridProvider? _ownedGridProvider;
        private readonly FdpEventBus _scopedBus;

        private IEcsModuleSystem _localGridBuilder = null!;
        private IEcsModuleSystem _visionBroadphase = null!;
        private IEcsModuleSystem _losRequestBatching = null!;
        private IEcsModuleSystem _sensorTrackDebounce = null!;

        private readonly Func<ISimulationView, Entity, float>? _colliderRadiusReader;

        // ⭐ The sight test; null ⇒ the planar 2-D sweep over _colliderRadiusReader. 📄 docs/DESIGN_Terrain_World.md §4.3.
        private readonly Fdp.Toolkit.Perception.LineOfSight.ILosStrategy? _losStrategy;

        // ⭐ CE-3018 — the live terrain the perception grid follows (W9); null ⇒ fixed placement.
        private readonly Func<Fdp.Toolkit.Terrain.TerrainWorld?>? _terrainSource;

        /// <summary>True when this module's perception grid follows the resident terrain — what a composition rail asserts.</summary>
        public bool FollowsTerrain => _terrainSource != null;

        /// <summary>
        /// ⭐⭐ The module as EVERY terrain host composes it (SimHost, editor ≡ CGF, Stride): 3-D sight through the resident
        /// world (§4.3) and a perception grid that rebases to it (§4.4, CE-3018). ⭐ One factory, so a host cannot wire
        /// the LOS strategy and forget the grid source — the two read the SAME live source (<c>TerrainWorldSource.Live</c>).
        /// </summary>
        public static CognitiveSpatialModule ForTerrainHost(EntityRepository world, PerceptionGridProvider? gridProvider = null)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            return new CognitiveSpatialModule(
                gridProvider,
                colliderRadiusReader: Fdp.Toolkit.Physics.Components.PhysicsColliderReaders.Radius,
                losStrategy: Fdp.Toolkit.Perception.LineOfSight.TerrainWorldLosStrategy.ForLiveWorld(world),
                terrainSource: Fdp.Toolkit.Terrain.TerrainWorldSource.Live(world));
        }

        /// <summary>
        /// <b>B3 — the capability RECEIVES its grid.</b>
        /// </summary>
        /// <param name="gridProvider">
        /// The perception grid's owner. <b>Pass one.</b> When <c>null</c> this module allocates and owns a
        /// private provider, which is the pre-B3 behaviour and is kept ONLY so the existing tests and any
        /// single-capability host keep working unchanged — a composition root that selects capabilities by
        /// role must pass the shared provider, or a node landing this capability through two roles
        /// allocates the grid twice.
        /// </param>
        /// <param name="colliderRadiusReader">Optional collider-radius reader for LOS batching.</param>
        /// <param name="losStrategy">The sight test (<c>TerrainWorldLosStrategy</c> on a terrain host); null ⇒
        /// the planar 2-D sweep over <paramref name="colliderRadiusReader"/>.</param>
        /// <param name="terrainSource">⭐ CE-3018 — the live terrain the perception grid rebases to. Prefer
        /// <see cref="ForTerrainHost"/>, which supplies it together with the LOS strategy.</param>
        public CognitiveSpatialModule(
            PerceptionGridProvider? gridProvider = null,
            Func<ISimulationView, Entity, float>? colliderRadiusReader = null,
            Fdp.Toolkit.Perception.LineOfSight.ILosStrategy? losStrategy = null,
            Func<Fdp.Toolkit.Terrain.TerrainWorld?>? terrainSource = null)
        {
            _terrainSource = terrainSource;

            // Own one only if nobody handed us one; _ownedGridProvider records which case we are in so
            // Dispose frees exactly what this module allocated and never what it borrowed.
            _ownedGridProvider = gridProvider is null ? new PerceptionGridProvider() : null;
            _localGrid         = (gridProvider ?? _ownedGridProvider!).Grid;

            _scopedBus = new FdpEventBus();
            _scopedBus.Register<LosCheckRequestEvent>();
            _scopedBus.Register<TargetVisibleEvent>();
            _scopedBus.Register<SensorTrackStateEvent>();

            _colliderRadiusReader = colliderRadiusReader;
            _losStrategy          = losStrategy;
        }

        public FdpEventBus ScopedBus => _scopedBus;

        public void RegisterSystems(ISystemRegistry registry)
        {
            _localGridBuilder = registry.RegisterManualSystem(new LocalGridBuilderSystem(_localGrid, _terrainSource));
            _visionBroadphase = registry.RegisterManualSystem(new VisionBroadphaseSystem(_localGrid));
            _losRequestBatching = registry.RegisterManualSystem(new LosRequestBatchingSystem(
                mockMode: false,
                colliderRadiusReader: _colliderRadiusReader,
                losStrategy: _losStrategy));
            _sensorTrackDebounce = registry.RegisterManualSystem(new SensorTrackDebounceSystem());
        }

        public void Tick(ISimulationView view, float dt)
        {
            if (dt <= 0f) return;

            var scopedView = new PerceptionScopedView(view, _scopedBus);

            _localGridBuilder.Execute(scopedView, dt);

            _visionBroadphase.Execute(scopedView, dt);
            _scopedBus.SwapBuffers();

            _losRequestBatching.Execute(scopedView, dt);
            _scopedBus.SwapBuffers();

            _sensorTrackDebounce.Execute(scopedView, dt);

            _scopedBus.SwapBuffers();
            var globalCmd = view.GetCommandBuffer();
            foreach (ref readonly var evt in _scopedBus.Read<SensorTrackStateEvent>())
            {
                globalCmd.PublishEvent(evt);
            }
        }

        public void Dispose()
        {
            // B3: free the grid ONLY when this module allocated it. A borrowed provider outlives us and is
            // disposed by whoever owns it -- disposing it here would free memory other capabilities still read.
            _ownedGridProvider?.Dispose();
            _scopedBus.Dispose();
        }

        private sealed class PerceptionScopedView : ISimulationView
        {
            private readonly ISimulationView _inner;
            private readonly FdpEventBus _scopedBus;
            private readonly PerceptionScopedCommandBuffer _scopedCmdBuf;

            public PerceptionScopedView(ISimulationView inner, FdpEventBus scopedBus)
            {
                _inner = inner;
                _scopedBus = scopedBus;
                _scopedCmdBuf = new PerceptionScopedCommandBuffer(inner.GetCommandBuffer(), scopedBus);
            }

            public uint Tick => _inner.Tick;
            public float Time => _inner.Time;
            public ref readonly T GetComponentRO<T>(Entity e) where T : unmanaged => ref _inner.GetComponentRO<T>(e);
            public T GetManagedComponentRO<T>(Entity e) where T : class => _inner.GetManagedComponentRO<T>(e);
            public bool IsAlive(Entity e) => _inner.IsAlive(e);
            public bool HasComponent<T>(Entity e) where T : unmanaged => _inner.HasComponent<T>(e);
            public bool HasManagedComponent<T>(Entity e) where T : class => _inner.HasManagedComponent<T>(e);
            public ReadOnlySpan<T> ReadEvents<T>() where T : unmanaged => _scopedBus.Read<T>();
            public System.Collections.Generic.IReadOnlyList<T> ReadManagedEvents<T>() => _inner.ReadManagedEvents<T>();
            public QueryBuilder Query() => _inner.Query();
            public IEntityCommandBuffer GetCommandBuffer() => _scopedCmdBuf;
        }

        private sealed class PerceptionScopedCommandBuffer : IEntityCommandBuffer
        {
            private readonly IEntityCommandBuffer _realEcb;
            private readonly FdpEventBus _scopedBus;

            public PerceptionScopedCommandBuffer(IEntityCommandBuffer realEcb, FdpEventBus scopedBus)
            {
                _realEcb = realEcb;
                _scopedBus = scopedBus;
            }

            public void PublishEvent<T>(in T evt) where T : unmanaged => _scopedBus.Publish(evt);
            public Entity CreateEntity() => _realEcb.CreateEntity();
            public void DestroyEntity(Entity entity) => _realEcb.DestroyEntity(entity);
            public void AddComponent<T>(Entity entity, in T component) where T : unmanaged => _realEcb.AddComponent(entity, component);
            public void AddEmptyComponent<T>(Entity entity) where T : unmanaged => _realEcb.AddEmptyComponent<T>(entity);
            public void SetComponent<T>(Entity entity, in T component) where T : unmanaged => _realEcb.SetComponent(entity, component);
            public void RemoveComponent<T>(Entity entity) where T : unmanaged => _realEcb.RemoveComponent<T>(entity);
            public void AddManagedComponent<T>(Entity entity, T? component) where T : class => _realEcb.AddManagedComponent(entity, component);
            public void SetManagedComponent<T>(Entity entity, T? component) where T : class => _realEcb.SetManagedComponent(entity, component);
            public void RemoveManagedComponent<T>(Entity entity) where T : class => _realEcb.RemoveManagedComponent<T>(entity);
            public unsafe void SetComponentRaw(Entity entity, int typeId, void* ptr, int size) => _realEcb.SetComponentRaw(entity, typeId, ptr, size);
            // Ruling 14 — a delegating wrapper must delegate this too, or the debug field write hits
            // the interface's throwing default instead of the real buffer behind it.
            public unsafe void SetComponentFieldRaw(Entity entity, int typeId, int byteOffset, void* ptr, int size) => _realEcb.SetComponentFieldRaw(entity, typeId, byteOffset, ptr, size);
            public void SetManagedComponentRaw(Entity entity, int typeId, object obj) => _realEcb.SetManagedComponentRaw(entity, typeId, obj);
            public void SetLifecycleState(Entity entity, EntityLifecycle state) => _realEcb.SetLifecycleState(entity, state);
        }
    }
}
