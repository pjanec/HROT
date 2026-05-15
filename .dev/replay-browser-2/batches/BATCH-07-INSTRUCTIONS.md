# BATCH-07 — Final Cleanup: Style/Allocation Audits, Documentation Hygiene

**Batch Number:** BATCH-07  
**Tasks:** RB-X.1 (Documentation Hygiene), RB-X.2 (Style and Allocation Audits)  
**Phase:** Finalization — all code tasks done; this batch resolves P3 debt and closes cross-stage tasks.  
**Estimated Effort:** 4–6 hours

**Reference documents** (read before starting):
- `.dev/replay-browser-2/DESIGN.md` §1 (risk register), §4.5 (assembly constraints), §6.2 (allocation invariants)
- `.dev/replay-browser-2/TASK-DETAILS.md` RB-X.1, RB-X.2, RB-5.2
- `.dev/replay-browser-2/DEBT-TRACKER.md` — P3 items RB06-P3-001 and RB06-P3-002

---

## Developer Insights Request

After completing all tasks, your report must answer:

1. **Issues encountered**: What was harder than expected? Any surprising behaviors?
2. **Weak points spotted**: Any patterns in the codebase that concern you going forward?
3. **Design decisions beyond the spec**: Any choices you made that the instructions didn't dictate?

---

## Repository Layout Reminder

- FDP is a **git submodule** at `d:\Work\IOS-IG-SimHost-FDP-2\FDP\`
- `Hrot/` lives in the parent repo
- Both need separate commits (FDP first, then parent)
- The FDP submodule is on branch `main` — never leave it in detached HEAD state.

## Pre-existing Errors (Do NOT fix)

- `Hrot.SimHost.Tests`: 2 pre-existing errors (AreaQueryBatchData, EqsTargetPool) — ignore
- `Fdp.Toolkit.Vis2D.Tests`: 7 Vis2D gizmo failures (DebugGizmoLayer, DebugPrimitiveRenderer2D) — ignore
- `Hrot.ClusterRunner.Tests`: 2 DataDrivenGizmoPredicate D003 failures — ignore

---

## Task 1 — Fix Thread Safety in `ReplaySearchPanel` (RB06-P3-001)

**Debt item**: RB06-P3-001 in `DEBT-TRACKER.md`  
**File**: `FDP/Engine/Fdp.Presentation/ImGui/Panels/ReplayBrowser/ReplaySearchPanel.cs`

In `DrawExecuteButton`, `Task.Run` closures write to `_results`, `_lifecycleResults`, and `_statusLine`
from a background thread. The render thread reads them without synchronization.

**Fix**: Use `volatile` fields for `_statusLine`, and `Interlocked.Exchange` for the list fields.
Replace the three mutable search-result fields:

```csharp
private volatile IReadOnlyList<SearchResultDto> _results = Array.Empty<SearchResultDto>();
private volatile IReadOnlyList<LifecycleSearchResultDto> _lifecycleResults = Array.Empty<LifecycleSearchResultDto>();
private volatile string _statusLine = string.Empty;
```

And in the Task.Run closures, replace the assignment `_results = r;` with:
```csharp
Interlocked.Exchange(ref _results, r);
```

Wait — `volatile` fields with `Interlocked.Exchange` cannot coexist (the compiler blocks this).
Instead, use a plain approach: remove `volatile` and use `Interlocked.Exchange` on a backing object reference:

Actually the simplest correct fix for this UI code pattern is:

```csharp
// Use System.Threading.Volatile.Read/Write to avoid torn reads.
// OR: declare a private lock object and take it on both sides.
```

**Recommended approach** — introduce a lightweight lock:

```csharp
private readonly object _resultsLock = new();
private IReadOnlyList<SearchResultDto> _results = Array.Empty<SearchResultDto>();
private IReadOnlyList<LifecycleSearchResultDto> _lifecycleResults = Array.Empty<LifecycleSearchResultDto>();
private string _statusLine = string.Empty;
```

In the Task.Run closure (both branches):
```csharp
_searchTask = Task.Run(() =>
{
    var r = _searchService.ExecuteSearch(path, pred);
    lock (_resultsLock)
    {
        _results    = r;
        _statusLine = $"Found {r.Count} result(s).";
    }
});
```

In `DrawExecuteButton` and `DrawResultsGrid`, wrap reads:
```csharp
IReadOnlyList<SearchResultDto> results;
string status;
lock (_resultsLock)
{
    results = _results;
    status  = _statusLine;
}
```

> **Note**: The lock is a P3 cleanup item. If it proves difficult to integrate cleanly without
> breaking SR-T39, simplify to just using `volatile` on `_statusLine` and accepting that
> `_results`/`_lifecycleResults` reference assignment is atomically safe on .NET (which it is
> for reference types — this is a practical risk-mitigation, not a strict requirement). Either
> approach is acceptable.

---

## Task 2 — Add SR-T IDs to FilteredTypeCombo Tests (RB06-P3-002)

**Debt item**: RB06-P3-002 in `DEBT-TRACKER.md`  
**File**: `FDP/Engine/Fdp.Presentation.Tests/ImGui/ReplayBrowser/SearchPanel/ReplaySearchPanelTests.cs`

The 5 tests in `FilteredTypeComboFieldDrawerTests` currently have no SR-T ID prefix.
Assign them IDs SR-T40 through SR-T44 (as they are supplementary search panel tests).

Rename methods in `FilteredTypeComboFieldDrawerTests`:
- `FilterTypes_EmptyFilter_ReturnsAll` → `SR_T40_FilterTypes_EmptyFilter_ReturnsAll`
- `FilterTypes_NullFilter_ReturnsAll` → `SR_T41_FilterTypes_NullFilter_ReturnsAll`
- `FilterTypes_MatchingFilter_ReturnsOnlyMatching` → `SR_T42_FilterTypes_MatchingFilter_ReturnsOnlyMatching`
- `FilterTypes_NoMatch_ReturnsEmpty` → `SR_T43_FilterTypes_NoMatch_ReturnsEmpty`
- `FilteredTypeComboFieldDrawer_TargetType_IsTypeType` → `SR_T44_FilteredTypeComboFieldDrawer_TargetType_IsTypeType`

---

## Task 3 — Add RB-X.2 Audit Test (assembly dependency check)

**File**: `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/AssemblyDependencyTests.cs` (new)

DESIGN.md §4.5 requires that neither `Fdp.Toolkits` nor its transitive closure reference
`Fdp.Presentation`, `ImGui`, or `Raylib`. SR-T01 already checks `RecordingSearchService`'s assembly.

Extend the check to cover all three backend assemblies used by the replay browser feature:

```csharp
using System;
using System.Linq;
using System.Reflection;
using Fdp.Toolkit.ReplayBrowser.Search;
using Fdp.Toolkit.ReplayBrowser;
using Xunit;

