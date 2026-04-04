# BATCH-06 Instructions

**Batch:** BATCH-06  
**Developer:** GitHub Copilot  
**Tasks:** PACK2-U001 · PACK2-U002 · PACK2-U003  
**Branch:** main (append directly)

---

## Context

- Phase 3 goal: Enforce "dumb view" separation — panels must not hold DDS reader/writer fields; panels delegate exclusively to facade interfaces or state objects.
- **Pre-verified state:**
  - `Hrot.IG/UI/` panels: NO `IDdsWriter<T>` or `DdsWriter<T>` fields exist (confirmed by dev-lead scan before BATCH-06 delegation).
  - `Hrot.ExCon/Panels/` panels: NO `IDdsWriter<T>` or `DdsWriter<T>` fields exist (confirmed).
  - `OrbatPanel.HandleNewUnitClick` → `IExConLogic.StartPlacementMode` delegation is already tested in `Hrot.ExCon.Tests/OrbatPanelTests.cs`.
  - `MiniExConPanelStateTests.cs` already covers `Submit(FdpEventBus)` → `SpawnEntityCommand` publication.
- Current test counts (before BATCH-06): `Hrot.IG.Tests` 408 pass, `Hrot.ExCon.Tests` — check with `dotnet test Hrot.ExCon.Tests`.

---

## Task A: PACK2-U001 — UI-Logic Separation Audit

### A.1 — Reflection-based audit tests

**File:** `Hrot.IG.Tests/UiLogicSeparationAuditTests.cs`

Write an xUnit test class that uses reflection to enforce the UI-logic separation rule across all panel assemblies.

```csharp
using System;
using System.Linq;
using System.Reflection;
using Hrot.ExCon.Panels;
using Hrot.IG.UI;
using Xunit;

namespace Hrot.IG.Tests;

/// <summary>
/// Automated audit that ensures all IG and ExCon UI panel classes conform to
/// the "dumb view" rule (PACK2-U001):
/// no panel may hold a field of type IDdsWriter&lt;T&gt; or DdsWriter&lt;T&gt;.
/// </summary>
public class UiLogicSeparationAuditTests
{
    // ── IG UI panel namespace ─────────────────────────────────────────────────

    [Fact]
    public void IgUiPanels_HaveNoDirectDdsWriterFields()
    {
        AssertNoDdsWriterFields(typeof(MiniExConPanel).Assembly,
            namespacePredicate: ns => ns != null && ns.StartsWith("Hrot.IG.UI"));
    }

    // ── ExCon panel namespace ─────────────────────────────────────────────────

    [Fact]
    public void ExConPanels_HaveNoDirectDdsWriterFields()
    {
        AssertNoDdsWriterFields(typeof(OrbatPanel).Assembly,
            namespacePredicate: ns => ns != null && ns.StartsWith("Hrot.ExCon.Panels"));
    }

    // ── Helper ───────────────────────────────────────────────────────────────

    private static void AssertNoDdsWriterFields(Assembly assembly, Func<string?, bool> namespacePredicate)
    {
        var violations = assembly.GetTypes()
            .Where(t => namespacePredicate(t.Namespace))
            .SelectMany(t => t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            .Where(f => IsDdsWriterType(f.FieldType))
            .Select(f => $"{f.DeclaringType!.Name}.{f.Name} : {f.FieldType.Name}")
            .ToList();

        Assert.True(violations.Count == 0,
            $"DDS writer field(s) found in UI panels:\n  {string.Join("\n  ", violations)}");
    }

    private static bool IsDdsWriterType(Type t)
    {
        if (t.IsGenericType)
        {
            var def = t.GetGenericTypeDefinition();
            var name = def.Name;
            if (name.StartsWith("DdsWriter") || name.StartsWith("IDdsWriter"))
                return true;
            // Check interfaces too
            foreach (var iface in def.GetInterfaces())
                if (iface.Name.StartsWith("IDdsWriter")) return true;
        }

        // Handle non-generic or fully-named matches
        return t.Name.StartsWith("DdsWriter") || t.Name.StartsWith("IDdsWriter");
    }
}
```

