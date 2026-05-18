# Flight Recorder Seeking and Rewind Mechanism

## Overview

The PlaybackController implements efficient random access to any point in a recording using a **Keyframe + Delta Replay** strategy.

## Core Algorithm

### Seeking to Any Frame

When you call `SeekToFrame(repo, targetFrame)`, the system:

1. **Finds the nearest previous keyframe** using `FindPreviousKeyframe(targetFrame)`
2. **Applies the keyframe** to reset the repository to that known good state
3. **Replays all delta frames** sequentially from the keyframe to the target

```csharp
private void SeekToFrame(EntityRepository repo, int targetFrame, int startKeyframe)
{
    // Step 1: Apply keyframe (full state snapshot)
    ApplyFrame(repo, startKeyframe);
    _currentFrameIndex = startKeyframe;
    
    // Step 2: Replay deltas up to target
    while (_currentFrameIndex < targetFrame)
    {
        _currentFrameIndex++;
        ApplyFrame(repo, _currentFrameIndex);  // Apply each delta
    }
}
```

### Example: Seeking from Frame 50 to Frame 15

```
Timeline:  [K0] ... [D10] ... [K15] ... [D20] ... [D50]
                              ↑                    ↑
                         Keyframe               Current
                         
Seeking back to Frame 15:
1. Find previous keyframe → Frame 15 (it IS a keyframe!)
2. Apply keyframe at Frame 15
3. No deltas needed (target == keyframe)
4. Done! State is now at Frame 15
```

### Example: Seeking from Frame 10 to Frame 48

```
Timeline:  [K0] ... [D10] ... [K20] ... [D30] ... [K40] ... [D45] [D46] [D47] [D48] [D49]
                    ↑                                      ↑
                 Current                              Keyframe            Target

Seeking forward to Frame 48:
1. Find previous keyframe → Frame 40
2. Apply keyframe at Frame 40 (full reset)
3. Replay deltas: 41, 42, 43, 44, 45, 46, 47, 48
4. Done! State is now at Frame 48
```

## Key Operations

### StepBackward (Rewind One Frame)

```csharp
public bool StepBackward(EntityRepository repo)
{
    if (_currentFrameIndex <= 0)
        return false;
    
    int targetFrame = _currentFrameIndex - 1;
    
    // Find the last keyframe before target
    int keyframeIndex = FindPreviousKeyframe(targetFrame);
    
    // Seek to keyframe and replay to target
    Se ekToFrame(repo, targetFrame, keyframeIndex);
    
    return true;
}
```

### Rewind (Back to Start)

```csharp
public void Rewind(EntityRepository repo)
{
    if (_frameIndex.Count > 0)
    {
        SeekToFrame(repo, 0);  // Seek to first frame
    }
}
```

### FastForward

```csharp
public void FastForward(EntityRepository repo, int frameCount)
{
    int targetFrame = Math.Min(_currentFrameIndex + frameCount, _frameIndex.Count - 1);
    SeekToFrame(repo, targetFrame);  // Use same seeking mechanism
}
```

## Performance Characteristics

### Keyframe Strategy

**Dense keyframes** (every 5-10 frames):
- ✅ **Pros**: Fast seeking (replay at most 5-10 deltas)
- ❌ **Cons**: Larger file size (full snapshots are big)

**Sparse keyframes** (every 100+ frames):
- ✅ **Pros**: Smaller file size (mostly deltas)
- ❌ **Cons**: Slower seeking (may replay 100+ deltas)

### Typical Configuration

```csharp
// Keyframe every 10 frames
for (int frame = 0; frame < totalFrames; frame++)
{
    if (frame % 10 == 0)
        recorder.CaptureKeyframe(repo, blocking: true);
    else
        recorder.CaptureFrame(repo, prevTick, blocking: true);
}
```

**Results:**
- File at frame 48 needs to replay 8 deltas max (from keyframe 40)
- Average seek replay: ~5 deltas
- Worst case: 9 deltas

## Frame Index

The PlaybackController builds an in-memory index of all frames on load:

```csharp
public struct FrameMetadata
{
    public long FilePosition;   // Where in the file
    public int FrameSize;       // How big
    public ulong Tick;          // Simulation tick
    public FrameType FrameType; // Keyframe or Delta
}
```

**Index Building** (happens once on controller creation):

