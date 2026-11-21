# BATCH-TDA-02-REPORT — Activate main-toolbar debug icons

**Workstream:** toolbar-debug-activate. **Date:** 2026-06-13. **Supersedes:** TDA-01 (correctly STOPPED).

## Summary

All 5 files edited. All 3 named builds: **0 warnings, 0 errors**. All named test suites: **Failed: 0**
(17/0 DebugSessionRegistry, 16/0 AiDebugCommands, 185/0 Hrot.Editor.Tests). No pre-existing tests
regressed. Pre-existing warnings in `Hrot.Blueprints.Tests` (CS0618 obsolete `IBlueprintTimeController`,
CS8601/CS8602 nullability) are in untouched files — not from this batch.

`FakeDebugSessionRegistry` in `AiDebugCommandsTests.cs:168` also required a trivial `SetActiveSession`
stub (the interface addition is breaking). That is the only file edited outside the 5 named files.

---

## File 0 — `BlueprintDebugSession.cs` (line 27)

**Change:** Added `Hrot.Editor.AiShared.Debug.IAiDebugSession` to the class declaration.

```diff
-public sealed class BlueprintDebugSession : IBlueprintDebugSession
+public sealed class BlueprintDebugSession : IBlueprintDebugSession, Hrot.Editor.AiShared.Debug.IAiDebugSession
```

**Verified type shapes** (read from source):

| Member | Core (`IBlueprintDebugSession`) | AiShared (`IAiDebugSession`) |
|--------|------|----------|
| `Breakpoint` record | `(BreakpointId Id, Guid AssetId, Guid GraphId, string NodeId, int HitCount, bool Enabled)` | `(BreakpointId Id, Guid AssetId, Guid ElementId, int HitCount, bool Enabled, string DisplayName)` |
| `BreakpointId` struct | `readonly record struct BreakpointId(int Value)` | `readonly record struct BreakpointId(int Value)` |
| `SetBreakpoint` | `BreakpointId SetBreakpoint(Guid assetId, Guid graphId, Guid nodeId)` | `BreakpointId SetBreakpoint(Guid assetId, Guid elementId)` |
| `ClearBreakpoint` | `void ClearBreakpoint(BreakpointId id)` | `void ClearBreakpoint(BreakpointId id)` |
| `GetBreakpoints` | `IReadOnlyList<Breakpoint> GetBreakpoints()` | `IReadOnlyList<Breakpoint> GetBreakpoints()` |
| `PausedAt` | `Breakpoint? PausedAt` | `Breakpoint? PausedAt` |

**Shared members already satisfied** (no code added — verified present & public):
`IsAttached`, `IsPaused`, `Detach()`, `ClearAllBreakpoints()`, `IsAnyBreakpointActive`,
`PausedOnEntity`, `Continue/StepOver/StepInto/StepOut/Pause`, `GetActiveEntities(Guid)`,
`OnSessionStateChanged`.

**Added members (all fully-qualified; no `using Hrot.Editor.AiShared.Debug`):**

1. **4 colliding breakpoint members** — explicit `IAiDebugSession` impls bridging to Core store:
   - `SetBreakpoint(Guid assetId, Guid elementId)` → `SetBreakpoint(assetId, Guid.Empty, elementId)`
   - `ClearBreakpoint(AiShared.BreakpointId id)` → `ClearBreakpoint(new BreakpointId(id.Value))`
   - `GetBreakpoints()` → converts Core→AiShared via `ToAiSharedBreakpoint`
   - `PausedAt` → converts Core→AiShared via `ToAiSharedBreakpoint`

2. **`ToAiSharedBreakpoint` helper** — maps Core `Breakpoint` (string `NodeId`) to AiShared
   `Breakpoint` (Guid `ElementId` + string `DisplayName`). `ElementId = Guid.TryParse(bp.NodeId)`,
   `DisplayName = bp.NodeId`.

3. **2 `IAiTraceObserver` methods** — explicit no-ops:
   - `BeginObservingAsset(Guid, TraceLevel)` — no-op (blueprint uses `DebugProbe.Sink`, not `AiTracerCoordinator`)
   - `EndObservingAsset(Guid)` — no-op (same rationale)

**Honest-bridge decisions:**
- Breakpoint members: bridged to real Core store (not throwing).
- Trace observer: intentional no-ops. `GetActiveEntities(Guid)` is already shared — the existing
  public method satisfies both interfaces, no duplication.
- No other `IAiDebugSession` members were unimplemented by the compiler.

---

## File 1 — `IDebugSessionRegistry.cs` (line 10)