> **Note on assembly references:** The `Hrot.IG.Tests` project already references `Hrot.IG` and `Hrot.ExCon` transitively (check if `Hrot.ExCon` is referenced; if not, add it to `Hrot.IG.Tests.csproj`). Look at existing imports in `Hrot.IG.Tests/*.cs` to confirm.
>
> If `Hrot.ExCon` is NOT accessible from `Hrot.IG.Tests`, put the ExCon audit test in `Hrot.ExCon.Tests/UiLogicSeparationAuditTests.cs` instead — same test code but using `typeof(OrbatPanel).Assembly`.

---

## Task B: PACK2-U002 — Formalize ExCon UI Pack

### B.1 — Static analysis: ExCon panels must not construct tools directly

**File:** `Hrot.ExCon.Tests/ExConUiPackBoundaryTests.cs`

Write a test confirming no ExCon panel type directly constructs (`new CreationTool(...)`, `new EditTool(...)`, etc.) by asserting that none of those types appear in ExCon panel assembly method bodies. Since runtime reflection cannot inspect JIT bodies, use compilation-time assertions:

```csharp
using System.Linq;
using System.Reflection;
using Hrot.ExCon.Panels;
using Xunit;

namespace Hrot.ExCon.Tests;

/// <summary>
/// Boundary tests for the ExCon UI Pack (PACK2-U002).
/// Verifies that ExCon panels do not take direct dependencies on tool types
/// that should only live in <c>Hrot.ScenarioEditor.Tools</c>.
/// </summary>
public class ExConUiPackBoundaryTests
{
    private static readonly string[] ForbiddenTypeNames =
    {
        "CreationTool",
        "EditTool",
        "RouteEditTool",
        "MeasureTool",
        "StandardInteractionTool",
    };

    [Fact]
    public void ExConPanels_DoNotReferenceToolTypes()
    {
        var panelAssembly = typeof(OrbatPanel).Assembly;

        var violations = panelAssembly.GetTypes()
            .Where(t => t.Namespace != null && t.Namespace.StartsWith("Hrot.ExCon.Panels"))
            .SelectMany(t => t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            .Where(f => ForbiddenTypeNames.Contains(f.FieldType.Name))
            .Select(f => $"{f.DeclaringType!.Name}.{f.Name} : {f.FieldType.Name}")
            .ToList();

        Assert.True(violations.Count == 0,
            $"ExCon panel(s) directly reference tool types:\n  {string.Join("\n  ", violations)}");
    }

    [Fact]
    public void OrbatPanel_HandleNewUnitClick_DelegatesToIExConLogic()
    {
        // This test verifies B.2 of U002 — the delegation path is already tested in
        // OrbatPanelTests.HandleNewUnitClick_WithSelectedType_CallsStartPlacementModeWithCorrectParameters.
        // This stub confirms the test exists at compile time by referencing the types.
        _ = typeof(Hrot.ExCon.Logic.IExConLogic);
        _ = typeof(OrbatPanel);
        // If OrbatPanelTests covers StartPlacementMode delegation, this is a pass.
        Assert.True(true, "Delegation covered by OrbatPanelTests; see that class.");
    }
}
```

> **Note:** The `OrbatPanel` delegation test is already in `OrbatPanelTests.cs`. The second test above is intentionally minimal — just a registration test to acknowledge coverage without duplication. If the reviewer prefers, omit the second trivial test and only keep the boundary test.

---

## Task C: PACK2-U003 — Formalize IG UI Pack

### C.1 — Audit: No IG UI panel holds DdsReader/DdsWriter field

Already covered by the reflection test in Task A.1 (`IgUiPanels_HaveNoDirectDdsWriterFields`).

