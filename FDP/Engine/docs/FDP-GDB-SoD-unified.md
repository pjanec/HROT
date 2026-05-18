# FDP - GDB & SoD snapshost and unification


This document shows the evolution of thoughts about the snapshotting system. It is cumulative as the solution evolves - needs to be read till the end.


This is an excellent question. "Global Double Buffering" is often misunderstood as just a graphics technique (Front/Back buffer), but applied to ECS architecture, it functions as a **"Master/Replica" database pattern**.

Here is exactly how it solves the slow module problem, how it differs from Snapshot-on-Demand (SoD), and the specific "Swap Logic" required to make it work.

---

### 1. The Concept: "Twin Universes"

Imagine you have two completely allocated `EntityRepository` instances in memory.
1.  **World A (Hot / Simulation):** The Main Thread owns this. It runs Physics, Timers, and Logic at 60Hz. It changes every 16ms.
2.  **World B (Cold / Background):** The Background Threads own this. It is a **frozen echo** of the past.

### 2. The Execution Flow (How it handles "Slow")

Let's say you have a **Slow AI Module** that takes **50ms** (approx. 3 frames) to make a decision.

#### Frame 1 (Start)
*   **Sync Point:** The Engine performs a **Full Sync**.
    *   It copies all dirty data from **World A** $\to$ **World B**.
    *   *Cost:* High (copying memory), but it happens on the Main Thread.
*   **Dispatch:** The Host tells the AI Module: "Go run your logic using **World B**."
*   **World A:** Resumes simulation.

#### Frame 2 (16ms later)
*   **World A:** Advances to Tick 101. Physics moves entities.
*   **World B:** Is **LOCKED** by the AI Module. It is still sitting at Tick 100.
*   **Sync Point:** The Host checks: "Is AI done?" -> **NO**.
*   **Action:** The Host **SKIPS** the sync. It does **not** copy A $\to$ B because B is busy. World A and World B drift apart.

#### Frame 3 (32ms later)
*   **World A:** Advances to Tick 102.
*   **World B:** Still locked by AI at Tick 100.
*   **Sync Point:** AI is still busy. No sync.

#### Frame 4 (48ms later)
*   **World A:** Advances to Tick 103.
*   **World B:** AI finishes! It sends commands to the Command Buffer based on Tick 100.
*   **Sync Point:** Host sees AI is idle.
*   **Action:** Host performs **Full Sync**.
    *   It copies changes from **World A (Tick 103)** $\to$ **World B**.
    *   World B "teleports" from Tick 100 to Tick 103.
*   **Dispatch:** Host tells AI: "Go run again on this new data."

---

### 3. Why this solves the issue

1.  **Zero contention:** The Main Thread never waits for a lock on individual components. It owns World A exclusively.
2.  **Consistency:** The AI sees a perfectly consistent world at Tick 100 for the entire 50ms duration. It doesn't matter that the "Real World" moved on; the AI is thinking about the past.
3.  **Simple Memory Management:** You don't allocate "Partial Snapshots" or "Union Masks." You just have two big blocks of RAM.

### 4. The Critical Difference: "Full Copy" vs. "Dirty Patching"

If you implement Global Double Buffering naively (copy *everything* every time), it is too slow.
You must combine it with **Dirty Patching**.

*   **Naive:** `memcpy` 500MB every time the AI finishes. (Too slow).
*   **Optimized:**
    *   World B remembers it contains **Version 100**.
    *   World A is at **Version 103**.
    *   The Sync operation only copies chunks where `Chunk.Version > 100`.
    *   *Result:* Only the data changed during the AI's "thinking time" is copied. Static terrain/units are untouched.

### 5. Pros and Cons vs. Snapshot-on-Demand

| Feature | Snapshot-on-Demand (SoD) | Global Double Buffering (GDB) |
| :--- | :--- | :--- |
| **Memory Usage** | **Low.** Allocates small buffers only for requested components. | **High.** Allocates 2x Full World RAM. |
| **Bandwidth** | **Minimal.** Copies only `Position` if AI only asks for `Position`. | **Medium.** Copies `Position`, `Health`, `Ammo`... anything that changed, even if AI ignores it. |
| **Complexity** | **High.** Need `UnionMask` logic, `ArrayPool`, `Snapshot` object lifecycle. | **Low.** Just 2 Repositories and a `Sync(A, B)` method. |
| **Fragmentation** | **High.** Many small arrays created/pooled. | **Zero.** Huge contiguous blocks reused forever. |
| **Scalability** | **High.** Can have 10 satellites reading different things. | **Low.** If Satellite 1 locks World B, Satellite 2 must wait (or read the same old data). |

### 6. When to use Global Double Buffering?

Use GDB if:
1.  **You have RAM to spare.** (Doubling 200MB to 400MB is fine on PC).
2.  **Your Satellites mostly need "Everything".** If the AI needs to read almost every component type to make decisions, SoD ends up copying the whole world anyway. GDB is more efficient for "Dense" reads.
3.  **You want code simplicity.** Implementing `Repo.SyncTo(Repo other)` is much easier than writing a `SnapshotManager`.

### 7. Implementation Note for FDP

If you choose GDB, `EntityRepository` needs a mode to act as a **"Passive Replica"**.

```csharp
public class EntityRepository
{
    // ... tables ...

    public void SyncFrom(EntityRepository master)
    {
        // 1. Sync Entities (Headers)
        // Copy dirty EntityIndex chunks
        
        // 2. Sync Components
        foreach(var type in _componentTables.Keys)
        {
            var masterTable = master.GetTable(type);
            var myTable = this.GetTable(type);
            
            // Tier 1: memcpy dirty chunks
            // Tier 2: Array.Copy dirty chunks
        }
    }
}
```

This is effectively a **"Full State Mirror"**. It is a very robust pattern used in networking (Client is a Double Buffer of the Server), so implementing it has dual benefits.


—-------------
RAM is not a problem. My satellites are different (some need more, some need less) but there are more of them then one. I definitely need a bacground thread satellites, but maybe i could aggregate them  in a way they all read the same frozen world and the slowest  defines the rate for all others; the critical requirement is that but they do not affect the critical modules on the hot path. Maybe i could start with the GDB prior of trying SoD? Also, how would we merge changes from world B with changes from world A? Do all satellites  write to command buffers only? What of there are conflicts (A is newer?)
—-------

This is a fantastic decision. Given that **RAM is cheap** and your priority is **Simplicity + Main Thread Stability**, starting with **Global Double Buffering (GDB)** is the engineering "sweet spot."

It eliminates 80% of the complexity of the "Snapshot-on-Demand" system (no union masks, no partial array pooling) while delivering 100% of the safety.

Here is the blueprint for the **Aggregated GDB Strategy**, how to handle the "Merge Back," and how to manage conflicts.

---

### 1. The Strategy: "The Bus Stop" (Aggregated Satellites)

You create **One Replica World** (World B). All background modules board the "Bus" together. The Bus doesn't leave (Sync) until everyone is back on board.

#### Architecture
*   **World A (Hot):** The Live Simulation.
*   **World B (Cold):** The Replica.
*   **Satellite Group:** A list of modules (AI, Net, Logs) that read World B.

#### The Cycle
1.  **Sync Phase (Main Thread):**
    *   Check: Are all Satellites `Idle`?
    *   **If No:** Skip Sync. World A continues alone. World B remains frozen at `Tick 100`.
    *   **If Yes:**
        *   `RepoB.SyncFrom(RepoA)`. (Copies dirty chunks).
        *   Set `RepoB.Version = RepoA.Version`.
        *   **Dispatch:** Tell *all* Satellites: "New data available (Tick 105). Go!"
2.  **Simulation (Main Thread):** Continues to Tick 106, 107...
3.  **Background (Worker Threads):**
    *   AI reads Repo B.
    *   Network reads Repo B.
    *   They run in parallel with each other AND the Main Thread.

