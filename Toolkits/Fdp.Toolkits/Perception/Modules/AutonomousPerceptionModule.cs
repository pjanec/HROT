using System;
using System.Collections.Generic;
using CarKinem.Spatial;
using Fdp.Kernel;
using Fdp.Kernel.Collections;
using FDP.Toolkit.Perception.Events;
using FDP.Toolkit.Perception.Systems;
using Fdp.ModuleHost.Core.Abstractions;

namespace FDP.Toolkit.Perception.Modules
{
    /// <summary>
    /// Wraps the four autonomous perception systems into a self-contained
    /// <see cref="IEcsModule"/> that can be installed independently of the Brain modules.
    ///
    /// <para><b>Execution model:</b> <see cref="ExecutionPolicy.SlowBackground"/> at 10 Hz.
    /// The kernel calls <see cref="Tick"/> on a background thread with a read-only
    /// Snapshot-on-Demand view of the simulation state.</para>
    ///
    /// <para><b>Memory:</b> Allocates a module-private <see cref="SpatialHashGrid"/> at
    /// construction time.  Call <see cref="Dispose"/> when the module is torn down to release
    /// the underlying native arrays.</para>
    ///
    /// <para><b>System registration:</b>
    /// All four systems—<see cref="LocalGridBuilderSystem"/>, <see cref="VisionBroadphaseSystem"/>,
    /// <see cref="LosRequestBatchingSystem"/>, and <see cref="ThreatEvaluationSystem"/>—are
    /// registered via <see cref="RegisterSystems"/>. All four implement <see cref="IEcsModuleSystem"/>
    /// and run on the background thread inside <see cref="Tick"/>.</para>
    ///
    /// <para><b>Bus isolation (BATCH-06 DEBT-07):</b>
    /// Inter-stage events (<see cref="LosCheckRequestEvent"/> and <see cref="TargetVisibleEvent"/>)
    /// flow through a module-private <see cref="FdpEventBus"/> (<c>_scopedBus</c>) rather than
    /// the global world bus.  A <see cref="PerceptionScopedView"/> wrapper redirects
    /// <c>GetCommandBuffer().PublishEvent</c> writes to the scoped bus and
    /// <c>ConsumeEvents&lt;T&gt;</c> reads from it.  Between pipeline stages the module calls
    /// <c>_scopedBus.SwapBuffers()</c> — a private operation that never touches the live-world
    /// event state, eliminating the global bus-corruption risk described in BATCH-05.
    /// Only the final ECB component mutations (TargetMemory updates) reach the real command
    /// buffer and are applied by the kernel's harvest loop.</para>
    ///
    /// <para><b>Physics-accurate LOS:</b> Pass a <paramref name="colliderRadiusReader"/> delegate
    /// to enable accurate segment-circle occlusion checks in production LOS mode.  Use:
    /// <code>
    /// (view, e) => view.HasComponent&lt;PhysicsCollider&gt;(e)
    ///              ? view.GetComponentRO&lt;PhysicsCollider&gt;(e).Radius : 0f
    /// </code>
    /// When <c>null</c>, occluders are treated as dimensionless points.</para>
    /// </summary>
    public sealed class AutonomousPerceptionModule : IEcsModule, IDisposable
    {
        /// <inheritdoc/>
        public string Name => "AutonomousPerception";

        /// <inheritdoc/>
        public ExecutionPolicy Policy => ExecutionPolicy.SlowBackground(10);

        // Module-private spatial grid. Shares native-memory pointers with the two grid systems.
        private readonly SpatialHashGrid _localGrid;

        // Module-private event bus for inter-stage event passing.
        // Prevents global bus corruption when the pipeline runs synchronously in tests.
        private readonly FdpEventBus _scopedBus;

        // ── Perception systems ─────────────────────────────────────────────────────

        private readonly LocalGridBuilderSystem   _localGridBuilder;
        private readonly VisionBroadphaseSystem   _visionBroadphase;
        private readonly LosRequestBatchingSystem _losRequestBatching;
        private readonly ThreatEvaluationSystem   _threatEvaluation;

