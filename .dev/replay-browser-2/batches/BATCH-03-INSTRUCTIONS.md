# BATCH-03 — Stage 3 Diff Engine Backend + Stage 2 History Trackers

**Status:** Ready for development
**Depends on:** BATCH-01, BATCH-02, BATCH-02C (all committed)

---

## Scope

| Item | Task ID | New tests |
|---|---|---|
| `DiffNode` hierarchy | RB-3.1 | DIF-T01, DIF-T02 (partial, no service yet) |
| `IComponentDiffService` + `ComponentDiffService` | RB-3.2 | DIF-T01..DIF-T13 |
| Wire changelog mode into `RecordingExportService` | RB-3.3 | EX-T27, EX-T28, EX-T29 |
| `EntitySelectionHistory` + `PlaybackHistoryTracker` | RB-2.1 | FND-T01..FND-T05 + randomized smoke |
| Corrective: translator invocation in export service | RB02C-P2-001 | EX-T22 (strengthened) |
| Corrective: improve EX-T20 with array-field component | RB02-P3-003 | EX-T20 (strengthened) |

**Reference:** `.dev/replay-browser-2/DESIGN.md` and `.dev/replay-browser-2/TASK-DETAILS.md`.
Consult DESIGN.md for all spec details. This batch only annotates what to do and where — it does
not repeat the design verbatim.

---

## Existing Codebase State

- `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/` — base for all new headless files
- `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/` — existing tests, harness
- `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/RecordingExportService.cs` — FULLY IMPLEMENTED (fix needed for translators)
- `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Export/RecordingExportServiceTests.cs` — 38 tests all green
- `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Support/FdpRecordingHarness.cs` — recording harness, HarnessPosition/HarnessVelocity components
- `FDP/Toolkits/Fdp.Toolkits/Scenario/ScenarioSerializer.cs` — has `Translators` property exposed; `Extract()` returns `Dictionary<string, object>`

---

## Task 1 — DiffNode Hierarchy (RB-3.1)

**Create:** `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Diff/DiffNode.cs`

Implement the data model from DESIGN.md §5.1 verbatim. The three types are:
- `DiffNode` (abstract base) — `Name`, `IsModified`
- `DiffObject` (sealed) — `Children`, `EvaluateModificationState()`
- `DiffValue` (sealed) — `OldValue`, `NewValue`, `ValueType`, `IsModified` set at construction time

**Rules:**
- `EvaluateModificationState()` sets `IsModified = Children.Exists(c => c.IsModified)`.
- `IsModified` on `DiffObject` is NOT set automatically — the caller must invoke `EvaluateModificationState()` after populating children.
- No reference to `Fdp.Presentation` or any ImGui assembly.

---

## Task 2 — IComponentDiffService + ComponentDiffService (RB-3.2)

**Create:**
- `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Diff/IComponentDiffService.cs` — interface per DESIGN.md §5.2
- `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Diff/ComponentDiffService.cs` — implementation

**Interface** (from DESIGN.md §5.2):
```csharp
public interface IComponentDiffService
{
    DiffNode? ComputeDiff(string name, JsonNode? oldNode, JsonNode? newNode, double epsilonTolerance);
    IReadOnlyList<DiffNode> ComputeEntityDiff(Entity entity, EntityRepository sandboxRepo,
        ScenarioSerializer serializer, Action applyStepFunc);
    IReadOnlyList<DiffNode> ComputeTreeDiff(JsonNode? before, JsonNode? after, double epsilonTolerance);
}
```