**The Trade-off:**
*   **Pro:** Zero impact on Main Thread (except the copy moment).
*   **Con:** The "Fastest" satellite is held back by the "Slowest."
    *   *Mitigation:* Since RAM is cheap, if the Network needs to run faster than the AI, just allocate **World C** dedicated to Network. You can scale from 1 Replica to N Replicas easily.

---

### 2. How to "Merge" Changes (The Write Path)

You asked: *"How would we merge changes from world B? Do all satellites write to command buffers only?"*

**The Golden Rule:** **World B is READ-ONLY.**
Satellites **never** modify World B. If they did, World A wouldn't know, and the changes would be overwritten at the next Sync.

**The Write Pipeline:**
1.  **Satellite Decision:** AI decides "Unit 5 must move."
2.  **Command Generation:** AI creates a struct: `MoveCommand { Id = 5, Dest = (10,10) }`.
3.  **Enqueue:** AI pushes this struct into a `ConcurrentQueue<ICommand>`.
4.  **Playback (Main Thread):**
    *   At the start of the *next* frame (e.g., Tick 108).
    *   Main Thread drains the queue.
    *   Main Thread calls `WorldA.GetComponentRW(5)` and applies the logic.

---

### 3. Handling Conflicts (A vs. B)

You asked: *"What if there are conflicts (A is newer)?"*

This is the definition of **Optimistic Concurrency**.
*   **Situation:**
    *   **World B (Tick 100):** Unit 5 is Alive. HP = 10.
    *   **AI (Thinking...):** "Unit 5 is low health. Send `HealCommand(Target=5)`."
    *   **World A (Tick 105):** Unit 5 took 20 damage and died. ID 5 is now recycled or null.
    *   **Execution (Tick 106):** Main Thread receives `HealCommand(5)`.

**The Conflict Resolution Strategy:**
The **Main Thread is the Dictator**. The Command Buffer must validate the state before applying.

#### Option 1: "Fire and Forget" (Naive)
*   **Logic:** `if (RepoA.IsAlive(cmd.Id)) { Heal(); }`
*   **Risk:** If ID 5 was recycled to a *new* tank, you just healed the wrong unit.
*   **Fix:** Always include `Generation` in your commands. `Entity` struct has this built-in.

#### Option 2: "Precondition Validation" (Robust)
The Command struct includes "What I saw when I made the decision."

```csharp
public struct HealCommand : ICommand
{
    public Entity Target; // Includes Index + Generation
    public int ExpectedHP; // What HP was it when I decided?
    
    public void Execute(EntityRepository repo)
    {
        // 1. Existence Check
        if (!repo.IsAlive(Target)) return; // It died. Fail.

        // 2. State Check (Optional)
        // If it was fully healed by someone else while I was thinking, abort?
        var currentHP = repo.GetComponentRO<Health>(Target).Value;
        if (currentHP != ExpectedHP) { /* Log warning or Abort */ }

        // 3. Apply
        ref var health = ref repo.GetComponentRW<Health>(Target);
        health.Value += 10;
    }
}
```

---

### 4. Is Tier 2 still tricky?

Even with Global Double Buffering, **Tier 2 (Managed) must use Records/Immutability.**

**Why?**
*   `RepoB.SyncFrom(RepoA)` performs a **Shallow Copy** of managed arrays.
*   `RepoA.List[5]` and `RepoB.List[5]` point to the **Same Object** on the Heap.
*   If the Main Thread modifies that object in place (`obj.List.Add(x)`), the Background Thread sees the change immediately (Tearing).

**Requirement:**
Even in GDB, you must use the `record` + `with` pattern for Tier 2 updates in the Main Thread to ensure `RepoA` points to a *new* object, leaving `RepoB` pointing to the *old* object.

### 5. Summary Implementation Plan

1.  **EntityRepository:** Add `SyncFrom(EntityRepository source)`.
    *   Use `CopyChunkToBuffer` for Tier 1 tables.
    *   Use `Array.Copy` for Tier 2 tables.
    *   Use `CopyChunkToBuffer` for `EntityIndex` (Metadata).
2.  **ModuleHost:** Allocate `_liveWorld` and `_replicaWorld`.
3.  **ModuleHost:** Add `ConcurrentQueue<ICommand> _commandBuffer`.
4.  **Satellites:** Inject `_replicaWorld` (as read-only interface) into Satellites.
5.  **Scheduler:** Implement the "Bus Stop" logic:
    ```csharp
    if (_satellites.All(s => s.IsIdle)) {
        _replicaWorld.SyncFrom(_liveWorld);
        foreach(var s in _satellites) Task.Run(() => s.UpdateAsync());
    }
    ```

**This is the safest, most stable starting point.** If you later find that the Network module is stuttering because the AI is too slow, you just allocate `_networkReplicaWorld` and decouple them. Simple.
Model



Model



Model



This is the implementation blueprint for the **3-World Global Double Buffering (GDB)** system.

This design focuses on **High Throughput** (minimizing copy costs via dirty flags) and **Correctness** (solving the Event frequency mismatch).

---

### 1. The Core Mechanism: `SyncFrom`
We need to teach `EntityRepository` how to become a mirror of another repository.

**File:** `Fdp.Kernel/EntityRepository.Sync.cs`

```csharp
public sealed partial class EntityRepository
{
    /// <summary>
    /// Synchronizes this repository (Replica) to match the Source (Live).
    /// </summary>
    /// <param name="source">The Live World</param>
    /// <param name="mask">Optional: Only copy specific components (e.g. AI only needs Position/Team)</param>
    public void SyncFrom(EntityRepository source, BitMask256? mask = null)
    {
        // 1. Sync Metadata (EntityIndex)
        // This is fast (Tier 1 copy). It ensures IsActive/Generations match exactly.
        _entityIndex.SyncFrom(source._entityIndex);
        
        // 2. Sync Components
        foreach (var typeId in _componentTables.Keys)
        {
            // Filtering: Skip if mask exists and bit is not set
            if (mask.HasValue && !mask.Value.IsSet(typeId)) continue;

            var myTable = _componentTables[typeId];
            var srcTable = source._componentTables[typeId];

            if (myTable is IUnmanagedComponentTable unmanaged)
            {
                // Tier 1: Raw Memory Copy
                ((NativeChunkTableAdapter)unmanaged).SyncDirtyChunks((NativeChunkTableAdapter)srcTable);
            }
            else
            {
                // Tier 2: Managed Reference Copy
                ((ManagedComponentTableAdapter)myTable).SyncDirtyChunks((ManagedComponentTableAdapter)srcTable);
            }
        }

        // 3. Sync Global Version
        this._globalVersion = source._globalVersion;
    }
}
```

**File:** `Fdp.Kernel/NativeChunkTable.cs` (and similarly for Managed)

```csharp
public void SyncDirtyChunks(NativeChunkTable<T> source)
{
    // Iterate chunks (0 to MaxChunks)
    for (int i = 0; i < source.TotalChunks; i++)
    {
        // 1. Version Check (The Optimization)
        // If my version matches source, data hasn't changed. Skip.
        uint srcVer = source.GetChunkVersion(i);
        if (_chunkVersions[i].Value == srcVer) continue;

        // 2. Liveness Check
        // If source chunk is empty/uncommitted, ensure we are too.
        if (!source.IsChunkAllocated(i))
        {
            if (this.IsChunkAllocated(i)) this.ClearChunk(i); // Free/Zero ours
            continue;
        }

        // 3. The Copy
        EnsureChunkAllocated(i);
        
        // Unsafe CopyBlock (memcpy)
        // 64KB copy takes ~2 microseconds
        Unsafe.CopyBlock(
            this.GetChunkDataPtr(i), 
            source.GetChunkDataPtr(i), 
            FdpConfig.CHUNK_SIZE_BYTES
        );

        // 4. Update Version
        _chunkVersions[i].Value = srcVer;
    }
}
```