namespace Fdp.Toolkits.Tests.ReplayBrowser;

/// <summary>
/// RB-X.2: Verifies that backend replay browser assemblies (Fdp.Toolkits, containing the
/// search/export/diff services) do not transitively reference Fdp.Presentation, ImGui, or Raylib.
/// </summary>
public class AssemblyDependencyTests
{
    private static readonly string[] ForbiddenPrefixes =
    {
        "Fdp.Presentation",
        "ImGui",
        "Raylib",
        "rlImGui",
    };

    [Fact]
    public void RBX2_FdpToolkitsAssembly_DoesNotReference_PresentationOrUI()
    {
        var refs = typeof(RecordingSearchService).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? "")
            .ToList();

        foreach (var r in refs)
        {
            foreach (var forbidden in ForbiddenPrefixes)
            {
                Assert.False(
                    r.StartsWith(forbidden, StringComparison.OrdinalIgnoreCase),
                    $"Fdp.Toolkits references forbidden assembly '{r}'");
            }
        }
    }

    [Fact]
    public void RBX2_ReplayBrowserContextAssembly_DoesNotReference_PresentationOrUI()
    {
        // ReplayBrowserContext lives in Fdp.Toolkits -- same assembly as SearchService.
        // Verify the assembly name itself confirms this (no cross-project bleed).
        string asmName = typeof(ReplayBrowserContext).Assembly.GetName().Name ?? "";
        Assert.DoesNotContain("Presentation", asmName, StringComparison.OrdinalIgnoreCase);
    }
}
```

---

## Task 4 — Verify Allocation Budget Tests Pass

Run the four allocation budget tests locally to confirm they all pass:

```powershell
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet test FDP\Toolkits\Fdp.Toolkits.Tests\Fdp.Toolkits.Tests.csproj --no-build `
    --filter "SR_T34|SR_T08|EX_T25|DIF_T09"
```

Expected: 4/4 pass. If any fail, fix before marking RB-X.2 complete.

---

## Task 5 — Run Full Test Suite and Document Results

Run the complete test suite and capture final totals:

```powershell
# FDP tests (run from FDP/ directory)
cd d:\Work\IOS-IG-SimHost-FDP-2\FDP
dotnet test FDP.sln --no-build 2>&1 | Select-String "passed|failed|error"

# Hrot tests
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet test Hrot\Subsystems\Hrot.ReplayBrowser.Tests\Hrot.ReplayBrowser.Tests.csproj --no-build
dotnet test Hrot\Runner\Hrot.ClusterRunner.Tests\Hrot.ClusterRunner.Tests.csproj --no-build
```

