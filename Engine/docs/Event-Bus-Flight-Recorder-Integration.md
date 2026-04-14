# Event Bus Integration with Flight Recorder

## Overview

The FdpEventBus and Flight Recorder are now fully integrated, allowing events to be recorded and replayed alongside entity state. This document explains the architecture, implementation, and usage.

## Architecture

### Component Relationship

```
┌─────────────────────────────────────┐
│      SimulationKernel / World       │
│                                     │
│  ┌──────────────┐  ┌─────────────┐ │
│  │   Entity     │  │   Event     │ │
│  │  Repository  │  │    Bus      │ │
│  │              │  │             │ │
│  │ (Persistent  │  │ (Transient  │ │
│  │   State)     │  │  Messages)  │ │
│  └──────────────┘  └─────────────┘ │
│         │                 │          │
│         └─────────┬───────┘          │
│                   │                  │
│         ┌─────────▼─────────┐       │
│         │ Flight Recorder   │       │
│         │  (Records Both)   │       │
│         └───────────────────┘       │
└─────────────────────────────────────┘
```

### Key Principles

1. **Siblings, Not Hierarchy**: EntityRepository and FdpEventBus are separate systems
2. **Persistent vs Transient**: Repository = state that persists, Bus = events that are one-frame
3. **Double Buffering**: Events in frame N are consumed in frame N+1
4. **Recording Timing**: Capture BEFORE SwapBuffers (from Pending buffer)
5. **Replay Injection**: Inject DIRECTLY into Current buffer (bypass Publish/Swap)

## Event Bus Buffer System

### Normal Execution Flow

```
Frame N:
  ┌─────────────┐         ┌────────────┐
  │  Systems    │────────>│  Pending   │ (Publish writes here)
  │   Write     │         │  (Write)   │
  └─────────────┘         └────────────┘
                  
  ┌─────────────┐         ┌────────────┐
  │  Systems    │<────────│  Current   │ (Consume reads from here)
  │    Read     │         │  (Read)    │
  └─────────────┘         └────────────┘

End of Frame:
  SwapBuffers() → Pending becomes Current, old Current is cleared

Frame N+1:
  - Systems now read events from previous frame
  - Systems write new events for next frame
```

### Recording Flow (PostSimulation Phase)

```
1. Systems execute → write events to Pending
2. PostSimulation begins
3. **Recorder captures GetPendingBytes()** ← Events from THIS frame
4. SwapBuffers() → Events become visible next frame
5. Next frame begins
```

### Replay Flow

```
1. Load frame data from disk
2. **ClearCurrentBuffers()** ← Prevent mixing old/new
3. **InjectIntoCurrent(typeId, bytes)** ← Make events immediately visible
4. Systems execute → read injected events from Current
5. (NO SwapBuffers during replay)
```

## API Reference

### FdpEventBus - Recording APIs

#### `GetAllPendingStreams()`
```csharp
public IEnumerable<INativeEventStream> GetAllPendingStreams()
```
- Returns all event streams with pending (unswapped) events
- **Used by**: RecorderSystem during PostSimulation
- **Returns**: Only streams with data in write buffer

#### `ClearCurrentBuffers()`
```csharp
public void ClearCurrentBuffers()
```
- Clears all Current (read) buffers
- **Used by**: PlaybackSystem before injecting new frame
- **Purpose**: Prevents mixing old replay events with new ones

#### `InjectIntoCurrent(int typeId, ReadOnlySpan<byte> data)`
```csharp
public void InjectIntoCurrent(int typeId, ReadOnlySpan<byte> data)
```
- Injects raw event bytes directly into Current buffer
- **Used by**: PlaybackSystem when applying a frame
- **Bypasses**: Normal Publish/Swap flow
- **Effect**: Events immediately visible to systems via Consume()

### INativeEventStream - Low-Level APIs

#### `GetPendingBytes()`
```csharp
ReadOnlySpan<byte> GetPendingBytes()
```
- Returns raw bytes from WRITE (Pending) buffer
- No locking needed (read-only, atomic count)
- Used for serialization during recording

####  `InjectIntoCurrent(ReadOnlySpan<byte> data)`
```csharp
void InjectIntoCurrent(ReadOnlySpan<byte> data)
```
- Writes raw bytes directly to READ (Current) buffer
- Auto-resizes if needed
- Uses memcpy for performance

#### `ClearCurrent()`
```csharp
void ClearCurrent()
```
- Clears READ (Current) buffer
- Thread-safe (uses lock)

## Implementation in Flight Recorder

### Recording Events (RecorderSystem.cs)

```csharp
public static void RecordDeltaFrame(
    EntityRepository repo, 
    FdpEventBus eventBus, 
    BinaryWriter writer,
    ulong fromVersion)
{
    // ... write header, entity destructions ...
    
    // ===== RECORD EVENTS =====
    var pendingStreams = eventBus.GetAllPendingStreams().ToList();
    writer.Write(pendingStreams.Count);
    
    foreach (var stream in pendingStreams)
    {
        writer.Write(stream.EventTypeId);
        
        ReadOnlySpan<byte> eventBytes = stream.GetPendingBytes();
        writer.Write(eventBytes.Length);
        writer.Write(eventBytes);
    }
    
    // ... write component chunks ...
}
```

### Replaying Events (PlaybackSystem.cs)

```csharp
public static void ApplyDeltaFrame(
    EntityRepository repo,
    FdpEventBus eventBus,
    BinaryReader reader)
{
    // ... apply entity destructions ...
    
    // ===== RESTORE EVENTS =====
    int streamCount = reader.ReadInt32();
    
    // Clear to prevent mixing
    eventBus.ClearCurrentBuffers();
    
    for (int i = 0; i < streamCount; i++)
    {
        int typeId = reader.ReadInt32();
        int byteCount = reader.ReadInt32();
        
        byte[] eventData = reader.ReadBytes(byteCount);
        
        // Inject directly into Current buffer
        eventBus.InjectIntoCurrent(typeId, eventData);
    }
    
    // ... apply component chunks ...
}
```

