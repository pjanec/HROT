# BATCH-TDA-02 — Activate main-toolbar debug icons via active-document session (supersedes TDA-01)  [RUNTIME GATE]

**Workstream:** toolbar-debug-activate. **Model: pro (Zoo).** **Repo root:** `D:\Work\IOS-IG-SimHost-FDP`.
**Restate & obey the Working Agreement** in `.dev/toolbar-debug-activate/TASK-TRACKER.md` (one task; no cheating;
finish until build 0 warnings + tests `Failed:0`; headless; tests assert real values; litter-free; report=truth;
**no codebase-memory tooling — read the actual source**). Touch ONLY the 5 files named below.

> This supersedes BATCH-TDA-01, which correctly STOPPED: the production `BlueprintDebugSession` implements only
> `IBlueprintDebugSession`, NOT `IAiDebugSession`, so it can't be assigned to the registry. This batch adds that
> prerequisite (File 0) first, then does the original wiring (Files 1–4).

## Problem (verified)
The main-toolbar AI-debug icons (`AiDebugCommands`, `Hrot.Blueprints.Editor/Debug/AiDebugCommands.cs`) gate every
icon's `IsEnabled` on `IDebugSessionRegistry.ActiveSession` (Continue/StepOver/StepInto/StepOut =
`Active() is { IsPaused: true }`; Pause = `Active() is { IsAttached: true, IsPaused: false }`; StepBack =
`Active() is IBlueprintDebugSession bp && bp.CurrentNodePointer > 0`). But **nothing in production ever sets
`ActiveSession`** (`TryAcquireSession` is called only from tests) → `ActiveSession` is permanently `null` → all
toolbar debug icons are permanently disabled/dark.

The working blueprint debugging runs on a separate path: `_blueprintDebugSession` is eagerly `.Attach()`ed in
`EditorSubsystem` (~line 991) and drawn directly by `DebugStepControls.Draw(_blueprintDebugSession)` (~line 1750);
that session is **never** in the registry. **Do NOT touch that path.**

**Blast radius (confirmed by grep):** the ONLY production reader of `ActiveSession` is `AiDebugCommands` (the
toolbar). The runtime-inspector / trace-timeline windows use the registry's observer API (`RegisterObserver` /
`ActiveObservers`), NOT `ActiveSession`. So this change is toolbar-scoped.

## Fix
Make the toolbar mirror the **active document's** debug session. Scope = **Blueprint only**: BTree/HSM debug
sessions are not yet attached/working → map them (and "no active doc") to `null`.

---

### File 0 (PREREQUISITE) — `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs`
Make the production session implement **both** interfaces:
```csharp
public sealed class BlueprintDebugSession : IBlueprintDebugSession, Hrot.Editor.AiShared.Debug.IAiDebugSession
```
Mirror the **established pattern** in `FakeBlueprintDebugSession` (`Hrot.Blueprints.Tests/Debug/AiDebugCommandsTests.cs:73-128`).

**Do NOT add `using Hrot.Editor.AiShared.Debug;`** — this file uses unqualified `Breakpoint`/`BreakpointId` for the
`Hrot.Blueprints.Core.Debug` types; importing the AiShared namespace would make them ambiguous. Fully-qualify all
AiShared references.

The class **already** satisfies these `IAiDebugSession` members via its existing `IBlueprintDebugSession`
implementation (identical signatures — verify each is present & public): `IsAttached`, `IsPaused`, `Detach()`,
`ClearAllBreakpoints()`, `IsAnyBreakpointActive`, `PausedOnEntity`, `Continue/StepOver/StepInto/StepOut/Pause`,
`GetActiveEntities(Guid)`, `OnSessionStateChanged`. Leave those as-is.

ADD only the members that are NOT already satisfied:

1. The 4 **colliding** breakpoint members — explicit `IAiDebugSession` impls bridging to the existing Core
   breakpoint store (these are NOT called via the toolbar path, but must be honest — NOT throw):
   ```csharp
   // ── IAiDebugSession bridge (toolbar/registry surface; UBP toolbar debug icons) ───────────────
   // BlueprintDebugSession also implements IAiDebugSession so it can be the registry's ActiveSession,
   // which drives the main-toolbar debug icons. The shared step/pause/state members above already
   // satisfy the contract; only the breakpoint members collide (AiShared BreakpointId/Breakpoint are
   // distinct types from the Core ones) → explicit interface impls that bridge to the Core store.
   Hrot.Editor.AiShared.Debug.BreakpointId Hrot.Editor.AiShared.Debug.IAiDebugSession.SetBreakpoint(
       Guid assetId, Guid elementId)
   {
       // The 2-arg AiShared surface has no graphId; bridge to the 3-arg Core API with Guid.Empty.
       var coreId = SetBreakpoint(assetId, Guid.Empty, elementId);
       return new Hrot.Editor.AiShared.Debug.BreakpointId(coreId.Value);
   }

   void Hrot.Editor.AiShared.Debug.IAiDebugSession.ClearBreakpoint(Hrot.Editor.AiShared.Debug.BreakpointId id)
       => ClearBreakpoint(new BreakpointId(id.Value));

   IReadOnlyList<Hrot.Editor.AiShared.Debug.Breakpoint>
       Hrot.Editor.AiShared.Debug.IAiDebugSession.GetBreakpoints()
   {
       var core = GetBreakpoints();
       var list = new List<Hrot.Editor.AiShared.Debug.Breakpoint>(core.Count);
       foreach (var bp in core) list.Add(ToAiSharedBreakpoint(bp));
       return list;
   }

   Hrot.Editor.AiShared.Debug.Breakpoint? Hrot.Editor.AiShared.Debug.IAiDebugSession.PausedAt
       => PausedAt is { } core ? ToAiSharedBreakpoint(core) : null;

   private static Hrot.Editor.AiShared.Debug.Breakpoint ToAiSharedBreakpoint(Breakpoint bp)
       => new(
           new Hrot.Editor.AiShared.Debug.BreakpointId(bp.Id.Value),
           bp.AssetId,
           Guid.TryParse(bp.NodeId, out var nid) ? nid : Guid.Empty,  // Core uses string NodeId; AiShared wants Guid ElementId
           bp.HitCount,
           bp.Enabled,
           bp.NodeId);
   ```
   - Confirm the Core `Breakpoint` member names by reading `IBlueprintDebugSession.cs` (record `Breakpoint` ~line 58:
     `Id` (BreakpointId), `AssetId`, `GraphId`, `NodeId` (string), `HitCount`, `Enabled`). Confirm the AiShared
     `Breakpoint` ctor (`Hrot.Editor.AiShared/Debug/Breakpoint.cs`: `(BreakpointId Id, Guid AssetId, Guid ElementId,
     int HitCount, bool Enabled, string DisplayName)`). Adjust the mapping to compile against the real shapes.
   - Confirm the existing public `PausedAt` is `Hrot.Blueprints.Core.Debug.Breakpoint?` and `GetBreakpoints()`
     returns `IReadOnlyList<Hrot.Blueprints.Core.Debug.Breakpoint>` (they are, per IBlueprintDebugSession).

2. The 2 **trace-observer** members (`IAiTraceObserver`, pulled in by `IAiDebugSession`) — explicit no-ops with a
   comment. The blueprint session observes globally via `DebugProbe.Sink`, NOT the per-asset `AiTracerCoordinator`
   ref-counting that `AiDebugSessionBase` uses for BTree/HSM; and nothing calls these on `ActiveSession` (blast
   radius confirmed). So they are intentional no-ops, not stubs of an unimplemented feature:
   ```csharp
   // IAiTraceObserver: blueprint trace data flows through DebugProbe.Sink (global), not the per-asset
   // AiTracerCoordinator ref-counting used by BTree/HSM — so these are intentional no-ops here.
   void Hrot.Editor.AiShared.Debug.IAiTraceObserver.BeginObservingAsset(
       Guid assetId, Hrot.Editor.AiShared.Debug.TraceLevel level) { }
   void Hrot.Editor.AiShared.Debug.IAiTraceObserver.EndObservingAsset(Guid assetId) { }
   ```
   (`GetActiveEntities(Guid)` is declared by BOTH `IAiTraceObserver` and `IBlueprintDebugSession` with the same
   signature — the existing single public method satisfies both; do NOT duplicate it.)

If the compiler reports any other `IAiDebugSession` member as unimplemented, implement it honestly by bridging to
the existing Core behaviour (or an explicit no-op only where the Core session genuinely has no equivalent and the
member is unreachable via the registry) — and note each such decision in the report. Do NOT throw.

---

### File 1 — `Hrot/Editor/Hrot.Editor.AiShared/Debug/IDebugSessionRegistry.cs`
Add to the interface:
```csharp
/// <summary>
/// Directly sets the active session WITHOUT any attach/detach side effects, firing <see cref="Changed"/>
/// when the value changes. Used by the composition root to make UI surfaces (e.g. the main toolbar) mirror
/// the active document's debug session. Unlike <see cref="ReleaseSession"/> this never calls Detach(), so it
/// is safe for eagerly-attached, long-lived sessions (the blueprint session).
/// </summary>
void SetActiveSession(IAiDebugSession? session);
```

### File 2 — `Hrot/Editor/Hrot.Editor.AiShared/Debug/DebugSessionRegistry.cs`
Implement it. Mirror the existing locking style; fire `Changed` only on an actual change. NO Attach/Detach:
```csharp
public void SetActiveSession(IAiDebugSession? session)
{
    bool changed;
    lock (_lock)
    {
        changed = !ReferenceEquals(ActiveSession, session);
        if (changed) ActiveSession = session;
    }
    if (changed) Changed?.Invoke();
}
```
Leave `TryAcquireSession`, `ReleaseSession`, the factories, and everything else UNCHANGED.

