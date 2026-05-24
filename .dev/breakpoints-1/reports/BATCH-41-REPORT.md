# BATCH-41 Report

**Workstream:** breakpoints-1
**Batch:** BATCH-41
**Task:** UBP-P4T2 -- StructEdit commit interception
**Status:** COMPLETED

---

## Summary

Implemented the `IMutationInterceptor` interface in `Fdp.Toolkits`, wired it through
`ComponentEditWindow` and `ComponentReflector`, and had `DataBreakpointManager` implement it.
When the sim is paused and an operator commits a component edit, the commit now routes to
`StageMutation` rather than writing directly to the repo.

---

## Files Created

### `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/IMutationInterceptor.cs` (NEW)

```csharp
using System;
using Fdp.Core;

namespace Fdp.Toolkit.Diagnostics.Gizmos;

/// <summary>
/// Allows an external pause manager to intercept component edits that would
/// otherwise be applied immediately.
/// When <see cref="IsPaused"/> is true, <see cref="StageMutation"/> is called
/// instead of the direct repo write.
/// </summary>
public interface IMutationInterceptor
{
    /// <summary>True while the simulation is paused by the breakpoint manager.</summary>
    bool IsPaused { get; }

    /// <summary>
    /// Stages a component mutation to be applied at the next step/continue boundary.
    /// </summary>
    void StageMutation(Entity entity, Type componentType, object componentValue);
}
```

---

## Files Modified

### `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointManager.cs`

- Added `IMutationInterceptor` to the class declaration (after `IActiveViewProvider`):
  ```csharp
  public sealed class DataBreakpointManager : IDataBreakpointManager, IActiveViewProvider, IMutationInterceptor
  ```
- `using Fdp.Toolkit.Diagnostics.Gizmos;` was already present.
- No new methods needed: `IsPaused` and `StageMutation` were already public members
  with matching signatures.

### `FDP/Engine/Fdp.Presentation/ImGui/Editing/ComponentEditWindow.cs`

- Added `using Fdp.Toolkit.Diagnostics.Gizmos;`
- Added field: `private readonly IMutationInterceptor? _interceptor;`
- Added optional `interceptor` param (last positional) to constructor, assigned to field
- Updated `ExecuteOkLogic()`:

```csharp
internal void ExecuteOkLogic()
{
    try
    {
        object newState = _session.Commit();

        if (_interceptor != null && _interceptor.IsPaused)
        {
            _interceptor.StageMutation(_targetEntity, _componentType, newState);
            CloseAndCleanup();
            return;
        }

        var ls = _sessionGetter();
        if (ls != null && ls.IsAlive(_targetEntity))
            ls.SetComponent(_targetEntity, _componentType, newState);
        CloseAndCleanup();
    }
    catch (EditValidationException ex)
    {
        _errorMessage = ex.Result.Errors.Count > 0
            ? ex.Result.Errors[0].Message
            : "Validation failed.";
        // Do NOT close on validation failure so the user can correct the value.
    }
}
```

### `FDP/Engine/Fdp.Presentation/ImGui/Utils/ComponentReflector.cs`

- Added `using Fdp.Toolkit.Diagnostics.Gizmos;`
- Added disambiguation aliases to resolve `FixedString32`/`FixedString64` ambiguity with
  the GizmoMap.Contracts external dependency:
  ```csharp
  using FixedString32 = Fdp.Core.FixedString32;
  using FixedString64 = Fdp.Core.FixedString64;
  ```
- Added property near other edit props (`EditPickerContext`):
  ```csharp
  /// <summary>
  /// Optional interceptor; when set and IsPaused, commits route to StageMutation.
  /// </summary>
  public IMutationInterceptor? MutationInterceptor { get; set; }
  ```
- Updated `TryOpenEditWindow` to pass `interceptor: MutationInterceptor` to the
  `ComponentEditWindow` constructor.

### `FDP/Engine/Fdp.Presentation.Tests/ImGui/Editing/ComponentEditWindowTests.cs`

- Added `using Fdp.Toolkit.Diagnostics.Gizmos;`
- Added `file sealed class MockMutationInterceptor : IMutationInterceptor`
- Added tests T-CE08h and T-CE08i (see below)

### `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/PendingMutationTests.cs`

- Added `using Fdp.Toolkit.Diagnostics.Gizmos;`
- Added two interceptor interface tests (see below)

---

## New Tests

### `Fdp.Presentation.Tests` -- `ComponentEditWindowTests`

| Test | Status |
|------|--------|
| `T_CE08h_WhilePaused_RoutesToStageMutation` | PASSED |
| `T_CE08i_WhileRunning_StillWritesDirect` | PASSED |

Total: 12/12 `ComponentEditWindowTests` passed.

Note: The full `Fdp.Presentation.Tests` suite includes Vis2D tests that require a
display/rendering context and hang in a headless environment. This is pre-existing
behavior unrelated to BATCH-41. The 205 tests in the `Fdp.Presentation.Tests`
namespace (excluding Vis2D) ran: **202 passed, 3 failed** -- the 3 failures are
pre-existing (in `EntityInspectorPanelTests`) and were present before this batch.

### `Hrot.Diagnostics.Breakpoints.Tests` -- `PendingMutationTests`

| Test | Status |
|------|--------|
| `Manager_CastToIMutationInterceptor_StagesToQueue_WhenPaused` | PASSED |
| `Manager_CastToIMutationInterceptor_IsPaused_FalseWhenRunning` | PASSED |

Total: **47/47** passed (45 pre-existing + 2 new).

---

## Build Result

```
dotnet build IOS-IG-SimHost.sln -c Debug
Build succeeded.
    0 Error(s)
```

Pre-existing CS0618 warnings in `Hrot.Blueprints.Tests` and `DataBreakpointManagerTests.cs`
(5 total, about `IBlueprintTimeController`) were present before this batch and were not
suppressed.

---

## Issues Encountered and Solutions

1. **CS0104 ambiguous `FixedString32`** in `ComponentReflector.cs`: Adding
   `using Fdp.Toolkit.Diagnostics.Gizmos;` introduced an ambiguity with
   `Fdp.Toolkit.Diagnostics.Gizmos.FixedString32` (from the GizmoMap.Contracts ExtDep)
   vs `Fdp.Core.FixedString32`. Resolved by adding explicit alias directives:
   `using FixedString32 = Fdp.Core.FixedString32;` and
   `using FixedString64 = Fdp.Core.FixedString64;`.

2. **xUnit v2 `Assert.Equal` with message**: The instruction template used
   `Assert.Equal(0, count, "message")` which is not supported in xUnit v2 (the third
   argument is `Func<T,T,bool>` not `string`). Replaced with `Assert.True(count == 0, "message")`.
