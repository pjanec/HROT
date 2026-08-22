# ADA-BATCH-13 Report — Group L: Live Mutation (Component Edit + Attribute Patch)

**Date:** 2026-06-15  
**Branch:** feat/ai-debug-api  
**Status:** DONE — both critical issues fixed, all tests green, live reproduce verified

---

## Summary of Fixes

### ISSUE 1 — StructEdit component edit was a NO-OP (CRITICAL)

**Root cause:** `ApplyJsonPatchToDocument` built a leaf-node map keyed by the full `JsonPath`
from StructEdit (e.g. `"$.Current"`, `"$.Position.X"`), but the lookup used bare keys
(`"Current"`, `"Position.X"`). Every lookup was a miss — the silently-caught exception hid
this completely. The Tier-1 test passed because it asserted on the boxed copy returned by
`Commit()`, not on a fresh ECS read.

**Fix (`DebugApiService.cs` — `CollectLeafNodes`):**
Stripped the leading `"$."` prefix from `node.JsonPath` when building the map:
```csharp
var key = node.JsonPath.StartsWith("$.") ? node.JsonPath.Substring(2) : node.JsonPath;
map[key] = node;
```

**Fix (`DebugApiService.cs` — `ApplyJsonValue`):**
Changed the silent `catch {}` to throw `ArgumentException` on parse failure (surfaced as 400).
Added early `return` after a matched leaf so nested-object sibling keys are not mishandled.

**Fix (`DebugApiService.cs` — `EditEntityComponent`):**
Wrapped `ApplyJsonPatchToDocument` in a try-catch for `ArgumentException` → returns error tuple
(HTTP 400 from host).

### ISSUE 2 — patchJson accepted only string, not nested JSON object (IMPORTANT)

**Root cause:** `ctx.Body?["patchJson"]?.GetValue<string>()` throws `InvalidOperationException`
when patchJson is a `JsonObject` node ("The node must be of type 'JsonValue'").

**Fix (`DebugApiHost.cs` — attribute route):**
Now accepts both forms:
- `JsonValue` (string) → `GetValue<string>()` as before
- `JsonObject` or `JsonArray` → `.ToJsonString()` to serialize to string

Returns clean 400 if the node is neither.

### MCP tool update (`index.mjs`)
Updated `patch_attribute` to accept patchJson as either object or string (schema type constraint removed, description updated).

### Tier-1 test fix (`DebugApiBatch13Tests.cs`)
Added two new tests that verify ECS persistence via fresh repo reads:
- `EditEntityComponent_Health_Current_PersistsToEcs` — adds Health{100,100}, edits Current to 50, re-reads from `h.Repo.GetComponentRO<Health>()` to confirm `after.Current == 50`
- `EditEntityComponent_InvalidValue_Returns400_ComponentUnchanged` — confirms `Current="xyz"` → error returned, repo value still 75

---

## Live Reproduce Output

**Server:** `dotnet Hrot.ClusterRunner.dll -m editor --debug-api --debug-api-port 7171 --headless`  
**Scenario loaded:** `test-move` (`waitForReady:true`)

### Step 1 — POST /entities/1000/attribute with NESTED JSON OBJECT {Name: "Alpha"}

**Request:**
```
POST http://localhost:7171/entities/1000/attribute
Content-Type: application/json

{"patchJson":{"Name":"Alpha"}}
```

**Response:** HTTP 200
```json
{"ok":true,"data":{...}}
```

**Verification (GET /entities/1000):**
```
"Name":"Alpha","ForceId":"Frie...
Contains Alpha: True
```

Result: ok:true, GET confirms EntityInfo.Name == "Alpha".

---

### Step 2 — POST /entities/1000/component Health {Current:50}

**Request:**
```
POST http://localhost:7171/entities/1000/component
Content-Type: application/json

{"componentType":"Health","patch":{"Current":50}}
```

**Response:** HTTP 200
```json
{"ok":true,"data":{"Health":{"Current":50,"Max":100},...}}
```

**Verification (fresh GET /entities/1000):**
```
"Health":{"Current":50,"Max":100}
```

Result: ok:true, fresh GET confirms Health.Current == 50 (persisted to ECS chunk).

---

### Step 3 — POST /entities/1000/component Health {Current:"xyz"} (invalid)

**Request:**
```
POST http://localhost:7171/entities/1000/component
Content-Type: application/json

{"componentType":"Health","patch":{"Current":"xyz"}}
```

**Response:** HTTP 400
```json
{
  "ok": false,
  "data": null,
  "error": "Invalid patch value: Cannot parse value for field 'Current' (expected Single): The JSON value could not be converted to System.Single. Path: $ | LineNumber: 0 | BytePositionInLine: 5.",
  "awaited": null
}
```

**Verification (GET /entities/1000 after):**
```
"Health":{"Current":50,"Max":100}
```

Result: HTTP 400 with helpful error message, Health.Current unchanged (still 50).

---

## Build & Test Results

### dotnet build IOS-IG-SimHost.sln
```
0 Error(s)
29 Warning(s) (pre-existing)
Time Elapsed 00:01:11.70
```

### dotnet test --filter "FullyQualifiedName~DebugApi"
```
Passed!  - Failed: 0, Passed: 107, Skipped: 0, Total: 107, Duration: 24 s
```

### npm run verify (tools/ai-debug-mcp)
```
=== Summary ===
  Passed: 193
  Failed: 0

VERIFICATION PASSED
```

---

## Files Changed

| File | Change |
|------|--------|
| `Hrot/Subsystems/Hrot.Editor/DebugApi/DebugApiService.cs` | Fix `CollectLeafNodes` (`$.` prefix strip), fix `ApplyJsonValue` (throw on parse error, early return on match), wrap `ApplyJsonPatchToDocument` call in try-catch |
| `Hrot/Subsystems/Hrot.Editor/DebugApi/DebugApiHost.cs` | Accept patchJson as string OR JSON object; return clean 400 on unusable input |
| `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/DebugApiBatch13Tests.cs` | Added `EditEntityComponent_Health_Current_PersistsToEcs` and `EditEntityComponent_InvalidValue_Returns400_ComponentUnchanged` with fresh ECS re-reads |
| `tools/ai-debug-mcp/src/index.mjs` | Updated `patch_attribute` schema/description to accept nested object |
| `tools/ai-debug-mcp/verify.mjs` | Step 13e: use nested objects; add edit_component persistence + invalid-value assertions |
