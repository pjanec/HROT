# BATCH-41 Instructions

**Workstream:** breakpoints-1
**Batch:** BATCH-41
**Previous batch:** BATCH-40 (APPROVED and committed)
**Responsible:** Developer

---

## Context

Read the following documents before writing any code:

- `.dev/breakpoints-1/DESIGN.md` — focus on §4.1 P4T2
- `.dev/breakpoints-1/TASK-DETAIL.md` — focus on `UBP-P4T2` section
- `.dev/breakpoints-1/TASK-TRACKER.md`
- `AGENTS.md` — editing invariants (non-negotiable)

---

## Task: UBP-P4T2 — StructEdit commit interception

**Design reference:** DESIGN.md §4.1

**Goal:** When an operator edits a component via the StructEdit window while the sim is paused,
the commit must route to `DataBreakpointManager.StageMutation` rather than writing directly to
the repo. This requires a new interface in `Fdp.Toolkits` (to avoid circular dependencies),
implementation on `DataBreakpointManager`, and wiring in `ComponentEditWindow`.

---

## Step 1 — Add `IMutationInterceptor` to `Fdp.Toolkits`

**File:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/IMutationInterceptor.cs`

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

## Step 2 — `DataBreakpointManager` implements `IMutationInterceptor`

**File:** `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointManager.cs`

1. Add `IMutationInterceptor` to the class declaration (after `IActiveViewProvider`):

```csharp
public sealed class DataBreakpointManager : IDataBreakpointManager, IActiveViewProvider, IMutationInterceptor
```

2. No new methods needed: `IsPaused` and `StageMutation` are already public members
   with matching signatures. The interface is satisfied by the existing implementation.

3. Add `using Fdp.Toolkit.Diagnostics.Gizmos;` if not already present.

---

## Step 3 — `ComponentEditWindow` accepts optional interceptor

**File:** `FDP/Engine/Fdp.Presentation/ImGui/Editing/ComponentEditWindow.cs`

**A. Add field:**
```csharp
private readonly IMutationInterceptor? _interceptor;
```

**B. Add `interceptor` parameter to the constructor** (at the END, as the last optional param,
after `customDrawers`):
```csharp
internal ComponentEditWindow(
    string id,
    string title,
    string owningPerspective,
    IEditSession session,
    Entity targetEntity,
    Type componentType,
    Func<IInspectableSession?> sessionGetter,
    IComponentPickerContext? pickerCtx = null,
    IReadOnlyDictionary<Type, IImGuiFieldDrawer>? customDrawers = null,
    IMutationInterceptor? interceptor = null)
    : base(id, title, owningPerspective, WindowScope.PerspectiveBound)
{
    _session       = session;
    _targetEntity  = targetEntity;
    _componentType = componentType;
    _sessionGetter = sessionGetter;
    _interceptor   = interceptor;
    _drawer        = new ComponentEditDrawer(session, pickerCtx, customDrawers);

    IsVolatile = true;
    ShowInMenu = false;
    IsOpen     = true;
}
```

**C. Modify `ExecuteOkLogic`** to route through the interceptor when paused:

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

**D. Add `using Fdp.Toolkit.Diagnostics.Gizmos;`** at the top of the file if not already present.

---

## Step 4 — `ComponentReflector` exposes the interceptor

**File:** `FDP/Engine/Fdp.Presentation/ImGui/Utils/ComponentReflector.cs`

**A. Add property** (near the other optional edit props like `EditPickerContext`):
```csharp
/// <summary>
/// Optional interceptor; when set and IsPaused, commits route to StageMutation.
/// </summary>
public IMutationInterceptor? MutationInterceptor { get; set; }
```

**B. Pass it when constructing `ComponentEditWindow`** in `TryOpenEditWindow`:
```csharp
EditWindowManager.RegisterWindow(new ComponentEditWindow(
    winId, title, EditOwningPerspective, editSession,
    e, type, EditSessionGetter!, EditPickerContext, _fieldDrawers,
    interceptor: MutationInterceptor));