---

### 2. The Event Accumulator (Solving the "Deaf AI")
This acts as the bridge between the high-frequency Live World and low-frequency Satellites.

**Concept:**
Instead of clearing the Event Bus at the end of the frame, we **Harvest** the internal buffers and store them in a queue.

**File:** `Fdp.Kernel/EventAccumulator.cs`

```csharp
public class EventAccumulator
{
    // Stores buffers from previous frames.
    // Key: FrameIndex, Value: List of Streams (Native & Managed) from that frame
    private readonly Queue<FrameEventData> _history = new();

    public void CaptureFrame(FdpEventBus liveBus, ulong frameIndex)
    {
        // 1. Extract buffers from Live Bus
        // This effectively "Steals" the buffers. 
        // The Live Bus will allocate fresh ones next frame (via Pooling).
        var frameData = liveBus.ExtractAndRetireBuffers(); 
        frameData.FrameIndex = frameIndex;
        
        _history.Enqueue(frameData);
    }

    public void FlushToReplica(FdpEventBus replicaBus)
    {
        // Inject ALL history into the Replica's Current (Read) buffer
        while (_history.TryDequeue(out var frameData))
        {
            // For Unmanaged: We can memcpy the raw bytes into the Replica's stream
            // effectively appending Frame 1 + Frame 2 + Frame 3...
            foreach (var stream in frameData.NativeStreams)
            {
                replicaBus.InjectIntoCurrent(stream.TypeId, stream.GetRawBytes());
                // Return stream to pool after injection
                stream.Dispose(); 
            }

            // For Managed: AddRange to list
            foreach (var stream in frameData.ManagedStreams)
            {
                replicaBus.InjectManagedIntoCurrent(stream.TypeId, stream.GetList());
            }
        }
    }
}
```

---

### 3. The Satellite Interface (Read-Only)
Satellites should not see `EntityRepository` because it exposes `SetComponent`. They need a constrained view.

**File:** `Fdp.Kernel/ISimulationView.cs`

```csharp
public interface ISimulationView
{
    // Read-Only Accessors
    ref readonly T GetComponentRO<T>(Entity e) where T : unmanaged;
    T GetManagedComponentRO<T>(Entity e) where T : class;
    
    // Querying
    EntityQueryBuilder Query();
    
    // Events
    ReadOnlySpan<T> ConsumeEvents<T>() where T : unmanaged;
    
    // Metadata
    uint CurrentTick { get; }
    float Time { get; }
}

// EntityRepository implements this interface implicitly
```

---

### 4. The Orchestrator (Module Host)
This puts it all together. This runs in your Application Layer (`BOne.Host`).

**File:** `BOne.Host/EngineLoop.cs`

```csharp
public class EngineLoop
{
    // Worlds
    private EntityRepository _liveWorld;
    private EntityRepository _fastReplica; // For Net/Recorder
    private EntityRepository _slowReplica; // For AI

    // Systems
    private EventAccumulator _slowAccumulator = new();
    private List<IBackgroundModule> _slowModules = new();
    
    // Write-Back
    private ConcurrentQueue<ICommand> _commandBuffer = new();

    public void Tick()
    {
        // ------------------------------------------------
        // 1. EXECUTE LIVE FRAME (60Hz)
        // ------------------------------------------------
        _liveWorld.Tick();
        PhysicsSystem.Update(_liveWorld);
        LogicSystem.Update(_liveWorld);

        // ------------------------------------------------
        // 2. FAST SYNC (Every Frame)
        // ------------------------------------------------
        // Always sync FastReplica immediately for Recorder/Net
        _fastReplica.SyncFrom(_liveWorld); 
        
        // Also copy events 1:1
        _liveWorld.Bus.CopyTo(_fastReplica.Bus); 

        // Trigger Recorder (Runs Async reading _fastReplica)
        Recorder.CaptureAsync(_fastReplica);

        // ------------------------------------------------
        // 3. SLOW SYNC (Conditional)
        // ------------------------------------------------
        
        // A. Capture events for history
        _slowAccumulator.CaptureFrame(_liveWorld.Bus, _liveWorld.GlobalVersion);

        // B. Check if AI is done/idle
        if (AreModulesIdle(_slowModules))
        {
            // Sync State (Only dirty chunks!)
            // Optional: Use a Mask here to copy only Logic data
            _slowReplica.SyncFrom(_liveWorld, _aiMask); 
            
            // Sync Events (Flush 6 frames of history)
            _slowAccumulator.FlushToReplica(_slowReplica.Bus);

            // Dispatch
            foreach(var mod in _slowModules)
            {
                Task.Run(() => mod.UpdateAsync(_slowReplica, _commandBuffer));
            }
        }

        // ------------------------------------------------
        // 4. COMMAND PLAYBACK
        // ------------------------------------------------
        while (_commandBuffer.TryDequeue(out var cmd))
        {
            // Validate: Is entity still alive in LiveWorld?
            if (_liveWorld.IsAlive(cmd.Entity))
            {
                cmd.Execute(_liveWorld);
            }
        }

        // ------------------------------------------------
        // 5. CLEANUP
        // ------------------------------------------------
        _liveWorld.EndOfFrame(); // Swaps Live Bus buffers
    }
}
```

### 5. Implementation Roadmap

1.  **Refactor `NativeChunkTable`**: Ensure it exposes `GetChunkDataPtr` (internal unsafe) and `GetChunkVersion`.
2.  **Implement `SyncFrom`**: Write the loop that iterates chunks and compares versions. This is the engine of the GDB.
3.  **Implement `EventAccumulator`**: This solves the logic gap.
4.  **Setup Host**: Create the loop shown above.

### Why this is "Safe" & "Friendly"

1.  **Developer Experience:** The AI developer writes `view.GetComponentRO<Position>(e)`. They don't know they are on a replica. They don't know about snapshots. It just works.
2.  **Safety:** The compiler prevents `view.SetComponent`. The Architecture prevents race conditions (World B is immutable while AI runs).
3.  **Performance:**
    *   **Tier 1:** `SyncFrom` is just a series of `memcpy`s for dirty chunks.
    *   **Tier 2:** `SyncFrom` is `Array.Copy` (reference copy).
    *   **Events:** Zero-copy accumulation (moving buffer pointers).

This is a robust, production-grade architecture for simulation.
Model



Yes, this is the **Ultimate Architectural pattern**.

By unifying GDB and SoD under a single abstraction, you treat **"Storage Strategy"** as a configuration setting, not a hard-coded architecture.

The secret is to realize that **a Snapshot IS a Repository**.
*   **GDB:** A persistent Repository that you sync incrementally.
*   **SoD:** A pooled/transient Repository that you sync from scratch (or sparse copy) and then recycle.

Here is how to build the **Unified Data Access Layer (UDAL)** so you can switch strategies instantly.

---

### 1. The Unified Interface: `ISimulationView`

This is the only thing your Modules (AI, Net, UI) ever see. They don't know if they are looking at the Live World, a Double Buffer, or a Tiny Snapshot.

```csharp
// Fdp.Kernel/Abstractions/ISimulationView.cs

public interface ISimulationView
{
    // 1. Metadata
    uint Tick { get; }
    float Time { get; }

    // 2. Component Access (READ ONLY)
    // Note: No SetComponent here!
    ref readonly T GetComponentRO<T>(Entity e) where T : unmanaged;
    T GetManagedComponentRO<T>(Entity e) where T : class;
    
    // 3. Existence
    bool IsAlive(Entity e);
    
    // 4. Querying
    // Returns a query builder that works on this specific view
    EntityQueryBuilder Query();

    // 5. Events
    // Returns events visible in this view (History + Current)
    ReadOnlySpan<T> ConsumeEvents<T>() where T : unmanaged;
}

// The EntityRepository implements this natively
public sealed partial class EntityRepository : ISimulationView { ... }
```

---

