QUESTIONS TO ARCHITECT

Q1 — Snapshot facility. Does the engine already have a generalized "Snapshot-on-Demand" facility that systems can request snapshots from, or is each module (Perception, etc.) hand-rolling its own component-data snapshot? If generalized, what's the API? If hand-rolled, what's the canonical pattern — copy-out of specific component arrays under a lock, or something more sophisticated? This decides how the EQS solver gets its world view consistently.


Q2 — Background thread pool. Is there a shared background-thread pool that EQS can submit work to (alongside Parallel.For), or should it own its own thread(s)? I'm specifically thinking about whether long-running solver work (a 4ms tick) should spin its own dedicated thread that wakes on a timer, or use Task.Run/ThreadPool for each candidate batch.


Q3 — Time source. The architect mentioned TimeSystem. For per-sensor scheduling ("refresh at 5 Hz"), should the solver use simulation time (which can pause/slow under TimeSystem control) or wall-clock time? My guess is simulation time so EQS pauses with the game, but I want to confirm.


Q4 — Async raycast batching. When AutonomousPerceptionModule submits raycasts, can multiple modules submit concurrently, and is there a batching mechanism in the raycast solver (group N rays into one SIMD operation), or are they processed one at a time? This affects whether the EQS solver can naively submit "30 LOS checks for this candidate batch" or needs to do its own coalescing.


Q5 — Per-tick budget existing patterns. The 10Hz dispatcher and Perception module both run at fixed rates with no apparent budget enforcement. If EQS introduces a real wall-time budget (e.g., "stop processing at 4ms"), does that fit any existing pattern in the engine, or is it the first system to do so? If first, are there preferences about how budget overruns are logged/reported (counter, event, log line)?


Q6 — Navmesh cost model. Once DotRecast is integrated, do you have prior data on how much a typical navmesh query costs (point-projection, path-existence-check, full path)? Even rough order-of-magnitude (1μs/10μs/100μs/1ms) would help me size the budget realistically.


Q7 — Variable-size list size limits. Variable [DdsManaged] List<T> in translators — is there a practical upper bound on list length per message? E.g., if a sensor produces a 50-entry ranked list, is that fine on the wire, or should we cap at 20-ish for DDS health?

REPLIES


1. The engine possesses a fully generalized Snapshot-on-Demand (SoD) facility; modules do not hand-roll their own snapshots. This system is deeply integrated into our `ModuleHostKernel` and is driven declaratively by a module's `ExecutionPolicy`.

   When a background module, such as the EQS solver, declares its execution policy as `SlowBackground` (or explicitly requests `DataStrategy.SoD`), the kernel automatically provisions the appropriate `ISnapshotProvider`. The API contract is strictly abstracted behind `AcquireView()` and `ReleaseView(ISimulationView view)`, which the kernel's dispatch loop invokes automatically before and after the module's `Tick()` method executes.

   Under the hood, the canonical pattern works as follows:

   - **Zero-Allocation Pooling:** Snapshots are full `EntityRepository` instances kept in a thread-safe `ConcurrentStack` via a `SnapshotPool`. When a view is released, it is soft-cleared and pushed back to the pool, guaranteeing zero heap allocations on the hot path.
   - **Filtered Synchronization:** When `AcquireView()` is called, the provider pulls a snapshot from the pool and invokes `SyncFrom` against the live world. It uses a `BitMask256` derived from the module's `GetRequiredComponents()` to selectively synchronize only the specific component types the module cares about. Unmanaged structs bypass per-entity copying and rely on high-speed block memory transfers.
   - **Convoy Sharing:** To optimize memory bandwidth, if multiple asynchronous modules run at the same frequency and require similar data, the kernel clusters them into a "convoy" using a `SharedSnapshotProvider`. The provider computes a union mask of their component requirements, performs exactly one `SyncFrom` copy against the live world, and shares that read-only snapshot across the background threads using reference counting (`_activeReaders`).
   - **Event History Accumulation:** Because background modules tick at lower frequencies (e.g., 10 Hz), the snapshot process uses an `EventAccumulator` to inject any transient events that occurred since the module's last execution directly into the snapshot's private replica bus.

   Thus, the EQS solver simply receives an `ISimulationView` interface in its `Tick` method. It gets a consistent, thread-safe snapshot of the spatial data and components it requested, completely isolated from the live world's 60Hz mutations and without introducing lock contention.