```csharp
private void BuildFrameIndex()
{
    _frameIndex.Clear();
    _fileStream.Position = _headerEndPosition;
    
    while (_fileStream.Position < _fileStream.Length)
    {
        long frameStart = _fileStream.Position;
        int frameSize = _reader.ReadInt32();
        
        // Peek at metadata
        ulong tick = _reader.ReadUInt64();
        byte frameType = _reader.ReadByte();
        
        _frameIndex.Add(new FrameMetadata
        {
            FilePosition = frameStart,
            FrameSize = frameSize,
            Tick = tick,
            FrameType = (FrameType)frameType
        });
        
        // Skip to next frame
        _fileStream.Position = dataStart + frameSize;
    }
}
```

This enables **O(1) frame access** - we know exactly where each frame is in the file.

## Seeking Use Cases

### Game Replay Scrubbing

```csharp
// User drags slider to 65% through the recording
using var controller = new PlaybackController("match.fdp");
int targetFrame = (int)(controller.TotalFrames * 0.65f);
controller.SeekToFrame(repo, targetFrame);  
// Instantly jumps to that point
```

### Debug: Jump to Failure Point

```csharp
// Crash happened at tick 5000, find it
controller.SeekToTick(repo, 5000);
// Now at the exact simulation tick where the error occurred
```

### Rewind for Instant Replay

```csharp
// Show the last 5 seconds again
int framesPerSecond = 60;
int rewindFrames = 5 * framesPerSecond;  // 300 frames

controller.FastForward(repo, -rewindFrames);  // Go back
controller.PlayToEnd(repo);  // Replay forward
```

## Implementation Details

### Why Keyframe + Delta?

**Pure Keyframes:**
- Every frame is a complete snapshot
- Pro: Instant seeking (just load one frame)
- Con: HUGE file sizes (100x larger!)

**Pure Deltas:**
- Only changes recorded
- Pro: Tiny file sizes
- Con: Must replay from start EVERY time (very slow)

**Hybrid (Keyframe + Delta):**
- Keyframes provide reset points
- Deltas provide efficient incremental changes
- **Best of both worlds**: Reasonable file size + Fast seeking

### Practical Example

Recording 1000 frames of a game with 100 entities:

| Strategy | File Size | Seek to Frame 999 |
|----------|-----------|-------------------|
| All Keyframes | 50 MB | 1 frame load (~1ms) |
| All Deltas | 5 MB | 999 frame replays (~500ms) |
| **Keyframe every 50** | **8 MB** | **1 keyframe + 49 deltas (~25ms)** |

## Entity Lifecycle and Seeking

### Challenge: Entities Created and Destroyed

When seeking backward or forward, entities may:
- Not exist yet (created later)
- Be alive (exists now)
- Be dead (destroyed earlier)
- Be recreated (same slot, different generation)

**The seeking mechanism handles this automatically** because:

1. **Keyframes contain EntityIndex chunks** - Full entity state
2. **Deltas contain destruction logs** - Explicit destroys
3. **PlaybackSystem repairs the EntityIndex** - Correct generations

### Example: Slot Reuse During Seek

```
Frame 1: Entity(index=0, gen=1) created
Frame 5: Entity(index=0, gen=1) destroyed
Frame 10: Entity(index=0, gen=2) created (same slot!)

Seeking to Frame 3:
- Apply keyframe at Frame 0 (empty)
- Replay deltas 1, 2, 3
- Result: Entity(0,1) exists ✅

Seeking to Frame 7:
- Apply keyframe at Frame 5
- Replay deltas 6, 7  
- Result: Entity(0,1) is DEAD ✅

Seeking to Frame 10:
- Apply keyframe at Frame 10 
- Result: Entity(0,2) exists, Entity(0,1) is still DEAD ✅
```

## Optimization: Smart Keyframe Placement

For optimal performance, place keyframes at:

1. **Regular intervals** - Every N frames (e.g., N=10)
2. **Change points** - Major state changes (level transitions, battles)
3. **Destruction waves** - After many entities destroyed (helps compression)

```csharp
bool shouldKeyframe = 
    frame % 10 == 0 ||                    // Regular interval
    destructionLog.Count > 100 ||         // Many destroys
    levelTransitionHappened;              // Major event

if (shouldKeyframe)
    recorder.CaptureKeyframe(repo);
else
    recorder.CaptureFrame(repo, prevTick);
```

## Summary

✅ **Seeking works by finding the closest previous keyframe and replaying deltas**
✅ **This provides O(log N) seek performance** with reasonable file sizes
✅ **Entity lifecycle is automatically handled** through EntityIndex restoration
✅ **Rewind, fast-forward, and seeking all use the same mechanism**
✅ **Frame index enables instant random access** to any frame in the recording

The design ensures that you can jump to **any point in a recording** with predictable performance, while maintaining **compact file sizes** through delta compression.
