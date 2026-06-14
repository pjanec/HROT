# ADA-BATCH-09 Report — NaN/Infinity-Safe Entity Serialization (Corrective)

**Batch:** ADA-BATCH-09  
**Tasks:** ADA-08-D02 corrective (NaN/Infinity-safe serialization across the DebugApi read surface)  
**Date:** 2026-06-14  
**Executor:** sonnet (Claude claude-sonnet-4-6)

---

## Root Cause Analysis

### Prior Attempt: Crash Fixed but Too Lossy

The prior attempt added `DebugApiDumpOptions` with NaN-safe converters and changed `DumpToJsonNode` (in `DebugApiService.cs`) to `JsonSerializer.SerializeToNode(dump, DebugApiDumpOptions)`. It also wrapped `_serializer.SerializeEntity(...)` in a `try-catch(JsonException)`.

This stopped the crash but was **too lossy**: the catch returned the entity with an **empty component dict + continue**. So a freshly-spawned tkbType 1001 (CivilianPedestrian) showed 0 components in both list and dump — permanently (the non-finite field is by-design, not transient). Whole entity classes became un-inspectable.

### Actual Root Cause

The crash is in `FDP/Toolkits/Fdp.Toolkits/Diagnostics/EntityStateExtractionService.cs`, inside `ExtractEntities()`, in the serializer path:

```csharp
var componentsJson = _serializer.SerializeEntity(_repo, entity, resolver!, snapshotableMask);
// ^ THIS THROWS for NaN-containing entities
```

`ScenarioSerializer.SerializeEntity` calls the compiled extraction delegate which calls
`FdpAutoSerializer.SerializeFieldToNode<T>(fieldValue)` → `JsonSerializer.SerializeToNode<T>(value, DefaultRelaxed)`.

In .NET 8, `JsonSerializer.SerializeToNode` works by:
1. Writing the value to an internal byte buffer via `Utf8JsonWriter`
2. Parsing the buffer back into a `JsonNode` via `JsonNode.Parse`

`VectorArrayConverter.Write` calls `writer.WriteRawValue("[NaN, NaN, NaN]")` which succeeds. But step 2 — `JsonNode.Parse` — does NOT support named float literals. `Utf8JsonReader` rejects `NaN` as invalid JSON:

```
JsonReaderException: 'N' is an invalid start of a value. LineNumber: 0 | BytePositionInLine: 7.
  at System.Text.Json.Nodes.JsonNode.Parse(...)
  at System.Text.Json.JsonSerializer.WriteNode[TValue](...)
  at System.Text.Json.JsonSerializer.SerializeToNode[TValue](...)
  at Fdp.Toolkit.Scenario.FdpAutoSerializer.SerializeFieldToNode[T](T value)
  at Fdp.Toolkit.Diagnostics.EntityStateExtractionService.ExtractEntities(...)
```

---

## Improved Fix

**File changed:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/EntityStateExtractionService.cs`

The improved fix catches `JsonException` thrown by `_serializer.SerializeEntity(...)` and
**falls back to the reflection-based per-component extraction** (the existing `else` branch)
instead of returning an empty component dict.

The fallback enumerates `_repo.GetRegisteredComponentTypes()` and collects raw component objects.
These raw objects are then serialized downstream by `DebugApiService.DumpToJsonNode` via
`DebugApiDumpOptions` — whose `NonFiniteFloatSentinelConverter`, `NonFiniteDoubleSentinelConverter`,
and NaN-safe vector converters render non-finite fields as string sentinels
(`"NaN"` / `"Infinity"` / `"-Infinity"`).

**Result:** All components are preserved and non-finite fields appear as string sentinels instead
of the entity being dropped or shown with empty components.

**Design note:** Fallback output is less readable than the translator/DTO path (raw struct field
names vs. translator-shaped DTOs) but all finite components are preserved and non-finite fields
are visible. This is documented as a lower-priority debt item (ADA-09-D01).

The `SanitizeNonFinite(componentsJson)` pass is retained for the non-throwing case (translator-produced
`JsonValue.Create(NaN)`) as defense-in-depth.

---

## Files Changed

| File | Change |
|------|--------|
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/EntityStateExtractionService.cs` | **Primary improved fix**: `JsonException` catch now falls back to reflection-based extraction instead of returning empty components; detailed inline documentation |
| `Hrot/Subsystems/Hrot.Editor/DebugApi/DebugApiSafeFloatConverters.cs` | Prior attempt — preserved; NaN-safe outbound converters for DebugApiDumpOptions |
| `Hrot/Subsystems/Hrot.Editor/DebugApi/DebugApiService.cs` | Prior attempt — preserved; `DebugApiDumpOptions` + `DumpToJsonNode` fix |
| `tools/ai-debug-mcp/verify.mjs` | Step 10e tightened: added assertion `componentCount > 0` to catch regression |
| `.dev/ai-debug-api/DEBT-TRACKER.md` | ADA-08-D02 → **RESOLVED**; ADA-09-D01 added (fallback yields raw vs. translator output) |