### File 3 — `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`
Wire `AiDocumentManager.ActiveChanged` → `debugRegistry.SetActiveSession(...)`. The local `debugRegistry`
(declared ~line 1955) and the `_aiDocumentManager` field (assigned ~line 2019: `_aiDocumentManager = new
AiDocumentManager(_perspectiveSwitcher);`) are both in scope in that same method. **Immediately after the
`_perspectiveSwitcher.SetDocumentManager(_aiDocumentManager)` line**, add:
```csharp
// Toolbar debug icons (AiDebugCommands) gate IsEnabled on debugRegistry.ActiveSession. Mirror the active
// document's debug session into the registry so those icons enable/disable live. Side-effect-free setter
// (NOT TryAcquire/Release) — the blueprint session is eagerly attached + is DebugProbe.Sink and must NOT be
// detached. Blueprint only: BTree/HSM debug sessions are not yet attached/working → mapped to null for now.
void SyncActiveDebugSession()
{
    Hrot.Editor.AiShared.Debug.IAiDebugSession? session = _aiDocumentManager?.Active?.Kind switch
    {
        Hrot.Editor.AiShared.AssetKind.Blueprint => _blueprintDebugSession,
        // BTree/HSM debug sessions are not yet attached/working — intentionally null until wired.
        _ => null,
    };
    debugRegistry.SetActiveSession(session);
}
_aiDocumentManager.ActiveChanged += SyncActiveDebugSession;
SyncActiveDebugSession(); // initialise for whatever doc (if any) is already active
```
Notes:
- `_aiDocumentManager.Active?.Kind` is the same accessor the `blueprint.compileReload` registration uses
  (search `"blueprint.compileReload"` in this file) — `Active.Kind` returns `Hrot.Editor.AiShared.AssetKind`.
- `_blueprintDebugSession` is `Hrot.Blueprints.Core.Debug.BlueprintDebugSession?`; after File 0 it implements
  `IAiDebugSession`, so the `switch` arm compiles directly. Keep null-safe.
- Do NOT change the `AiDebugCommands` registration, the `DebugStepControls` draw path, `bpBlueprintSession.Attach()`,
  the BTree/HSM factory registrations, or anything else.

### File 4 — tests: `Hrot/Editor/Hrot.Editor.AiShared.Tests/Debug/DebugSessionRegistryTests.cs`
Add focused tests for `SetActiveSession` (reuse the existing `SessionA`/fake session types in that file):
1. `SetActiveSession(session)` → `ActiveSession` is that session **and** `Changed` fired once.
2. `SetActiveSession(null)` after a set → `ActiveSession` is null and `Changed` fired.
3. Setting the **same** reference twice → `Changed` fires only on the first (no redundant event).
4. **Crucially**, `SetActiveSession(null)` does **NOT** call `Detach()` on the previously-set session — assert via
   a fake whose `Detach()` flips a bool / increments a counter (contrast with the existing
   `ReleaseSession_CallsDetach` test, which proves Release DOES detach). This guards the whole reason for the new
   method. If the existing fake has no `Detach` spy, add a minimal one in the test file only.

The `EditorSubsystem` wiring + the dual-interface session are composition-root / integration → the **[RUNTIME
GATE]** (lead confirms icons light up). Do not invent a heavy fake to test the composition-root lambda. (Existing
`AiDebugCommandsTests` already cover the toolbar enable/disable logic against a dual-interface session.)

## Build & test (no `BLUEPRINT_REGENERATE_SNAPSHOTS`)
```
dotnet build Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Hrot.Blueprints.Editor.csproj
dotnet build Hrot/Editor/Hrot.Editor.AiShared/Hrot.Editor.AiShared.csproj
dotnet build Hrot/Subsystems/Hrot.Editor/Hrot.Editor.csproj
dotnet test  Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj --filter "FullyQualifiedName~DebugSessionRegistry"
dotnet test  Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj --filter "FullyQualifiedName~AiDebugCommands"
dotnet test  Hrot/Subsystems/Hrot.Editor.Tests/Hrot.Editor.Tests.csproj
```
All `Failed: 0` (the `Hrot.Blueprints.Tests` PRE-1 set may have pre-existing failures — stay a *subset*, 0 new);
build 0 warnings.

## Definition of done
- `BlueprintDebugSession` implements both `IBlueprintDebugSession` + `IAiDebugSession` (shared members reused;
  4 colliding breakpoint members bridged via explicit impls; trace-observer no-ops documented; nothing throws).
- `SetActiveSession` added to interface + impl (side-effect-free, fires `Changed` on change, never `Detach`s).
- `EditorSubsystem` syncs `debugRegistry.ActiveSession` from the active document's kind on `ActiveChanged`
  (Blueprint → `_blueprintDebugSession`, else `null`) plus an initial sync.
- The 4 new registry tests green; build 0 warnings; `Hrot.Editor.Tests` `Failed: 0`; no new `AiDebugCommands`/
  `Hrot.Blueprints.Tests` failures.
- Write `.dev/toolbar-debug-activate/reports/BATCH-TDA-02-REPORT.md` (the diffs, verified type shapes/member names,
  every honest-bridge vs no-op decision, build/test output; note REVIEW-TDA is the runtime gate).

If anything else can't be done as specified, STOP and write the blocker in the report.
