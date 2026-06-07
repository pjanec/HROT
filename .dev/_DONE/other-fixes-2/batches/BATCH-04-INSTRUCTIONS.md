# BATCH-04: BTree Debug Overlay Wiring & Async Badge Rendering

**Batch Number:** BATCH-04  
**Tasks:** FIX2-007, FIX2-013  
**Priority:** HIGH / MEDIUM  
**Dependencies:** None from previous batches (BTree area is independent)

---

## Mandatory Workflow

**Read AGENTS.md at the repo root before writing a single line of code.**

Complete tasks in strict sequence. For each task:
1. Define the **success condition** BEFORE touching any code.
2. Implement the fix.
3. Write / update tests that drive the **production path**.
4. Run the relevant test project and confirm all tests pass.
5. Fix any failures before moving to the next task.

Do NOT ask for permission at any step. Do NOT stop early. Finish both tasks, make all tests green, then write the report.

---

## Onboarding & Workflow

### Required Reading (in order)
1. **Task details:** `.dev/other-fixes-2/TASK-DETAIL.md` -- sections FIX2-007, FIX2-013
2. **Source finding BPF-026:** `.dev/blueprint-fixes-1/TASK-DETAIL.md` -- BPF-026
3. **Source finding BPF-045:** `.dev/blueprint-fixes-1/TASK-DETAIL.md` -- BPF-045
4. **BTree editor design doc (§12.4):** search for `Blueprint_Subsystem_Editor_Detailed_Design.md` -- §12 / §12.4 for the BTree overlay rendering steps

### Source Code Areas
- **BTree debug session:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BTree/BTreeDebugSession.cs`
- **BTree asset contributor:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BTree/BTreeAssetContributor.cs`
- **BTree overlay renderer:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BTree/BTreeRuntimeOverlayRenderer.cs`
- **Test project:** `Hrot/Subsystems/AI/Hrot.BTree.Editor/` or `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/`  
  (search for where BTree editor tests live -- `file_search "BTreeDebugSession*Tests*"` or similar)

### Build & Test
```
cd d:\WORK\IOS-IG-SimHost-FDP
dotnet build Hrot\Subsystems\Blueprints\Hrot.Blueprints.Tests\Hrot.Blueprints.Tests.csproj --nologo -v q
dotnet test Hrot\Subsystems\Blueprints\Hrot.Blueprints.Tests\Hrot.Blueprints.Tests.csproj --filter "FullyQualifiedName!~AllocationFree" --nologo
```

Also build and test BTree editor project if it exists separately:
```
dotnet build Hrot\Subsystems\AI\Hrot.BTree.Editor\Hrot.BTree.Editor.csproj --nologo -v q
```

### Report Submission
Submit report to: `.dev/other-fixes-2/reports/BATCH-04-REPORT.md`

---

## Context

FIX2-007: `BTreeDebugSession.SetDebugMetadata()` was implemented correctly, but it has zero production callers. `BTreeAssetContributor` passes the asset's `DebugMetadata` to the projector but NOT to the debug session. So at runtime the session's metadata is null, `RunningElementId` stays null, and the overlay renders nothing.

FIX2-013: `BTreeDebugSession.Update()` symbolicates trace records' `NodeVisualId` correctly now, but `BTreeRuntimeOverlayRenderer` only has 3 render sections (running node, stack ancestry, status glyphs). The design §12.4 step-4 async-pending clock-icon block (`GetRecentAsyncHistory` -> `DrawAsyncBadge`) is completely absent.

---

## Tasks

### Task 1 -- FIX2-007: Wire `SetDebugMetadata()` in the production asset-load path

**Full details:** `.dev/other-fixes-2/TASK-DETAIL.md#fix2-007`

**Success condition (define before coding):**
A test calls `SetDebugMetadata(blob.DebugMetadata, assetId)` via the production contributor path (not directly), then calls `Update(snap)`, and asserts that `session.RunningElementId` is non-null and matches the symbolicated visual ID for the running element index. If `SetDebugMetadata` is not called by the contributor, the test fails.

**What to fix:**
- In `BTreeAssetContributor.cs` (around line 53), after the asset's `DebugMetadata` is passed to the projector, also call `debugSession.SetDebugMetadata(asset.DebugMetadata, assetId)`.
- `BTreeAssetContributor` likely receives the debug session via DI or a parameter -- wire it in if not already present.

**Test required:**
- Test name: `BTreeDebugSession_Update_AfterContributorLoad_ReturnsNonNullRunningElementId` (or similar)
- Must: simulate the contributor loading an asset (call the contributor's load/register method), then call `Update(snap)` with a non-zero `RunningElementId` index, and assert `session.RunningElementId != null`.
- Must NOT: call `SetDebugMetadata()` directly from the test.

---

### Task 2 -- FIX2-013: Add async-badge overlay render path per §12.4 step 4

**Full details:** `.dev/other-fixes-2/TASK-DETAIL.md#fix2-013`

**Success condition (define before coding):**
A test calls `BTreeRuntimeOverlayRenderer.Render()` with a session that has recent async history entries (returned from `GetRecentAsyncHistory`), and asserts that the render output (via `LastRenderedAsyncBadges` or similar observable property) contains the expected async-pending node visual IDs. If you remove the async-badge section, the test fails.

**What to fix:**
- In `BTreeRuntimeOverlayRenderer.cs`, add step-4: after the existing 3 sections, call `session.GetRecentAsyncHistory()` and for each entry call `DrawAsyncBadge(entry.NodeVisualId)` (or the equivalent rendering call per the design §12.4).
- If `GetRecentAsyncHistory` doesn't exist on the debug session interface, add it (and implement in `BTreeDebugSession`).
- If `DrawAsyncBadge` doesn't exist on the renderer, add it.

**Test required:**
- Test name: `BTreeRuntimeOverlayRenderer_Render_DrawsAsyncBadges_ForPendingAsyncNodes` (or similar)
- Must: create a `BTreeDebugSession` with metadata and async history, call `Render()` on the overlay renderer, and assert the async badges are rendered (via an observable property on the renderer or a mock callback).
- Must NOT: call `DrawAsyncBadge` directly; must go through the `Render()` production path.

---

## Quality Standards

**PRODUCTION PATH:** Tests must go through the production contributor/renderer path. Direct calls to `SetDebugMetadata()` or `DrawAsyncBadge()` from tests do NOT count.

**ALL EXISTING TESTS (886) MUST STAY GREEN.**

---

## Developer Insights (Report Questions)

1. Where exactly does `BTreeAssetContributor` receive or resolve the debug session? What changes were needed to wire it in?
2. How did you approach the headless rendering assertion for the async badge (observable property vs. mock vs. command list)?
3. Did you find any additional gaps in the BTree overlay rendering pipeline?
4. **Suggested commit message** for this batch.