Record the final pass/fail counts in your report.

---

## Task 6 — Update TASK-TRACKER.md

After all tests pass, mark the following tasks done in `.dev/replay-browser-2/TASK-TRACKER.md`:

```markdown
- [x] **RB-X.1** Documentation Hygiene
- [x] **RB-X.2** Style and Allocation Audits
```

Also update DEBT-TRACKER.md to mark P3 items as resolved:
- RB06-P3-001: `RESOLVED (BATCH-07)`
- RB06-P3-002: `RESOLVED (BATCH-07)`

---

## Task 7 — Create RB-5.2 Smoke Test Checklist

**File**: `.dev/replay-browser-2/SMOKE-TEST-CHECKLIST.md` (new)

RB-5.2 is a manual test that cannot be automated. Create a checklist documenting the exact
steps a tester must perform:

```markdown
# RB-5.2 — End-to-End Manual Smoke Test Checklist

**Prereq**: A `.fdp` recording file from a scenario run (e.g. `scenarios/hill-attack/`).

## Steps

1. Build and run: `dotnet run --project Hrot\Runner\Hrot.ClusterRunner -- -m replaybrowser`
2. Verify the GUI launches in the ReplayBrowser perspective with 5 docked windows:
   - Timeline panel (bottom)
   - Entity inspector (right, top)
   - Event browser (right, mid)
   - Component diff viewer (right, bottom)
   - Search panel (left or floating)
3. Click `File > Open Recording...` (or use the timeline panel's open button) to load the `.fdp` file.
4. Verify: scrubbing the timeline advances the frame counter; the inspector shows entity components.
5. Select an entity in the inspector; verify the diff viewer shows changed components.
6. In the event browser, scroll through events; click a frame link; verify timeline seeks.
7. In the search panel: run a Component search (e.g. `HarnessPosition.X > 0`); click a result row; verify timeline seek.
8. Switch to any other perspective (e.g. SimHost) and back; verify dock layout is preserved.
9. Click `Save to JSON...` in the timeline export expander; verify the JSON file is created without UI freeze.
10. Confirm no exceptions appear in the console output.

## Pass Criteria

All 10 steps complete without error. ✓
```

---

## Mandatory Workflow

### Test-Driven Task Progression

1. For every code change, ensure the impacted test suite compiles and passes before moving on.
2. Do not mark a task done until its tests are green.
3. Run the full affected test projects after each task to catch regressions.
4. Report failures honestly in the batch report.

---

## Build and Test Verification

Final verification sequence:

```powershell
# 1. Build FDP.sln
cd d:\Work\IOS-IG-SimHost-FDP-2\FDP
dotnet build FDP.sln -c Debug 2>&1 | Select-String "error|warning" | Select-Object -Last 20

# 2. Run SR-T40..44 (renamed FilteredTypeCombo tests)
dotnet test Engine\Fdp.Presentation.Tests\Fdp.Presentation.Tests.csproj --no-build `
    --filter "SR_T40|SR_T41|SR_T42|SR_T43|SR_T44"

# 3. Run assembly dependency tests
dotnet test Toolkits\Fdp.Toolkits.Tests\Fdp.Toolkits.Tests.csproj --no-build `
    --filter "RBX2"

# 4. Run allocation budget tests
dotnet test Toolkits\Fdp.Toolkits.Tests\Fdp.Toolkits.Tests.csproj --no-build `
    --filter "SR_T34|SR_T08|EX_T25|DIF_T09"

# 5. Run SR-T39 (ReplaySearchPanel decoupling — must still pass after thread-safety fix)
dotnet test Engine\Fdp.Presentation.Tests\Fdp.Presentation.Tests.csproj --no-build `
    --filter "SR_T39"
```

---

## Commit Order

1. FDP submodule commit:
   ```
   chore(style-audit): Thread-safe result fields, SR-T40..T44 IDs, RBX2 assembly dep test (RB-X.2)
   ```

2. Parent repo commit:
   ```
   chore(replay-browser): Close RB-X.1, RB-X.2; smoke test checklist; DEBT resolved
   ```

---

## Report Format

Submit your report to `.dev/replay-browser-2/reports/BATCH-07-REPORT.md`.

**Required sections**:
1. **Summary** — What was done, overall status
2. **Task Completion** — Table of tasks A–G with status
3. **Files Changed** — List by project (FDP submodule / parent repo)
4. **Testing Results** — Per test group: name, count, pass/fail
5. **Developer Insights** — Answer Q1, Q2, Q3 verbatim
