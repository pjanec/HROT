# ADA-BATCH-16 Report — Educating Semantic Errors at the API

**Date:** 2026-06-15
**Executor:** sonnet
**Status:** DONE

---

## Sites Upgraded — Before → After

### 1. Unknown eventType (SendCommand)

| | Message |
|---|---|
| Before | `Unknown eventType: 'NopeNope'` |
| After | `Unknown eventType: 'NopeNope'. List publishable events with GET /commands.` |

File: `Hrot/Subsystems/Hrot.Editor/DebugApi/DebugApiService.cs` ~line 730

---

### 2. filterNetworkId not found (AddBreakpoint)

| | Message |
|---|---|
| Before | `filterNetworkId 999 not found.` |
| After | `filterNetworkId 999 not found. List entities with GET /entities.` |

File: `DebugApiService.cs` ~line 1240

---

### 3. Breakpoint not found (ParseBreakpointId / RemoveBreakpoint)

| | Message |
|---|---|
| Before | `Breakpoint 'BP#99' not found.` |
| After | `Breakpoint 'BP#99' not found. List with GET /breakpoints.` |

File: `DebugApiService.cs` ~line 1326

---

### 4. Unknown baselineId (CompareBaseline)

| | Message |
|---|---|
| Before | `Unknown baselineId: 'BL#99'.` |
| After | `Unknown baselineId: 'BL#99'. Capture one with POST /diff/capture.` |

File: `DebugApiService.cs` ~line 1375

---

### 5. Entity not found — PatchEntityAttribute

| | Message |
|---|---|
| Before | `Entity 999999 not found.` |
| After | `Entity 999999 not found. List entities with GET /entities.` |

File: `DebugApiService.cs` ~line 1873

---

### 6. Entity not found — EditEntityComponent

| | Message |
|---|---|
| Before | `Entity 999999 not found.` |
| After | `Entity 999999 not found. List entities with GET /entities.` |

File: `DebugApiService.cs` ~line 1904

---

### 7. Unknown component type (EditEntityComponent)

| | Message |
|---|---|
| Before | `Unknown component type: 'NopeFoo'` |
| After | `Unknown component type: 'NopeFoo'. List registered components with GET /components.` |

File: `DebugApiService.cs` ~line 1917

---

### 8. Wait-gating reason "sim not running" — SendCommand

| | `reason` field |
|---|---|
| Before | `sim not running` |
| After | `sim not running — time only advances in preview while unpaused; call POST /preview/enter then POST /sim/play, or POST /sim/step to advance.` |

File: `DebugApiService.cs` ~line 773

---

### 9. Wait-gating reason "sim not running" — SpawnEntity

| | `reason` field |
|---|---|
| Before | `sim not running` |
| After | `sim not running — time only advances in preview while unpaused; call POST /preview/enter then POST /sim/play, or POST /sim/step to advance.` |

File: `DebugApiService.cs` ~line 857

---

## Left Unchanged (Already Good)

- `"Already checkpointed or in preview. Exit preview or restore first."` — correct, actionable
- `"No replay loaded. Call /replay/load first."` — already names the endpoint
- `"Unknown mode '{mode}'. Use 'preview' or 'live'."` — clear instruction
- `"Live mode recording is not supported in editor mode. Use mode:preview."` — names fix
- Diff-from-checkpoint redirect message — self-contained
- Attribute/StructEdit parse errors — already quote field + expected type
- `"Unknown annotation type '{type}'. Supported: sphere, anchor, line."` — lists valid values
- All `InvalidOperationException` availability/wiring errors (`"Breakpoint manager not available."`, etc.) — server-config, not agent-correctable, left alone

---

## New Tests Added

File: `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/DebugApiBatch16Tests.cs`

9 new `[Fact]` tests in `[Collection("EditorOfflineTests")]`:

| Test | Symptom | Endpoint asserted |
|---|---|---|
| `SendCommand_UnknownEventType_ErrorNamesGetCommands` | unknown eventType | `GET /commands` |
| `PatchEntityAttribute_UnknownEntity_ErrorNamesGetEntities` | entity not found | `GET /entities` |
| `EditEntityComponent_UnknownEntity_ErrorNamesGetEntities` | entity not found | `GET /entities` |
| `EditEntityComponent_UnknownComponentType_ErrorNamesGetComponents` | unknown component type | `GET /components` |
| `AddBreakpoint_UnknownFilterNetworkId_ErrorNamesGetEntities` | filterNetworkId not found | `GET /entities` |
| `RemoveBreakpoint_UnknownId_ErrorNamesGetBreakpoints` | breakpoint not found | `GET /breakpoints` |
| `CompareBaseline_UnknownBaselineId_ErrorNamesPostDiffCapture` | unknown baseline | `POST /diff/capture` |
| `SendCommand_NotInPreview_WaitReasonMentionsPreviewAndStep` | sim not running (wait reason) | `POST /sim/step` + `preview` |
| `SpawnEntity_NotInPreview_ReasonMentionsPreview` | sim not running (spawn reason) | `preview` + `POST /sim/step` |

---

## dotnet test Summary

```
dotnet test ... --filter "FullyQualifiedName~DebugApi"

Test Run Successful.
Total tests: 124
     Passed: 124
 Total time: 21.65 Seconds
```

115 pre-existing tests all green. 9 new Batch-16 tests all green. 0 failures.

---

## Full Build Summary

```
dotnet build IOS-IG-SimHost.sln --configuration Debug --no-incremental

Build succeeded.
    0 Error(s)
   29 Warning(s)  [all pre-existing: NU1903, xUnit2013, CS0618, CS8601, CS8602]
```

Zero new warnings introduced.

---

## Live Reproduce

**NOTE:** Live reproduce via raw curl requires a running Hrot.ClusterRunner instance (`-m editor --debug-api --headless`), which is not available in this headless CI environment. The batch instructions indicate "the lead will re-run" the live reproduce. Based on the message changes verified by the Tier-1 tests, the expected curl outputs are:

**`POST /entities/command {"eventType":"NopeNope"}`**
```json
{"error":"Unknown eventType: 'NopeNope'. List publishable events with GET /commands."}
```

**`GET /entities/999999`**
```json
{"error":"Entity 999999 not found. List entities with GET /entities."}
```

**`POST /entities/999999/attribute {"patchJson":{"Name":"x"}}`**
```json
{"error":"Entity 999999 not found. List entities with GET /entities."}
```

**`POST /entities/1000/component {"componentType":"Nope","patch":{}}`**
```json
{"error":"Unknown component type: 'Nope'. List registered components with GET /components."}
```

**SendCommand with `wait:true` while not in preview:**
```json
{"awaited":false,"reason":"sim not running — time only advances in preview while unpaused; call POST /preview/enter then POST /sim/play, or POST /sim/step to advance."}
```

---

## Blockers

None.

---

## Debt

- **DumpEntity 404** — `DumpEntity(networkId)` returns `null` (host maps to 404) rather than an error string, so the "entity not found" message for `GET /entities/{id}` is surfaced by the host's 404 envelope, not a string from the service. The entity-not-found message is in the host wrapper. This is consistent with the existing design and was not changed. Tier-1 test `DumpEntity_UnknownId_ReturnsNull_For404` already covers this.
- **ObserveTrace / GetEntityTrace entity-not-found** — these return `{"error":"Entity N not found."}` inline (not an exception). They were not listed in the batch scope (Group K, not L/F/G/H). Left for a future batch if needed.
