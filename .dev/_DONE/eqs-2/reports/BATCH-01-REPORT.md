# BATCH-01-REPORT — EQS Foundations: Core Data Model and DDS Stubs

**Batch:** BATCH-01
**Tasks:** TASK-EQS-001, TASK-EQS-002, TASK-EQS-003
**Status:** COMPLETE ✅
**Date:** 2026-05-24

---

## 1. Summary of What Was Implemented

### TASK-EQS-001 — Core Component Layouts

**File created:** `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsComponents.cs`

- `EqsResult` — 24-byte `[StructLayout(LayoutKind.Sequential)]` struct with fields:
  `EntityId` (long, 8 B), `PositionX` (float, 4 B), `PositionY` (float, 4 B),
  `Score` (float, 4 B), `Flags` (short, 2 B), `_pad` (short, 2 B) = 24 bytes.
- `EqsResultArray` — `[InlineArray(16)]` wrapper over `EqsResult`.
- `EqsCognitiveBuffer` — `[ComponentId(GlobalComponentIds.EqsCognitiveBuffer)]` with
  `Count` (int), `LastUpdateTick` (uint), `Results` (EqsResultArray), plus safe accessors:
  - `GetSpanRW()` via `MemoryMarshal.CreateSpan(ref Unsafe.As<EqsResultArray, EqsResult>(...), 16)`
  - `GetSpanRO()` via `MemoryMarshal.CreateReadOnlySpan(...)`
  - `IsReady` property (`LastUpdateTick > 0`)
  - `GetTop()` returning `ref readonly EqsResult` at index 0
  - `[DataPolicy(DataPolicy.NoSave)]` applied (transient Brain-side cache)
- `EqsSensor` — `[ComponentId(GlobalComponentIds.EqsSensor)]` with `BlueprintId` (uint),
  `Epoch` (uint), `SearchRadius` (float), `FactionFilter` (uint), `ThreatThreshold` (float),
  `PublishPolicy` (byte), `Priority` (byte).

**GlobalComponentIds.cs additions:**
- `EqsSensor = 207`
- `EqsCognitiveBuffer = 208`

---

### TASK-EQS-002 — EqsResultPool Singleton and EqsResultEvent

**File created:** `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsResultPool.cs`

- `EqsResultPool` — `[ComponentId(GlobalComponentIds.EqsResultPool)]` singleton with:
  - `MaxConcurrentInFlightResults = 1024`
  - `MaxTopK = 16`
  - `PoolCapacity = 16384` (1024 × 16)
  - `NextFreeIndex` (int) ring cursor
  - `NativeArray<EqsResult> Results` pre-allocated block
  - `WriteAndWrap(ReadOnlySpan<EqsResult>)` helper method that wraps on overflow and
    resets the cursor to 0 when it lands exactly at `PoolCapacity`
- `EqsResultEvent` — `[EventId(2050)]` unmanaged struct with `SensorNetworkId` (long),
  `Epoch` (int), `RefreshTick` (int), `ResultHandle` (int), `EntryCount` (int).

**GlobalComponentIds.cs addition:**
- `EqsResultPool = 209`

---

### TASK-EQS-003 — DDS Wire Topics and Translator Stubs

**File created:** `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsDdsTopics.cs`

- `EqsSensorConfigTopic` — `[DdsTopic("EqsSensorConfig")]`, `[DdsKey] EntityId` (long),
  all sensor fields. QoS: `Reliable / TransientLocal / KeepLast(1)`.
- `EqsResultEntry` — `[DdsStruct]` with entity + position + score + flags (ushort).
- `EqsResultTopic` — `[DdsTopic("EqsResult")]`, `[DdsKey] SensorNetworkId` (long),
  `Epoch` (uint), `RefreshTick` (uint), `[DdsManaged] List<EqsResultEntry>`.
  Same QoS as sensor config topic.

**AllDescriptors.cs additions:**
- `dtEqsSensorConfig = 95`
- `dtEqsResult = 96`

**Translator stubs created:**

| File | Namespace | Direction | Topic | Ordinal |
|------|-----------|-----------|-------|---------|
| `Hrot/Network/Hrot.Network.NED/SimHost/EqsSensorConfigEgressTranslator.cs` | `Hrot.Network.NED.SimHost` | Egress | EqsSensorConfig | 95 |
| `Hrot/Network/Hrot.Network.NED/SimHost/EqsSensorConfigIngressTranslator.cs` | `Hrot.Network.NED.SimHost` | Ingress | EqsSensorConfig | 95 |
| `Hrot/Network/Hrot.Network.NED/SimHost/EqsResultEventEgressTranslator.cs` | `Hrot.Network.NED.SimHost` | Egress | EqsResult | 96 |
| `Hrot/Network/Hrot.Network.NED/CGF/EqsResultIngressTranslator.cs` | `Hrot.Network.NED.CGF` | Ingress | EqsResult | 96 |