```

(The existing line only passes up to `_fieldDrawers`; add the `interceptor:` named argument.)

**C. Add `using Fdp.Toolkit.Diagnostics.Gizmos;`** if not already present.

---

## Step 5 — Tests in `Fdp.Presentation.Tests`

**File:** `FDP/Engine/Fdp.Presentation.Tests/ImGui/Editing/ComponentEditWindowTests.cs`

**Add a `MockMutationInterceptor` helper** as a file-scoped class (near `FakeEditSession` and
`FakeInspectableSession` at the top of the file):

```csharp
file sealed class MockMutationInterceptor : IMutationInterceptor
{
    public bool IsPaused { get; set; }
    public List<(Entity Entity, Type ComponentType, object Value)> Staged { get; } = new();
    public void StageMutation(Entity entity, Type componentType, object componentValue)
        => Staged.Add((entity, componentType, componentValue));
}
```

**Add `using Fdp.Toolkit.Diagnostics.Gizmos;`** to the top of the test file.

**Add two tests to `ComponentEditWindowTests`:**

```csharp
// T-CE08h: Interceptor present and paused -- routes to StageMutation, not SetComponent.
[Fact]
public void T_CE08h_WhilePaused_RoutesToStageMutation()
{
    var interceptor = new MockMutationInterceptor { IsPaused = true };
    var committed   = new object();
    var session     = new FakeEditSession { CommitResult = committed };
    var inspectable = new FakeInspectableSession(isAlive: true);
    var entity      = new Entity(11, 1);

    var win = new ComponentEditWindow(
        "id_h", "Title", "Perspective",
        session, entity, typeof(object),
        () => inspectable,
        interceptor: interceptor);

    win.ExecuteOkLogic();

    Assert.Equal(1, interceptor.Staged.Count);
    Assert.Equal(entity, interceptor.Staged[0].Entity);
    Assert.Same(committed, interceptor.Staged[0].Value);
    Assert.False(inspectable.SetComponentWasCalled,
        "Direct repo write must not occur when interceptor is paused.");
    Assert.False(win.IsOpen,
        "Window must close after staging.");
}

// T-CE08i: Interceptor present but NOT paused -- falls through to direct write.
[Fact]
public void T_CE08i_WhileRunning_StillWritesDirect()
{
    var interceptor = new MockMutationInterceptor { IsPaused = false };
    var committed   = new object();
    var session     = new FakeEditSession { CommitResult = committed };
    var inspectable = new FakeInspectableSession(isAlive: true);
    var entity      = new Entity(12, 1);

    var win = new ComponentEditWindow(
        "id_i", "Title", "Perspective",
        session, entity, typeof(object),
        () => inspectable,
        interceptor: interceptor);

    win.ExecuteOkLogic();

    Assert.Equal(0, interceptor.Staged.Count,
        "StageMutation must not be called when running.");
    Assert.True(inspectable.SetComponentWasCalled,
        "Direct repo write must occur when interceptor is not paused.");
    Assert.False(win.IsOpen);
}
```

---

## Step 6 — Tests in `Hrot.Diagnostics.Breakpoints.Tests`

**File:** `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/PendingMutationTests.cs`

Add two tests to the existing `PendingMutationTests` class:

**`Manager_CastToIMutationInterceptor_StagesToQueue_WhenPaused`:**

```csharp
[Fact]
public void Manager_CastToIMutationInterceptor_StagesToQueue_WhenPaused()
{
    ComponentTypeRegistry.Clear();
    var (manager, liveRepo, _, _) = ManagerFactory.Create();
    liveRepo.RegisterComponent<TestHealth>();

    // Pause by triggering a hit.
    var entity = liveRepo.CreateEntity();
    liveRepo.AddComponent(entity, new TestHealth { Current = 0 });
    var preTick = manager.PreTickSnapshot;
    preTick.SyncFrom(liveRepo);

    var bpId = manager.Add(new Breakpoint
    {
        Id = BreakpointId.Invalid, Enabled = true,
        OccurrenceThreshold = 1, DisplayName = "iface"
    });
    var bp = manager.AllBreakpoints.First(b => b.Id == bpId);
    manager.OnHit(bp, entity);

    // Use via interface.
    IMutationInterceptor interceptor = manager;
    Assert.True(interceptor.IsPaused);
    interceptor.StageMutation(entity, typeof(TestHealth), new TestHealth { Current = 77 });

    Assert.Equal(1, manager.PendingMutationsCount);
    // Live repo is rewound; original value unchanged by staging.
    Assert.Equal(0, liveRepo.GetComponent<TestHealth>(entity).Current);
}
```

**`Manager_CastToIMutationInterceptor_IsPaused_FalseWhenRunning`:**

```csharp
[Fact]
public void Manager_CastToIMutationInterceptor_IsPaused_FalseWhenRunning()
{
    ComponentTypeRegistry.Clear();
    var (manager, _, _, _) = ManagerFactory.Create();

    IMutationInterceptor interceptor = manager;
    Assert.False(interceptor.IsPaused);
}
```

---

## File Checklist

**New files to create:**
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/IMutationInterceptor.cs`