The EQS solver does not own a dedicated thread that wakes on a timer, nor does the engine implement a bespoke shared background-thread pool. Everything relies on the standard .NET `ThreadPool`.

The `AreaQuerySolverSystem` is registered within a module (like `CognitiveSpatialModule` or `EqsModule`) that defines its execution policy as `ExecutionPolicy.SlowBackground(10)` to run asynchronously at 10 Hz. The `ModuleHostKernel` handles the orchestration. When the frequency threshold is met, the kernel automatically offloads the module's entire tick to the ThreadPool via a standard `Task.Run` dispatch.

Because the module executes on the ThreadPool, if the solver needs to evaluate candidate batches concurrently during its tick, it can seamlessly use standard `Parallel.For` or the engine's `ForEachParallel` utility. Both the kernel's async module dispatch and the parallel partitioner share the same underlying ThreadPool, naturally balancing the load without thread oversubscription.

Furthermore, spinning a dedicated thread would bypass the kernel's safety envelope. When the `ModuleHostKernel` dispatches the background task, it wraps it in a cooperative timeout race (`Task.WhenAny` against a `MaxExpectedRuntimeMs` delay) tied to a Circuit Breaker. If a long-running solver heavily overruns its budget or hangs, the kernel will log a timeout, record the failure in the circuit breaker, and safely abandon the task to ensure the background pool isn't permanently starved.





Yes, your guess is entirely correct. For per-sensor scheduling or background refresh rates like a 5 Hz EQS evaluation, the solver must strictly use simulation time.

Our engine enforces a strict separation between simulation time and wall-clock time through the `TimeSystem` and the `GlobalTime` singleton. Using simulation time (`GlobalTime.TotalTime` or `ISimulationView.Time`) ensures that the EQS solver natively respects the game's `TimeScale` (smoothly handling slow-motion or fast-forward) and properly halts when the simulation pauses.

Furthermore, our architecture demands strict determinism. If the solver relied on wall-clock time (such as `DateTime.UtcNow` or `GlobalTime.TotalWallTicks`), the frequency of queries would vary depending on the host machine's CPU speed and render framerate. This would instantly break our lockstep cluster synchronization and our deterministic `FlightRecorder` replays. Wall-clock time is explicitly reserved for real-world synchronization layers, like recording keyframes or updating UI elements that shouldn't be affected by slow-motion.

Under the hood, you actually won't need to hand-roll this 5 Hz timer inside the solver logic. The `ModuleHostKernel` manages module execution frequencies natively. If you assign the EQS module an `ExecutionPolicy.SlowBackground(5)`, the kernel's `ShouldRunThisFrame` method will automatically evaluate its execution based on elapsed simulation frames. Since the engine runs at a base rate of 60 Hz, the kernel will simply dispatch the solver task every 12 simulation frames.

If your solver needs to track internal, per-sensor cooldowns or individual query timeouts across its execution ticks, it should read `ISimulationView.Time` or `ISimulationView.Tick` provided in its `Execute` or `Tick` method signature. This transparently provides the correct, deterministic simulation time without needing to interact with the `TimeSystem` directly.



Multiple modules can safely submit raycast queries concurrently because submission is completely lock-free; systems simply publish `RaycastRequestEvent`s via their thread-local `EntityCommandBuffer`s to the event bus. You do not need to build your own coalescing mechanism inside the EQS solver.

When the `RaycastSolverSystem` executes, it reads the entire accumulated batch of requests from the bus at once. However, it does not group N rays into a single vectorized SIMD operation (i.e., we do not use SIMD ray packets). Instead, it uses a `Parallel.For` loop to distribute the batch across the `.NET` thread pool, resolving each ray independently. Each thread performs its own broad-phase AABB spatial hash query, followed by narrow-phase `Intersection2D.RaycastCircle` checks against the candidate colliders.