### 2. The Strategy Pattern: `ISnapshotProvider`

This sits in your Host/Scheduler. It hides the complexity of *how* the view is created.

```csharp
public interface ISnapshotProvider : IDisposable
{
    // Called by Scheduler at Phase 4 (Sync Point)
    // mask: What data does the consumer need?
    // lastSeenTick: Used for event accumulation logic
    ISimulationView AcquireView(BitMask256 mask, uint lastSeenTick);
    
    // Called by Satellite when logic finishes
    void ReleaseView(ISimulationView view);
}
```

---

### 3. Implementation A: `DoubleBufferProvider` (GDB)

This implementation maintains a **Persistent Replica**. It ignores the `mask` because it just syncs everything (or a broad "Fast/Slow" config).

```csharp
public class DoubleBufferProvider : ISnapshotProvider
{
    private readonly EntityRepository _live;
    private readonly EntityRepository _replica; // Persistent World B
    private readonly EventAccumulator _events = new();

    public DoubleBufferProvider(EntityRepository live)
    {
        _live = live;
        _replica = new EntityRepository(); // Allocates full memory
    }

    public ISimulationView AcquireView(BitMask256 mask, uint lastSeenTick)
    {
        // 1. Sync State (Dirty Chunks Only)
        // We ignore 'mask' here to keep the replica complete, 
        // OR we use the mask to only sync what's requested if we want "Filtered GDB"
        _replica.SyncFrom(_live);

        // 2. Sync Events
        _events.Capture(_live.Bus);
        _events.FlushTo(_replica.Bus, lastSeenTick);

        return _replica; // Return the persistent object
    }

    public void ReleaseView(ISimulationView view)
    {
        // Do nothing. The replica stays alive for next frame.
    }
}
```

---

### 4. Implementation B: `OnDemandProvider` (SoD)

This implementation uses an **Object Pool** of empty Repositories. It syncs *only* what is requested and wipes the repo on return.

```csharp
public class OnDemandProvider : ISnapshotProvider
{
    private readonly EntityRepository _live;
    private readonly ConcurrentStack<EntityRepository> _pool = new();
    private readonly EventAccumulator _events = new();

    public ISimulationView AcquireView(BitMask256 mask, uint lastSeenTick)
    {
        if (!_pool.TryPop(out var snapshotRepo))
        {
            snapshotRepo = new EntityRepository(); 
        }

        // 1. Sync ONLY the Mask (Deep Optimization)
        // This method copies EntityIndex + specific component tables
        snapshotRepo.SyncFrom(_live, mask);

        // 2. Sync Events
        _events.Capture(_live.Bus);
        _events.FlushTo(_snapshotRepo.Bus, lastSeenTick);

        return snapshotRepo;
    }

    public void ReleaseView(ISimulationView view)
    {
        var repo = (EntityRepository)view;
        
        // Clear metadata but keep pages allocated for reuse?
        // Or Soft Clear to avoid decommitting memory?
        repo.SoftClear(); 
        
        _pool.Push(repo);
    }
}
```

---

### 5. How to switch "Very Quickly"

In your `ModuleHost` configuration, you map Modules to Strategies.

```csharp
// AppConfig.cs
public void Configure(ModuleHost host)
{
    // Scenario 1: PC (High RAM) -> Use GDB for everything
    host.SetDefaultStrategy(new DoubleBufferProvider(liveWorld));

    // Scenario 2: Console/Heavy Logic -> Hybrid
    var fastGdb = new DoubleBufferProvider(liveWorld); // For Net/Recorder
    var smartSod = new OnDemandProvider(liveWorld);    // For AI
    
    host.RegisterModule(new NetworkModule(), fastGdb);
    host.RegisterModule(new AiModule(), smartSod);
}
```

### 6. Where the implementations overlap (Reuse)

You reuse 95% of the code.

1.  **`SyncFrom`:** Both strategies use the same `EntityRepository.SyncFrom(source, mask)` method.
    *   GDB calls it with `mask = null` (Sync all dirty).
    *   SoD calls it with `mask = specific_bits` (Sync specific columns).
2.  **`EventAccumulator`:** Used by both. GDB flushes to persistent bus; SoD flushes to transient bus. Logic is identical.
3.  **`NativeChunkTable`:** Used by both.
    *   GDB tables are dense (fully allocated).
    *   SoD tables are sparse (only requested columns are allocated via `VirtualAlloc`).

### Summary of Benefits

*   **Zero Refactoring Risk:** You write your AI module against `ISimulationView`. It works today with GDB. Tomorrow, if you need to save RAM, you switch the config to SoD. The AI code **does not change**.
*   **Best of Both Worlds:** You can run GDB for the "Fast Lane" (Network) and SoD for the "Slow Lane" (AI) simultaneously in the same engine instance.
*   **Testing:** You can easily mock `ISimulationView` for unit tests without spinning up a full engine loop.

**Verdict:** Implement `ISimulationView` and `EntityRepository.SyncFrom`. This gives you the GDB capability immediately, and the SoD capability is just a "Pooled GDB" implementation away.
Model



Yes, this unified approach preserves **all** the optimizations we designed for SoD (Snapshot-on-Demand), including the zero-copy Event History.

You lose nothing. You simply gain a new **"Caching Strategy"**.

Here is how the architecture unifies, how the optimizations persist, and how to implement the "Shared Slow GDB" (The Convoy Pattern).

---

### 1. Unified Engine: One logic, two configurations

The core machinery (`SyncFrom`, `EventAccumulator`, `CopyChunkToBuffer`) is identical. The only difference is **Persistence**.

| Feature | **SoD Strategy** | **GDB Strategy** | **Shared Implementation** |
| :--- | :--- | :--- | :--- |
| **Component Data** | Sparse (Allocated on demand) | Dense (Pre-allocated) | `EntityRepository.SyncFrom(mask)` |
| **Events** | Transient (History Passed via Ptrs) | Accumulated (History Appended via Ptrs) | `EventAccumulator` |
| **Updates** | "Create New" every request | "Update Existing" when idle | `SyncDirtyChunks` logic |
| **Memory** | Returned to Pool | Kept in RAM | `NativeChunkTable` |

**The Optimization Preservation:**
Because `SyncFrom` checks `chunk.Version`, the GDB strategy becomes **Incremental GDB**.
*   **Frame 1:** Full Copy.
*   **Frame 2:** Physics updates 5 chunks.
*   **Sync:** GDB only copies those 5 chunks.
*   **Result:** You get the stability of Double Buffering with the bandwidth efficiency of SoD.

---

### 2. The "Shared Slow GDB" (The Convoy Pattern)

You asked: *"How could we reuse GDB buffer for multiple slow modules where slower defines the pace?"*

We implement this using a **Reference Counting Strategy**. Think of the Shared World as a **Bus**. The Bus waits at the station until every passenger (Satellite) has finished their trip.

#### The Components

1.  **The Shared Repository:** One persistent `EntityRepository` instance.
2.  **The Gatekeeper:** A class `SharedSnapshotProvider`.
3.  **The Lock:** An atomic `_activeReaders` counter.

#### The Workflow

1.  **Sync Phase (Main Thread):**
    *   Host asks `SharedProvider`: "Can I update?"
    *   Provider checks: `if (_activeReaders == 0)`
        *   **YES:**
            *   `SyncFrom(LiveWorld)`.
            *   `EventAccumulator.Flush()`. (Injects all history since last sync).
            *   Returns: `true` (Updated).
        *   **NO:**
            *   Skips Sync.
            *   Returns: `false` (Stale data).
2.  **Dispatch Phase:**
    *   Host iterates all Slow Satellites.
    *   If `Updated == true`: Dispatch ALL satellites.
    *   If `Updated == false`: Do nothing (everyone waits).
