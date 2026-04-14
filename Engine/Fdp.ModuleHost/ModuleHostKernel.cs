// File: ModuleHost/ModuleHostKernel.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Fdp.Kernel;
using Fdp.Kernel.Logging;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Providers;
using Fdp.ModuleHost.Scheduling;
using Fdp.ModuleHost.Resilience;
using System.Runtime.CompilerServices;
using System.Threading;
using Fdp.ModuleHost.Time;

[assembly: InternalsVisibleTo("ModuleHost.Tests")]
[assembly: InternalsVisibleTo("ModuleHost.Tests")]

namespace Fdp.ModuleHost
{
    public struct ModuleStats
    {
        public string ModuleName;
        public int ExecutionCount;
        public CircuitState CircuitState;
        public int FailureCount;
    }

    /// <summary>
    /// Central orchestrator for module execution.
    /// Manages module registration, provider assignment, and execution pipeline.
    /// </summary>
    public sealed class ModuleHostKernel : IDisposable
    {
        private readonly EntityRepository _liveWorld;
        private readonly EventAccumulator _eventAccumulator;
        private readonly List<ModuleEntry> _modules = new();
        private SnapshotPool? _snapshotPool;
        
        // ═══════════════════════════════════════════════════════════
        // RCU (Read-Copy-Update) Hot-Plugging Infrastructure
        // ═══════════════════════════════════════════════════════════
        
        /// <summary>
        /// The currently-active immutable execution topology.
        /// Read by the main thread every frame. Written atomically via Volatile.Write
        /// during SystemPhase.BeforeSync. Zero allocations on the hot path.
        /// </summary>
        private KernelExecutionTopology _activeTopology = null!;

        /// <summary>
        /// Pending topology swap queued by a background compilation task.
        /// Written by the background thread (Volatile.Write); consumed and cleared
        /// by the main thread at the next BeforeSync boundary. Protected by
        /// <see cref="_topologyChangeSemaphore"/> to allow at most one pending operation.
        /// </summary>
        private PendingTopologyOperation? _pendingOperation;

        /// <summary>
        /// Modules that have been removed via RCU swap but still have in-flight tasks.
        /// The main thread's harvest loop natively drains these entries — no disconnected
        /// background monitors. Only accessed on the main thread; no locking required.
        /// </summary>
        private readonly List<ModuleEntry> _drainingModules = new();

        /// <summary>
        /// Serializes topology compilation tasks so at most one background compile
        /// is in-flight at a time. Released only after the atomic swap is confirmed
        /// by the main thread, ensuring each caller gets a consistent baseline topology.
        /// </summary>
        private readonly SemaphoreSlim _topologyChangeSemaphore = new(1, 1);

        /// <summary>
        /// Static global systems registered before Initialize(). Stored separately so
        /// they can be re-inserted into every newly-compiled SystemScheduler during
        /// dynamic topology rebuilds without losing system instance state.
        /// </summary>
        private readonly List<IEcsModuleSystem> _registeredGlobalSystems = new();

        // ═══════════════════════════════════════════════════════════
        // Scheduling (legacy accessor — maintained for test/profiling access)
        // ═══════════════════════════════════════════════════════════
        private bool _initialized = false;
        
        private uint _currentFrame = 0;
        
        // Time Control
        private ITimeController? _timeController;
        private float _initialTimeScale = 1.0f;
        
        // Public Accessor for GlobalTime
        public GlobalTime CurrentTime { get; private set; }

        public ModuleHostKernel(EntityRepository liveWorld, EventAccumulator eventAccumulator)
        {
            _liveWorld = liveWorld ?? throw new ArgumentNullException(nameof(liveWorld));
            _eventAccumulator = eventAccumulator ?? throw new ArgumentNullException(nameof(eventAccumulator));
        }

        /// <summary>
        /// Sets the time controller implementation.
        /// Must be called before Initialize().
        /// </summary>
        public void SetTimeController(ITimeController controller)
        {
             if (_initialized)
                throw new InvalidOperationException("Cannot set time controller after initialization");
             _timeController = controller ?? throw new ArgumentNullException(nameof(controller));
             // Apply any pending timescale
             _timeController.SetTimeScale(_initialTimeScale);
        }
        
        /// <summary>
        /// Phases that are actually executed for global systems by the kernel's Update loop.
        /// SystemPhase.Simulation is only executed for module systems on background threads.
        /// </summary>
        private static readonly HashSet<SystemPhase> _validGlobalPhases = new()
        {
            SystemPhase.Input,
            SystemPhase.BeforeSync,
            SystemPhase.PostSimulation,
            SystemPhase.Export
        };
        
        /// <summary>
        /// Register a global system (runs on main thread).
        /// </summary>
        public void RegisterGlobalSystem<T>(T system) where T : IEcsModuleSystem
        {
            if (_initialized)
                throw new InvalidOperationException("Cannot register systems after Initialize() called");
            
            // Validate that the system's phase will actually be executed for global systems.
            // SystemPhase.Simulation is only run for module systems (background threads),
            // so a global system marked with it would silently never execute.
            // Use system.GetType() (not typeof(T)) to get the concrete type even when
            // called polymorphically, e.g. RegisterGlobalSystem<IEcsModuleSystem>(concreteInstance).
            var concreteType = system.GetType();
            var phaseAttr = (UpdateInPhaseAttribute?)Attribute.GetCustomAttribute(
                concreteType, typeof(UpdateInPhaseAttribute), inherit: true);
            
            if (phaseAttr != null && !_validGlobalPhases.Contains(phaseAttr.Phase))
            {
                throw new InvalidOperationException(
                    $"System '{concreteType.Name}' is marked with [UpdateInPhase(SystemPhase.{phaseAttr.Phase})] " +
                    $"but is being registered as a global system. " +
                    $"The kernel only executes phases [{string.Join(", ", _validGlobalPhases)}] for global systems. " +
                    $"SystemPhase.Simulation is reserved for module systems running on background threads. " +
                    $"Use SystemPhase.PostSimulation instead, or register this system within a module.");
            }
            
            // Track the system for topology rebuilds (dynamic hot-plugging)
            _registeredGlobalSystems.Add(system);
        }
        
        /// <summary>
        /// Sets the simulation time scale.
        /// </summary>
        public void SetTimeScale(float scale)
        {
            if (_timeController != null)
            {
                _timeController.SetTimeScale(scale);
            }
            else
            {
                _initialTimeScale = scale;
            }
        }
        
        /// <summary>
        /// Access to the active system scheduler for profiling/debugging.
        /// After <see cref="Initialize"/> this always reflects the live execution topology,
        /// including any dynamically installed modules.
        /// </summary>
        public SystemScheduler SystemScheduler => _initialized ? _activeTopology.Scheduler
            : throw new InvalidOperationException("Kernel not yet initialized. Call Initialize() first.");

        /// <summary>
        /// Returns a read-only snapshot of the <see cref="IEcsModule.Name"/> values for
        /// all modules currently registered on this kernel.  Includes modules in any
        /// lifecycle state (Ready / Draining).
        ///
        /// <para>Intended for diagnostics and test assertions only.
        /// Do not use this API on the hot path.</para>
        /// </summary>
        public IReadOnlyList<string> GetRegisteredModuleNames()
            => _modules.Select(m => m.Module.Name).ToList().AsReadOnly();

        /// <summary>
        /// Returns a read-only snapshot of the concrete <see cref="Type.Name"/> values for
        /// all modules currently registered on this kernel.
        ///
        /// <para>Intended for diagnostics and test assertions only.
        /// Do not use this API on the hot path.</para>
        /// </summary>
        public IReadOnlyList<string> GetRegisteredModuleTypeNames()
            => _modules.Select(m => m.Module.GetType().Name).ToList().AsReadOnly();