Because of this architecture, your EQS solver can naively fire off 30 LOS checks for a candidate batch simply by publishing 30 `RaycastRequestEvent`s. The `RaycastSolverSystem` will automatically aggregate them alongside requests from the perception or ballistics modules and efficiently load-balance the work across available CPU cores.

The only strict limitation to observe is the global `PhysicsConstants.RaycastBatchCapacity`, which is pre-allocated to 4096. As long as the combined volume of in-flight raycasts across all systems per frame stays under this limit, the unmanaged `RaycastBatchData` ring buffer will easily handle your naive submissions.



Introducing a wall-time budget fits perfectly into a well-established pattern within the engine; EQS would absolutely not be the first system to do this.

The engine natively supports time-sliced execution to distribute heavy workloads across multiple frames without stalling the CPU.

**Existing Time-Slicing Patterns** The primary pattern relies on the `EntityRepository.QueryTimeSliced` method, which takes an `IteratorState` object to allow pausing and resuming execution across multiple frames,. You can pass it a specific budget alongside a `TimeSliceMetric`, such as `TimeSliceMetric.WallClockTime`,. Under the hood, the enumerator periodically checks the system stopwatch (every 64 entities) against the budget,. If the elapsed time exceeds the budget, it saves the current index into the `IteratorState.NextEntityId`, sets `IsComplete = false`, and cleanly exits the loop.

We already use this extensively in the `MapLayerAssignmentSystem`, which allocates a strict 1.0ms wall-clock budget per frame (`PerFrameBudgetMs = 1.0`) to avoid burning through the CPU budget when evaluating massive numbers of entities,. Additionally, the `TimeSystem` provides a `HasTimeRemaining(double estimatedCostMs)` helper to check if a frame still has available CPU time,.

You should utilize these mechanisms in the EQS solver to evaluate candidate batches.

**Budget Overruns and Reporting** There is a distinct difference between gracefully yielding your time slice and violating a hard execution budget:

1. **Graceful Yielding:** When your EQS solver reaches its 4ms budget and exits its evaluation loop to resume on the next tick (using `IteratorState`), this is normal, expected behavior. You do not need to log this as an error or budget overrun.
2. **Hard Overruns:** If your solver gets stuck, ignores its budget, and exceeds the `MaxExpectedRuntimeMs` defined in its `ExecutionPolicy` (which defaults to at least 1 frame's worth of time based on frequency), the `ModuleHostKernel` will intervene,. The kernel wraps background module execution in a cooperative cancellation race (`Task.WhenAny` against a delay task),. If your solver times out, the kernel will automatically log the overrun to `Console.Error` and record a "Timeout" failure in the module's `CircuitBreaker`.

Therefore, you do not need to hand-roll custom logging for catastrophic overruns; the kernel already enforces this safety envelope. Simply design your EQS batches to check the stopwatch or use `QueryTimeSliced` to exit cleanly before that threshold is reached,.



We ha no prior data. Assume standard Recast performance.





While CycloneDDS and our `[DdsManaged] List<T>` network serialization natively handle variable-size payloads and network fragmentation, you will not need to worry about DDS health because we strictly cap these sizes at the unmanaged ECS tier *before* they ever reach the network translators.

For your specific sensor example, it will never produce a 50-entry ranked list. In our engine, both the `TargetMemory` cognitive buffer and the raw `SensorContactList` are strictly hardcapped by `PerceptionConstants.MaxTrackedTargets`, which limits them to exactly 4 targets. If a sensor detects more targets than this limit, the system evaluates the threat scores and seamlessly replaces the lowest-scoring target with the new one.

This strict capacity capping is applied across all subsystems to preserve zero-allocation, deterministic performance:

- **EQS Queries:** The unmanaged `EqsTargetPool` limits area query results, sizing the pool to support up to 64 concurrent queries with a maximum of 16 results each.
- **Raycasts:** Raycast batches are globally capped by `PhysicsConstants.RaycastBatchCapacity` at a strict limit of 4096 rays per frame, silently dropping excess rays if the budget is exceeded.

Because our engine enforces these tight, fixed-size capacities in its internal memory structures, the variable-length payloads mapped into `[DdsManaged]` lists for DDS transit are naturally kept extremely small and network-friendly by design.