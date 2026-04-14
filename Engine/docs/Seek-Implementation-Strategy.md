# Seek Implementation Strategy: Leveraging Efficient Event Skipping

## Overview
Recent updates to the Flight Recorder file format (Version 2) and the `PlaybackSystem` have laid the critical infrastructure required to implement a high-performance **"Seek"** capability.

This document explains why "Managed Event Skipping" was the prerequisite for efficient seeking and outlines the algorithm for implementing `PlaybackController.Seek()`.

## The Challenge: Cost of Deserialization
In the Fast Data Plane (FDP) architecture, "Seeking" to a specific point in time (e.g., Tick 5000) involves:
1.  Locating the nearest previous **Keyframe** (e.g., Tick 4800).
2.  Loading that Keyframe to restore the base state.
3.  Sequentially applying every **Delta Frame** from 4801 to 5000 to evolve the state.

### The Bottleneck
Previously, applying a Delta Frame required reading **all** data within it.
*   **Entity Components**: Fast (raw memory copy).
*   **Unmanaged Events**: Fast (raw copy).
*   **Managed Events**: **Extremely Slow** (Requires `BinaryReader`, `Reflection`, `Activator.CreateInstance`, and string parsing).

If a user wanted to jump from Tick 0 to Tick 5000, the system had to fully deserialize 5000 frames' worth of Managed Events (e.g., chat logs, complex AI states, debug strings), even though that data would be immediately discarded because the user only cares about the *final* state at Tick 5000.

This made "Seeking" essentially as slow as "Playing Fast Forward".

## The Solution: Managed Event Skipping (Format v2)
We introduced a **Block Size Tracking** mechanism in Format Version 2.

### File Format Change
Every block of Managed Events in the recording is now prefixed with its total byte size:
```
[BlockSize: int32] [Type Name] [Count] [Serialized Data...]
```

### Zero-Cost Skipping
When the `PlaybackSystem` applies a frame with `processEvents = false`:
1.  It reads the `BlockSize`.
2.  It executes `stream.Seek(BlockSize, SeekOrigin.Current)`.
3.  **Result**: The file pointer effectively "teleports" over the complex managed data. No reflection, no allocation, no GC pressure.

## Implementing `PlaybackController.Seek(targetTick)`

With this foundation, the `Seek` feature can now be implemented efficiently.

### Algorithm
```csharp
public void Seek(ulong targetTick)
{
    // 1. Pause Playback
    _isPlaying = false;

    // 2. Find Nearest Keyframe
    // (Assuming we have an index of keyframes)
    long keyframeOffset = _keyframeIndex.FindClosestBefore(targetTick);
    _stream.Position = keyframeOffset;

    // 3. Fast-Forward Loop
    while (_currentTick < targetTick)
    {
        // READ NEXT FRAME
        // Crucial: Pass 'processEvents = false'
        _playbackSystem.ApplyFrame(
            _repo, 
            _reader, 
            eventBus: null,       // No event bus needed for intermediate frames
            processEvents: false  // ENABLE SKIPPING
        );
        
        // At this point:
        // - Entity State is updated (Fast Memcpy)
        // - Events are skipped (Instant Seek)
    }

    // 4. Final Frame (Target Tick)
    // Optionally apply the final frame with events if we want to show them immediately
    _playbackSystem.ApplyFrame(_repo, _reader, _eventBus, processEvents: true);
}
```

## Performance Implications
*   **Before**: Seeking time was dominated by `FdpAutoSerializer.Deserialize()`, creating massive GC pressure and CPU load.
*   **After**: Seeking time is dominated only by File I/O bandwidth and raw memory copies.

This enables "Scrubbing" (dragging the timeline slider) to feel responsive even in recordings containing megabytes of text or complex event data.





-----------------------------------

# Critical review of the idea above

This is a **sound and highly effective strategy**. The implementor has correctly identified the primary bottleneck (deserialization of ignored data) and utilized the new infrastructure to resolve it.

Here is the detailed evaluation of the strategy, including a few critical "watch-outs" to ensure the implementation is robust.

### 1. Verification of the Solution
The core premise is mathematically sound regarding performance:

*   **Before (Naive Seek):** Cost = $O(N) \times (IO + Memcpy + \text{Reflection})$.
    *   Since Reflection/Deserialization is orders of magnitude slower than Memcpy, seeking was CPU-bound.