        /// <summary>
        /// Initialises the module and allocates the module-private spatial grid.
        /// </summary>
        /// <param name="colliderRadiusReader">
        /// Optional delegate for reading the bounding radius of each candidate collider entity.
        /// When supplied, enables physics-accurate segment-circle occlusion tests in production
        /// LOS mode.  Pass <c>null</c> to treat all occluders as point entities.
        /// See <see cref="LosRequestBatchingSystem.ColliderRadiusReader"/>.
        /// </param>
        public AutonomousPerceptionModule(
            Func<ISimulationView, Entity, float>? colliderRadiusReader = null)
        {
            _localGrid = SpatialHashGrid.Create(
                PerceptionConstants.LocalGridWidth,
                PerceptionConstants.LocalGridHeight,
                PerceptionConstants.LocalGridCellSize,
                PerceptionConstants.LocalGridMaxEntities,
                Allocator.Persistent);

            _scopedBus = new FdpEventBus();
            _scopedBus.Register<LosCheckRequestEvent>();
            _scopedBus.Register<TargetVisibleEvent>();

            _localGridBuilder   = new LocalGridBuilderSystem(_localGrid);
            _visionBroadphase   = new VisionBroadphaseSystem(_localGrid);
            _losRequestBatching = new LosRequestBatchingSystem(
                mockMode: false,
                colliderRadiusReader: colliderRadiusReader);
            _threatEvaluation   = new ThreatEvaluationSystem();
        }