**Existing files to modify:**
- `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointManager.cs`
  — Add `IMutationInterceptor` to class declaration
  — Add `using Fdp.Toolkit.Diagnostics.Gizmos;` if not present
- `FDP/Engine/Fdp.Presentation/ImGui/Editing/ComponentEditWindow.cs`
  — Add `_interceptor` field
  — Add `interceptor` param to ctor (last, optional)
  — Update `ExecuteOkLogic` with intercept check
  — Add `using Fdp.Toolkit.Diagnostics.Gizmos;`
- `FDP/Engine/Fdp.Presentation/ImGui/Utils/ComponentReflector.cs`
  — Add `MutationInterceptor` property
  — Pass `interceptor: MutationInterceptor` to `ComponentEditWindow` ctor call
  — Add `using Fdp.Toolkit.Diagnostics.Gizmos;`
- `FDP/Engine/Fdp.Presentation.Tests/ImGui/Editing/ComponentEditWindowTests.cs`
  — Add `file sealed class MockMutationInterceptor`
  — Add `using Fdp.Toolkit.Diagnostics.Gizmos;`
  — Add T-CE08h and T-CE08i test methods
- `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/PendingMutationTests.cs`
  — Add `using Fdp.Toolkit.Diagnostics.Gizmos;`
  — Add two new test methods

---

## Build and Test Requirements

1. Run: `dotnet build IOS-IG-SimHost.sln -c Debug` — must complete with 0 errors, 0 warnings
   from BATCH-41 files. (Pre-existing 5 CS0618 warnings in Hrot.Blueprints.Tests and
   DataBreakpointManagerTests.cs are acceptable — do not suppress them with new NoWarn entries.)
2. Run: `dotnet test FDP/Engine/Fdp.Presentation.Tests/Fdp.Presentation.Tests.csproj`
   — must pass ALL existing tests + 2 new (T-CE08h, T-CE08i)
3. Run: `dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/...`
   — must pass ALL 45 existing tests + 2 new = 47 minimum

---

## Report

Write the report to: `.dev/breakpoints-1/reports/BATCH-41-REPORT.md`

The report must include:
- List of all files modified/created
- Full list of new test names and pass/fail status
- Build result (0 errors, 0 new warnings from BATCH-41 files)
- The exact final `ExecuteOkLogic()` method body
- The exact `IMutationInterceptor` interface definition
- Any issues encountered and solutions

---

## Key Rules
- Do NOT redeclare `TestHealth`, `EntityLabel`, etc. from other test files in the same project
- `[Collection("ComponentRegistry")]` is already on `PendingMutationTests` — new test methods
  inherit the collection marker automatically (they are in the same class)
- Do NOT use Unicode characters in new comments or string literals
- `TreatWarningsAsErrors` is active — fix every warning