*   **After (Optimized Seek):** Cost = $O(N) \times (IO + Memcpy)$.
    *   By treating managed event blocks as "opaque blobs" of bytes, the CPU cost drops to near zero for those segments.
    *   The "Seek" operation becomes bounded primarily by disk read speed, which is exactly where you want a playback system to be.

### 2. Critical Implementation Details (The "Gotchas")

While the high-level logic is correct, there are three specific technical nuances the implementor must address to prevent bugs.

#### A. The "Double Read" Problem in Step 4
The algorithm provided in the snippet has a logic flaw at the end of the loop:

```csharp
// 3. Fast-Forward Loop
while (_currentTick < targetTick)
{
    // Reads Frame N. Stream advances to N+1.
    _playbackSystem.ApplyFrame(..., processEvents: false); 
}

// 4. Final Frame (Target Tick)
// ERROR: Stream is now pointing at targetTick + 1
_playbackSystem.ApplyFrame(..., processEvents: true); 
```

**The Fix:** You cannot read the "Target Frame" twice (once to update state, and again to show events) unless you seek back.
**Better Logic:** Handle the condition *inside* the loop.

```csharp
while (_currentTick <= targetTick) // Note inclusive
{
    bool isFinalFrame = (_currentTick == targetTick);
    
    _playbackSystem.ApplyFrame(
        _repo, 
        _reader, 
        eventBus: isFinalFrame ? _eventBus : null, 
        processEvents: isFinalFrame // Only pay deserialization cost for the frame user sees
    );
}
```

#### B. Managed *Components* vs. Managed *Events*
The document states: *"Entity State is updated (Fast Memcpy)"*.
This is true for **Tier 1 (Unmanaged)** components.
However, if your simulation uses **Tier 2 (Managed)** components (e.g., `ManagedComponentTable<List<Waypoint>>`), those **cannot be skipped**.

*   **Reason:** Delta frames depend on the previous state. If Frame 4999 updates a `List<T>` on an entity, you *must* deserialize that list to update the Repository state, even if you are fast-forwarding.
*   **Performance Impact:** If your simulation relies heavily on mutating managed components every frame, seeking will still be slower than pure unmanaged seeking.
*   **Recommendation:** Ensure the team understands that "Zero-Cost Skipping" applies to **Events** (Transient) but not **Components** (Persistent).

#### C. Synchronization of `TimeSystem`
When you "teleport" the repository state from Tick 0 to Tick 5000, you must also manually update the `TimeSystem` singleton.

If you don't:
1.  Repo is at Tick 5000.
2.  `TimeSystem` (internal state) thinks it is at Tick 0.
3.  Next `Update()` call calculates `DeltaTime` based on `Now - TimeAtTick0`, potentially resulting in a massive delta (e.g., 500 seconds) if your system relies on wall-clock differential.

**Requirement:** The `Seek` method must call `_timeSystem.SnapTo(totalTime, targetTick)` after the loop finishes.

### 3. Refined Algorithm Suggestion

Here is the corrected logic incorporating the fixes:

```csharp
public void Seek(ulong targetTick)
{
    _isPlaying = false;

    // 1. Smart Jump (Keyframe)
    // Find keyframe <= targetTick
    var keyframe = _keyframeIndex.FindClosestBefore(targetTick);
    
    // Optimization: If current position is closer to target than the keyframe, 
    // and we are before target, don't rewind! Just roll forward from here.
    if (_currentTick > targetTick || _currentTick < keyframe.Tick)
    {
        _stream.Position = keyframe.FileOffset;
        // Apply Keyframe (Always process events? No, usually false for keyframe unless target==keyframe)
        _playbackSystem.ApplyFrame(_repo, _reader, null, processEvents: targetTick == keyframe.Tick);
    }

    // 2. Roll Forward
    // We assume ApplyFrame reads the tick from the stream and updates _repo.GlobalVersion
    while (_repo.GlobalVersion < targetTick)
    {
        // Peek next tick to know if we are at the target
        // (Or rely on loop condition if ApplyFrame handles reading)
        
        // Logic: We process the frame. If it's the target frame, we turn on events.
        // NOTE: This assumes we read until we HIT the target tick.
        
        // Calculate if next frame is the target
        // This requires peeking or strict loop control. 
        // Simpler approach:
        
        bool isTarget = (_repo.GlobalVersion + 1 == targetTick); 
        
        _playbackSystem.ApplyFrame(
            _repo, 
            _reader, 
            eventBus: isTarget ? _eventBus : null, 
            processEvents: isTarget 
        );
    }
    
    // 3. Sync Time System (Crucial for interpolation/rendering)
    // Assuming we have access to the frame's timestamp or calculate it via tick rate
    double time = targetTick * _fixedTimeStep; 
    _timeSystem.SnapTo(time, targetTick);
}
```