**Algorithm rules** (from DESIGN.md §5.2):
- If both are `JsonObject`: recurse over the union of keys (keys in `old` only get `newVal = "null"`, keys in `new` only get `oldVal = "null"`).
- `JsonValueKind.Number` leaf: parse as double; `|old-new| < epsilon` => `IsModified=false`, still emit the leaf (don't discard).
- Other leaves: `ToJsonString()` string equality.
- Arrays that differ at any index: emit as single `DiffValue` leaf (the full old/new array JSON strings), do NOT recurse into array elements — keeps visual output clean (DESIGN.md §5.2 and design-talk.md lines 382-384).
- After building a `DiffObject`, always call `EvaluateModificationState()` before returning it.
- Even if a subtree has no modifications, still emit it. Only the top-level "no components changed" case returns an empty list.

**`ComputeEntityDiff`:**
1. Call `serializer.Serialize(repo, new ScenarioHeader(serializer.SubsystemType))` — this is the "before" state.
2. Call `applyStepFunc()` exactly once.
3. Call `serializer.Serialize(repo, ...)` again — this is the "after" state.
4. Extract the per-component JSON nodes from both DOMs, diff each component.
5. Return the list of per-component `DiffNode`s.

**`ComputeTreeDiff(before, after, epsilon)`:**
- If `before == null`: all leaves in `after` are emitted as modified (new value side populated, old = "null").
- If `after == null`: all leaves in `before` are emitted as modified (old value populated, new = "null").
- Otherwise: call `ComputeDiff("root", before, after, epsilon)` and return children of the resulting root.

### Tests for RB-3.2 — `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Diff/ComponentDiffServiceTests.cs`

See DESIGN.md §5.4 for the full DIF-T table. Every test is non-negotiable.

**DIF-T01:** Two identical `JsonObject`s produce a root `DiffObject.IsModified == false` with no `DiffValue` having `IsModified==true`.

**DIF-T02:** Single leaf change: `{ "X": 1 }` vs `{ "X": 2 }`. Only that leaf's `IsModified` is true; propagates up to root via `EvaluateModificationState`.

**DIF-T03:** Disjoint keys: key only in old emits `DiffValue(oldVal=json, newVal="null", IsModified=true)`. Key only in new emits `DiffValue(oldVal="null", newVal=json, IsModified=true)`.

**DIF-T04:** Numeric epsilon: `{ "X": 0.1 }` vs `{ "X": 0.1005 }` with epsilon=0.001: `IsModified=false`. Same with difference 0.002: `IsModified=true`.

**DIF-T05:** Mixed type leaf (was number `42`, now string `"hello"`): emitted as modified with the correct `ValueType` (string).

**DIF-T06:** Arrays differing at any index: `[1,2,3]` vs `[1,2,4]` produces a single `DiffValue` for the full array — NOT three leaf values. Verify `IsModified=true` and `OldValue`/`NewValue` are the full array JSON strings.

**DIF-T07:** `ComputeEntityDiff` calls `applyStepFunc` exactly once, confirmed by a call-count counter closure. Also verify the serializations happen before and after the call (if you track a simple mutable counter, it changes between the two serialize calls).

**DIF-T08:** `ComputeEntityDiff` returns empty list when entity does not exist / was destroyed before the call (use a fresh repo with no entities).

**DIF-T09:** Allocation budget — `GC.GetTotalAllocatedBytes(true)` before/after 1000 calls of `ComputeDiff` on a 200-leaf tree must be < 1 MB total. Use a flat `JsonObject` with 200 numeric properties for the 200-leaf test.

**DIF-T10:** Same tree diffed twice in a row produces identical results with zero modifications the second time.

**DIF-T11:** `ComputeTreeDiff(null, postState, ε)` — all leaves in `postState` are emitted as modified (entity birth). Verify: for a `postState` with 3 leaves, all 3 have `IsModified=true` and `OldValue == "null"`.

**DIF-T12:** `ComputeTreeDiff(preState, null, ε)` — all leaves in `preState` emitted as modified with `NewValue == "null"`.

**DIF-T13:** Hide-unchanged pruning rule. Build a tree manually: `DiffObject("root")` > `DiffObject("SimTransform")` > `DiffObject("Position")` > `DiffObject("Inner")` > `DiffValue("X", "1", "2", IsModified=true)`. Call `EvaluateModificationState()` bottom-up. Then write a simple recursive headless tree-walker that simulates the `_hideUnchanged=true` prune (`if (!node.IsModified) return`). Assert the walker visits exactly the 4-node chain and skips any sibling nodes you add with `IsModified=false`.

---

## Task 3 — Wire Changelog Mode (RB-3.3)

**Modify:** `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/RecordingExportService.cs`

The changelog path is activated when `options.FormatMode == ExportFormatMode.Changelog` and
`options.TargetEntities` is non-empty.

**Algorithm** (DESIGN.md §3.6):
- Maintain `Dictionary<Entity, JsonNode?> baselines` (initially all null for each target entity).
- Per frame, per target entity:
  1. If `!repo.IsAlive(entity)` → `baselines[entity] = null`; skip.
  2. Serialize the entity's current component state to `JsonNode`. Use `autoSerializer.TryExtract()` per component to build a flat `JsonObject` keyed by component name (same approach as AbsoluteState but aggregated to one DOM node per entity, not per-component).
  3. Compute `diffService.ComputeTreeDiff(baselines[entity], current, options.EpsilonTolerance)`.
  4. If the result list has any entry with `IsModified == true`, emit a `ChangelogEntryDto`:
     - `FrameIndex = currentFrame`
     - `WallClockTicks = meta.WallClockTicks`
     - `RelativeWallTimeSec = (meta.WallClockTicks - firstFrameWallTicks) / TicksPerSecond`
     - `SimTimeSec` from `GlobalTime`
     - `EntityHandle = guidResolver.Resolve(entity)`
     - `Mutations = result`
  5. `baselines[entity] = current`.
- When `FormatMode == Changelog`, root JSON is an **array** (not an object with "Frames" key). Each element is a `ChangelogEntryDto` serialized via `JsonSerializer.Serialize(writer, entry, ...)`.
- `IComponentDiffService` is required; add it as a constructor parameter: `RecordingExportService(ScenarioSerializer? serializer = null, IComponentDiffService? diffService = null)`. Instantiate `ComponentDiffService` internally if null is passed.

**IMPORTANT:** When `FormatMode == Changelog` and `TargetEntities` is empty, emit an empty JSON array and return without error.

### Tests for RB-3.3 — add to `RecordingExportServiceTests.cs`

**EX-T27:** Use `FdpRecordingHarness` to build a recording with one entity (e.g., using `HarnessPosition`). Record 5 frames:
- Frame 0 (keyframe): Position.X = 1.0
- Frame 1 (delta): Position.X = 2.0 (mutated)
- Frame 2 (delta): Position.X = 2.0 (unchanged)
- Frame 3 (delta): Position.X = 3.0 (mutated)
- Frame 4 (delta): Position.X = 4.0 (mutated)

Export with `FormatMode = Changelog`, `TargetEntities = [entity]`, epsilon = 0.001.
Assert:
- Root JSON is an array.
- Exactly 3 entries (frames 1, 3, 4 each have a mutation).
- Each entry has `FrameIndex` matching the expected mutated frame ordinal.
- Each entry's `Mutations` list contains at least one `DiffNode` with `IsModified=true`.

**EX-T28:** Same setup but epsilon = 2.0. Build a recording where Position.X changes by 0.5 (less than epsilon). Assert the result array is empty (mutation suppressed).

**EX-T29:** Build a recording: entity exists in frames 0-2, destroyed in frame 3, frames 4+ have no entity. Export with Changelog, target = [entity]. Assert:
- Only frames 0-2 produce entries (frame 0 is the birth from null baseline).
- No entries for frame 3 or later (entity destroyed → baseline reset to null).
- No crash.

---

## Task 4 — History Trackers (RB-2.1)

**Create:**
- `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/EntitySelectionHistory.cs`
- `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/PlaybackHistoryTracker.cs`

Follow the spec in TASK-DETAILS.md §RB-2.1 and DESIGN.md §4.6 exactly.

**Invariants for `EntitySelectionHistory`:**
- `PushSelection(e)`: if `e == currentSelection` (duplicate) → no-op. If navigating (in GoBack/GoForward) → no-op. Otherwise push, truncate forward stack, fire `OnSelectionChanged`.
- `GoBack()`: set navigating flag, move pointer back, fire `OnSelectionChanged`, clear navigating flag.
- `GoForward()`: same but forward.
- `CanGoBack`, `CanGoForward` reflect the stack state.

**Invariants for `PlaybackHistoryTracker`:** Mirrors all four above but for `int` frame indices and `OnSeekRequested`.

### Tests for RB-2.1

**Test file:** `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/History/EntitySelectionHistoryTests.cs`

**FND-T01:** `PushSelection` sets `CanGoBack=true`. `GoBack` emits `OnSelectionChanged` exactly once with the previous entity. `GoForward` emits `OnSelectionChanged` exactly once with the next entity.

**FND-T02:** Pushing the same entity twice consecutively results in a history of size 1 (no duplicate). `CanGoBack=false` after only one distinct push.

**FND-T03:** After `GoBack`, calling `PushSelection(newEntity)` truncates the forward stack. `CanGoForward=false` after the new push.

**FND-T04:** Re-entrance guard: inside the `OnSelectionChanged` handler, call `PushSelection` with a different entity. It must be suppressed — no recursive loop, no extra `OnSelectionChanged` firing. Verify the `OnSelectionChanged` callback was invoked exactly once for the original `GoBack`.

**FND-T05:** `PlaybackHistoryTracker` smoke: push 3 frame indices (5, 10, 15). `CanGoBack=true`, `CanGoForward=false`. GoBack → `OnSeekRequested(10)`. GoBack again → `OnSeekRequested(5)`. Now `CanGoBack=false`. GoForward → `OnSeekRequested(10)`. Push 20 (truncates forward). `CanGoForward=false`. Assert count via CanGoBack/Forward state transitions.

**Randomized smoke test (FND-T05-Smoke):** Generate 100 random operations over an `EntitySelectionHistory` using a seeded random: 60% PushSelection (random entity from pool of 5), 20% GoBack, 20% GoForward. After each operation: `Assert.True(CanGoBack || !CanGoForward || history.Count >= 1)` — i.e., the history never has `CanGoForward=true` and `CanGoBack=false` unless there is at least one item. Specifically check: if `CanGoBack` is false and `CanGoForward` is false, the stack is empty or has exactly one entry. Use `Assert.True` with meaningful messages. Must not throw for any sequence.

---

## Task 5 — Corrective: Translator Invocation in RecordingExportService (RB02C-P2-001)

**Modify:** `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/RecordingExportService.cs`
**Modify:** `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Export/RecordingExportServiceTests.cs`

### Production code fix

`ScenarioSerializer.Translators` is a public `IReadOnlyList<IEntityScenarioTranslator>`. Use it.

In `ExportToJson()`, inside the per-entity component loop, replace the current bare `autoSerializer.TryExtract(sandboxRepo, entity, bit, guidResolver)` call with a dispatch that:

1. **Before the per-bit loop**, for each translator in `_serializer.Translators`:
   - Call `translator.CanTranslate(sandboxRepo, entity)`.
   - If true, call `translator.Extract(sandboxRepo, entity, guidResolver)` → get `Dictionary<string, object>`.
   - For each key-value pair in the result, add to a `Dictionary<string, JsonNode?> translatorPayloads` indexed by component name.

2. **In the per-bit loop**, after resolving `compName`:
   - Check `translatorPayloads.TryGetValue(compName, out var translatorPayload)`.
   - If found and non-null, use that as `payload`; skip `autoSerializer.TryExtract`.
   - Otherwise use `autoSerializer.TryExtract(sandboxRepo, entity, bit, guidResolver)`.

3. The `translator.Extract()` result value is `object`. Cast it to `JsonNode?` via `value as JsonNode`.

When `_serializer == null`, there are no translators to invoke — skip step 1.

The `guidResolver` already in scope (type `DiagnosticGuidResolver`) implements `IGuidResolver` — pass it directly to `Extract()`.

### EX-T22 strengthened assertion

Update `EX_T22_CustomTranslator_IsHonored_PayloadReflectsStubDto` to also assert that "FooBlackboard" appears in the **actual `ExportToJson()` output file** (not just in `ScenarioSerializer.Serialize()`).

Steps for the updated test:
1. Build a recording using `FdpRecordingHarness` that has an entity with `HarnessVelocity` component.
2. Create a `ScenarioSerializer` with `FooHarnessBlackboardTranslator` registered.
3. Export using `new RecordingExportService(serializer: serializer).ExportToJson(fdpPath, outPath, ...)`.
4. Parse the output JSON and find the `HarnessVelocity` component entry for the entity.
5. Assert the component's `Payload` contains `"Source": "FooBlackboard"` (or the full JSON text contains `"FooBlackboard"`).

The `FooHarnessBlackboardTranslator` is already in the test file. Its `Extract()` returns:
```csharp
new Dictionary<string, object>
{
    ["HarnessVelocity"] = new JsonObject
    {
        ["Source"] = JsonValue.Create("FooBlackboard"),
        ["Vx"] = JsonValue.Create(comp.Vx),
        ["Vy"] = JsonValue.Create(comp.Vy),
    }
}
```
The key `"HarnessVelocity"` must match the component name produced by `autoSerializer.GetComponentName(bit)` or `ComponentTypeRegistry.GetType(bit)?.Name`. Verify this works. If the names don't match, adjust the translator's key to match.

**Remove** the prior `ScenarioSerializer.Serialize()` assertion from EX-T22 (that was a workaround; the export assertion is the real test). Keep it or not — your choice — but the `ExportToJson` assertion is now required.

---

## Task 6 — Corrective: Improve EX-T20 with Array-Field Component (RB02-P3-003)

**Modify:** `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Support/FdpRecordingHarness.cs`
(and all support files that need a new component type)

**Problem:** `HarnessPosition` has individual float fields `X, Y, Z` — serialized as a JSON object, not an array. `FlattenNumericArrays` never processes a real array payload.

**Fix:**
1. Add a new harness component `HarnessTransform` (component ID 204) with a float array field, e.g.:
   ```csharp
   [ComponentId(204)]
   public struct HarnessTransform
   {
       [Key(0)] public float[] Position; // serialized as JSON array [x, y, z]
   }
   ```
   OR use a struct containing a `System.Numerics.Vector3` if `FdpAutoSerializer` serializes it as an array. Use whichever type `FdpAutoSerializer` serializes as a JSON array (not a JSON object). Check existing test components or `ScenarioSerializerTests.cs` for a pattern.

2. Register `HarnessTransform` in `FdpRecordingHarness`.

3. Update `EX_T20_NumericArrayPayloads_AreFlattenedToSingleLine` to:
   - Build a recording with an entity carrying `HarnessTransform`.
   - Export it.
   - Parse the output and find the `HarnessTransform` component's `Payload`.
   - Assert `Payload` is a JSON array OR contains a JSON array (depending on component shape).
   - Assert there is no multi-line array in the output: the array `[x, y, z]` must appear on a single line (no `\n` inside `[...]`).
   - Use a regex or string search: `Assert.DoesNotMatch(new Regex(@"\[\s*[\d.]+,\s*\n"), text)`.

If `FdpAutoSerializer` does not serialize `float[]` as a JSON array (it may serialize it as null or skip it), use a `System.Numerics.Vector3` field instead (check existing serializer tests for what produces a JSON array).

---

## Build Requirements

- Run `dotnet build FDP/FDP.sln` — zero errors required.
- Run `dotnet test FDP/FDP.sln --filter "FullyQualifiedName~ReplayBrowser"` — all tests pass.
- No new failures in any other test project.

---

## Deliverables

**New production files:**
- `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Diff/DiffNode.cs`
- `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Diff/IComponentDiffService.cs`
- `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Diff/ComponentDiffService.cs`
- `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/EntitySelectionHistory.cs`
- `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/PlaybackHistoryTracker.cs`

**Modified production files:**
- `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/RecordingExportService.cs` (translator dispatch + changelog mode)

**New test files:**
- `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Diff/ComponentDiffServiceTests.cs` (DIF-T01..DIF-T13)
- `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/History/EntitySelectionHistoryTests.cs` (FND-T01..FND-T05 + smoke)

**Modified test files:**
- `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Export/RecordingExportServiceTests.cs` (EX-T22 strengthened, EX-T20 improved, EX-T27..T29 added)
- `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Support/FdpRecordingHarness.cs` (HarnessTransform component)

---

## Report Format

When done, write `.dev/replay-browser-2/reports/BATCH-03-REPORT.md` with:
- Summary of each task (one paragraph each)
- List of all new/modified files
- Test result counts (pass/fail per project)
- Any design deviations with rationale
- Any blockers or open questions