---

## Build Status

```
dotnet build IOS-IG-SimHost.sln
→ 0 Error(s), 2 Warning(s) (pre-existing NU1903 for MessagePack, unrelated)
```

---

## Test Summary

### EntityState-specific tests

```
Passed!  - Failed: 0, Passed: 8, Skipped: 0, Total: 8 — Fdp.Toolkits.Tests.dll (EntityState filter)
```

### DebugApi suite (Hrot.ClusterRunner.Integration.Tests, DebugApi filter)

```
Passed!  - Failed: 0, Passed: 75, Skipped: 0, Total: 75
```

### EventSerializationHelper tests

```
Passed!  - Failed: 0, Passed: 3, Skipped: 0, Total: 3
```

### Fdp.Core serialization tests

```
Failed: 3 (Benchmark_SetRawObject_Performance, Benchmark_CommandBuffer_Playback,
          RealisticMilitarySimulation_CompleteScenario_MeasuresPerformance) — ALL PRE-EXISTING
Passed: 1154 (our change introduced zero new failures)
```

No golden snapshot changes.

---

## Live Reproduce

All ADA-08-D02 test cases confirmed passing with **non-zero component counts**:

### GET /entities
```
ok=True, 3 entities (networkId 1000, 1001, 1002 — including NaN entity)
```

### GET /entities/1001 (NaN entity — 35 components, sentinel visible)
```json
{
  "ok": true,
  "data": {
    "EntityId": [1, 1],
    "NetworkId": 1001,
    "Components": {
      "SimTransform": { "Position": ["NaN", "NaN", "NaN"], "Rotation": [...] },
      "SimVelocity": { "Linear": ["NaN", "NaN", "NaN"], ... },
      ... (35 components total)
    }
  }
}
```

Key: `"NaN"` string sentinels appear for non-finite vector fields (not bare `NaN` literals).
Component count = 35 (non-zero — proves the fallback path preserves components).

### POST /diff/compare
```
ok=True, 2 entities changed
```

---

## npm verify

```
Passed: 109
Failed: 0
VERIFICATION PASSED

Relevant Step 10e output:
  ✓ list_entities (NaN entity present) succeeded
  ✓ list_entities (NaN entity present) ok:true
  list_entities count: 3
  NaN entity networkId: 1001
  ✓ get_entity(1001) (NaN entity) succeeded
  ✓ get_entity(1001) ok:true
  get_entity(1001) ok — Node JSON.parse succeeded (no NaN literal)
  get_entity(1001) component count: 35
  ✓ NaN entity must have non-zero component count (got 35) — fallback path must preserve components
  NaN-sentinel check: sentinels found in entity JSON (as expected)
  ✓ diff_state (NaN-entity present) succeeded
```

---

## ADA-08-D02 Status

**RESOLVED** — `GET /entities`, `GET /entities/{networkId}`, and `POST /diff/compare` all return
`ok:true` with **non-zero component counts** when entities with NaN component fields are present
in the world. Non-finite fields render as string sentinels `"NaN"` / `"Infinity"` / `"-Infinity"`.

---

## Residual Debt

| ID | Description | Priority |
|----|-------------|----------|
| ADA-09-D01 | When `_serializer.SerializeEntity` throws `JsonException` (NaN-containing entity), the fallback path yields raw component field names (struct field names, e.g. `SimTransform.Position`) rather than the translator-shaped DTO names that the normal `ScenarioSerializer` path would produce (e.g. custom DTO from `BrainBlackboardTranslator`). The data is inspectable and all components are present; it is simply less human-readable than translator output. Fixing this would require a per-field NaN-safe serialization approach within the translator pipeline. Lower priority. | P3 |