3.  **Satellite Execution:**
    *   Satellite A runs (fast). Calls `Dispose()` on snapshot view. `_activeReaders--`.
    *   Satellite B runs (slow). Still holding `_activeReaders > 0`.
    *   *Next Frame:* Host sees `_activeReaders > 0`. Cannot Sync.
4.  **Completion:**
    *   Satellite B finishes. Calls `Dispose()`. `_activeReaders` hits 0.
    *   *Next Frame:* Host sees 0. Performs Sync.

---

### 3. Implementation: The `SharedSnapshotProvider`

This fits into the `ISnapshotProvider` interface defined previously.

```csharp
public class SharedSnapshotProvider : ISnapshotProvider
{
    private readonly EntityRepository _live;
    private readonly EntityRepository _replica;
    private readonly EventAccumulator _eventAcc = new();
    
    // Thread safety for tracking readers
    private int _activeReaders = 0;
    
    // Track when we last successfully synced
    private uint _lastSyncedTick = 0;

    public SharedSnapshotProvider(EntityRepository live)
    {
        _live = live;
        _replica = new EntityRepository(); // Allocates once
    }

    /// <summary>
    /// Attempts to update the replica. 
    /// Call this ONCE per frame before dispatching satellites.
    /// </summary>
    public bool TryUpdateReplica()
    {
        // 1. Check Locks
        if (_activeReaders > 0) return false; // Convoy is stuck

        // 2. Perform Sync
        // Note: Mask is null (Full Sync) or a Union Mask of all registered slow modules
        _replica.SyncFrom(_live); 

        // 3. Accumulate Events
        // Captures all events from Live since _lastSyncedTick
        _eventAcc.Capture(_live.Bus); 
        _eventAcc.FlushTo(_replica.Bus); // Appends to Replica Bus

        _lastSyncedTick = _live.GlobalVersion;
        return true;
    }

    // Called by each Satellite when it starts
    public ISimulationView AcquireView()
    {
        Interlocked.Increment(ref _activeReaders);
        return _replica; // Return the shared instance
    }

    // Called by each Satellite when it finishes
    public void ReleaseView(ISimulationView view)
    {
        Interlocked.Decrement(ref _activeReaders);
    }
}
```

### 4. Integration in Module Host

```csharp
// Host Logic
void Update()
{
    // ... Sim ...

    // PHASE 4: SYNC
    
    // 1. Try to move the "Slow World" forward
    bool slowWorldUpdated = _slowProvider.TryUpdateReplica();

    if (slowWorldUpdated)
    {
        // 2. Dispatch the Convoy
        foreach(var module in _slowModules)
        {
            var view = _slowProvider.AcquireView();
            Task.Run(() => {
                try { module.Run(view); }
                finally { _slowProvider.ReleaseView(view); }
            });
        }
    }
    
    // Note: Fast modules (Network) use a separate Provider 
    // that syncs every frame because it has no readers holding locks.
}
```

### 5. Does `EventAccumulator` still work?

**Yes.** This is the beauty of the unified design.

*   **In SoD:** `EventAccumulator` captures frames 1, 2, 3. The Snapshot is ephemeral, so we pass pointers to 1, 2, 3 directly to the reader.
*   **In GDB:** `EventAccumulator` captures frames 1, 2, 3. The Snapshot is persistent. We **Inject** 1, 2, 3 into the Replica's `FdpEventBus`.
    *   The `FdpEventBus.InjectIntoCurrent` (which we added for the Recorder) accepts raw pointers.
    *   It **copies** the data from the accumulated buffers into the Replica's buffer (or appends the pages).
    *   **Result:** The Replica's bus now contains a continuous stream of events covering the entire time gap.

### Summary

1.  **Optimization Kept:** You still use Dirty Chunk tracking (`SyncFrom`). You still use Zero-Alloc event capturing (`EventAccumulator`).
2.  **New Capability:** You can now "Park" multiple modules on a single frozen memory block.
3.  **Safety:** The `RefCount` mechanism ensures the Main Thread never overwrites the Slow World while the AI is analyzing it.

This is the most robust way to implement the "Slow Lane."
Model



This configuration is the "Gold Standard" for high-performance simulation.

Based on your specific data overlap (Recorder needs 100%, Network needs 50%, AI needs Mix), the optimal mapping is a **3-World Topology** using **Global Double Buffering (GDB)** for both off-thread lanes.

Here is the exact mapping of roles to architectures.

---

### The Topology Diagram

1.  **World A (Live)**: The Source of Truth.
2.  **World B (Fast Replica)**: 100% GDB. Syncs every frame.
    *   *Consumers:* Flight Recorder, Network.
3.  **World C (Slow Replica)**: Filtered GDB. Syncs when idle.
    *   *Consumers:* AI, Analytics, UI.

---

### 1. Mapping World B: The "Fast Lane"
**Strategy:** Full Global Double Buffer (100% Data).
**Sync Rate:** Every Frame (60Hz).

#### Why this fits:
*   **Flight Recorder:** Needs **100%** of data. Therefore, World B *must* contain everything. You cannot optimize memory here by masking columns, or the recorder will miss data.
*   **Network:** Needs **50%** of data. It runs at high frequency (20Hz-60Hz).
*   **Synergy:** Since World B already contains the 100% needed by the Recorder, the Network module simply "piggybacks" on it. The Network System reads World B but **ignores** the internal physics columns it doesn't care about.

#### The Workflow (Fast Lane):
1.  **Sync Phase:** Host calls `WorldB.SyncFrom(WorldA, mask: null)`. (Copies 100% of Dirty Chunks).
2.  **Dispatch:**
    *   **Task 1 (Recorder):** Reads World B. Compresses 100% of data to disk.
    *   **Task 2 (Network):** Reads World B. Filters for "NetworkComponents". Serializes packets.
3.  **Parallelism:** Task 1 and Task 2 run in parallel on worker threads. Since World B is Read-Only, this is safe.

---

### 2. Mapping World C: The "Slow Lane"
**Strategy:** Filtered GDB (Shared Provider / Convoy Pattern).
**Sync Rate:** Variable (determined by slowest consumer).

#### Why this fits:
*   **AI:** Is slow. It cannot run at 60Hz. It needs a stable world view for ~50ms.
*   **Data Mix:** It likely needs `Position`, `Team`, `Health`, `Ammo` (Logic), but *not* `TextureIndex` or `SoundEmitter` (Presentation), nor `InterpolationHistory` (Network).
*   **Efficiency:** By using a **BitMask**, we prevent copying useless "VFX/Presentation" data to World C. This saves memory bandwidth during the sync.

#### The Workflow (Slow Lane):
1.  **Check:** Host checks `_slowLaneLock`. If > 0, skip update.
2.  **Sync Phase:** Host calls `WorldC.SyncFrom(WorldA, mask: _aiMask)`.
    *   *Optimization:* Only dirty chunks containing AI-relevant components are copied.
3.  **Events:** Host flushes `EventAccumulator` (History of last X frames) into World C.
4.  **Dispatch:**
    *   **Task 3 (AI):** Reads World C. Calculates pathfinding.
    *   **Task 4 (UI/Map):** Reads World C. Updates mini-map icons.

---

### 3. Comparison of configurations

| Feature | **World B (Fast)** | **World C (Slow)** |
| :--- | :--- | :--- |
| **Primary User** | Flight Recorder | AI / Logic |
| **Secondary User** | Network | UI / Analytics |
| **Data Scope** | **100%** (All Tables) | **~60%** (Logic Mask) |
| **Sync Strategy** | **Immediate** (Every Frame) | **Convoy** (When Idle) |
| **Event Strategy** | **Direct Swap** (1:1 with Live) | **Accumulator** (N:1 History) |
| **Memory Cost** | High (Full duplicate) | Medium (Sparse duplicate) |

---

### 4. Implementation Details: The "Smart" Sync

You need to configure the masks in your Host.