        /// <summary>
        /// Registers all four perception systems into the kernel registry.
        /// </summary>
        public void RegisterSystems(ISystemRegistry registry)
        {
            // All four systems are executed directly inside Tick() using the SlowBackground
            // direct-execution pattern (same as PerceptionModule).  No kernel-level
            // system-scheduler registration is required or supported.
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Runs all four perception sub-systems in pipeline order on the background thread.
        /// Inter-stage events flow through a module-private <c>_scopedBus</c> to avoid touching
        /// global world event state.  Only ECB component mutations reach the live world.
        ///
        /// Pipeline:
        /// <list type="number">
        ///   <item>LocalGridBuilder populates the spatial grid from the snapshot.</item>
        ///   <item>VisionBroadphase emits <see cref="LosCheckRequestEvent"/>s → scoped bus.</item>
        ///   <item>Scoped bus swap makes LOS requests readable.</item>
        ///   <item>LosRequestBatching reads LOS requests, emits <see cref="TargetVisibleEvent"/>s → scoped bus.</item>
        ///   <item>Scoped bus swap makes visible-target events readable.</item>
        ///   <item>ThreatEvaluation reads visible events, writes TargetMemory → real ECB.</item>
        /// </list>
        /// </remarks>
        public void Tick(ISimulationView view, float dt)
        {
            var scopedView = new PerceptionScopedView(view, _scopedBus);

            // Stage 1: Rebuild local grid from world state (no event bus involvement).
            _localGridBuilder.Execute(scopedView, dt);

            // Stage 2: Vision broadphase emits LosCheckRequestEvents → scoped bus write buffer.
            _visionBroadphase.Execute(scopedView, dt);
            _scopedBus.SwapBuffers(); // LosCheckRequestEvents now in scoped read buffer.

            // Stage 3: LOS batching reads requests (scoped), emits TargetVisibleEvents → scoped bus.
            _losRequestBatching.Execute(scopedView, dt);
            _scopedBus.SwapBuffers(); // TargetVisibleEvents now in scoped read buffer.

            // Stage 4: Threat evaluation reads visible events (scoped), writes TargetMemory → real ECB.
            _threatEvaluation.Execute(scopedView, dt);
        }

        /// <summary>Disposes the module-private <see cref="SpatialHashGrid"/> and scoped bus.</summary>
        public void Dispose()
        {
            _localGrid.Dispose();
            _scopedBus.Dispose();
        }

        // ── Inner types ───────────────────────────────────────────────────────────

        /// <summary>
        /// Wraps an <see cref="ISimulationView"/> to route perception inter-stage event
        /// operations through a module-private <see cref="FdpEventBus"/>.
        /// All non-event operations delegate transparently to the underlying view.
        /// </summary>
        private sealed class PerceptionScopedView : ISimulationView
        {
            private readonly ISimulationView _inner;
            private readonly FdpEventBus _scopedBus;
            private readonly PerceptionScopedCommandBuffer _scopedCmdBuf;

            public PerceptionScopedView(ISimulationView inner, FdpEventBus scopedBus)
            {
                _inner     = inner;
                _scopedBus = scopedBus;
                _scopedCmdBuf = new PerceptionScopedCommandBuffer(inner.GetCommandBuffer(), scopedBus);
            }

            public uint Tick  => _inner.Tick;
            public float Time => _inner.Time;

            public ref readonly T GetComponentRO<T>(Entity e) where T : unmanaged
                => ref _inner.GetComponentRO<T>(e);

            public T GetManagedComponentRO<T>(Entity e) where T : class
                => _inner.GetManagedComponentRO<T>(e);

            public bool IsAlive(Entity e)           => _inner.IsAlive(e);
            public bool HasComponent<T>(Entity e) where T : unmanaged => _inner.HasComponent<T>(e);
            public bool HasManagedComponent<T>(Entity e) where T : class => _inner.HasManagedComponent<T>(e);

            /// <summary>
            /// Consumes perception inter-stage events from the module-private scoped bus.
            ///
            /// <para><b>Explicit event whitelist (BATCH-09 DEBT-09 contract):</b>
            /// Only events registered on the scoped bus are consumable via this method.
            /// Currently those are:
            /// <list type="bullet">
            ///   <item><see cref="LosCheckRequestEvent"/> — emitted by <see cref="VisionBroadphaseSystem"/>, consumed by <see cref="LosRequestBatchingSystem"/>.</item>
            ///   <item><see cref="TargetVisibleEvent"/>   — emitted by <see cref="LosRequestBatchingSystem"/>, consumed by <see cref="ThreatEvaluationSystem"/>.</item>
            /// </list>
            /// All other event types return an empty span regardless of what the global world
            /// bus holds.  This is <b>by design</b>: the scoped bus is a private pipeline
            /// channel that must not be contaminated by world-level events, and world-level
            /// events must not be silently shadowed by empty scoped reads.
            /// </para>
            /// <para>
            /// Any future system that needs to observe world-level <em>unmanaged</em> events
            /// from within the perception pipeline must either:
            /// (a) register the event type on <c>_scopedBus</c> and have an upstream system
            ///     mirror it from the world bus, or
            /// (b) read directly from the inner view via a separate overload rather than
            ///     extending this scoped path — to keep the isolation contract explicit.
            /// </para>
            /// </summary>
            public ReadOnlySpan<T> ConsumeEvents<T>() where T : unmanaged
                => _scopedBus.Consume<T>();

            public System.Collections.Generic.IReadOnlyList<T> ConsumeManagedEvents<T>()
                => _inner.ConsumeManagedEvents<T>();

            public QueryBuilder Query() => _inner.Query();

            /// <summary>
            /// Returns a command buffer that routes <c>PublishEvent</c> to the scoped bus
            /// and all component mutations to the real underlying ECB.
            /// </summary>
            public IEntityCommandBuffer GetCommandBuffer() => _scopedCmdBuf;
        }

        /// <summary>
        /// Command buffer wrapper: <c>PublishEvent&lt;T&gt;</c> writes to the scoped bus;
        /// all component mutation methods delegate to the real ECB.
        /// </summary>
        private sealed class PerceptionScopedCommandBuffer : IEntityCommandBuffer
        {
            private readonly IEntityCommandBuffer _realEcb;
            private readonly FdpEventBus _scopedBus;

            public PerceptionScopedCommandBuffer(IEntityCommandBuffer realEcb, FdpEventBus scopedBus)
            {
                _realEcb   = realEcb;
                _scopedBus = scopedBus;
            }

            /// <summary>
            /// Routes event publishing to the module-private scoped bus instead of the global bus.
            /// </summary>
            public void PublishEvent<T>(in T evt) where T : unmanaged
                => _scopedBus.Publish(evt);

            // ── Component mutations delegate to the real ECB ─────────────────────

            public Entity CreateEntity()                                          => _realEcb.CreateEntity();
            public void DestroyEntity(Entity entity)                              => _realEcb.DestroyEntity(entity);
            public void AddComponent<T>(Entity entity, in T component) where T : unmanaged => _realEcb.AddComponent(entity, component);
            public void SetComponent<T>(Entity entity, in T component) where T : unmanaged => _realEcb.SetComponent(entity, component);
            public void RemoveComponent<T>(Entity entity) where T : unmanaged     => _realEcb.RemoveComponent<T>(entity);
            public void AddManagedComponent<T>(Entity entity, T? component) where T : class    => _realEcb.AddManagedComponent(entity, component);
            public void SetManagedComponent<T>(Entity entity, T? component) where T : class    => _realEcb.SetManagedComponent(entity, component);
            public void RemoveManagedComponent<T>(Entity entity) where T : class  => _realEcb.RemoveManagedComponent<T>(entity);
            public unsafe void SetComponentRaw(Entity entity, int typeId, void* ptr, int size) => _realEcb.SetComponentRaw(entity, typeId, ptr, size);
            public void SetManagedComponentRaw(Entity entity, int typeId, object obj)          => _realEcb.SetManagedComponentRaw(entity, typeId, obj);
            public void SetLifecycleState(Entity entity, EntityLifecycle state)   => _realEcb.SetLifecycleState(entity, state);
        }
    }
}
