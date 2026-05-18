# Delta Frame Versioning Bug Fix

## Problem Summary

Delta frames were being recorded with only headers (17 bytes) and no actual component data. This caused playback to fail - when seeking to a specific tick, only the keyframe would be applied, and subsequent delta frames containing modifications would be ignored.

### Root Cause

The issue was in the **order of operations** during recording:

**WRONG ORDER (Bug):**
```csharp
ref var pos = ref repo.GetUnmanagedComponentRW<Position>(entity);
pos.X = frame;          // 1. Modify component at GlobalVersion V
repo.Tick();            // 2. Increment to GlobalVersion V+1  
recorder.CaptureFrame(repo, prevTick, blocking: true); // 3. Record delta
```

**Why this fails:**
- Component is modified at GlobalVersion V
- `Tick()` increments GlobalVersion to V+1
- Recording checks: `chunkVersion > prevTick` → `V > V` → **FALSE** ❌
- Delta frame is written with **no component data** (just 17-byte header)

**CORRECT ORDER (Fix):**
```csharp
repo.Tick();            // 1. Increment to GlobalVersion V+1
ref var pos = ref repo.GetUnmanagedComponentRW<Position>(entity);
pos.X = frame;          // 2. Modify component at GlobalVersion V+1
recorder.CaptureFrame(repo, prevTick, blocking: true); // 3. Record delta
```

**Why this works:**
- `Tick()` increments GlobalVersion to V+1
- Component is modified at GlobalVersion V+1  
- Recording checks: `chunkVersion > prevTick` → `V+1 > V` → **TRUE** ✅
- Delta frame contains full component data

## Files Modified

### Test Files Fixed
1. **DebugPlayback/Program.cs** - Fixed `CreateTestRecording()` method
2. **Fdp.Tests/Play backDebugTests.cs** - Fixed `CreateTestRecordingWithLogging()` method
3. **Fdp.Tests/RecorderDeltaLogicTests.cs** - Already had correct order (line 160)

### New Test File
4. **Fdp.Tests/DeltaFrameVersioningTests.cs** - Comprehensive validation suite

## Verification

### Delta Frame Size Analysis

**Before Fix:**
- Keyframe: ~131KB (full data)
- Delta frames: 17 bytes each (header only, NO data)
- Result: Position X = 5 (only keyframe applied)

**After Fix:**
- Keyframe: ~131KB (full data)
- Delta frames: >100 bytes each (header + component data)
- Result: Position X = 8 (keyframe + deltas correctly applied)

### Test Outcomes

All tests now pass:
- ✅ `DeltaFrameRecording_CorrectTickOrder_CapturesChanges` - Validates correct order
- ✅ `DeltaFrameRecording_IncorrectTickOrder_FailsToCaptureChanges` - Documents why wrong order fails
- ✅ `DeltaFrameSize_ContainsComponentData_NotJustHeader` - Proves delta frames contain data  
- ✅ `VersionTracking_ComponentModification_UpdatesChunkVersion` - Validates version tracking
- ✅ `DebugDeltaApplication_InvestigatesDeltaFrameIssue` - End-to-end validation

## Version Tracking Mechanism

The RecorderSystem uses version tracking to detect changes:

```csharp
// RecorderSystem.cs:388
if (table.GetVersionForEntity(i) > sinceVersion) return true;
```

When a component is modified:
1. `GetUnmanagedComponentRW<T>()` is called
2. Component table's chunk version is updated to `GlobalVersion`
3. During recording, this version is compared against `prevTick`

**Critical requirement:** The modification must happen **after** `Tick()` so the version is greater than `prevTick`.

## Best Practices for Recording

### Correct Pattern
```csharp
using var recorder = new AsyncRecorder(filePath);
uint prevTick = 0;

for (int frame = 0; frame < frameCount; frame++)
{
    // 1. Advance simulation tick FIRST
    repo.Tick();
    uint currentTick = repo.GlobalVersion;
    
    // 2. Make modifications (tagged with currentTick)
    repo.SetUnmanagedComponent(entity, newValue);
    
    // 3. Record (modifications at currentTick > prevTick)
    if (isKeyframe)
        recorder.CaptureKeyframe(repo, blocking: true);
    else
        recorder.CaptureFrame(repo, prevTick, blocking: true);
    
    // 4. Update baseline
    prevTick = currentTick;
}
```

### Incorrect Pattern (AVOID)
```csharp
// ❌ WRONG - Do not use this order
for (int frame = 0; frame < frameCount; frame++)
{
    uint prevTick = repo.GlobalVersion;  // ❌ Save BEFORE Tick()
    
    // Modify first
    repo.SetUnmanagedComponent(entity, newValue);
    
    // Then Tick() - increments version AFTER modification
    repo.Tick();
    
    // Recording fails - modification happened at prevTick, not current
    recorder.CaptureFrame(repo, prevTick, blocking: true);
}
```

## Impact

This fix ensures that:
1. Delta frames correctly capture component modifications  
2. Playback seeking applies all intermediate deltas, not just keyframes
3. File sizes are efficient (deltas are much smaller than keyframes)
4. Recording performance is maintained (no unnecessary full snapshots)

## Related Documentation

- See `docs/FDP-FlightRecorder.md` sections on Delta Logic and Version Tracking
- See `Fdp.Kernel/FlightRecorder/RecorderSystem.cs` lines 205-208 for version checking
- See `Fdp.Kernel/FlightRecorder/RecorderSystem.cs` lines 379-391 for `HasChunkChanged()` implementation