```csharp
// In ModuleHost Startup
void Setup()
{
    // 1. Define Masks
    var allBits = BitMask256.All;
    var aiBits = new BitMask256();
    aiBits.SetBits(typeof(Position), typeof(Team), typeof(Health), typeof(Orders)); // etc

    // 2. Create Providers
    // Fast Lane: 100% Data, No Lock logic (always overwrites)
    _fastProvider = new DoubleBufferProvider(_live, mask: allBits);
    
    // Slow Lane: Logic Data, Convoy Lock logic
    _slowProvider = new SharedSnapshotProvider(_live, mask: aiBits);
}

void Tick()
{
    // ... Simulation ...

    // 1. Fast Sync (Always runs)
    var fastView = _fastProvider.UpdateAndAcquire(); 
    // Dispatch Recorder & Network using 'fastView'
    // They don't lock the provider, they just use the data before next Tick overwrites it.

    // 2. Slow Sync (Conditional)
    if (_slowProvider.TryUpdateReplica())
    {
        var slowView = _slowProvider.AcquireLockedView();
        // Dispatch AI. AI *must* call slowView.Dispose() (or Release) when done.
    }
}
```

### 5. Why not SoD?

You *could* use SoD for the AI lane. However, since you mentioned "Multiple slow modules" and "Common Data," **GDB** is better here because:
1.  **Shared Memory:** If you have 3 AI modules, SoD might create 3 separate snapshot buffers (wasting RAM). GDB puts them all on one World C.
2.  **Stability:** With GDB, you allocate the memory *once* at startup. There is zero risk of `Out of Memory` crashes during a heavy battle because the buffers are pre-reserved.

### Summary

*   **Recorder + Network** $\to$ **World B (Full GDB)**.
    *   *Why:* Recorder forces 100% data requirement. Network piggybacks for free.
*   **AI + Slow Logic** $\to$ **World C (Filtered GDB)**.
    *   *Why:* Decouples tick rate. Filters out rendering/physics-internal garbage to save bandwidth.

This setup maximizes throughput while respecting the distinct frequency requirements of your modules.
Model



Model



Model



This makes **perfect sense** and represents a highly mature optimization of the architecture.

You are effectively proposing a **Hybrid State Strategy**:
1.  **High-Frequency / Dense Data:** Use **Global Double Buffering (GDB)**.
2.  **Low-Frequency / Sparse Data:** Use **Snapshot-on-Demand (SoD)**.

This aligns perfectly with the physics of memory bandwidth. Copying everything is efficient if you do it often (hot cache). Copying specific slices is efficient if you do it rarely (low bandwidth).

Here is how to structure this **"Best of Both Worlds"** architecture.

---

### 1. The Architecture Diagram

*   **World A (Live):** The Source of Truth.
*   **World B (Fast Replica):** A persistent `EntityRepository`.
    *   **Sync Rate:** Every Frame (60Hz).
    *   **Scope:** 100% of Data (or `Network | Recorder` mask).
    *   **Users:** Flight Recorder, Network.
*   **AI Snapshot (Transient):** A pooled `SimSnapshot` object.
    *   **Sync Rate:** On Request (e.g., 5Hz, 1Hz).
    *   **Scope:** Specific Mask (e.g., `Position | Team | Health`).
    *   **Users:** AI, Analytics.

---

### 2. Why World B fits Network + Recorder perfectly

Since you confirmed both run at 60Hz (or near it):
1.  **Shared Overhead:** The cost of `memcpy` from World A to World B is paid once. Both modules read from B without blocking A.
2.  **Stability:** Memory is allocated once. No GC spikes.
3.  **Simplicity:** No need for "Union Masks" or complex dependency graphs for the fast lane. It's just a mirror.

**Implementation:**
```csharp
// Phase 4: Sync
_fastReplica.SyncFrom(_liveWorld); // 100% Copy (Dirty chunks only)

// Phase 5: Dispatch
Task.Run(() => _recorder.Process(_fastReplica));
Task.Run(() => _network.Process(_fastReplica));
```

---

### 3. The Custom AI API (SoD)

You asked: *"Can the AI module call an API method when its tick starts?"*

**Yes, but with a restriction:** The "Call" must happen on the **Main Thread** during the Sync Phase. The AI *Logic* (Background) cannot demand a snapshot asynchronously because the Live World might be mutating.

Here is the pattern for the **"Pull-Based" AI Scheduler**:

#### The API
```csharp
public interface ISnapshotFactory
{
    // Called by the Module's Orchestrator (on Main Thread)
    // Returns a pooled snapshot object
    ISimulationView CreateSnapshot(BitMask256 componentMask);
}
```

#### The Usage (AI Module)
The AI Module is split into two parts: the **Scheduler** (Main Thread) and the **Logic** (Worker Thread).

```csharp
public class AiModule : IModule
{
    private BitMask256 _requiredData; // Pos, Team, etc.
    private Task _currentTask;

    // Called by Main Loop every frame
    public void Tick(ISnapshotFactory factory)
    {
        // 1. Rate Limiting logic
        if (!IsDue()) return;
        
        // 2. Concurrency Logic
        // If previous brain is still thinking, don't overlap.
        if (_currentTask != null && !_currentTask.IsCompleted) return;

        // 3. THE API CALL (Snapshot-on-Demand)
        // This copies ONLY the columns the AI needs into a pooled buffer.
        using var snapshot = factory.CreateSnapshot(_requiredData);

        // 4. Dispatch
        // We pass the snapshot ownership to the task. 
        // Note: We need a way to keep 'snapshot' alive until task finishes.
        // Usually via Reference Counting or manual Dispose inside the task.
        
        _currentTask = Task.Run(() => 
        {
            try {
                RunBrain(snapshot);
            }
            finally {
                snapshot.Dispose(); // Return buffers to pool
            }
        });
    }
}
```

### 4. Why this is better than "World C" (Slow GDB)

For AI specifically, SoD beats GDB because:

1.  **Sparse Data:** AI usually ignores heavy components like `InterpolationHistory` (Network) or `MeshRenderData` (Graphics). Copying those to a "World C" is waste. SoD only copies what the mask asks for.
2.  **Variable Cadence:** If you have different AI systems (Squad AI @ 10Hz, Commander AI @ 1Hz), SoD handles this naturally.
    *   Frame 6: `CreateSnapshot(SquadMask)`
    *   Frame 60: `CreateSnapshot(SquadMask | CommanderMask)`
    *   *World C* would force you to sync the Commander data every time the Squad data needed an update, or manage complex dirty states.

### 5. Memory Layout Implications

*   **World B:** Is a standard `EntityRepository`. It has `NativeChunkTable` instances.
*   **SoD Snapshot:** Can reuse the **Same Internal Structure**.
    *   It effectively creates a "Mini Repository" that only has `NativeChunkTable`s for the requested types.
    *   It populates those tables using the same `CopyChunkToBuffer` logic.

This means your **Replay System** (which uses `EntityRepository`) and your **AI** (which uses `ISnapshot`) can share the exact same `ISimulationView` interface (read-only access).

### 6. Summary

**Final Architecture Recommendation:**

1.  **Kernel:**
    *   `EntityRepository` supports `SyncFrom(source)` (for GDB).
    *   `EntityRepository` supports `SyncFrom(source, mask)` (for SoD).
    *   `ISimulationView` interface for read-only access.

2.  **Host:**
    *   Maintains **Live World**.
    *   Maintains **World B (Replica)**. Syncs 100% every frame.
    *   Maintains **Snapshot Pool** (Pool of empty Repositories).

3.  **Modules:**
    *   **Recorder/Network:** Read World B.
    *   **AI:** Request pooled Snapshot from Host when due.

This allows you to optimize the "Fast Lane" for throughput (Bulk Copy) and the "Slow Lane" for bandwidth (Selective Copy), satisfying all your requirements.
Model



The memory layout difference fundamentally boils down to **"Persistence vs. Transience"** and **"Full Topology vs. Sparse Topology."**

