# BATCH-05C Instructions — Add SR-T09 (QueryDelta Chunk-Skip Gate)

**Context**: BATCH-05 was CHANGES REQUIRED because SR-T09 is completely missing from the test
suite. All other SR-T tests pass and are genuine. The only required work is adding SR-T09.

---

## Reference documents

- DESIGN.md §6.3 SR-T table, row SR-T09
- TASK-DETAILS.md §RB-4.7 ("Chunk-skip gate SR-T09 passes")
- BATCH-05-INSTRUCTIONS.md lines 527-531 (SR-T09 specification)

---

## Why SR-T09 is separate from SR-T34

SR-T34 tests that `QueryDelta` has zero allocation when no entities match (Fix 3: removed
`new List<IComponentTable>()` from inside the function). That is an ALLOCATION gate.

SR-T09 tests a different property: **only the mutating entity is yielded** when 100+ stationary
entities coexist. This is a CORRECTNESS gate — it verifies the combination of:
- Level 2 ComponentMask filtering (entities without the component are rejected)
- Correct version tracking during linear replay (QueryDelta finds entities changed since lastVersion)

Note: `RecordingSearchService` switched to linear scan (Fix 1), but `EntityRepository.QueryDelta`
is still used by external callers and is a core primitive. SR-T09 tests `QueryDelta` directly.

---

## Implementation

### Step 1: Add SR-T09 to QueryDeltaChunkSkipTests.cs (NEW FILE)

Create `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Search/QueryDeltaChunkSkipTests.cs`.