### 4. Final Verdict
The strategy is **APPROVED**.

It correctly leverages the FDP architecture to solve the deserialization bottleneck. As long as the "Double Read" bug is fixed and the distinction regarding Managed Components is understood, this will provide the high-performance seeking capability required for professional analysis tools.


------------------------
does that mean that the "old" seek we have today has no trouble with the time system?
---------------------------
**Yes, the "old" seek has the exact same trouble.**

The optimization we discussed (skipping managed events) changes **how fast** you get to the target frame, but it does not change **what happens** to the `TimeSystem` state when you arrive.

Here is the breakdown of why both versions suffer from this, and why you likely didn't notice it (or handled it externally) before.

### The "Split Brain" of Time
In your architecture, Time exists in two places:

1.  **The Data (The Snapshot):**
    *   **Location:** `EntityRepository.GetSingleton<GlobalTime>()`
    *   **Status:** This is **Perfect** in both Old and New seek.
    *   **Why:** The `PlaybackSystem.ApplyFrame` method reads the Singletons chunk from the disk. This overwrites the `GlobalTime` component in the repository with the exact value from the recording (e.g., `TotalTime = 50.0`, `FrameCount = 3000`).
    *   **Result:** If you render a frame immediately after seeking, the UI will show the correct time.

2.  **The Driver (The Logic):**
    *   **Location:** The `TimeSystem` C# class (specifically private fields like `_accumulatedTotalTime` and `_lastTimestamp`).
    *   **Status:** This is **Broken** in both Old and New seek (unless you fix it).
    *   **Why:** The `TimeSystem` class is a *system*. It is not an entity. It is not serialized. When `ApplyFrame` restores the repository data, the `TimeSystem` class **does not know about it**. It still thinks `_accumulatedTotalTime` is whatever it was before you clicked "Seek".

### The "Glitch" Moment
The trouble doesn't happen *during* the seek. It happens the moment you click **Play** (or call `TimeSystem.Update`) after seeking.

**Scenario:**
1.  You start at Tick 0. `TimeSystem` thinks `_totalTime = 0`.
2.  You **Seek** to Tick 3000 (Old or New method).
3.  `Repo` now has `GlobalTime` saying `TotalTime = 50.0`.
4.  `TimeSystem` *still* thinks `_totalTime = 0`.
5.  **You Unpause.**
6.  `TimeSystem.Update()` runs. It calculates `dt = 0.016`.
7.  It does `_accumulatedTotalTime += dt` (Result: `0.016`).
8.  It calls `repo.SetSingleton(...)` overwriting the repository.
9.  **GLITCH:** The game world effectively teleports from **Time 50.0** back to **Time 0.016**.

### Why the New Strategy Highlighted It
The new strategy highlighted this requirement because when writing "High-Performance" code, we stop relying on implicit side effects.

In the "Old" (slow) seek, you were running the full deserialization pipeline. It is possible (though unlikely in your code dump) that you had a listener or a hook that saw the `GlobalTime` change and updated the system.

But structurally, **neither implementation updates the `TimeSystem` private fields automatically.**

### The Fix (For Both)
You must explicitly tell the driver that the data has changed underneath it.

```csharp
public void Seek(ulong targetTick)
{
    // ... [Fast or Slow Seek Logic] ...
    
    // 1. Apply the final frame (Updates the Repository Data)
    _playbackSystem.ApplyFrame(_repo, _reader, ...);

    // 2. Read the Truth from the Repository
    var restoredTime = _repo.GetSingletonUnmanaged<GlobalTime>();

    // 3. Update the Driver
    // This fixes the internal counters of the TimeSystem class
    _timeSystem.SnapTo(restoredTime.TotalTime, restoredTime.FrameCount);
}
```