Even though both implement the same `ISimulationView` interface, under the hood they organize RAM differently to optimize for their specific lifecycles.

### 1. The Visual Layout Comparison

#### **GDB (World B - Fast Replica)**
**Layout Strategy:** **"The Shadow Clone"**
It has the exact same "shape" as your Live World. If the Live World has 20 component tables, World B has 20 component tables allocated.

```text
[ World B (Persistent Heap Object) ]
   |
   +-- [Metadata Table] (Allocated, matches Live)
   |
   +-- [Table: Position] (Tier 1)
   |      +-- Chunk 0: [ 64KB Page (VirtualAlloc) ]
   |      +-- Chunk 1: [ 64KB Page (VirtualAlloc) ]
   |
   +-- [Table: Health]   (Tier 1)
   |      +-- Chunk 0: [ 64KB Page ]
   |
   +-- [Table: SquadAI]  (Tier 2)
          +-- Chunk 0: [ object[1024] ] -> References to Heap Objects
```
*   **Key Feature:** The memory addresses (`IntPtr`) for Chunk 0 are **stable**. They typically do not change between frames; the data inside them is just overwritten via `memcpy`.
*   **Holes:** If Entity 5 is dead, there is a "hole" (garbage data) at Index 5 in all tables, just like the Live World.

---

#### **SoD (AI Snapshot)**
**Layout Strategy:** **"The Bag of Parts"**
It is a lightweight shell holding *only* the specific arrays requested by the AI.

```text
[ Snapshot Object (Pooled) ]
   |
   +-- [Metadata Table] (Copied)
   |
   +-- [Table: Position]
   |      +-- Chunk 0: [ byte[] from ArrayPool ] OR [ NativePtr from Pool ]
   |      +-- Chunk 1: [ byte[] from ArrayPool ]
   |
   +-- [Table: Health]
   |      +-- Chunk 0: [ byte[] from ArrayPool ]
   |
   +-- [Table: SquadAI]  -- NOT REQUESTED (Reference is null)
   |
   +-- [Table: Velocity] -- NOT REQUESTED (Reference is null)
```
*   **Key Feature:** The memory buffers are **ephemeral**. Frame 1's snapshot might use Buffer A. Frame 6's snapshot might use Buffer Z.
*   **Compactness:** It physically lacks the tables for data the AI didn't ask for.

---

### 2. Tier 1 (Unmanaged) Differences

| Feature | GDB (World B) | SoD (AI Snapshot) |
| :--- | :--- | :--- |
| **Backing Store** | **`NativeChunkTable`** | **`byte[]`** (Recommended) or `NativeMemoryBlock` |
| **Allocation** | `VirtualAlloc` (OS Paging) | `ArrayPool<byte>` (Managed Heap) |
| **Pointers** | **Stable** (Address reuse) | **Volatile** (Address changes every snap) |
| **Copy Method** | `Unsafe.CopyBlock` (Ptr to Ptr) | `fixed` + `Unsafe.CopyBlock` (Ptr to Managed) |
| **GC Pressure** | **Zero** | **Zero** (if Pooled correctly) |

**Why SoD should use `byte[]`:**
For temporary snapshots, using C# `byte[]` arrays (pinned when needed) is often safer and easier to debug than managing `VirtualAlloc` pointers that only exist for 50ms. Since the AI accesses data via `MemoryMarshal.Cast<byte, Position>(span)`, it doesn't care if the underlying memory is Managed or Unmanaged.

### 3. Tier 2 (Managed) Differences

| Feature | GDB (World B) | SoD (AI Snapshot) |
| :--- | :--- | :--- |
| **Backing Store** | `T[]` (Persistent Arrays) | `T[]` (Rented Arrays) |
| **Persistence** | Arrays live forever. | Arrays returned to pool immediately. |
| **Cloning** | `Array.Copy` (Overwrite slots) | `Array.Copy` (Populate fresh slots) |
| **Safety** | **Dangerous:** If AI holds ref too long, GDB overwrites it. | **Safe:** Snapshot owns the array until `Dispose()`. |

### 4. Critical Memory Behavior: "The Overwrite vs. The Swap"

This is the most important distinction for your "Slow Module" logic.

#### GDB Behavior: "The Overwrite"
When you sync World B:
1.  You **Write Over** the existing memory at `0x1000`.
2.  If the Network thread is reading `0x1000`, you must ensure strict thread synchronization (Phase Barrier).
3.  **Memory Address:** `0x1000`.

#### SoD Behavior: "The Swap"
When you create a Snapshot:
1.  You grab a **Fresh Buffer** at `0x5000`.
2.  You copy data to it.
3.  You hand `0x5000` to the AI.
4.  Next frame, the Main Thread continues using `0x1000`.
5.  **Memory Address:** Distinct (`0x1000` vs `0x5000`).

**Consequence:**
*   **GDB** forces the Network to be fast (finish before next Sync).
*   **SoD** allows the AI to be slow (hold onto `0x5000` for 10 frames without blocking the Main Thread).

### Summary Implementation Guide

1.  **World B (GDB):** Use the exact same class (`EntityRepository`) as World A. It allocates `NativeChunkTable`s internally. You just call `SyncFrom`.
2.  **AI Snapshot (SoD):** Do **not** create a full `EntityRepository`. Create a lightweight struct `SimSnapshot`.

```csharp
public class SimSnapshot : ISimulationView, IDisposable
{
    // Arrays of buffers indexed by [TypeId][ChunkIndex]
    private byte[][][] _tier1Buffers; 
    private object[][][] _tier2Buffers;
    
    // ... Implement GetComponentRO using these buffers ...
}
```

This keeps your AI snapshots lightweight and prevents the overhead of the full Kernel logic (BitMasks, Archetypes, Allocators) from weighing down the snapshot process.






# FDP-DES-012: Hybrid State Architecture - summarized final solution
**Project:** FDP Kernel (Fast Data Plane)  
**Feature:** Concurrency & Snapshotting  
**Status:** Draft  
**Author:** FDP Architecture Team  

---

## 1. Introduction & Problem Statement

### 1.1 Context
The FDP engine is designed for high-frequency military simulation, targeting a strict **60Hz (16.6ms)** simulation loop. However, modern simulation requirements include heavy "Background Modules" (Strategic AI, Pathfinding, Network Serialization, Analytics) whose computational cost far exceeds the frame budget.

### 1.2 The Latency Mismatch
There is a fundamental conflict between the **Budget** and the **Cost**:
*   **Physics/Hot Path:** Must execute every 16ms.
*   **Heavy Logic (AI):** May require 50ms–200ms to compute.

A traditional **Fork/Join Job System** fails here. If the Main Thread waits for a 50ms AI job to finish, the frame rate drops to 20FPS. If we run the AI asynchronously without protection, it reads memory while Physics writes to it, causing **State Tearing** (inconsistent data, race conditions, crashes).

### 1.3 Goals
1.  **Decoupling:** Completely isolate the simulation frame rate from the logic processing load.
2.  **Consistency:** Provide background modules with a stable, atomic view of the world ("The World at Tick 100") even as the simulation advances to Tick 105.
3.  **Throughput:** Support high-bandwidth consumers (Flight Recorder) that need 100% of the data without stalling the Main Thread.

---

## 2. Architectural Overview: The Hybrid State Strategy

### 2.1 The "Twin Universes" Concept
We introduce a strict separation of memory spaces. The **Live World** is mutable and owned by the Main Thread. **Replica Worlds** (Snapshots) are immutable and owned by Background Threads.

### 2.2 The Unified Interface (`ISimulationView`)
To allow modules to switch between different buffering strategies without code changes, we abstract data access behind a read-only interface.