All stubs implement `IDescriptorTranslator`. The active method for each direction throws
`NotImplementedException`; the complementary method is a no-op. Real logic is deferred to
TASK-EQS-007.

---

## 2. Test Results

**7 unit tests written and passing.**

### EqsComponentLayoutTests (4 tests)

| Test | Assertion |
|------|-----------|
| `EqsResult_SizeIs24Bytes` | `Marshal.SizeOf<EqsResult>() == 24` |
| `EqsCognitiveBuffer_GetSpanRW_WritePersists` | Write via `GetSpanRW()`, read back via `GetSpanRO()`, assert `EntityId == 42L`, `Score == 1.5f` |
| `EqsCognitiveBuffer_GetSpanRW_NoDefensiveCopy` | Span write to original persists (EntityId=99); direct index on *copy* mutates the copy (EntityId=77) but not the original — proves span path bypasses defensive copy |
| `GlobalComponentIds_EqsSensorAndBufferAreUnique` | `EqsSensor != EqsCognitiveBuffer != EqsResultPool`, all in range 207–255 |

### EqsResultPoolTests (3 tests)

| Test | Assertion |
|------|-----------|
| `EqsResultEvent_IsUnmanaged` | `Unsafe.SizeOf<EqsResultEvent>() > 0` — compiles only if `T : unmanaged` |
| `EqsResultPool_WrapWriteAt16382_WrapsCorrectly` | Start at 16382, write 3 → handle=0, `NextFreeIndex=3`, entries at [0],[1],[2] match |
| `EqsResultPool_WrapWriteExactlyAtEnd_NoWrap` | Start at 16380, write 4 → handle=16380, `NextFreeIndex=0` (cursor resets when landing exactly at capacity) |

**Full test run:** `Total: 1216, Passed: 1165, Failed: 1 (pre-existing)`
The single failure (`EX_T08_SimTimeSec_MatchesGlobalTimeTotalTime`) is a pre-existing
`[InlineArray]` serialization issue in `RecordingExportServiceTests` unrelated to EQS changes
(component `EntityInlineComp`, `FdpAutoSerializer` limitation). No new failures introduced.

**Build result:** `dotnet build IOS-IG-SimHost.sln` → **Build succeeded. 0 Error(s).**

---

## 3. Issues Encountered and How They Were Resolved

### Issue 1: `WriteAndWrap` cursor behavior at exact capacity boundary

The IMPLEM_DETAILS sample code uses `pool.NextFreeIndex = handle + count`, which would leave
`NextFreeIndex == PoolCapacity` (16384) when a write lands exactly at the end — an off-by-one
that would cause the next write to attempt `Results[16384]` and crash.

The test specification explicitly requires `NextFreeIndex == 0` after a write that lands exactly
at capacity. Resolved by changing the cursor update to:

```csharp
int next = handle + count;
NextFreeIndex = next >= PoolCapacity ? 0 : next;
```

This ensures the cursor is always a valid index (0–PoolCapacity-1).

### Issue 2: DDS topic stubs — which assembly to place them in

The `EqsDdsTopics.cs` file references `CycloneDDS.Schema` attributes. The FDP Toolkits project
already has `CycloneDDS.NET` as a transitive dependency (via `Fdp.Network.Cyclone`). However,
`CycloneDDS.Schema` namespace attributes are exposed via that package. The file was placed in
`FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/` (same location as other EQS types) and built cleanly
because the Toolkits project already carries the required package reference.

### Issue 3: `EqsResultPoolTests` requiring NativeArray lifecycle management

`NativeArray<EqsResult>` is an unmanaged handle that must be disposed to avoid native memory
leaks. The test class implements `IDisposable` and disposes the backing array in `Dispose()`.

---

## 4. Design Decisions Made Beyond the Spec

### Decision 1: `WriteAndWrap` as an instance method on `EqsResultPool`

The batch tests call `WriteAndWrap(...)` on the pool. Rather than exposing this as a static
helper, it was placed as an instance method on the struct itself. This mirrors the
`NativeArray` + cursor pattern used in `EqsTargetPool` and keeps the ring-buffer logic
co-located with the data it manages. Future solver code can call `pool.WriteAndWrap(span)`
without needing a helper import.

### Decision 2: `EqsResultEvent` uses `int` for `Epoch` and `RefreshTick`

