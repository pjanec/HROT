# BATCH-13 Report

**Batch:** BATCH-13
**Task:** TASK-UAI-P4-03 (SC-P4-03-1, SC-P4-03-2)
**Status:** APPROVED — all tasks complete, build green, 18/18 tests passing

---

## Summary

Implemented the runtime AI tuning registry and gizmo-backed console (Slice 1, scalar tunables).
Two new projects were created and registered in the solution. DDS code generation was explicitly
disabled for both projects because neither defines DDS topic types.

---

## Projects Created

| Project | Path |
|---|---|
| `Hrot.Diagnostics.Tuning` | `Hrot/Diagnostics/Hrot.Diagnostics.Tuning/` |
| `Hrot.Diagnostics.Tuning.Tests` | `Hrot/Diagnostics/Hrot.Diagnostics.Tuning.Tests/` |

Both projects were added to `IOS-IG-SimHost.sln` under the existing `Diagnostics` solution folder
(GUID `{5E4C52BA-6213-E083-B735-5DDE0CCE6DA3}`).

---

## Files Created

### Hrot.Diagnostics.Tuning

| File | Description |
|---|---|
| `Hrot.Diagnostics.Tuning.csproj` | Project file; references `Fdp.Toolkits`, `GizmoMap.Contracts`; `CycloneDdsDisableCodeGen=true` |
| `TuningKey.cs` | `public readonly struct TuningKey` — FNV-1a-32 name hash |
| `TuningKind.cs` | `public enum TuningKind { Float, Int, Bool }` |
| `TuningScope.cs` | `public enum TuningScope { Global, PerNodeRole, PerEntity, PerSquad }` |
| `TuningOwner.cs` | `public enum TuningOwner { Brain, Muscle }` |
| `Tunable.cs` | `public sealed class Tunable` — descriptor with `Read`/`Write` delegates |
| `TuningChangeEvent.cs` | `public readonly struct TuningChangeEvent` — change record (deferred wiring) |
| `TuningAttribute.cs` | `[TunableAttribute]` — field/property annotation with min/max/scope/owner |
| `TuningRegistry.cs` | `public sealed class TuningRegistry` — lock-free queue, `BeginFrame` drain, clamping |
| `UtilityTuningBinder.cs` | `public static class UtilityTuningBinder` — registers 4 tunables per consideration |
| `Gizmos/TuningConsoleGizmo.cs` | `public sealed class TuningConsoleGizmo : IStatefulGizmo` — struct-inspector draw |

### Hrot.Diagnostics.Tuning.Tests

| File | Description |
|---|---|
| `Hrot.Diagnostics.Tuning.Tests.csproj` | Test project file; xUnit 2.9.2 |
| `TuningRegistryTests.cs` | 8 tests covering clamping, queue behaviour, key equality |
| `TuningConsoleGizmoTests.cs` | 6 tests covering draw calls, JSON parsing, toggle |
| `UtilityTuningBinderTests.cs` | 4 tests covering registration count, read/write delegates |

---

## Key Design Decisions

- **`TuningRegistry` uses a lock-guarded queue** so that `Apply` (called from any thread) is
  safe, and `BeginFrame` (called from the game thread) drains the queue without a prolonged lock.
- **`UtilityTuningBinder` write delegates** replace the entire `UtilityConsideration` array
  element because `UtilityConsideration` is a `readonly struct`. The replacement pattern:
  `option.Considerations[ci] = new UtilityConsideration(...)` with the changed field and all
  other fields copied from the original.
- **`CycloneDdsDisableCodeGen=true`** added to both project files. The `buildTransitive`
  CycloneDDS.NET targets would otherwise scan all `public` types and attempt to generate IDL,
  which fails because the project types are plain C# types with no DDS semantics.
- **Deferred**: SC-P4-03-3 (replay honesty via FlightRecorder), SC-P4-03-4 (DDS Brain routing),
  SC-P4-03-5 (Muscle routing) — depend on infrastructure not yet present.

---

## Build Result

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

## Test Result

```
Passed!  - Failed: 0, Passed: 18, Skipped: 0, Total: 18
```