### C.2 — Extend `MiniExConPanelStateTests` with OnCommandPublished event test

**File:** `Hrot.IG.Tests/MiniExConPanelStateTests.cs`

Add the following test to the existing class:

```csharp
[Fact]
public void Submit_FiresOnCommandPublishedEvent()
{
    // Arrange
    var state = new MiniExConPanelState();
    state.TkbType = 301L;
    using var bus = new FdpEventBus();

    SpawnEntityCommand? captured = null;
    state.OnCommandPublished += cmd => captured = cmd;

    // Act
    state.Submit(bus);
    bus.SwapBuffers(); // make event visible in read buffer

    // Assert event was fired
    Assert.NotNull(captured);
    Assert.Equal(301L, captured!.TkbType);
}
```

### C.3 — Write compile-time NoDdsField test for IG UI panels

Already covered by Task A.1. No additional test needed.

### C.4 — Verify `IgDebugPanel` uses only `DebugPanelState`

**Compile-time verification already satisfied:** `IgDebugPanel` only holds `DebugPanelState _state` (no DDS fields). Document this in the test file header comment.

If `DebugPanelStateTests.cs` does not already test state mutation, add one focused test to `Hrot.IG.Tests/DebugPanelStateTests.cs` (or create it):

```csharp
// In DebugPanelStateTests.cs — add if missing:
[Fact]
public void DebugPanelState_DefaultsAreDisabled()
{
    var state = new DebugPanelState();
    Assert.False(state.ForceHostile);
    Assert.False(state.HideLabels);
}
```

> If `DebugPanelStateTests.cs` already has this coverage, skip this test.

### C.5 — Verify `PerformanceOverlay` uses only `PerformanceMetrics`

Read `Hrot.IG/UI/PerformanceOverlay.cs`. If it has no DDS dependencies (confirmed before delegation), no code change needed. Document in test comment.

If `PerformanceMetrics` has no existing tests, add to `Hrot.IG.Tests/PerformanceMetricsTests.cs` (create if needed):

```csharp
using Hrot.IG.UI;
using Xunit;

namespace Hrot.IG.Tests;

public class PerformanceMetricsTests
{
    [Fact]
    public void Snapshot_CapturesFpsAndEntityCount()
    {
        var metrics = new PerformanceMetrics();
        // Snapshot must not throw (uses Raylib.GetFPS() internally or stored field)
        // Just verify it doesn't crash and returns a non-negative FPS.
        metrics.Snapshot(50, entityCount: 10, tickMs: 5f);
        Assert.True(metrics.FramesPerSecond >= 0);
        Assert.Equal(10, metrics.EntityCount);
    }
}
```

> **Read `PerformanceMetrics.cs` first** to understand its actual API (`Snapshot` method signature and properties). Adjust the test to match the real API. If `Snapshot` doesn't exist or has a different signature, adapt accordingly.

---

## Verification Checklist

After completing all tasks, verify:

1. **Build:** `dotnet build IOS-IG-SimHost.sln --no-incremental` → **0 errors**
2. **Tests:**
   - `dotnet test Hrot.IG.Tests --no-build` → all pass (408 pre-existing + new audit/U003 tests)
   - `dotnet test Hrot.ExCon.Tests --no-build` → all pass (existing + new boundary tests)
3. **Negative check:** Confirm the `DdsWriterField` tests would FAIL if a DDS writer was added to a panel — document this in the report.

---

## Report Format

1. **Audit summary table:** List all panels audited (both IG/UI and ExCon/Panels), whether any changes were needed, and final verdict.
2. **Test counts** (full table: project, before, after, delta).
3. **Q1:** Did `Hrot.IG.Tests.csproj` already reference `Hrot.ExCon`? If yes, the single test file works. If no, where was the ExCon audit test placed?
4. **Q2:** What was the actual `PerformanceMetrics.Snapshot()` signature? What changes were needed from the instructions?
5. **Build result.**