The test must:
1. Spawn 100 stationary entities with only `HarnessPosition` (no `HarnessVelocity`).
2. Spawn 1 mutating entity with `HarnessVelocity { Vx = 0 }`.
3. Build a recording: 1 keyframe + 5 delta frames. Each delta frame mutates only `HarnessVelocity`.
4. Replay via `PlaybackController` + `EntityRepository` directly (not `RecordingSearchService`).
5. Register all components from the manifest via reflection (same pattern as EventScannerCompilerTests).
6. Build `EntityQuery` targeting `HarnessVelocity` via `repo.Query().WithComponentId(velocityTypeId).Build()`.
7. Per delta frame:
   - Capture `lastVersion = repo.GlobalVersion` AFTER the keyframe (before delta frame #1).
   - After each `playback.StepForward(repo)` (delta frame): call `repo.QueryDelta(velocityQuery, lastVersion, countingAction)`.
   - Assert `visitCount == 1` — only the mutating entity is yielded, NOT the 100 stationary entities.
   - Assert `visitedEntityIndex == mutatingEntity.Index`.
   - Update `lastVersion = repo.GlobalVersion`.

**Key assertions**:
- `Assert.Equal(1, visitCount)` per delta frame — not 100, not 101.
- `Assert.Equal(mutatingEntityIndex, visitedEntityIndex)` — the correct entity.

### Step 2: Verify test name is SR_T09

The test method MUST be named `SR_T09_QueryDelta_YieldsOnlyMutatingEntity_NotStationary`.

### Step 3: Use the RegisterComponents helper from EventScannerCompilerTests

Copy the same `RegisterComponents(EntityRepository repo, PlaybackController playback)` helper
(reflection-based, catches exceptions) into the new test class. Do NOT modify EventScannerCompilerTests.

### Step 4: Build and run tests

After implementing:
```
cd d:\Work\IOS-IG-SimHost-FDP-2\FDP
dotnet test Toolkits\Fdp.Toolkits.Tests\Fdp.Toolkits.Tests.csproj --filter "FullyQualifiedName~ReplayBrowser" -v minimal
```

Expected: 113 passed (was 112; +1 from SR-T09).

---

## Template for the new test file

```csharp
using System;
using System.IO;
using Fdp.Core;
using Fdp.Core.FlightRecorder;
using Fdp.Toolkit.ReplayBrowser.Search;
using Fdp.Toolkit.ReplayBrowser.Support;
using Xunit;

namespace Fdp.Toolkit.ReplayBrowser.Search
{
    /// <summary>
    /// SR-T09: QueryDelta correctness gate -- yields only the mutating entity, not stationary ones.
    /// </summary>
    public class QueryDeltaChunkSkipTests : IDisposable
    {
        public QueryDeltaChunkSkipTests()
        {
            ComponentTypeRegistry.Clear();
        }

        public void Dispose() { }

        // ── SR-T09: QueryDelta yields only the mutating entity ────────────────

        [Fact]
        public void SR_T09_QueryDelta_YieldsOnlyMutatingEntity_NotStationary()
        {
            // Arrange: 100 stationary entities (HarnessPosition only) + 1 mutating entity
            // (HarnessVelocity). On each delta frame, only the HarnessVelocity entity mutates.
            // QueryDelta with a HarnessVelocity query must yield exactly 1 entity per frame.
            const int stationaryCount = 100;
            string fdpPath;
            int mutatingEntityIndex;

            using (var h = new FdpRecordingHarness())
            {
                for (int i = 0; i < stationaryCount; i++)
                    h.SpawnEntity().WithComponent(new HarnessPosition { X = i });

                h.SpawnEntity().WithComponent(new HarnessVelocity { Vx = 0f });
                var mutatingEntity = h.LastSpawned;
                mutatingEntityIndex = mutatingEntity.Index;

                h.Tick(); h.RecordKeyframe();

                for (int frame = 1; frame <= 5; frame++)
                {
                    float vx = frame;
                    h.MutateComponent<HarnessVelocity>(mutatingEntity,
                        c => new HarnessVelocity { Vx = vx });
                    h.Tick(); h.RecordDelta();
                }

                fdpPath = h.BuildToTempFile();
            }

            try
            {
                int velocityTypeId = ComponentTypeRegistry.GetId(typeof(HarnessVelocity));

                using var playback = new PlaybackController(fdpPath);
                var repo = new EntityRepository();
                RegisterComponents(repo, playback);

                var velocityQuery = repo.Query().WithComponentId(velocityTypeId).Build();

                // Step through: keyframe, then 5 delta frames.
                uint lastVersion = 0;
                bool seenKeyframe = false;

                while (playback.StepForward(repo))
                {
                    if (!seenKeyframe)
                    {
                        // After keyframe: capture baseline version.
                        lastVersion  = repo.GlobalVersion;
                        seenKeyframe = true;
                        continue;
                    }

                    // Delta frame: QueryDelta must yield exactly 1 entity.
                    int visitCount        = 0;
                    int visitedEntityIndex = -1;

                    repo.QueryDelta(velocityQuery, lastVersion, entity =>
                    {
                        visitCount++;
                        visitedEntityIndex = entity.Index;
                    });

                    Assert.Equal(1, visitCount);
                    Assert.Equal(mutatingEntityIndex, visitedEntityIndex);

                    lastVersion = repo.GlobalVersion;
                }

                Assert.True(seenKeyframe, "Recording must contain at least a keyframe");
            }
            finally
            {
                try { if (File.Exists(fdpPath)) File.Delete(fdpPath); } catch { }
            }
        }

        // ── Helper ───────────────────────────────────────────────────────────

        private static void RegisterComponents(EntityRepository repo, PlaybackController playback)
        {
            var manifest = playback.Metadata?.SchemaManifest;
            if (manifest == null) return;
            foreach (var kvp in manifest)
            {
                Type? type = ComponentTypeRegistry.GetType(kvp.Key);
                if (type == null) continue;
                try
                {
                    var m = typeof(EntityRepository)
                        .GetMethod("RegisterComponent",
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                            new[] { typeof(DataPolicy?) })!
                        .MakeGenericMethod(type);
                    m.Invoke(repo, new object?[] { null });
                }
                catch { }
            }
        }
    }
}
```

---

## Checklist

- [ ] `QueryDeltaChunkSkipTests.cs` created with `SR_T09_QueryDelta_YieldsOnlyMutatingEntity_NotStationary`.
- [ ] Test uses `FdpRecordingHarness` to build a real `.fdp` recording.
- [ ] Test uses `PlaybackController` + `EntityRepository` directly (NOT `RecordingSearchService`).
- [ ] Test asserts `visitCount == 1` per delta frame.
- [ ] Test asserts `visitedEntityIndex == mutatingEntityIndex` per delta frame.
- [ ] 113 ReplayBrowser tests pass total (was 112).
- [ ] `dotnet build` on `FDP.sln` succeeds with 0 errors.

---

## Report

Write the batch report to `.dev/replay-browser-2/reports/BATCH-05C-REPORT.md`.