        /// <summary>
        /// Initialize kernel: build execution orders, validate dependencies.
        /// Must be called after all modules/systems registered, before Update().
        /// </summary>
        public void Initialize()
        {
            if (_initialized)
                throw new InvalidOperationException("Already initialized");
            
            // Create global pool
            _snapshotPool = new SnapshotPool(_schemaSetup, warmupCount: 10);
            
            // Validate Time Controller
            if (_timeController == null)
            {
                throw new InvalidOperationException("TimeController not set. Use SetTimeController() to inject an implementation (e.g. from FDP.Toolkit.Time).");
            }
            
            // 1. Validate policies and ensure schemas are registered for all startup modules
            foreach (var entry in _modules)
            {
                try
                {
                    entry.Module.Policy.Validate();
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Module '{entry.Module.Name}' has invalid execution policy: {ex.Message}", ex);
                }
                
                EnsureComponentsRegistered(entry.Module);
            }
            
            // 2. Assign providers using the unified allocation logic
            foreach (var entry in _modules)
            {
                if (entry.Provider == null)
                {
                    AssignProviderForDynamicInstall(_modules, entry);
                }
                entry.LifecycleState = ModuleLifecycleState.Ready;
            }
            
            // Build the initial execution topology:
            // Registers all module systems into a fresh SystemScheduler,
            // captures system instances for reuse on future topology rebuilds,
            // and performs the topological sort.
            _activeTopology = BuildTopology(_modules);
            
            // Throws CircularDependencyException if cycles detected
            
            _initialized = true;
        }

        private BitMask256 CalculateUnionMask(List<ModuleEntry> modules)
        {
            var unionMask = new BitMask256();
            
            foreach (var entry in modules)
            {
                unionMask.BitwiseOr(entry.ComponentMask);
            }
            
            return unionMask;
        }

        /// <summary>
        /// Ensures that any component types declared by <paramref name="module"/> via
        /// <see cref="IEcsModule.GetRequiredComponents"/> are registered on the live
        /// <see cref="EntityRepository"/> before provider allocation occurs.
        ///
        /// <para>
        /// Safe to call from background threads. Uses the same reflection-based generic
        /// invocation pattern found in <c>EntityRepository.SyncFrom</c>.
        /// Silently skips types that are already registered globally.
        /// </para>
        /// </summary>
        private void EnsureComponentsRegistered(IEcsModule module)
        {
            var requiredComponents = module.GetRequiredComponents();
            if (requiredComponents == null) return;

            foreach (var type in requiredComponents)
            {
                if (ComponentTypeRegistry.GetId(type) >= 0)
                    continue; // Already registered globally — nothing to do.

                // Register the component on the live EntityRepository via reflection.
                // Once registered here, EntityRepository.SyncFrom auto-propagates it
                // to any internal snapshot repos on their next sync cycle.
                try
                {
                    var method = typeof(EntityRepository)
                        .GetMethod(nameof(EntityRepository.RegisterComponent))!
                        .MakeGenericMethod(type);
                    method.Invoke(_liveWorld, new object[] { null! });

                    FdpLog<ModuleHostKernel>.Info(
                        "[ModuleHost] Registered novel component '{0}' for module '{1}'.",
                        type.Name, module.Name);
                }
                catch (Exception ex)
                {
                    FdpLog<ModuleHostKernel>.Warn(
                        "[ModuleHost] Could not register component '{0}' for module '{1}': {2}",
                        type.Name, module.Name, ex.Message);
                }
            }
        }

        private BitMask256 GetComponentMask(IEcsModule module)
        {
            var requiredComponents = module.GetRequiredComponents();
            
            // Default: sync all components (conservative)
            if (requiredComponents == null || !requiredComponents.Any())
            {
                return CreateFullMask();
            }
            
            // Optimized: sync only required components
            var mask = new BitMask256();
            foreach (var componentType in requiredComponents)
            {
                int typeId = ComponentTypeRegistry.GetId(componentType);
                if (typeId >= 0 && typeId < 256)
                {
                    mask.SetBit(typeId);
                }
                else
                {
                    // Log warning: Component type not registered
                    FdpLog<ModuleHostKernel>.Warn(
                        "Warning: Module '{0}' requires unregistered component: {1}",
                        module.Name,
                        componentType.Name);
                }
            }
            
            return mask;
        }
        
        private BitMask256 CreateFullMask()
        {
            var mask = new BitMask256();
            for (int i = 0; i < 256; i++)
            {
                mask.SetBit(i);
            }
            return mask;
        }

        /// <summary>
        /// Registers a module with optional provider override.
        /// If provider is null, default will be assigned during Initialize().
        ///
        /// <para><b>Ownership contract:</b>
        /// The kernel does <b>not</b> take ownership of registered modules.
        /// Modules that implement <see cref="IDisposable"/> are <b>not</b> disposed when
        /// <see cref="Dispose"/> is called on this kernel — only snapshot providers are
        /// disposed automatically.  Callers must dispose modules themselves, either via
        /// a <c>using</c> block declared outside the kernel's <c>using</c> scope (LIFO
        /// ensures the kernel is disposed before the module is flushed), or via
        /// <c>IScenario.OnShutdown</c> / a similar teardown hook.
        /// </para>
        /// </summary>
        public void RegisterModule(IEcsModule module, ISnapshotProvider? provider = null)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            
            if (_initialized)
                throw new InvalidOperationException(
                    "Cannot register modules after initialization. " +
                    "For runtime hot-plugging use InstallModuleAsync (single) or InstallModulesAsync (batch).");
            
            var policy = module.Policy;
            
            // Validate locally to ensure defaults (like FailureThreshold) are set.
            // We suppress exceptions here because specific validation errors (like Mode mismatches)
            // should be handled during Initialize() phase or AutoAssignProviders(), 
            // consistent with previous behavior.
            try { policy.Validate(); } catch { }

            var entry = new ModuleEntry
            {
                Module = module,
                Provider = provider!, 
                HasManualProvider = provider != null,
                FramesSinceLastRun = 0,
                
                // Initialize resilience components from locally validated Policy
                MaxExpectedRuntimeMs = policy.MaxExpectedRuntimeMs,
                FailureThreshold = policy.FailureThreshold,
                CircuitResetTimeoutMs = policy.CircuitResetTimeoutMs,
                
                CircuitBreaker = new ModuleCircuitBreaker(
                    failureThreshold: policy.FailureThreshold,
                    resetTimeoutMs: policy.CircuitResetTimeoutMs
                )
            };
            
            _modules.Add(entry);
        }
        
        /// <summary>
        /// Main update loop.
        /// Drives the TimeController to advance simulation time, then executes the frame.
        /// </summary>
        public void Update()
        {
            if (!_initialized)
                throw new InvalidOperationException("Must call Initialize() before Update()");
            
            // 1. Advance Time via Controller
            GlobalTime globalTime = _timeController!.Update();
            CurrentTime = globalTime;
            
            // 2. Execute Frame with calculated delta
            UpdateInternal(globalTime.DeltaTime, globalTime);
        }

        /// <summary>
        /// Legacy/Manual update loop.
        /// Allows driving the kernel with an external delta time.
        /// Note: TimeController will still be updated but its delta might be ignored or conflicted 
        /// if using master mode with manual dt.
        /// Use Update() (no args) for standard TimeController-driven execution.
        /// </summary>
        [Obsolete("Use Update() utilizing SteppingTimeController instead. This legacy overload will cause deterministic desync.", false)]
        public void Update(float deltaTime)
        {
            // Create a synthetic GlobalTime if called manually
            var time = new GlobalTime
            {
                 DeltaTime = deltaTime,
                 TotalTime = _liveWorld.SimulationTime + deltaTime, // Approx
                 FrameNumber = (long)_currentFrame + 1,
                 TimeScale = 1.0f
            };
            
            // Note: If we use this legacy path, we might desync the _timeController state.
            // Ideally we should push this dt to controller?
            // But controller usually measures wall clock.
            // We'll proceed with internal update logic.
            
            UpdateInternal(deltaTime, time);
        }
        
