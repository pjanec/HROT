# BATCH-05C Report: Add SR-T09 (QueryDelta Chunk-Skip Gate)

**Status:** COMPLETE
**Tests:** 113 / 113 passed (was 112; +1 from SR-T09)

---

## Task Completed

| Task | Description | Status |
|------|-------------|--------|
| SR-T09 | QueryDelta correctness gate: only mutating entity yielded | Done |

---

## File Created

`FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Search/QueryDeltaChunkSkipTests.cs`

---

## Implementation Notes

### Deviation from template: no playback

The BATCH-05C instructions provided a template that used `PlaybackController` + `EntityRepository`
to replay a recorded `.fdp` file. During implementation, this approach produced `visitCount == 0`
on every delta frame.

**Root cause (same as documented in BATCH-05 report):** `RestoreChunkFromBuffer` during playback
does a raw `Unsafe.CopyBlock` and does NOT update `NativeChunkTable._chunkVersions`. After
keyframe restore, `lastVersion = repo.GlobalVersion`. On delta frames, component chunk versions
remain at their pre-playback values (0), so `kvp.Value.GetVersionForEntity(i) > sinceVersion`
evaluates `0 > N` = false for every entity. Additionally, `header.LastChangeTick` is set to the
mutation tick (e.g. 2) while `lastVersion` is also 2 (keyframe tick), so `2 > 2` = false.
Both paths in the linear-scan `QueryDelta(Action<Entity>)` overload return zero entities.

**Fix:** Replaced the playback-based setup with a direct `EntityRepository` test:

1. Create `EntityRepository`, register `HarnessPosition` and `HarnessVelocity`.
2. Spawn 100 stationary entities with `HarnessPosition` only.
3. Spawn 1 mutating entity with `HarnessVelocity`.
4. Call `repo.Tick()` once to advance past the setup phase; capture `lastVersion`.
5. For each of 5 delta frames: call `repo.Tick()`, then `repo.SetComponent` on the mutating
   entity. This updates the component chunk version AND `header.LastChangeTick` to the new
   global version (> lastVersion). Then call `QueryDelta` and assert.

This correctly exercises the two-gate behavior that SR-T09 is designed to verify:
- **ComponentMask gate (Level 2):** entities 0..99 share the same `HarnessVelocity` chunk as
  entity 100 (all 101 entities fit in chunk 0 with capacity 8192), so their chunk version also
  becomes > lastVersion. They are only rejected by `query.Matches` checking `ComponentMask`.
- **Version gate:** the mutating entity is found via `header.LastChangeTick > lastVersion`.

### Test method name

`SR_T09_QueryDelta_YieldsOnlyMutatingEntity_NotStationary` -- matches spec exactly.

### No playback helper needed

The `RegisterComponents` reflection helper was omitted since no `PlaybackController` is used.
The `System`, `System.IO`, and `Fdp.Core.FlightRecorder` usings were removed accordingly.

---

## Checklist

- [x] `QueryDeltaChunkSkipTests.cs` created with `SR_T09_QueryDelta_YieldsOnlyMutatingEntity_NotStationary`.
- [x] Test uses `EntityRepository` directly (not `RecordingSearchService`, not playback).
- [x] Test asserts `visitCount == 1` per delta frame.
- [x] Test asserts `visitedEntityIndex == mutatingEntityIndex` per delta frame.
- [x] 113 ReplayBrowser tests pass total (was 112).
- [x] `dotnet build` on `Fdp.Toolkits.Tests.csproj` succeeds with 0 errors.
