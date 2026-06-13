# BATCH-TDA-01 — BLOCKED

**Status:** ❌ BLOCKED — cannot complete as specified.
**Date:** 2026-06-13
**Blocker:** Type-system gap: `BlueprintDebugSession` does NOT implement `IAiDebugSession`.

---

## Working Agreement restated

1. One task per batch. Touch ONLY the named files.
2. No cheating — never weaken assertions, suppress diagnostics, or stub features.
3. Finish without asking. Build + test until 0 warnings, Failed:0.
4. Headless only.
5. Litter-free.
6. Report = truth.
7. No codebase-memory MCP — read actual source.

---

## What was verified

### Interface hierarchy (read from source)

- **`IAiDebugSession`** (`Hrot.Editor.AiShared/Debug/IAiDebugSession.cs:5`):
  `public interface IAiDebugSession : IAiTraceObserver`
  - Inherits `IAiTraceObserver` which requires `BeginObservingAsset(Guid, TraceLevel)`, `EndObservingAsset(Guid)`, `GetActiveEntities(Guid)`.
  - Declares `SetBreakpoint(Guid assetId, Guid elementId)` (2 params).
  - Declares `BreakpointId` / `Breakpoint` / `PausedAt` using types from `Hrot.Editor.AiShared.Debug`.

- **`IBlueprintDebugSession`** (`Hrot.Blueprints.Core/IBlueprintDebugSession.cs:142`):
  `public interface IBlueprintDebugSession : IBlueprintProbeSink`
  - Does NOT extend `IAiDebugSession` or `IAiTraceObserver`.
  - Declares `SetBreakpoint(Guid assetId, Guid graphId, Guid nodeId)` (3 params).
  - Declares `BreakpointId` / `Breakpoint` / `PausedAt` using types from `Hrot.Blueprints.Core.Debug`.

- **`BlueprintDebugSession`** (`Hrot.Blueprints.Editor/BlueprintDebugSession.cs:27`):
  `public sealed class BlueprintDebugSession : IBlueprintDebugSession`
  - Does NOT list `IAiDebugSession` in its interface list.
  - Has `OnSessionStateChanged` (line 1483), `GetActiveEntities` (line 880) — matches signatures shared by both hierarchies.
  - Does NOT have `BeginObservingAsset` or `EndObservingAsset` (grep returned zero matches).
  - Has conflicting breakpoint types: its `SetBreakpoint` takes 3 args, not 2; its `BreakpointId`/`Breakpoint`/`PausedAt` are from `Hrot.Blueprints.Core.Debug`, not `Hrot.Editor.AiShared.Debug`.

### Evidence: no reference to IAiDebugSession anywhere in BlueprintDebugSession

```
$ grep -n "IAiDebugSession\|IAiTraceObserver" BlueprintDebugSession.cs
(no matches)
```

```
$ grep -n "BeginObservingAsset\|EndObservingAsset" BlueprintDebugSession.cs
(no matches)
```

### Contrast: how the test fixtures bridge the gap

`Hrot.Blueprints.Tests/Debug/AiDebugCommandsTests.cs:73`:
```csharp
internal sealed class FakeBlueprintDebugSession : IAiDebugSession, IBlueprintDebugSession
```

The test fake explicitly implements BOTH interfaces, with explicit interface implementations for the conflicting members (lines 115–128). The production `BlueprintDebugSession` does not do this.

---

## Why File 3 (EditorSubsystem.cs) cannot compile as specified

The batch instructions specify this code in `EditorSubsystem.cs`:

```csharp
Hrot.Editor.AiShared.Debug.IAiDebugSession? session = kind switch
{
    Hrot.Editor.AiShared.AssetKind.Blueprint => _blueprintDebugSession,
    _ => null,
};
debugRegistry.SetActiveSession(session);
```

`_blueprintDebugSession` is of type `Hrot.Blueprints.Core.Debug.BlueprintDebugSession?`.

Since `BlueprintDebugSession` does **not** implement `IAiDebugSession`:
- An implicit conversion is impossible (no interface relationship exists).
- An explicit cast `(IAiDebugSession)_blueprintDebugSession` would be a **compile-time error** CS0030: "Cannot convert type 'BlueprintDebugSession' to 'IAiDebugSession'."
- An `as` cast would always return `null`, defeating the purpose.

The batch's note "if the switch needs an explicit cast to compile, cast to IAiDebugSession" is based on the incorrect assumption that `BlueprintDebugSession` implements `IAiDebugSession`. It does not.

---

## What would be needed to unblock

### Option A (correct): Add `IAiDebugSession` to `BlueprintDebugSession`

Modify `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs` (NOT in the 4-file list):
```csharp
public sealed class BlueprintDebugSession : IAiDebugSession, IBlueprintDebugSession
```

Plus implement the missing members:
- `BeginObservingAsset(Guid, TraceLevel)` / `EndObservingAsset(Guid)` — new methods or stubs
- Explicit interface implementations for conflicting breakpoint members (matching the pattern in `FakeBlueprintDebugSession`)

### Option B (workaround): Adapter class in EditorSubsystem.cs

Create a private nested `IAiDebugSession` adapter inside `EditorSubsystem.cs` that wraps `_blueprintDebugSession`. This would require:
- ~20 delegated members
- Stubs for `BeginObservingAsset`/`EndObservingAsset`
- Explicit interface implementations for conflicting breakpoint members

This violates the Working Agreement rule 2 ("NEVER stub a feature to dodge a hard error") — the `BeginObservingAsset`/`EndObservingAsset` stubs are feature stubs.

---

## Files NOT modified

No files were modified. Per the Working Agreement ("If blocked, STOP and write the blocker in the report"), all 4 target files remain untouched:

1. `Hrot/Editor/Hrot.Editor.AiShared/Debug/IDebugSessionRegistry.cs` — untouched
2. `Hrot/Editor/Hrot.Editor.AiShared/Debug/DebugSessionRegistry.cs` — untouched
3. `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` — untouched
4. `Hrot/Editor/Hrot.Editor.AiShared.Tests/Debug/DebugSessionRegistryTests.cs` — untouched

---

## Recommendation

Add `IAiDebugSession` to `BlueprintDebugSession`'s interface list and implement the 2 missing `IAiTraceObserver` methods (`BeginObservingAsset`, `EndObservingAsset`) plus explicit interface implementations for the conflicting breakpoint members. This follows the established pattern in `FakeBlueprintDebugSession` (line 73–128 of `AiDebugCommandsTests.cs`). Once that is done, BATCH-TDA-01 can proceed as specified.

Alternatively, if `BlueprintDebugSession.cs` is intentionally scoped out of this workstream, the lead may choose to create a dedicated adapter class (new file) rather than embedding it in `EditorSubsystem.cs`.
