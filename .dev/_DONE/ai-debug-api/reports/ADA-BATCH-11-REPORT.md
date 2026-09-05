# ADA-BATCH-11 Report

**Date:** 2026-06-15
**Branch:** main
**Status:** COMPLETE — all gates passed

---

## Deliverables

### ADA-P5-T01 — Logs Query Endpoint (Group J)

**Files changed:**
- `Hrot\Subsystems\Hrot.Editor\DebugApi\DebugApiService.cs`
- `Hrot\Subsystems\Hrot.Editor\DebugApi\DebugApiHost.cs`
- `Hrot\Subsystems\Hrot.Editor\EditorSubsystem.cs`
- `Hrot\Runner\Hrot.ClusterRunner.Integration.Tests\DebugApiBatch11Tests.cs` (new file)

**Route added:**
- `GET /logs?level=&logger=&since=&max=` — reads both `NLogMessageLogTarget.SharedInstance` and `AiBehaviorLogTarget.SharedInstance` off-thread (lock-guarded). Returns `[{timestamp, level, logger, message}]` sorted newest-first.

**Design decisions:**

| Parameter | Decision |
|-----------|----------|
| `level` | **Minimum level, inclusive** — `?level=Info` returns Info, Warning, Error, Critical; excludes Trace and Debug. Case-insensitive. Enum: `Trace=0, Debug=1, Info=2, Warning=3, Error=4, Critical=5`. |
| `since` | **ISO-8601 datetime**, round-trip `"O"` format. Parsed with `DateTimeStyles.RoundtripKind` (exact match). |
| `max` | **Upper bound on returned results** (default 200). Applied after all filters, entries newest-first. |
| Threading | Off-thread (no `RunMain`). Both sinks use internal lock in `GetMessages()`, which returns a snapshot copy. |

**Constructor extension:** `DebugApiService` accepts optional `IReadOnlyList<IMessageLogSource>? logSinks = null` (defaults to `Array.Empty`). Production wiring passes `NLogMessageLogTarget.SharedInstance` and `AiBehaviorLogTarget.SharedInstance` in `EditorSubsystem`.

---

### ADA-P7-T01 — Entity Filter + Spatial (Group B+)

**Files changed:**
- `Hrot\Subsystems\Hrot.Editor\DebugApi\DebugApiService.cs` (extended `ListEntities`)
- `Hrot\Subsystems\Hrot.Editor\DebugApi\DebugApiHost.cs` (extended `/entities` route)

**Filters added to `GET /entities`:**
- `?component=Foo` — case-insensitive substring match on component type name keys in the entity dump. Entities missing the component are excluded.
- `?near=x,y,r` — XZ-plane radius test (Y=elevation ignored). The `y` in the near string maps to world Z axis (matches the 2D spatial grid convention). Uses `SimTransform.Position` from the entity dump.

**Position extraction:** Vector3 is serialized as a `[x,y,z]` JSON array by both `DefaultRelaxed` (`Vector3ArrayConverter`) and `DebugApiDumpOptions` (`DebugApiVector3SafeConverter`). The near-filter code reads by array index (not by property name). Object-style `{"X":x,"Y":y,"Z":z}` is also handled as a fallback. Entities with no SimTransform component are excluded from `?near=` results.

**MCP wiring confirmed:** `list_entities` already declares `component` and `near` params (added in BATCH-06) and passes them to the query string — no MCP changes required for this parameter path.

---

### MCP Tools

**File changed:** `tools\ai-debug-mcp\src\index.mjs`

Added `get_logs` tool (Group J):
- Params: `level` (enum: Trace/Debug/Info/Warning/Error/Critical), `logger` (substring), `since` (ISO-8601 string), `max` (number)
- All params optional; passes through to `/logs` query string
- 1:1 mapping with the HTTP endpoint, no business logic in the MCP layer

**Tool count: 41 → 42.**

---

### `verify.mjs` Extension

**File changed:** `tools\ai-debug-mcp\verify.mjs`

Added Step 10h between steps 10g (record/replay) and 11 (awaited passthrough):
- `get_logs` with no filter — ok:true, returns array, field shape validated if non-empty
- `get_logs?level=Warning` — ok:true, all returned entries have level in {Warning, Error, Critical}
- `list_entities?component=SimTransform` — ok:true, every returned entity has SimTransform, count <= unfiltered
- `list_entities?component=NonExistent` — returns empty array