## Usage in Systems

### Writing a System that Uses Both

```csharp
public class DamageSystem
{
    public void Update(EntityRepository repo, FdpEventBus eventBus)
    {
        // CONSUME events from previous frame
        var hits = eventBus.Consume<BulletHitEvent>();
        
        foreach (ref readonly var hit in hits)
        {
            // QUERY repository for validation
            if (repo.HasManagedComponent<Health>(hit.Target))
            {
                // MODIFY entity state
                var health = repo.GetManagedComponentRW<Health>(hit.Target);
                health.Value -= hit.Damage;
                
                // PUBLISH new event (goes to next frame)
                if (health.Value <= 0)
                {
                    eventBus.Publish(new DeathEvent 
                    { 
                        Entity = hit.Target,
                        Killer = hit.Source 
                    });
                }
            }
        }
    }
}
```

### Simulation Kernel Integration

```csharp
public class SimulationKernel
{
    public EntityRepository Repository { get; }
    public FdpEventBus EventBus { get; }
    private AsyncRecorder? _recorder;
    
    public void Tick()
    {
        // 1. Execute all systems
        foreach (var system in _systems)
        {
            system.Update(Repository, EventBus);
        }
        
        // 2. Post-Simulation: Record BEFORE swap
        if (_recorder != null)
        {
            RecorderSystem.RecordDeltaFrame(
                Repository, 
                EventBus, 
                _recorder.Writer, 
                Repository.GlobalVersion - 1);
        }
        
        // 3. Swap event buffers (makes events visible next frame)
        EventBus.SwapBuffers();
        
        // 4. Tick repository (increments version)
        Repository.Tick();
    }
}
```

## File Format Extension

### Delta Frame Format (Extended)

```
┌─────────────────────────────────────┐
│ FrameHeader (existing)              │
├─────────────────────────────────────┤
│ Entity Destructions (existing)      │
├─────────────────────────────────────┤
│ **EVENT BLOCK (NEW)**               │
│                                     │
│ ┌─────────────────────────────────┐ │
│ │ StreamCount (int32)             │ │
│ ├─────────────────────────────────┤ │
│ │ For each stream:                │ │
│ │   - EventTypeId (int32)         │ │
│ │   - ByteCount (int32)           │ │
│ │   - EventBytes (byte[])         │ │
│ └─────────────────────────────────┘ │
├─────────────────────────────────────┤
│ Component Chunks (existing)         │
└─────────────────────────────────────┘
```

### Keyframe Format (Extended)

```
┌─────────────────────────────────────┐
│ FrameHeader                         │
├─────────────────────────────────────┤
│ **EVENT BLOCK**                     │
│ (Same format as delta)              │
├─────────────────────────────────────┤
│ Full State Snapshot                 │
│ (All component chunks)              │
└─────────────────────────────────────┘
```

## Testing

### Test Coverage

1. **Event Recording**: Verify events are captured in frames
2. **Event Replay**: Verify events are visible during playback
3. **Timing**: Verify events appear in correct frame
4. **Seeking**: Verify seeking through event-heavy scenarios
5. **Integration**: Military simulation with explosions/fire events

### Example Test

```csharp
[Fact]
public void FlightRecorder_RecordsAndReplaysEvents()
{
    using var repo = new EntityRepository();
    using var eventBus = new FdpEventBus();
    
    // Record
    using (var recorder = new AsyncRecorder("test.fdp"))
    {
        for (int frame = 0; frame < 100; frame++)
        {
            // Publish events
            eventBus.Publish(new DamageEvent { Amount = frame });
            
            // Record BEFORE swap
            RecorderSystem.RecordDeltaFrame(repo, eventBus, /* ... */);
            
            // Swap
            eventBus.SwapBuffers();
            repo.Tick();
        }
    }
    
    // Replay
    using var replayRepo = new EntityRepository();
    using var replayBus = new FdpEventBus();
    using var controller = new PlaybackController("test.fdp");
    
    for (int frame = 0; frame < 100; frame++)
    {
        controller.StepForward(replayRepo, replayBus);
        
        var events = replayBus.Consume<DamageEvent>();
        Assert.Equal(1, events.Length);
        Assert.Equal(frame, events[0].Amount);
    }
}
```

## Performance Considerations

1. **Zero-Copy Serialization**: Uses Span<byte> for direct memory access
2. **No Allocation**: Event bytes copied directly, no intermediate buffers
3. **Lazy Stream Creation**: Event streams only created when first event published
4. **Filtering**: Only streams with pending events are recorded
5. **Lock-Free Publishing**: Concurrent event publishing uses atomic operations

## Future Enhancements

1. **Managed Events**: Support for FdpEventBus.PublishManaged<T>()
2. **Event Compression**: Delta encoding for repeated events
3. **Event Filtering**: Record only specific event types
4. **Event Timestamps**: Sub-frame timing information
5. **Event Metadata**: Source system, priority, tags

## Summary

The Event Bus is now fully integrated with Flight Recorder:

✅ Events recorded during PostSimulation (from Pending buffer)  
✅ Events injected during replay (into Current buffer)  
✅ Proper timing: events in frame N visible in frame N+1  
✅ Zero-copy serialization for performance  
✅ Complete isolation: Repository and Bus are independent  
✅ Ready for testing with military simulation

The integration is **non-invasive**: existing code continues to work, and the event bus can be used independently or with the recorder.