        private void UpdateInternal(float deltaTime, GlobalTime globalTime)
        {
            if (!_initialized)
                throw new InvalidOperationException("Must call Initialize() before Update()");
            
            // Read the active topology ONCE for this frame.
            // The RCU swap below may update _activeTopology, at which point we also update
            // this local reference so the rest of the frame uses the new topology.
            var topology = Volatile.Read(ref _activeTopology);

            // 1. ADVANCE TIME
            _liveWorld.Tick(); // Increment version
            _liveWorld.SetSimulationTime((float)globalTime.TotalTime); // Update repository time
            _liveWorld.SetSingletonUnmanaged(globalTime); // Update GlobalTime singleton for components
            
            CurrentTime = globalTime;
            _currentFrame = (uint)globalTime.FrameNumber;
            
            // ═══════════ PHASE: Input ═══════════
            topology.Scheduler.ExecutePhase(SystemPhase.Input, _liveWorld, deltaTime);
            
            // ═══════════════════════════════════════════════════════════
            // RCU TOPOLOGY SWAP — O(1) atomic pointer swap
            // Applied immediately before BeforeSync so the new module begins
            // participating in BeforeSync systems and dispatch this same frame.
            // ═══════════════════════════════════════════════════════════
            var pendingOp = Volatile.Read(ref _pendingOperation);
            if (pendingOp != null)
            {
                // Clear the pending slot before signalling — prevents a race where
                // TrySetResult triggers a continuation that itself calls Install/Uninstall
                // before we have fully transitioned to the new topology.
                Volatile.Write(ref _pendingOperation, null);
                topology = pendingOp.NewTopology;
                Volatile.Write(ref _activeTopology, topology);

                // If this was an uninstall, move the old entry/entries to the draining queue.
                // The main thread's harvest loop manages them from here — no disconnected workers.
                if (pendingOp.DrainEntries != null)
                    _drainingModules.AddRange(pendingOp.DrainEntries);

                // Signal the awaiting Install/Uninstall Task — the swap is complete.
                pendingOp.SwapCompletion.TrySetResult();
            }

            // ═══════════ PHASE: BeforeSync ═══════════
            topology.Scheduler.ExecutePhase(SystemPhase.BeforeSync, _liveWorld, deltaTime);
            
            // FLUSH LIVE WORLD BUFFERS
            if (_liveWorld._perThreadCommandBuffer != null)
            {
                foreach (var cmdBuffer in _liveWorld._perThreadCommandBuffer.Values)
                {
                    if (cmdBuffer.HasCommands)
                    {
                        cmdBuffer.Playback(_liveWorld);
                    }
                }
            }
            
            // 3. EVENT SWAP (Critical: Make Input events visible)
            _liveWorld.Bus.SwapBuffers();
            
            // 4. SYNC & CAPTURE
            // Capture event history
            // Use GlobalVersion to align with SnapshotProvider logic which tracks GlobalVersion
            _eventAccumulator.CaptureFrame(_liveWorld.Bus, _liveWorld.GlobalVersion);
            
            // Update Sync-Point Providers
            foreach (var entry in topology.Modules)
            {
                // Only update provider if it exists (Direct strategy has null)
                entry.Provider?.Update();
            }
            
            // ═══════════ HARVEST PHASE (active modules) ═══════════
            foreach (var entry in topology.Modules)
            {
                // Harvest completed async tasks
                if (entry.CurrentTask != null && entry.CurrentTask.IsCompleted)
                {
                    HarvestEntry(entry);
                }
            }

            // ═══════════ HARVEST PHASE (draining modules — native draining) ═══════════
            // Modules that have been removed via RCU swap continue to be harvested here
            // until their in-flight task finishes and their leased view is released.
            // Only then is final disposal dispatched to a background worker.
            for (int i = _drainingModules.Count - 1; i >= 0; i--)
            {
                var drainingEntry = _drainingModules[i];

                bool taskDone = drainingEntry.CurrentTask == null
                             || drainingEntry.CurrentTask.IsCompleted;

                if (taskDone)
                {
                    if (drainingEntry.CurrentTask != null)
                        HarvestEntry(drainingEntry); // release leased view, playback commands

                    drainingEntry.LifecycleState = ModuleLifecycleState.Disposed;
                    _drainingModules.RemoveAt(i);

                    // Capture for closure — dispatch final dispose off the hot path
                    var entryToDispose = drainingEntry;
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            if (entryToDispose.Module is IDisposable disposableModule)
                                disposableModule.Dispose();
                            // Dispose provider only if it is exclusive to this module
                            // (shared providers keep serving other modules)
                            if (entryToDispose.HasExclusiveProvider &&
                                entryToDispose.Provider is IDisposable disposableProvider)
                            {
                                disposableProvider.Dispose();
                            }
                        }
                        catch (Exception ex)
                        {
                            FdpLog<ModuleHostKernel>.Warn(
                                "[ModuleHost] Exception during background disposal of '{0}': {1}",
                                entryToDispose.Module.Name, ex.Message);
                        }
                        finally
                        {
                            // Signal UninstallModuleAsync awaiter — teardown is complete
                            entryToDispose.DrainCompletionSource?.TrySetResult();
                        }
                    });
                }
            }
            
            // ═══════════ DISPATCH PHASE ═══════════
            var tasksToWait = new List<Task>();
            
            foreach (var entry in topology.Modules)
            {
                // Always accumulate time (logic time)
                entry.AccumulatedDeltaTime += deltaTime;
                
                // If still running, let it continue (accumulating time for next run)
                if (entry.CurrentTask != null)
                {
                    continue;
                }
                
                // If idle, check frequency
                bool shouldRun = ShouldRunThisFrame(entry);
                
                if (shouldRun)
                {
                    ISimulationView view;
                    
                    if (entry.Module.Policy.Strategy == DataStrategy.Direct)
                    {
                        // Direct access to live world (Synchronous only)
                        view = _liveWorld;
                    }
                    else
                    {
                        if (entry.Provider == null)
                        {
                            // Should theoretically not happen if Validate() worked, but safe guard
                             continue;
                        }
                        // Capture the provider reference once before calling AcquireView.
                        // A background topology-upgrade task may concurrently mutate entry.Provider
                        // (e.g. when promoting a SoD convoy to a wider SharedSnapshotProvider).
                        // By capturing here we guarantee that LeasedProvider always matches the
                        // provider that actually created the view, regardless of any concurrent write.
                        var acquireProvider = entry.Provider;
                        view = acquireProvider.AcquireView();
                        entry.LeasedProvider = acquireProvider;
                    }
                    
                    entry.LeasedView = view;
                    entry.LastView   = view; // Keep for reference if needed
                    
                    // Consume accumulated time for this tick
                    float moduleDelta = entry.AccumulatedDeltaTime;
                    entry.AccumulatedDeltaTime = 0f;
                    
                    // Dispatch execution
                    if (entry.Module.Policy.Mode == RunMode.Synchronous)
                    {
                        // Synchronous run (main thread)
                        try
                        {
                            entry.Module.Tick(view, moduleDelta);
                            System.Threading.Interlocked.Increment(ref entry.ExecutionCount);
                            
                            // Playback commands immediately for sync modules
                            PlaybackCommands(entry);
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"[ModuleHost] Sync Module '{entry.Module.Name}' exception: {ex}");
                        }
                        
                        // Release view — use LeasedProvider (captured at acquire time) so the
                        // view is always returned to the provider that created it, even if
                        // entry.Provider was remapped by a concurrent topology upgrade.
                        if (entry.Module.Policy.Strategy != DataStrategy.Direct)
                        {
                            entry.LeasedProvider?.ReleaseView(view);
                        }
                        entry.LeasedView     = null;
                        entry.LeasedProvider = null;
                        entry.CurrentTask = null; // No task
                    }
                    else
                    {
                        // Safe Execution (Async/FrameSynced)
                        entry.CurrentTask = ExecuteModuleSafe(entry, view, moduleDelta);
                    }
                    
                    entry.FramesSinceLastRun = 0;
                    entry.LastRunTick = _liveWorld.GlobalVersion > 0 ? _liveWorld.GlobalVersion - 1 : 0; 
                    
                    // Check Policy: If FrameSynced, we must wait
                    if (entry.Module.Policy.Mode == RunMode.FrameSynced)
                    {
                        if (entry.CurrentTask != null)
                             tasksToWait.Add(entry.CurrentTask);
                    }
                }
                else
                {
                    entry.FramesSinceLastRun++;
                }
            }
            
            // ═══════════ SYNC WAIT (Fast Modules) ═══════════
            if (tasksToWait.Count > 0)
            {
                Task.WaitAll(tasksToWait.ToArray());
                
                // Harvest immediately
                foreach (var entry in topology.Modules)
                {
                    if (entry.CurrentTask != null && entry.Module.Policy.Mode == RunMode.FrameSynced)
                    {
                        HarvestEntry(entry);
                    }
                }
            }
            
            // ═══════════ PHASE: PostSimulation ═══════════
            topology.Scheduler.ExecutePhase(SystemPhase.PostSimulation, _liveWorld, deltaTime);
            
            // ═══════════ PHASE: Export ═══════════
            topology.Scheduler.ExecutePhase(SystemPhase.Export, _liveWorld, deltaTime);
            
            _currentFrame++;
        }

        /// <summary>
        /// Safely executes a module with timeout and exception handling.
        /// Integrates with circuit breaker for resilience.
        /// </summary>
        private async Task ExecuteModuleSafe(ModuleEntry entry, ISimulationView view, float dt)
        {
            // 1. Check Circuit Breaker
            if (entry.CircuitBreaker != null && !entry.CircuitBreaker.CanRun())
            {
                // Circuit is open - skip execution
                // We must release the view!
                // NOTE: If we return here, HarvestEntry won't be called because CurrentTask terminates early?
                // Actually if ExecuteModuleSafe returns Task, and we await it.
                // But if we return here 'early', the task completes.
                // HarvestEntry checks 'IsCompleted'.
                // AND HarvestEntry releases view.
                // So returning here is Safe IF we ensure HarvestEntry runs.
                // HarvestEntry runs in Update() loop.
                return;
            }
            
            // 2. Determine Timeout
            int timeout = entry.MaxExpectedRuntimeMs;
            if (timeout <= 0)
            {
                timeout = 1000; // Default safety timeout
            }
            
            // 3. Create Cancellation Token (for cooperative cancellation)
            using var cts = new CancellationTokenSource(timeout);
            
            // 4. Run Module with Timeout Race
            // We return Exception? to avoid throwing on thread pool which might crash test runner
            var tickTask = Task.Run<Exception?>(() => 
            {
                try
                {
                    entry.Module.Tick(view, dt);
                    System.Threading.Interlocked.Increment(ref entry.ExecutionCount);
                    return null; // Success
                }
                catch (Exception ex)
                {
                    return ex; // Return exception as result
                }
            }, cts.Token);
            
            var delayTask = Task.Delay(timeout);
            var completedTask = await Task.WhenAny(tickTask, delayTask);
            
            // 5. Check Result
            if (completedTask == tickTask)
            {
                // Module completed within timeout
                Exception? result = null;
                try
                {
                    result = await tickTask;
                }
                catch (OperationCanceledException)
                {
                    // Cancelled
                    entry.CircuitBreaker?.RecordFailure("Cancelled");
                    Console.Error.WriteLine($"[ModuleHost][CANCELLED] Module '{entry.Module.Name}' was cancelled.");
                    return;
                }
                catch (Exception ex)
                {
                    // Should be caught inside, but just in case
                    result = ex;
                }
                
                if (result == null)
                {
                    // Success - record in circuit breaker
                    entry.CircuitBreaker?.RecordSuccess();
                }
                else
                {
                    // Exception occurred
                    Exception ex = result;
                    Console.Error.WriteLine($"[ModuleHost] Module '{entry.Module.Name}' threw exception: {ex.Message}");
                    Console.Error.WriteLine(ex.StackTrace);
                    
                    entry.CircuitBreaker?.RecordFailure(ex.GetType().Name);
                    
                    Console.Error.WriteLine(
                        $"[ModuleHost][CRASH] Module '{entry.Module.Name}' crashed: {ex.Message}");
                }
            }
            else
            {
                // TIMEOUT
                entry.CircuitBreaker?.RecordFailure("Timeout");
                
                Console.Error.WriteLine(
                    $"[ModuleHost][TIMEOUT] Module '{entry.Module.Name}' timed out after {timeout}ms. " +
                    $"Task abandoned (may continue running in background as zombie).");
                
                // Prevent unobserved task exception if the zombie task eventually faults
                _ = tickTask.ContinueWith(t => 
                {
                     if (t.IsFaulted) { var _ = t.Exception; } 
                }, TaskContinuationOptions.OnlyOnFaulted);
            }
        }

        private void HarvestEntry(ModuleEntry entry)
        {
            // 1. Playback commands
            PlaybackCommands(entry);
            
            // 2. Release view — always use the provider that issued the view (LeasedProvider),
            //    NOT entry.Provider. The two can diverge when AssignProviderForDynamicInstall
            //    reroutes a convoy to a new SharedSnapshotProvider while an async task is
            //    still in-flight holding a view from the old provider.
            if (entry.LeasedView != null)
            {
                entry.LeasedProvider?.ReleaseView(entry.LeasedView);
                entry.LeasedView     = null;
                entry.LeasedProvider = null;
            }
            
            // 3. Handle faults
            if (entry.CurrentTask?.IsFaulted == true)
            {
                Console.Error.WriteLine($"Module {entry.Module.Name} failed: {entry.CurrentTask.Exception}");
            }
            
            // 4. Cleanup
            entry.CurrentTask = null;
        }

        private void PlaybackCommands(ModuleEntry entry)
        {
            if (entry.LeasedView is EntityRepository repo)
            {
                if (repo._perThreadCommandBuffer != null)
                {
                    foreach (var cmdBuffer in repo._perThreadCommandBuffer.Values)
                    {
                        if (cmdBuffer.HasCommands)
                        {
                            FdpLog<ModuleHostKernel>.Trace(
                                "[Playback] Playing commands for {0}",
                                entry.Module.Name);
                            cmdBuffer.Playback(_liveWorld);
                        }
                        else 
                        {
                            //Console.WriteLine($"[Playback] No commands for {entry.Module.Name}");
                        }
                    }
                }
            }
        }
        
        public List<ModuleStats> GetExecutionStats()
        {
            var stats = new List<ModuleStats>();
            // Use the active topology so stats reflect the live module set,
            // including any dynamically installed/uninstalled modules.
            var topology = _initialized ? _activeTopology : null;
            var source = topology != null ? (IEnumerable<ModuleEntry>)topology.Modules : _modules;
            foreach (var entry in source)
            {
                stats.Add(new ModuleStats
                {
                    ModuleName = entry.Module.Name,
                    ExecutionCount = entry.ExecutionCount,
                    CircuitState = entry.CircuitBreaker?.State ?? CircuitState.Closed,
                    FailureCount = entry.CircuitBreaker?.FailureCount ?? 0
                });
                
                entry.ExecutionCount = 0;
            }
            return stats;
        }
        
        private bool ShouldRunThisFrame(ModuleEntry entry)
        {
            var policy = entry.Module.Policy;
            
            // 1. Reactive Check (Batch-02)
            bool triggered = false;
            
            if (entry.Module.WatchEvents != null && entry.Module.WatchEvents.Count > 0)
            {
                foreach (var evt in entry.Module.WatchEvents)
                {
                    if (_liveWorld.Bus.HasEvent(evt))
                    {
                        triggered = true;
                        break;
                    }
                }
            }
            
            if (!triggered && entry.Module.WatchComponents != null && entry.Module.WatchComponents.Count > 0)
            {
                foreach (var comp in entry.Module.WatchComponents)
                {
                     if (_liveWorld.HasComponentChanged(comp, entry.LastRunTick))
                     {
                         triggered = true;
                         break;
                     }
                }
            }
            
            if (triggered) return true;
            
            // 2. Periodic Check
            int targetHz = policy.TargetFrequencyHz;
            if (targetHz <= 0) targetHz = 60; // 0 means every frame
            
            if (targetHz >= 60) return true;
            
            int framesToSkip = 60 / targetHz;
            if (framesToSkip < 1) framesToSkip = 1;
            
            return (entry.FramesSinceLastRun + 1) >= framesToSkip;
        }
        
        private Action<EntityRepository>? _schemaSetup;

        /// <summary>
        /// Sets the schema setup action used to initialize registered component types
        /// on internal repositories (e.g. snapshots for SoD or replicas for GDB).
        /// </summary>
        public void SetSchemaSetup(Action<EntityRepository> setup)
        {
            _schemaSetup = setup;
        }
        
        /// <summary>
        /// Swap the time controller at runtime (e.g., pause/unpause in distributed systems).
        /// Transfers state from old to new controller.
        /// </summary>
        public void SwapTimeController(ITimeController newController)
        {
            if (newController == null)
                throw new ArgumentNullException(nameof(newController));
            
            if (!_initialized)
                throw new InvalidOperationException("Cannot swap controller before Initialize()");
            
            // Get current state from old controller
            var currentState = _timeController!.GetCurrentState();
            float currentScale = _timeController!.GetTimeScale();
            
            // Seed new controller with current state
            newController.SeedState(currentState);
            newController.SetTimeScale(currentScale);
            
            // Dispose old controller
            _timeController?.Dispose();
            
            // Install new controller
            _timeController = newController;
            
            // Update CurrentTime property
            CurrentTime = currentState;
            
            FdpLog<ModuleHostKernel>.Info(
                "[TimeController] Swapped to {0}, TotalTime={1:F3}s, Frame={2}",
                newController.GetType().Name,
                currentState.TotalTime,
                currentState.FrameNumber);
        }
        
        /// <summary>
        /// Get current time controller (for inspection/debugging).
        /// </summary>
        public ITimeController GetTimeController()
        {
            if (!_initialized)
                throw new InvalidOperationException("Time controller not initialized yet");
            
            return _timeController!;
        }

        /// <summary>
        /// Manually advance a single frame (Stepped/Paused mode).
        /// </summary>
        public void StepFrame(float deltaTime)
        {
            if (!_initialized) throw new InvalidOperationException("Not initialized");
            
            GlobalTime time;
            
            // Support different stepping controllers
            if (_timeController is ISteppableTimeController steppable)
            {
                time = steppable.Step(deltaTime);
            }
            else
            {
                 throw new InvalidOperationException($"Current controller {_timeController?.GetType().Name} does not support manual stepping.");
            }
            
            CurrentTime = time;
            UpdateInternal(time.DeltaTime, time);
        }
        


        /// <summary>
        /// Releases all kernel resources, waits for in-flight module tasks (up to 2 s),
        /// and disposes snapshot providers.
        ///
        /// <para><b>Module disposal:</b>
        /// Registered <see cref="IEcsModule"/> instances that implement <see cref="IDisposable"/>
        /// are <b>not</b> disposed here (see <see cref="RegisterModule"/> for the ownership contract).
        /// Only snapshot providers are disposed automatically.  Modules removed via
        /// <see cref="UninstallModuleAsync"/> are an exception — they are disposed on the
        /// background drain thread when their in-flight tasks complete.
        /// </para>
        /// </summary>
        public void Dispose()
        {
            // Collect all in-flight tasks: active + draining modules
            var allModules = _initialized
                ? _activeTopology.Modules.Concat(_drainingModules)
                : _modules.AsEnumerable();

            var pendingTasks = allModules
                .Where(m => m.CurrentTask != null && !m.CurrentTask.IsCompleted)
                .Select(m => m.CurrentTask!)
                .ToArray();
            
            if (pendingTasks.Length > 0)
            {
                try 
                {
                    Task.WaitAll(pendingTasks, 2000); // Wait up to 2s
                }
                catch (AggregateException) { /* Ignore faults */ }
                catch (TimeoutException) { /* Proceed anyway */ }
            }

            // Dispose providers — deduplicate to avoid double-dispose on shared providers
            var disposedProviders = new HashSet<object>(ReferenceEqualityComparer.Instance);
            foreach (var entry in allModules)
            {
                if (entry.Provider is IDisposable disposable && disposedProviders.Add(entry.Provider))
                {
                    disposable.Dispose();
                }
            }
            
            _timeController?.Dispose();
            _topologyChangeSemaphore.Dispose();
            _modules.Clear();
            _drainingModules.Clear();
        }
        
        // ═══════════════════════════════════════════════════════════════════════════════
        // Dynamic Module Hot-Plugging: Public API
        // ═══════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Asynchronously installs a module into the live kernel without interrupting the
        /// 60Hz main loop. Returns a <see cref="Task"/> that completes once the module is
        /// fully live — its systems are executing, its memory is allocated, and the next
        /// frame will tick it.
        /// 
        /// <para>
        /// The heavy work (provider allocation, dependency graph compilation) is performed
        /// entirely on a background thread. The main thread only performs an O(1) atomic
        /// pointer swap during the next <see cref="SystemPhase.BeforeSync"/> phase.
        /// </para>
        ///
        /// <para>
        /// Concurrent calls are serialized: each caller waits until the previous topology
        /// change has been committed before starting its own background compilation.
        /// </para>
        /// </summary>
        /// <param name="module">The module to install. Must not already be installed.</param>
        /// <exception cref="ArgumentNullException"><paramref name="module"/> is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// Kernel not initialized, or <paramref name="module"/> is already installed.
        /// </exception>
        public async Task InstallModuleAsync(IEcsModule module)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            if (!_initialized)
                throw new InvalidOperationException(
                    "Cannot dynamically install modules before Initialize() is called.");

            await _topologyChangeSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                // Guard against double-install
                var currentTopology = Volatile.Read(ref _activeTopology);
                if (currentTopology.Modules.Any(e => e.Module == module))
                    throw new InvalidOperationException(
                        $"Module '{module.Name}' is already installed in this kernel.");

                var policy = module.Policy;
                var newEntry = new ModuleEntry
                {
                    Module = module,
                    Provider = null!,
                    FramesSinceLastRun = 0,
                    MaxExpectedRuntimeMs = policy.MaxExpectedRuntimeMs > 0 ? policy.MaxExpectedRuntimeMs : 1000,
                    FailureThreshold = policy.FailureThreshold > 0 ? policy.FailureThreshold : 3,
                    CircuitResetTimeoutMs = policy.CircuitResetTimeoutMs > 0 ? policy.CircuitResetTimeoutMs : 5000,
                    CircuitBreaker = new ModuleCircuitBreaker(
                        failureThreshold: policy.FailureThreshold > 0 ? policy.FailureThreshold : 3,
                        resetTimeoutMs: policy.CircuitResetTimeoutMs > 0 ? policy.CircuitResetTimeoutMs : 5000),
                    LifecycleState = ModuleLifecycleState.Loading
                };

                // Compile the new topology on a background thread — no stalls on the 60Hz loop.
                var newTopology = await Task.Run(() =>
                {
                    // Take a consistent snapshot of the current modules as our baseline.
                    var baseline = Volatile.Read(ref _activeTopology);
                    var newModuleList = new List<ModuleEntry>(baseline.Modules) { newEntry };

                    // Task 2 – ECS Schema Upgrade: register novel component types before
                    // provider allocation so GetComponentMask sees valid type IDs.
                    EnsureComponentsRegistered(module);

                    // Provision memory/providers for the new module.
                    // This is the heavy work: mask calculation, pool allocation, etc.
                    AssignProviderForDynamicInstall(newModuleList, newEntry);

                    newEntry.LifecycleState = ModuleLifecycleState.Ready;

                    return BuildTopology(newModuleList);
                }).ConfigureAwait(false);

                // Queue the swap for the main thread to apply at the next BeforeSync boundary.
                var swapTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Volatile.Write(ref _pendingOperation,
                    new PendingTopologyOperation(newTopology, swapTcs, drainEntries: null));

                // Await the main thread's confirmation that the swap has happened.
                // Once this returns the module is live.
                await swapTcs.Task.ConfigureAwait(false);

                FdpLog<ModuleHostKernel>.Info(
                    "[ModuleHost] Module '{0}' installed and live.", module.Name);
            }
            finally
            {
                _topologyChangeSemaphore.Release();
            }
        }

        /// <summary>
        /// Asynchronously uninstalls a module from the live kernel. Returns a <see cref="Task"/>
        /// that completes only after the module has been fully drained:
        /// <list type="bullet">
        ///   <item>Its entry has been atomically removed from the execution topology.</item>
        ///   <item>All in-flight background tasks have finished execution.</item>
        ///   <item>All leased views have been returned to their providers.</item>
        ///   <item>Exclusive providers and unmanaged memory pools have been disposed.</item>
        /// </list>
        ///
        /// <para>
        /// The main thread's harvest loop manages draining natively — no disconnected
        /// background monitors that could race with <see cref="EntityRepository"/> access.
        /// </para>
        /// </summary>
        /// <param name="module">The module to uninstall. Must currently be installed.</param>
        /// <exception cref="ArgumentNullException"><paramref name="module"/> is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// Kernel not initialized, or <paramref name="module"/> is not currently installed.
        /// </exception>
        public async Task UninstallModuleAsync(IEcsModule module)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            if (!_initialized)
                throw new InvalidOperationException(
                    "Cannot dynamically uninstall modules before Initialize() is called.");

            await _topologyChangeSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                var currentTopology = Volatile.Read(ref _activeTopology);
                var targetEntry = currentTopology.Modules.FirstOrDefault(e => e.Module == module);
                if (targetEntry == null)
                    throw new InvalidOperationException(
                        $"Module '{module.Name}' is not currently installed in this kernel.");

                // Mark as draining and set up the drain completion source.
                targetEntry.LifecycleState = ModuleLifecycleState.Draining;
                var drainTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                targetEntry.DrainCompletionSource = drainTcs;

                // Build new topology without the module on a background thread.
                var newTopology = await Task.Run(() =>
                {
                    var current = Volatile.Read(ref _activeTopology);
                    var newModuleList = current.Modules
                        .Where(e => e != targetEntry)
                        .ToList();
                    return BuildTopology(newModuleList);
                }).ConfigureAwait(false);

                // Queue the swap — and pass the entry so UpdateInternal adds it to _drainingModules.
                var swapTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Volatile.Write(ref _pendingOperation,
                    new PendingTopologyOperation(newTopology, swapTcs, drainEntries: new[] { targetEntry }));

                // Wait for the swap (module is now unhooked from dispatching — new ticks stop).
                await swapTcs.Task.ConfigureAwait(false);

                FdpLog<ModuleHostKernel>.Info(
                    "[ModuleHost] Module '{0}' unhooked from topology. Draining...", module.Name);

                // Wait for the native harvest loop to fully drain and dispose the module.
                // This Task completes only when HarvestEntry has released the leased view
                // and the background disposal worker has finished.
                await drainTcs.Task.ConfigureAwait(false);

                FdpLog<ModuleHostKernel>.Info(
                    "[ModuleHost] Module '{0}' fully drained and disposed.", module.Name);
            }
            finally
            {
                _topologyChangeSemaphore.Release();
            }
        }

        // ===============================================================================
        // Dynamic Module Hot-Plugging: Batch Public API
        // ===============================================================================

        /// <summary>
        /// Asynchronously installs multiple modules into the live kernel as a single atomic
        /// operation. All requested modules are compiled into one new
        /// <see cref="KernelExecutionTopology"/> on a background thread; the 60&nbsp;Hz main
        /// loop is only paused for an O(1) pointer swap at the next
        /// <see cref="SystemPhase.BeforeSync"/> boundary, at which point every module in the
        /// batch becomes live simultaneously — no torn states.
        /// </summary>
        /// <param name="modules">The modules to install. None may already be installed.</param>
        /// <exception cref="ArgumentNullException"><paramref name="modules"/> or any element is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// Kernel not initialized, or any module in the list is already installed.
        /// </exception>
        public async Task InstallModulesAsync(IReadOnlyList<IEcsModule> modules)
        {
            if (modules == null) throw new ArgumentNullException(nameof(modules));
            if (modules.Count == 0) return;
            if (!_initialized)
                throw new InvalidOperationException(
                    "Cannot dynamically install modules before Initialize() is called.");

            await _topologyChangeSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                var currentTopology = Volatile.Read(ref _activeTopology);

                // Validate: no nulls, no double-installs
                foreach (var m in modules)
                {
                    if (m == null) throw new ArgumentNullException(nameof(modules),
                        "Module list must not contain null entries.");
                    if (currentTopology.Modules.Any(e => e.Module == m))
                        throw new InvalidOperationException(
                            $"Module '{m.Name}' is already installed in this kernel.");
                }

                // Create all new entries upfront with Loading state.
                var newEntries = new List<ModuleEntry>(modules.Count);
                foreach (var m in modules)
                {
                    var policy = m.Policy;
                    newEntries.Add(new ModuleEntry
                    {
                        Module = m,
                        Provider = null!,
                        FramesSinceLastRun = 0,
                        MaxExpectedRuntimeMs  = policy.MaxExpectedRuntimeMs  > 0 ? policy.MaxExpectedRuntimeMs  : 1000,
                        FailureThreshold      = policy.FailureThreshold      > 0 ? policy.FailureThreshold      : 3,
                        CircuitResetTimeoutMs = policy.CircuitResetTimeoutMs > 0 ? policy.CircuitResetTimeoutMs : 5000,
                        CircuitBreaker = new ModuleCircuitBreaker(
                            failureThreshold:    policy.FailureThreshold      > 0 ? policy.FailureThreshold      : 3,
                            resetTimeoutMs:      policy.CircuitResetTimeoutMs > 0 ? policy.CircuitResetTimeoutMs : 5000),
                        LifecycleState = ModuleLifecycleState.Loading
                    });
                }

                // Compile a single new topology with ALL new entries — background thread.
                var newTopology = await Task.Run(() =>
                {
                    var baseline = Volatile.Read(ref _activeTopology);
                    var newModuleList = new List<ModuleEntry>(baseline.Modules);
                    newModuleList.AddRange(newEntries);

                    // Schema upgrade for all new modules before provider assignment.
                    foreach (var entry in newEntries)
                        EnsureComponentsRegistered(entry.Module);

                    // Assign providers sequentially so that each successive module sees the
                    // providers already assigned to earlier modules in the batch (convoy detection).
                    foreach (var entry in newEntries)
                        AssignProviderForDynamicInstall(newModuleList, entry);

                    foreach (var entry in newEntries)
                        entry.LifecycleState = ModuleLifecycleState.Ready;

                    return BuildTopology(newModuleList);
                }).ConfigureAwait(false);

                // Single atomic swap activates every new module in the same frame.
                var swapTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Volatile.Write(ref _pendingOperation,
                    new PendingTopologyOperation(newTopology, swapTcs, drainEntries: null));

                await swapTcs.Task.ConfigureAwait(false);

                FdpLog<ModuleHostKernel>.Info(
                    "[ModuleHost] Batch-installed {0} module(s) atomically.", modules.Count);
            }
            finally
            {
                _topologyChangeSemaphore.Release();
            }
        }

        /// <summary>
        /// Asynchronously uninstalls multiple modules from the live kernel as a single atomic
        /// operation. All specified modules are removed from the topology in one background
        /// compilation pass and the pointer swap deactivates them all in the same frame.
        /// Returns only after every removed module has been fully drained and disposed.
        /// </summary>
        /// <param name="modules">The modules to uninstall. All must currently be installed.</param>
        /// <exception cref="ArgumentNullException"><paramref name="modules"/> or any element is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// Kernel not initialized, or any module in the list is not currently installed.
        /// </exception>
        public async Task UninstallModulesAsync(IReadOnlyList<IEcsModule> modules)
        {
            if (modules == null) throw new ArgumentNullException(nameof(modules));
            if (modules.Count == 0) return;
            if (!_initialized)
                throw new InvalidOperationException(
                    "Cannot dynamically uninstall modules before Initialize() is called.");

            await _topologyChangeSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                var currentTopology = Volatile.Read(ref _activeTopology);

                // Resolve entries and set up drain TCS for each module.
                var targetEntries = new List<ModuleEntry>(modules.Count);
                var drainTcsList  = new List<TaskCompletionSource>(modules.Count);

                foreach (var m in modules)
                {
                    if (m == null) throw new ArgumentNullException(nameof(modules),
                        "Module list must not contain null entries.");
                    var entry = currentTopology.Modules.FirstOrDefault(e => e.Module == m);
                    if (entry == null)
                        throw new InvalidOperationException(
                            $"Module '{m.Name}' is not currently installed in this kernel.");

                    entry.LifecycleState = ModuleLifecycleState.Draining;
                    var drainTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    entry.DrainCompletionSource = drainTcs;

                    targetEntries.Add(entry);
                    drainTcsList.Add(drainTcs);
                }

                // Build topology without the removed modules.
                var newTopology = await Task.Run(() =>
                {
                    var current = Volatile.Read(ref _activeTopology);
                    var newModuleList = current.Modules
                        .Where(e => !targetEntries.Contains(e))
                        .ToList();
                    return BuildTopology(newModuleList);
                }).ConfigureAwait(false);

                // Single atomic swap removes all modules simultaneously.
                var swapTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Volatile.Write(ref _pendingOperation,
                    new PendingTopologyOperation(newTopology, swapTcs, drainEntries: targetEntries));

                await swapTcs.Task.ConfigureAwait(false);

                FdpLog<ModuleHostKernel>.Info(
                    "[ModuleHost] Batch-unhooked {0} module(s). Draining...", modules.Count);

                // Wait until every removed module has been fully harvested and disposed.
                await Task.WhenAll(drainTcsList.Select(t => t.Task)).ConfigureAwait(false);

                FdpLog<ModuleHostKernel>.Info(
                    "[ModuleHost] Batch-uninstall of {0} module(s) complete.", modules.Count);
            }
            finally
            {
                _topologyChangeSemaphore.Release();
            }
        }

        /// <summary>
        /// Returns whether the specified module is currently installed and active in the kernel.
        /// </summary>
        public bool IsModuleInstalled(IEcsModule module)
        {
            if (!_initialized) return false;
            return _activeTopology.Modules.Any(e => e.Module == module);
        }

        /// <summary>
        /// Returns the <see cref="ModuleLifecycleState"/> of the specified module,
        /// or <c>null</c> if the module is not known to the kernel in any state.
        /// </summary>
        public ModuleLifecycleState? GetModuleLifecycleState(IEcsModule module)
        {
            if (!_initialized) return null;

            // Check active topology
            var active = _activeTopology.Modules.FirstOrDefault(e => e.Module == module);
            if (active != null) return active.LifecycleState;

            // Check draining
            var draining = _drainingModules.FirstOrDefault(e => e.Module == module);
            if (draining != null) return draining.LifecycleState;

            return null;
        }

        // ═══════════════════════════════════════════════════════════════════════════════
        // Dynamic Module Hot-Plugging: Internal Helpers
        // ═══════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Compiles a new <see cref="KernelExecutionTopology"/> from the given module list.
        /// Creates a fresh <see cref="SystemScheduler"/>, registers all global systems and
        /// all module-provided systems (reusing cached instances where possible), then
        /// performs topological sorting. Safe to call from any thread.
        /// </summary>
        private KernelExecutionTopology BuildTopology(List<ModuleEntry> modules)
        {
            var scheduler = new SystemScheduler();

            // Static global systems always come first in every topology
            foreach (var sys in _registeredGlobalSystems)
                scheduler.RegisterSystem(sys);

            // Module-provided systems — reuse cached instances to preserve state
            foreach (var entry in modules)
            {
                if (entry.RegisteredSystems != null)
                {
                    // Re-register the same instances (no new objects created)
                    foreach (var sys in entry.RegisteredSystems)
                        scheduler.RegisterSystem(sys);
                }
                else
                {
                    // First time: capture system instances via CapturingSystemRegistry
                    var capturing = new CapturingSystemRegistry(scheduler);
                    entry.Module.RegisterSystems(capturing);
                    entry.RegisteredSystems = capturing.Captured;
                }
            }

            scheduler.BuildExecutionOrders();

            return new KernelExecutionTopology(
                modules.AsReadOnly(),
                scheduler);
        }

        /// <summary>
        /// Assigns the appropriate <see cref="ISnapshotProvider"/> to a newly installed
        /// <paramref name="newEntry"/> using a topology-wide evaluation of the full
        /// <paramref name="allModules"/> list. This ensures that shared providers
        /// (e.g. <see cref="SharedSnapshotProvider"/> for SoD convoys) correctly incorporate
        /// the new module's component mask and aren't allocated in isolation.
        ///
        /// <para>
        /// For the initial version, existing modules keep their providers unless they need
        /// to join a new shared group. A future enhancement should fully re-evaluate the
        /// <c>UnionMask</c> and replace shared providers when the mask expands.
        /// </para>
        /// </summary>
        private void AssignProviderForDynamicInstall(List<ModuleEntry> allModules, ModuleEntry newEntry)
        {
            // Cache component mask
            newEntry.ComponentMask = GetComponentMask(newEntry.Module);

            try { newEntry.Module.Policy.Validate(); } catch { /* Handled during full init */ }

            var policy = newEntry.Module.Policy;

            switch (policy.Strategy)
            {
                case DataStrategy.Direct:
                    newEntry.Provider = null!;
                    newEntry.HasExclusiveProvider = false;
                    break;

                case DataStrategy.GDB:
                {
                    // Find existing GDB group members with the same execution characteristics
                    var groupMembers = allModules
                        .Where(e => e != newEntry
                            && e.Module.Policy.Strategy == DataStrategy.GDB
                            && e.Module.Policy.Mode == policy.Mode
                            && e.Module.Policy.TargetFrequencyHz == policy.TargetFrequencyHz
                            && e.Provider is DoubleBufferProvider
                            && !e.HasManualProvider)
                        .ToList();

                    if (groupMembers.Count > 0)
                    {
                        var existingGdb = groupMembers[0].Provider as DoubleBufferProvider;

                        // Re-evaluate UnionMask: if the new module introduces components not
                        // covered by the current provider, allocate a wider provider.
                        if (existingGdb?.UnionMask == null)
                        {
                            // Provider is full-sync — already covers all components.
                            newEntry.Provider = groupMembers[0].Provider;
                            newEntry.HasExclusiveProvider = false;
                        }
                        else
                        {
                            var allInConvoy = groupMembers.Concat(new[] { newEntry }).ToList();
                            var newUnionMask = CalculateUnionMask(allInConvoy);

                            if (newUnionMask.Equals(existingGdb.UnionMask.Value))
                            {
                                // Mask unchanged — reuse the existing provider.
                                newEntry.Provider = groupMembers[0].Provider;
                                newEntry.HasExclusiveProvider = false;
                            }
                            else
                            {
                                // Mask expanded: allocate a new DoubleBufferProvider with wider mask.
                                var newGdbProvider = new DoubleBufferProvider(
                                    _liveWorld, _eventAccumulator, newUnionMask, _schemaSetup);

                                // Point all convoy members (+ new entry) at the new provider.
                                // Old provider will be unreferenced and eligible for GC once
                                // any in-flight views from the previous frame are released.
                                foreach (var e in allInConvoy)
                                {
                                    e.Provider = newGdbProvider;
                                    e.HasExclusiveProvider = false;
                                }
                            }
                        }
                    }
                    else
                    {
                        var unionMask = CalculateUnionMask(new List<ModuleEntry> { newEntry });
                        newEntry.Provider = new DoubleBufferProvider(
                            _liveWorld, _eventAccumulator, unionMask, _schemaSetup);
                        newEntry.HasExclusiveProvider = true;
                    }
                    break;
                }

                case DataStrategy.SoD:
                {
                    var groupMembers = allModules
                        .Where(e => e != newEntry
                            && e.Module.Policy.Strategy == DataStrategy.SoD
                            && e.Module.Policy.Mode == policy.Mode
                            && e.Module.Policy.TargetFrequencyHz == policy.TargetFrequencyHz
                            && !e.HasManualProvider)
                        .ToList();

                    if (groupMembers.Count == 0)
                    {
                        // Exclusive OnDemandProvider for a solo module
                        newEntry.Provider = new OnDemandProvider(
                            _liveWorld, _eventAccumulator, newEntry.ComponentMask,
                            _schemaSetup, initialPoolSize: 5);
                        newEntry.HasExclusiveProvider = true;
                    }
                    else
                    {
                        // Find existing shared provider for the convoy
                        var existingShared = groupMembers
                            .FirstOrDefault(e => e.Provider is SharedSnapshotProvider)
                            ?.Provider as SharedSnapshotProvider;

                        if (existingShared != null)
                        {
                            // Re-evaluate UnionMask: if the incoming module introduces components not
                            // covered by the existing provider, allocate a new wider provider.
                            var allInConvoy = groupMembers.Concat(new[] { newEntry }).ToList();
                            var newUnionMask = CalculateUnionMask(allInConvoy);

                            if (newUnionMask.Equals(existingShared.UnionMask))
                            {
                                // Mask unchanged — reuse the existing shared provider.
                                newEntry.Provider = existingShared;
                                newEntry.HasExclusiveProvider = false;
                            }
                            else
                            {
                                // Mask expanded: allocate a new SharedSnapshotProvider.
                                var newSharedProvider = new SharedSnapshotProvider(
                                    _liveWorld, _eventAccumulator, newUnionMask, _snapshotPool!);

                                // Reroute all convoy members (+ new entry) to the new provider.
                                // The old provider will naturally drain as in-flight views are
                                // released back to it by the still-running harvest loop.
                                foreach (var e in allInConvoy)
                                {
                                    e.Provider = newSharedProvider;
                                    e.HasExclusiveProvider = false;
                                }
                            }
                        }
                        else
                        {
                            // Promote the convoy to a SharedSnapshotProvider
                            var groupPlusnew = groupMembers.Concat(new[] { newEntry }).ToList();
                            var unionMask = CalculateUnionMask(groupPlusnew);
                            var sharedProvider = new SharedSnapshotProvider(
                                _liveWorld, _eventAccumulator, unionMask, _snapshotPool!);

                            foreach (var e in groupPlusnew)
                            {
                                e.Provider = sharedProvider;
                                e.HasExclusiveProvider = false;
                            }
                        }
                    }
                    break;
                }

                default:
                    FdpLog<ModuleHostKernel>.Warn(
                        "[ModuleHost] Unknown DataStrategy for module '{0}'. No provider assigned.",
                        newEntry.Module.Name);
                    break;
            }
        }

        internal class ModuleEntry
        {
            public IEcsModule Module { get; set; } = null!;
            public ISnapshotProvider Provider { get; set; } = null!;
            public int FramesSinceLastRun { get; set; }
            public ISimulationView? LastView { get; set; }
            public int ExecutionCount; // Field for Interlocked
            
            // Async State (NEW - for World C)
            public Task? CurrentTask { get; set; }
            public ISimulationView? LeasedView { get; set; }

            /// <summary>
            /// The <see cref="ISnapshotProvider"/> that issued <see cref="LeasedView"/>.
            /// Captured at acquire time so that <c>HarvestEntry</c> can always release
            /// the view back to the correct provider, even when <see cref="Provider"/> has
            /// been remapped (e.g. on a convoy UnionMask upgrade).
            /// </summary>
            public ISnapshotProvider? LeasedProvider { get; set; }

            public float AccumulatedDeltaTime { get; set; }
            public uint LastRunTick { get; set; }  // For reactive scheduling prep
            
            // Caching
            public BitMask256 ComponentMask; 
            
            // NEW for BATCH-04: Resilience
            public ModuleCircuitBreaker? CircuitBreaker { get; set; }
            public int MaxExpectedRuntimeMs { get; set; }
            public int FailureThreshold { get; set; }
            public int CircuitResetTimeoutMs { get; set; }

            // ═══════════════════════════════════════════════════════════
            // Dynamic Module Hot-Plugging State
            // ═══════════════════════════════════════════════════════════

            /// <summary>
            /// Current lifecycle state of this module entry.
            /// Transitions: Loading → Ready (on RCU swap-in) → Draining (on RCU swap-out) → Disposed.
            /// </summary>
            public ModuleLifecycleState LifecycleState { get; set; } = ModuleLifecycleState.Ready;

            /// <summary>
            /// Signalled by the main-thread draining harvester once the module's in-flight task
            /// is done and providers are released. Completing this TCS unblocks the
            /// <see cref="ModuleHostKernel.UninstallModuleAsync"/> awaiter.
            /// </summary>
            public TaskCompletionSource? DrainCompletionSource { get; set; }

            /// <summary>
            /// Cached references to system instances registered by this module.
            /// Reused when rebuilding the execution topology to avoid re-creating system objects
            /// and losing any accumulated state (e.g. profiling counters, cached queries).
            /// </summary>
            public List<IEcsModuleSystem>? RegisteredSystems { get; set; }

            /// <summary>
            /// True when this entry owns its provider exclusively and should dispose it
            /// on teardown. False when the provider is shared with other modules.
            /// </summary>
            public bool HasExclusiveProvider { get; set; }

            /// <summary>
            /// True if the user manually assigned a provider during initial setup.
            /// These modules should NOT be forcefully grouped into shared convoys during dynamic installs.
            /// </summary>
            public bool HasManualProvider { get; set; }
        }

        // ═══════════════════════════════════════════════════════════
        // RCU Infrastructure: Pending Topology Operation
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Represents an atomic topology change compiled on a background thread and
        /// queued for application on the next <see cref="SystemPhase.BeforeSync"/> boundary.
        /// </summary>
        private sealed class PendingTopologyOperation
        {
            /// <summary>The new topology to atomically swap in on the next frame.</summary>
            public KernelExecutionTopology NewTopology { get; }

            /// <summary>
            /// Signalled by the main thread immediately after the pointer swap.
            /// Completing this TCS unblocks the <c>await swapTcs.Task;</c> line in
            /// <see cref="ModuleHostKernel.InstallModuleAsync"/> or
            /// <see cref="ModuleHostKernel.UninstallModuleAsync"/>.
            /// </summary>
            public TaskCompletionSource SwapCompletion { get; }

            /// <summary>
            /// For uninstall operations: the entries removed from the topology.
            /// The main thread adds them to <c>_drainingModules</c> during the swap.
            /// Null or empty for install operations.
            /// </summary>
            public IReadOnlyList<ModuleEntry>? DrainEntries { get; }

            public PendingTopologyOperation(
                KernelExecutionTopology newTopology,
                TaskCompletionSource swapCompletion,
                IReadOnlyList<ModuleEntry>? drainEntries)
            {
                NewTopology = newTopology;
                SwapCompletion = swapCompletion;
                DrainEntries = drainEntries;
            }
        }

        // ═══════════════════════════════════════════════════════════
        // RCU Infrastructure: Capturing System Registry
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Wraps a <see cref="SystemScheduler"/> to record which system instances a module
        /// registers. Cached instances are reused on subsequent topology rebuilds, preserving
        /// system state (e.g. profiling counters, cached queries) across hot-plug events.
        /// </summary>
        private sealed class CapturingSystemRegistry : ISystemRegistry
        {
            private readonly SystemScheduler _scheduler;
            public List<IEcsModuleSystem> Captured { get; } = new();

            public CapturingSystemRegistry(SystemScheduler scheduler) => _scheduler = scheduler;

            public void RegisterSystem<T>(T system) where T : IEcsModuleSystem
            {
                Captured.Add(system);
                _scheduler.RegisterSystem(system);
            }
        }
    }
}