**Change:** Added `SetActiveSession` to the interface.

```diff
     IAiDebugSession? ActiveSession { get; }
+
+    /// <summary>
+    /// Directly sets the active session WITHOUT any attach/detach side effects…
+    /// </summary>
+    void SetActiveSession(IAiDebugSession? session);
+
     event Action? Changed;
```

---

## File 2 — `DebugSessionRegistry.cs` (line 64)

**Change:** Implemented `SetActiveSession`. Mirrors existing locking style; fires `Changed` only
on an actual change. No Attach/Detach.

```diff
+    public void SetActiveSession(IAiDebugSession? session)
+    {
+        bool changed;
+        lock (_lock)
+        {
+            changed = !ReferenceEquals(ActiveSession, session);
+            if (changed) ActiveSession = session;
+        }
+        if (changed) Changed?.Invoke();
+    }
```

---

## File 3 — `EditorSubsystem.cs` (after line 2020)

**Change:** After `_perspectiveSwitcher.SetDocumentManager(_aiDocumentManager)`, added:

```csharp
void SyncActiveDebugSession()
{
    Hrot.Editor.AiShared.Debug.IAiDebugSession? session = _aiDocumentManager?.Active?.Kind switch
    {
        Hrot.Editor.AiShared.AssetKind.Blueprint => _blueprintDebugSession,
        _ => null,
    };
    debugRegistry.SetActiveSession(session);
}
_aiDocumentManager.ActiveChanged += SyncActiveDebugSession;
SyncActiveDebugSession();
```

- Uses `_aiDocumentManager.Active?.Kind` (same accessor as `blueprint.compileReload` at ~line 3258).
- `_blueprintDebugSession` (declared line 201) is `Hrot.Blueprints.Core.Debug.BlueprintDebugSession?`;
  after File 0 it satisfies `IAiDebugSession`, so the switch arm compiles directly.
- BTree/HSM → `null` (those debug sessions are not yet attached/working — lead decision).
- Initial sync covers the case where a doc is already active before the first `ActiveChanged` fires.
- **Untouched:** `AiDebugCommands` registration, `DebugStepControls.Draw` path,
  `bpBlueprintSession.Attach()`, BTree/HSM factory registrations.

---

## File 4 — `DebugSessionRegistryTests.cs`

**Added:**
- `DetachCountingFake` class — minimal `IAiDebugSession` impl that counts `Detach()` calls.
  `OnSessionStateChanged` uses explicit add/remove (not field-like event) to avoid CS0067.
- 4 test methods:

| Test | Assertion |
|------|-----------|
| `SetActiveSession_SetsActiveSessionAndFiresChanged` | `ActiveSession` is the session; `Changed` fired once |
| `SetActiveSession_NullAfterSet_ClearsAndFiresChanged` | `ActiveSession` is null; `Changed` fired |
| `SetActiveSession_SameReferenceTwice_FiresChangedOnlyOnce` | Same ref → no `Changed` (0 count) |
| `SetActiveSession_Null_DoesNotCallDetach` | `DetachCallCount == 0`, `IsAttached` still `true`, `ActiveSession` is null |

---

## Build & test results

```
dotnet build Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Hrot.Blueprints.Editor.csproj  → 0 warnings ✅
dotnet build Hrot/Editor/Hrot.Editor.AiShared/Hrot.Editor.AiShared.csproj                     → 0 warnings ✅
dotnet build Hrot/Subsystems/Hrot.Editor/Hrot.Editor.csproj                                   → 0 warnings ✅
dotnet test  Hrot/Editor/Hrot.Editor.AiShared.Tests --filter "FullyQualifiedName~DebugSessionRegistry"
  → Passed: 17, Failed: 0 ✅
dotnet test  Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests --filter "FullyQualifiedName~AiDebugCommands"
  → Passed: 16, Failed: 0 ✅
dotnet test  Hrot/Subsystems/Hrot.Editor.Tests/Hrot.Editor.Tests.csproj
  → Passed: 185, Failed: 0 ✅
```

## Side-effect note

`FakeDebugSessionRegistry` in `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/AiDebugCommandsTests.cs:168`
received a trivial `SetActiveSession` stub (`public void SetActiveSession(IAiDebugSession? session) => ActiveSession = session;`)
— required because adding a method to the interface is a breaking change for all implementors.

---

## RUNTIME GATE

**REVIEW-TDA** (lead confirms): run a blueprint in the editor — verify the main-toolbar **Pause** icon
lights while running, and **Continue/Step/StepBack** light when paused at a breakpoint; in-canvas
`DebugStepControls` still work unchanged.
