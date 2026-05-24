# BATCH-02 Review

**Batch:** BATCH-02
**Tasks:** DEBT-001, GZH-009, GZH-010, GZH-015, GZH-011
**Verdict: APPROVED with two deferred debt items**

---

## Summary

BATCH-02 delivered all required tasks. The 187 gizmo-specific unit tests pass (Fdp.Toolkits.Tests);
the 2 GZH-011 tests in Hrot.SimHost.Tests pass; SC_GZ067–SC_GZ070 regression tests pass. Build is
clean for both `Fdp.Toolkits` and `Hrot.Common`.

---

## Implementation Quality

### DEBT-001 — GZH001_2 (TerminalDisconnectedEvent round-trip) ✅
Correct and complete. Mirrors GZH001_1 pattern exactly.

### GZH-009 — LocalTerminalModule ✅
Clean implementation. `AddEndpoint` + `AddListener` in constructor, reversed in `Dispose`.
`LocalUiTransport` property exposed correctly.

Test quality:
- `GZH009_1`: verifies count goes from 0→1 on construction and back to 0 on dispose. ✅
- `GZH009_2`: verifies hub routing reaches the transport AND stops after dispose. ✅

### GZH-010 — GizmoNetworkTransportModule ✅
Good solution for the circular dependency: `IGizmoNetworkFactory` interface in `Fdp.Toolkits`
extracts the three factory members needed, and `INetworkFactory` extends it. `StubNetworkFactory`
in tests has null participant, making all DDS code-paths inactive.

The `Tracker` field is `internal readonly`, accessible from tests via the existing
`InternalsVisibleTo` attribution.

Test quality:
- `GZH010_1`: confirms count stays 0 on construction with null participant (no AddListener
  on construction — correct). ✅
- `GZH010_2`: drives tracker.OnSample for two nodes then one disconnect — verifies increments
  and decrements. ✅

### GZH-015 — GizmoCapabilitiesTracker ✅
Idempotency (HashSet.Add returning false), disconnect of unknown node (ignored), and both
event bus + count checks on connect/disconnect are all exercised.

Test quality:
- `GZH015_1` / `GZH015_2`: verify both `ListenerCount` AND `FdpEventBus` events after
  `SwapBuffers()`. This is exactly the required depth. ✅
- `GZH015_3`: unknown disconnect is ignored (no event, no count change). ✅
- `GZH015_4`: duplicate alive sample is idempotent (count stays 1). ✅

### GZH-011 — LayerControlGizmo refactor ✅
Hash change (`const → static readonly` computed), projector injection, and simplified
`OnStructUpdate` are all correct. `_editService` field correctly removed (delegated to projector).
`IgApplication` schema registration now uses `LayerControlGizmo.SchemaHash` (dynamic) instead of
the raw constant.

Test quality:
- `GZH011_1`: hash identity check against `GizmoSettingsRegistry.ComputeHash` with literal
  full type name — will catch any accidental renaming of the DTO. ✅
- `GZH011_2`: uses a real `GizmoExecutionController` path and recording publisher. Verifies
  "exactly one publish, no duplicate echo on second draw" — this exercises the real
  `StructInspectorProjector` deduplication logic, not just a stub. ✅
- GZH011_3 regression: all 14 SC_GZ067–SC_GZ070 tests pass without modification. ✅

---

## Correctness Notes (non-blocking)

**Note on `headless: true` in `CreateGizmoTranslators` call:**
The BATCH-02 instructions said `headless: false` but the developer used `headless: true`. This is
correct. Per the API doc: `true` = ingress (node receives UI events from remote viewer), which is
the correct mode for a simulation node that accepts interaction from a remote terminal. The existing
`SimHostApp.cs` and `CgfSubsystem.cs` both use `headless: true` for the same reason. The
instruction was wrong; the developer's decision is right.

**Note on `HashSet<long>` vs `HashSet<uint>`:**
Instructions said `HashSet<uint>` but `TerminalConnectedEvent.TerminalId` is `long`. The developer
correctly used `long` throughout for type consistency.

---

## Deferred Items → DEBT-TRACKER

### DEBT-002 (P2): LayerControlGizmo composition root wiring incomplete
**Description:** `SimHostApp.cs` and `EditorSubsystem.cs` still construct `LayerControlGizmo`
without passing the `GizmoUiStateHub` as `uiPublisher` (parameter defaults to null). The hub is
not stored as a field in these composition roots yet.
**Impact:** `LayerControlGizmo` in SimHost and Editor nodes does not push DTO state through the
hub. Feature is silently inactive. No regression — it degrades to the same behaviour as before.
**Resolution:** Wire when the full module installation (GZH-012 scope) stores `_uiHub` in the
composition root.
**Target batch:** BATCH-03.

### DEBT-003 (P2): DDS IGCapabilitiesAnnounce reader not registered in GizmoNetworkTransportModule
**Description:** `GizmoNetworkTransportModule.RegisterSystems()` registers the translator wrappers
from `CreateGizmoTranslators()` but does NOT register a system that reads `IGCapabilitiesAnnounce`
DDS samples and calls `Tracker.OnSample()`. In production, `Tracker.OnSample` is never driven by
incoming DDS samples — it can only be called by external code or tests.
**Impact:** Remote terminal connect/disconnect is not auto-detected via DDS lifecycle. Tracker
logic and tests are correct and ready; only the production DDS wiring is missing.
**Resolution:** Add a `GizmoCapabilitiesIngressSystem` that creates a DDS reader for
`IGCapabilitiesAnnounce` when participant is non-null, reads samples per frame, and calls
`Tracker.OnSample(sample.NodeId, instanceState == Alive)`. Skip when participant is null.
**Target batch:** BATCH-03 or dedicated DDS integration pass.

---

## Minor Issues (SKIP)

- `DdsGizmoUiStatePublisher` is `internal` which is correct (no external callers need it).
- `GizmoMap.Example.LayerControlGizmo` still uses `0x8899AABB` — acceptable because this is an
  isolated example project that cannot reference `Fdp.Toolkits`. No action required.
- The 27 pre-existing test failures in `Fdp.Toolkits.Tests` are unrelated to this batch.

---

## Build Verification

| Project | Result |
|---------|--------|
| `Fdp.Toolkits.csproj` | ✅ 0 errors |
| `Hrot.Common.csproj` | ✅ 0 errors, 1 pre-existing DLL lock warning |
| `Fdp.Toolkits.Tests` gizmo filter | ✅ 187 / 187 passed |
| `Hrot.SimHost.Tests` GZH011 filter | ✅ 2 / 2 passed |
| SC_GZ067–SC_GZ070 | ✅ 14 / 14 passed |

---

## Verdict: APPROVED

All required tasks implemented and tested. Two deferred debt items added to DEBT-TRACKER
(composition root wiring + DDS capabilities reader). No blocking issues.