```csharp
public interface ISimulationView
{
    // Metadata
    uint Tick { get; }
    float Time { get; }

    // Component Access (Read-Only)
    ref readonly T GetComponentRO<T>(Entity e) where T : unmanaged;
    T GetManagedComponentRO<T>(Entity e) where T : class;
    
    // Existence Check
    bool IsAlive(Entity e);

    // Event Stream (Unified History)
    ReadOnlySpan<T> ConsumeEvents<T>() where T : unmanaged;
    
    // Query Support
    EntityQueryBuilder Query();
}
```
*Note: `EntityRepository` implements this interface, allowing the Main Thread to use the same logic/queries as background threads.*

### 2.3 The 3-World Topology
We utilize three distinct memory contexts to service different classes of modules.

1.  **World A (Live):** The Source of Truth. Mutable. Runs at 60Hz.
2.  **World B (Fast Replica):** A **Global Double Buffer (GDB)**.
    *   **Sync Rate:** Every Frame (60Hz).
    *   **Data Scope:** 100% (Full Copy).
    *   **Consumers:** Flight Recorder, Network.
3.  **World C (Slow Lane):** Configurable **Snapshot-on-Demand (SoD)**.
    *   **Sync Rate:** On Request (e.g., 5Hz, 10Hz).
    *   **Data Scope:** Filtered (Logic components only).
    *   **Consumers:** AI, UI, Analytics.

---

## 3. Core Mechanics & Implementation

### 3.1 Synchronization Logic (`SyncFrom`)
The engine creates Replicas/Snapshots not by deep cloning, but by **Differential Copying**.

*   **Mechanism:** `EntityRepository.SyncFrom(EntityRepository source, BitMask256 mask)`.
*   **Dirty Patching:** We iterate the `NativeChunkTable`. We compare `Chunk.Version` in Source vs. Destination.
    *   If `Version` matches: Skip.
    *   If `Version` differs: Copy.
*   **Tier 1 (Unmanaged):** Performs `Unsafe.CopyBlock` (memcpy) of the 64KB chunk. Fast.
*   **Tier 2 (Managed):** Performs `Array.Copy` of the reference array. Fast.
    *   *Critical:* Relies on Tier 2 objects being **Immutable Records**. We copy pointers to the objects, not the objects themselves.

### 3.2 Event Accumulation
Background modules running at 10Hz must not miss transient events (Explosions) that occurred during the 5 skipped frames.

*   **The Accumulator:** A buffer that harvests events from the Live Bus every frame.
*   **Injection:** When a Snapshot/Replica is synced, the Accumulator flushes its history into the Snapshot's `FdpEventBus`.
*   **Result:** The AI sees a continuous stream of events `[Frame 100...105]` when it wakes up at Frame 106.

### 3.3 The Write Path (Command Buffers)
Background modules are **Read-Only**. They cannot call `SetComponent`. To mutate state, they generate **Commands**.

*   **Pattern:** `ICommand` struct containing `TargetEntity`, `ActionData`, and **Validation Data**.
*   **Optimistic Concurrency:** Since the AI acts on old data, the World might have changed (e.g., Target died).
    *   Commands must include `ExpectedGeneration` or `ExpectedPosition`.
    *   The Main Thread validates these preconditions before applying the command.

---

## 4. Detailed Strategy Analysis

### 4.1 Strategy A: Global Double Buffering (GDB)
**Concept:** "The Shadow Clone." A persistent `EntityRepository` instance kept in sync with the Live World.

*   **Primary Use:** **World B (Fast Lane)**.
*   **Pros:** 
    *   Zero allocation during Sync (reuses existing pages).
    *   Highest possible throughput for "Full World" consumers (Recorder).
*   **Cons:** 
    *   High Memory Usage (2x RAM).
*   **The "Convoy" Pattern:** If multiple slow modules share a GDB (e.g., World C), the GDB is locked (cannot sync) until *all* readers have finished. The slowest module dictates the update rate.

### 4.2 Strategy B: Snapshot-on-Demand (SoD)
**Concept:** "The Bag of Parts." A transient, pooled object containing only the specific component arrays requested by the consumer.

*   **Primary Use:** **World C (Slow Lane / AI)** optimization.
*   **Pros:**
    *   Bandwidth Efficiency: Copies only `Position` and `Team` (e.g., 5% of memory), ignoring `RenderMesh` or `PhysicsInternal`.
    *   Decoupled Timing: Different AI agents can request snapshots at different phases without blocking each other.
*   **Cons:**
    *   Complexity: Requires `SnapshotPool` and careful lifecycle management (`Dispose()`).
*   **Memory Layout:** Unlike GDB (stable pointers), SoD buffers change addresses every snapshot.

---

## 5. Developer Usage Guide

### 5.1 Writing a Background Module
Developers write logic against `ISimulationView`. They are agnostic to the storage strategy.

```csharp
public class StrategyAiModule : IBackgroundModule
{
    public void Run(ISimulationView view, ICommandBuffer writer)
    {
        // 1. Read State (Safe, Consistent)
        var query = view.Query().With<Position>().With<Team>().Build();
        
        query.ForEach(entity => {
            // 2. Make Decisions
            // 3. Write Command
            writer.Enqueue(new MoveCommand(entity, newDest));
        });
    }
}
```

### 5.2 Configuring the Host
The `ModuleHost` determines which strategy a module uses via configuration.

```csharp
// AppConfig.cs
void Configure(ModuleHost host)
{
    // Network needs low latency -> Bind to Fast Replica (GDB)
    host.BindModule<NetworkModule>(Strategy.GlobalDoubleBuffer_Fast);

    // AI needs specific data -> Bind to On-Demand (SoD)
    var aiMask = new BitMask256(typeof(Position), typeof(Team));
    host.BindModule<AiModule>(new SnapshotOnDemandStrategy(aiMask));
}
```

---

## 6. Performance & Requirements

### 6.1 Memory Considerations
*   **RAM Cost:** GDB requires allocating specific "Shadow Worlds." For 100k entities, this is manageable (~200-500MB per world).
*   **Bandwidth:** Tier 1 Logic data is often interspersed with Tier 1 Physics data. SoD minimizes CPU cache pollution by skipping the copy of Physics data during the AI sync.

### 6.2 Tier 2 Safety (Crucial)
Because Tier 2 uses **Reference Copying**, developers **MUST** use C# Records and Immutable Collections.
*   **Violation:** Modifying a `List<T>` in the Live World will corrupt the Snapshot reading that same list instance.
*   **Enforcement:** Use the `FdpTypeValidator` (Reflection) at startup to crash if mutable collections are detected in Tier 2 components.

---

## 7. Engine Integration & Roadmap

### 7.1 Kernel Changes
1.  **Refactor `NativeChunkTable`:** Expose `GetChunkDataPtr` (internal) and `CopyColumnTo`.
2.  **Implement `SyncFrom`:** The core dirty-chunk copy logic.
3.  **Implement `EventAccumulator`:** The history buffer bridge.

### 7.2 Host Changes (The Loop)
```csharp
void EngineLoop()
{
    // 1. Live Simulation (Physics/Logic)
    _liveWorld.Tick();

    // 2. Fast Sync (Every Frame)
    _fastReplica.SyncFrom(_liveWorld);
    DispatchFastModules(_fastReplica); // Recorder, Net

    // 3. Slow Sync (Conditional)
    if (ShouldSyncAI()) {
        // Option A: Sync World C (GDB)
        _slowReplica.SyncFrom(_liveWorld, _aiMask);
        DispatchSlowModules(_slowReplica);
        
        // Option B: Create SoD
        var snap = _snapshotPool.Create(_liveWorld, _aiMask);
        DispatchSlowModules(snap);
    }

    // 4. Merge Commands
    _commandBuffer.Execute(_liveWorld);
}
```

### 7.3 Migration Path
1.  Implement **GDB (World B)** first. This immediately enables the Async Flight Recorder and Thread-Safe Networking.
2.  Refactor AI to use `ISimulationView` reading from World B (temporarily).
3.  Implement **SoD (World C)** optimization later if memory bandwidth becomes a bottleneck.