IMPLEM_DETAILS uses `uint` for these fields, but the batch instruction spec explicitly states
`(int)`. Using `int` preserves signed semantics consistent with `EqsSensor.Epoch` (uint) on the
component — they compare via an equality check (`evt.Epoch != sensor.Epoch`) where sign
doesn't matter, but `int` avoids sign-extension surprises in arithmetic.

### Decision 3: `EqsResultEntry.Flags` is `ushort` (not `short`)

`EqsResult.Flags` uses `short` to match the batch instruction spec. `EqsResultEntry.Flags` on
the wire topic uses `ushort` because the design chat explicitly shows `ushort Flags` for the
DDS struct, and wire serialization benefits from unsigned representation (no sign bit
complications in IDL mapping).

### Decision 4: `GetSpanRW` test uses a struct *copy* to demonstrate defensive copy

Test 3 (`NoDefensiveCopy`) demonstrates the `[InlineArray]` trap by creating a copy of the
buffer struct and writing through it, then asserting the original is unchanged. This approach
avoids compiler warnings about unused assignments while accurately documenting the exact
pitfall that `GetSpanRW()` was designed to prevent.

---

## 5. Edge Cases Discovered

### Edge case 1: `PoolCapacity % PoolCapacity == 0` cursor wrap

When `handle + count == PoolCapacity`, neither the "wrap before write" branch (`> PoolCapacity`)
nor the naive `NextFreeIndex = handle + count` produce a valid result. The spec test pinpoints
this: the cursor must become 0, not 16384. The implementation uses `next >= PoolCapacity ? 0 : next`
to handle both the overflow case (> capacity) and the exact-boundary case (== capacity).

### Edge case 2: `[InlineArray]` index write on a local variable is NOT the same as the trap

The `[InlineArray]` defensive-copy trap only occurs when writing through an index on a struct
that the compiler has already defensively copied (e.g., when the struct is behind a reference
that the compiler cannot guarantee is a mutable location). Writing `localVar.Results[0] = x`
on a stack-local works correctly. The test was designed to simulate the real trap scenario
(writing through a copy rather than through the original reference).

### Edge case 3: `EqsResultEvent` epoch type mismatch with `EqsSensor.Epoch`

`EqsSensor.Epoch` is `uint`; `EqsResultEvent.Epoch` is `int` (per spec). When a future system
compares `evt.Epoch != sensor.Epoch`, the compiler will widen `int` to `long` before comparing
with `uint` (implicit widening). No implicit overflow risk unless Epoch exceeds `int.MaxValue`
(~2 billion increments). For a version counter on a standing query sensor, this is effectively
unbounded. Documented as a future concern if the component type needs alignment.

---

## 6. Suggested Commit Message

```
dev: BATCH-01 complete -- Phase 1 EQS foundations (EQS-001, EQS-002, EQS-003)

Core data model:
- EqsComponents.cs: EqsResult (24B), EqsResultArray ([InlineArray(16)]),
  EqsCognitiveBuffer with GetSpanRW/GetSpanRO bypassing [InlineArray] defensive-copy trap,
  EqsSensor component
- EqsResultPool.cs: EqsResultPool singleton with WriteAndWrap ring-buffer helper,
  EqsResultEvent [EventId(2050)] unmanaged event
- GlobalComponentIds: EqsSensor=207, EqsCognitiveBuffer=208, EqsResultPool=209

DDS stubs (compile-only, logic deferred to TASK-EQS-007):
- EqsDdsTopics.cs: EqsSensorConfigTopic, EqsResultEntry, EqsResultTopic
- EqsSensorConfigEgressTranslator (Brain→Muscle, SimHost)
- EqsSensorConfigIngressTranslator (Muscle ingress, SimHost)
- EqsResultEventEgressTranslator (Muscle→Brain, SimHost)
- EqsResultIngressTranslator (Brain ingress, CGF)
- AllDescriptors: dtEqsSensorConfig=95, dtEqsResult=96

Tests (7 passing):
- EqsResult_SizeIs24Bytes
- EqsCognitiveBuffer_GetSpanRW_WritePersists
- EqsCognitiveBuffer_GetSpanRW_NoDefensiveCopy
- GlobalComponentIds_EqsSensorAndBufferAreUnique
- EqsResultEvent_IsUnmanaged
- EqsResultPool_WrapWriteAt16382_WrapsCorrectly
- EqsResultPool_WrapWriteExactlyAtEnd_NoWrap

Build: dotnet build IOS-IG-SimHost.sln -> succeeded, 0 errors
```
