Combining the Area Query (EQS) and Perception systems into a single background module is the required Data-Oriented Design (DOD) approach to achieve safe concurrent execution and optimal CPU utilization. Sharing a single unmanaged spatial grid between the 60 Hz main-thread physics update and a 10 Hz background AI module introduces a severe race condition. By merging them, we guarantee thread isolation from the 60 Hz main simulation and eliminate the overhead of building the spatial hash grid twice per background tick. To accurately reflect its domain boundary, this merged module should be named `CognitiveSpatialModule`.

### 1. Merging and Grid Isolation
To achieve thread safety and lock-free execution, the `CognitiveSpatialModule` must not rely on the main-thread `SpatialGridData` ECS singleton. Instead, it must instantiate a private `SpatialHashGrid` at construction time and hold the native memory pointers internally. 

In the module's execution pipeline, `LocalGridBuilderSystem` runs as the very first step on the background thread inside the module's `Tick`. It safely reads entity positions from the read-only Snapshot-on-Demand (SoD) view and populates the private grid. Subsequent background solvers, such as `VisionBroadphaseSystem` and `AreaQuerySolverSystem`, receive this private grid via constructor dependency injection. This isolates the solvers, guaranteeing that all cognitive spatial queries execute against a stable data structure without risk of memory corruption from the main-thread physics state.

### 2. Communicating with the Main World
Synchronization between the 60 Hz main thread and the 10 Hz background thread relies entirely on the `EventAccumulator` and thread-local `IEntityCommandBuffer` instances. This pipeline guarantees zero lock contention and prevents event loss across mismatched thread frequencies.

**The Request (Main Thread -> Background)**
When the BTree logic requires a query, it publishes an `AreaQueryRequestEvent` to the live main-thread `FdpEventBus`. At the end of every 60 Hz frame, the `EventAccumulator` captures the event history. When the 10 Hz background module executes, it acquires its SoD view. During this phase, the `EventAccumulator` flushes all captured events from the preceding frames directly into the snapshot's isolated replica bus. The background `AreaQuerySolverSystem` then consumes the accumulated batch cleanly by calling `view.ReadEvents<AreaQueryRequestEvent>()`.

**Bulk Data Transfer (Split-Context Live Injection)**
FDP enforces a strict 24-byte footprint for all unmanaged events, meaning it is mathematically impossible to pack dynamically sized arrays of resolved targets into the event payload. To bypass this without heap allocations, the architecture mandates an indirection pattern via shared memory. 

The background solver uses a split-context approach: it is injected with the live `EntityRepository` alongside its read-only SoD view. It retrieves the unmanaged `EqsTargetPool` singleton directly from the live world. Because this singleton simply wraps a persistent `NativeArray` pointer, the background thread performs zero-allocation, thread-safe writes to deposit the packed entity handles directly into the live pool.

**The Response (Background -> Main Thread)**
Background modules cannot write to the live bus directly. Once the solver writes the entity targets to the unmanaged pool, it constructs an `AreaQueryResultEvent` carrying only the `TargetGroupHandle` integer index, the `TargetCount`, and the `NewPoolNextFreeIndex`. It publishes this event into its thread-local `IEntityCommandBuffer`. 

When the asynchronous background task completes, the `ModuleHostKernel` running on the main thread detects it during its `HarvestEntry` phase. The kernel extracts the background thread's command buffer and calls `Playback()`. This safely flushes the `AreaQueryResultEvent` onto the live main-thread event bus. Finally, `AreaQueryResultMaterializationSystem` executes synchronously on the main thread during `SystemPhase.Input`, intercepting the event and finalizing the write into the `AreaQueryBatchData` ring buffer while advancing the live pool's cursor.