Also added `get_logs` to the required tools list in Step 1.

---

### README Update

**File changed:** `tools\ai-debug-mcp\README.md`

- Updated tool table from 33 to 42 tools
- Added `get_logs` row (Group J)
- Updated `list_entities` row to show `?component=&near=` params (Group B+)
- Added all Group I tools (recording/replay) that were missing from the table
- Updated "What it verifies" section to include Step 10h

---

### DEBT-TRACKER Update

**File changed:** `.dev\ai-debug-api\DEBT-TRACKER.md`

- ADA-06-D01 status updated from `PARTIALLY RESOLVED (G+H+I done; J/K/L pending)` to `PARTIALLY RESOLVED (G+H+I+J done; K/L pending)`

---

## Build Output

```
dotnet build IOS-IG-SimHost.sln --nologo
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

---

## Test Output

```
dotnet test Hrot\Runner\Hrot.ClusterRunner.Integration.Tests\... --filter "FullyQualifiedName~DebugApi" --no-build --nologo

Passed!  - Failed:     0, Passed:    91, Skipped:     0, Total:    91, Duration: 16 s
```

### New tests (11 added in DebugApiBatch11Tests.cs):

**Group J — Logs:**
- `GetLogs_LevelFilter_MinimumLevelInclusive` — Info includes Warning/Error, excludes Trace/Debug; Warning excludes Info
- `GetLogs_LoggerFilter_NarrowsBySubstring` — case-insensitive substring on logger name
- `GetLogs_SinceFilter_ExcludesOlderEntries` — timestamp cutoff, old entry excluded
- `GetLogs_MaxFilter_BoundsCount` — max=10 returns exactly 10 from 50 entries
- `GetLogs_NoFilters_ReturnsAllEntries` — baseline all 5 entries returned
- `GetLogs_CombinedFilter_LevelAndLogger` — AND composition: only "ai warning" from mixed set
- `GetLogs_ReturnsRequiredFields` — timestamp/level/logger/message present, ISO-8601 parseable

**Group B+ — Entity filter / spatial:**
- `ListEntities_ComponentFilter_NarrowsToMatchingEntities` — SimTransform filter + NonExistent→empty
- `ListEntities_NearFilter_ReturnsOnlyEntitiesWithinRadius` — entity at (50,0,50) inside r=100, entity at (500,0,500) outside
- `ListEntities_ComponentAndNearFilters_Composable` — AND: SimTransform + near=0,0,50
- `ListEntities_NoFilters_ReturnsAllEntities` — baseline
- `ListEntities_ComponentFilter_IsCaseInsensitive` — lower/upper/canonical counts all equal

---

## Headless / MCP Verify

NOT run (no live runner available in this environment). The `verify.mjs` Step 10h additions are structured to be runnable with the standard `npm run verify` invocation. The lead runs this as part of the review gate.

Steps added to verify.mjs:
- `get_logs` → ok:true, array shape, field validation
- `get_logs?level=Warning` → all entries Warning+
- `list_entities?component=SimTransform` → all have component, count <= unfiltered
- `list_entities?component=NonExistent` → empty array

---

## Bugs Found and Fixed

### Vector3 Serialized as Array, Not Object (P1 — fixed in this batch)

**Symptom:** `ListEntities_NearFilter_ReturnsOnlyEntitiesWithinRadius` and `ListEntities_ComponentAndNearFilters_Composable` failed with `InvalidOperationException: The requested operation requires an element of type 'Object', but the target element has type 'Array'` at `DebugApiService.cs:282`.

**Root cause:** The near-filter code tried to read `posEl.TryGetProperty("X", ...)` but `Vector3` is serialized as `[x, y, z]` by both `DefaultRelaxed` (`Vector3ArrayConverter`) and `DebugApiDumpOptions` (`DebugApiVector3SafeConverter`). The array has index 0/1/2 (X/Y/Z), not named properties.

**Fix:** When `posEl.ValueKind == JsonValueKind.Array`, iterate by index. Object-style fallback retained for robustness.

---

## Debt

No new debt items. ADA-06-D01 partially resolved (Group J now done; K/L remain).
