# BATCH-17: Phase 5 Epilogue (ANC-P5-08a-08b PlayMontageChainNode Custom Drawer - Core Implementation)

**Batch Number:** BATCH-17  
**Tasks:** ANC-P5-08a, ANC-P5-08b  
**Phase:** Phase 5 - Blueprint authoring primitives (epilogue - editor drawer)  
**Estimated Effort:** 10-14 hours  
**Priority:** MEDIUM  
**Dependencies:** BATCH-15 approved (Phase 8 part 1); BATCH-10 approved (AiPrimitive infrastructure)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch implements the custom editor drawer for `PlayMontageChainNode` (the nine-primitive array authoring UI), enabling designers to add/remove/reorder montage chain entries with a dynamic UI instead of manually editing eight fixed reflection-generated slots.

**MANDATORY WORKFLOW:** Complete tasks 08a → 08b in sequence with passing tests between each:

1. **ANC-P5-08a:** Implement → Write tests → **ALL tests pass** ✅
2. **ANC-P5-08b:** Implement → Write tests → **ALL tests pass** ✅

**DO NOT** move to 08b until 08a tests pass. Do not stop and ask for permission to run tests, fix compilation errors, or complete obvious plumbing. Finish implementation and report when both tasks are complete.

### Required Reading (IN ORDER)

1. **Task Detail:** `.dev/anim-ctrl/TASK-DETAIL.md` — Addendum A section (ANC-P5-08a, 08b)
2. **Design Doc 5:** `.dev/anim-ctrl/DD-5_BlueprintPrimitives_v1_1.md` — §14.5 (drawer requirements)
3. **Previous Reviews:** 
   - `.dev/anim-ctrl/reviews/BATCH-10-REVIEW.md` (AiPrimitive infra confirmation)
   - `.dev/anim-ctrl/reviews/BATCH-15-REVIEW.md` (Phase 8 context)
4. **Exemplar Code:**
   - `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/NodeDrawers/IBlueprintNodeDrawer.cs` (drawer interface)
   - `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/NodeDrawers/INodeEditSession.cs` (session interface)
   - `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/NodeDrawers/WhenNodeDrawer.cs` (exemplar drawer + session)
   - `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/NodeDrawers/WhenNodeDrawerTests.cs` (exemplar tests)
   - `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintEditorBootstrap.cs` (registration point)
5. **Animation Node Definition:**
   - `Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Nodes/AnimationActionNodes.cs` — `PlayMontageChainNode` structure
6. **Query API:**
   - `Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Queries/IAnimationTkbQueries.cs` (GetPlayableMontages, GetMontage)
   - `Hrot/Toolkits/Fdp.Toolkits/Blueprints/Catalogs/AssetCatalog.cs` (current-class context)
7. **Stable ID Hashing:**
   - `Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Hashing/StableIdHasher.cs` (name→id resolution)

### Source Code Location

**Primary work area:**
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/NodeDrawers/` — Create `PlayMontageChainNodeDrawer.cs` and `PlayMontageChainNodeSession.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Tests/NodeDrawers/` — Create `PlayMontageChainNodeDrawerTests.cs`

**Updated registration:**
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintEditorBootstrap.cs` — Extend `CreateNodeDrawerRegistry`

**Node under edit:**
- `Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Nodes/AnimationActionNodes.cs` — `PlayMontageChainNode` struct

### Report Submission

Write report to:
`.dev/anim-ctrl/reports/BATCH-17-REPORT.md`

If blocked by architectural contradiction or missing dispatch-keying clarity, document in:
`.dev/anim-ctrl/questions/BATCH-17-QUESTIONS.md`

---

## Context

**Why now?** Phase 5 runtime implementation is complete (BATCH-10 approved all nine AiPrimitive nodes). The custom drawer was deferred as a lower-priority editor enhancement (D-15). With full feature delivery expected, this batch brings the editor authoring ergonomics in line with the runtime capability.

**Scope:** This batch covers **core drawer implementation + dynamic UI** (08a–08b). Validation feedback (08c) and wiring tests (08d) are in BATCH-17-CONTINUATION if needed.

**Related Tasks:**
- [ANC-P5-07](../TASK-DETAIL.md#anc-p5-07--aiprimitive-registration--cross-subsystem-reuse) - AiPrimitive registration (completed BATCH-10)
- [ANC-P5-08](../TASK-DETAIL.md#anc-p5-08--playmontagechainnode-custom-drawer-editor) - Custom drawer (this batch)
- DD-5 §14.5 (`PlayMontageChainNode` authoring drawer requirements)

---

## 🎯 Batch Objectives

- Implement `PlayMontageChainNodeDrawer : IBlueprintNodeDrawer` to recognize and handle `PlayMontageChainNode` in the Blueprint editor.
- Implement `PlayMontageChainNodeSession : INodeEditSession` to render a dynamic montage chain UI with add/remove/reorder controls.
- Verify drawer registration and session lifecycle through unit + wiring tests.
- Ensure developer can edit chain entries and `ChainCount` without manual reflection-slot management.

---

## ✅ Tasks

### Task 1: Drawer + Session Skeleton (ANC-P5-08a)

**Files:**
- NEW: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/NodeDrawers/PlayMontageChainNodeDrawer.cs`
- NEW: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/NodeDrawers/PlayMontageChainNodeSession.cs`
- UPDATED: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/NodeDrawers/IBlueprintNodeDrawer.cs` (if needed for clarification)

**Task Definition:** See [TASK-DETAIL.md — Addendum A / ANC-P5-08a](../TASK-DETAIL.md#anc-p5-08a--drawer--session-skeleton)

**Description:**

Create a drawer and session pair mirroring the `WhenNodeDrawer`/`WhenNodeSession` pattern. The drawer must:

1. Implement `IBlueprintNodeDrawer`
2. Inject `IAnimationTkbQueries`, `IEditService`, and current-class context provider
3. Implement `Handles(Node node)` to recognize the `PlayMontageChainNode` AiPrimitive node
   - **CRITICAL DECISION POINT:** Determine dispatch keying via confirmed **Route A** or **Route B** per Addendum A "Integration route (decision)" section
   - Route A: Node-level keying (inspect `AiPrimitiveDecl`/primitive ID on the hosted node)
   - Route B: Field-level via `[MontageChainPicker]` attribute on `ChainedMontages` field
   - Record your chosen route in a comment at the top of the drawer class
4. Implement `CreateSession(Node node, BlueprintAsset parentAsset)` returning the session

The session must:

1. Implement `INodeEditSession` (properties: `bool IsDirty { get; }`, methods: `void Draw()`, `void ResetDirty()`)
2. Accept the node and parent asset in the constructor
3. Store a working copy of the chain state (montage IDs, counts)
4. Provide `IsDirty` tracking (set true on any edit, false on `ResetDirty`)
5. Pre-populate from the node's current `ChainedMontages` array and `ChainCount` at construction

**Design Reference:**
- **Exemplar:** `WhenNodeDrawer` + `WhenNodeSession` (mirror structure, session lifecycle, dirty tracking)
- **DD-5 §14.5:** Drawer inputs/outputs, ChainCount management, per-slot sub-drawer ergonomics
- **DD-1 §6.3:** Chain constraint (≤8 entries, same-slot requirement)

**Tests Required:**

Create unit tests in `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Tests/NodeDrawers/PlayMontageChainNodeDrawerTests.cs`:

- ✅ `Handles_ReturnsTrueForPlayMontageChainNode` — `Handles()` returns true for a chain AiPrimitive node
- ✅ `Handles_ReturnsFalseForOtherNodeTypes` — `Handles()` returns false for WhenNode, other action nodes, etc.
- ✅ `CreateSession_ReturnsNonNullSession` — `CreateSession()` returns a valid `INodeEditSession`
- ✅ `Session_IsDirtyInitiallyFalse` — Session starts with `IsDirty == false`
- ✅ `Session_IsDirtyToggle_OnEdit` — Any edit sets `IsDirty = true` (test next task for actual edit behavior)
- ✅ `Session_ResetDirty_ClearsFlag` — `ResetDirty()` sets `IsDirty = false`

**⚠️ QUALITY STANDARDS**

- Tests must verify **actual behavior**, not just "does this exist?"
- Each test must be independently runnable
- No shallow "object exists" assertions (all assertions must verify meaningful state)

**Success Criteria:**

- ✅ `PlayMontageChainNodeDrawer` compiles and satisfies `IBlueprintNodeDrawer`
- ✅ `PlayMontageChainNodeSession` compiles and satisfies `INodeEditSession`
- ✅ Drawer can be instantiated with required dependencies
- ✅ Session can be created from a valid node
- ✅ 6 unit tests pass (3 drawer + 3 session lifecycle)
- ✅ Dispatch-keying route (A or B) documented in code comments
- ✅ Solution builds clean: `dotnet build IOS-IG-SimHost.sln -c Debug --no-restore -maxcpucount:4`

---

### Task 2: Dynamic Chain-Entry UI + ChainCount Management (ANC-P5-08b)

**Files:**
- UPDATE: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/NodeDrawers/PlayMontageChainNodeSession.cs` — Add `Draw()` method with UI rendering + state management
- UPDATE: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Tests/NodeDrawers/PlayMontageChainNodeDrawerTests.cs` — Add UI behavior tests

**Task Definition:** See [TASK-DETAIL.md — Addendum A / ANC-P5-08b](../TASK-DETAIL.md#anc-p5-08b--dynamic-chain-entry-ui--chaincount-management)

**Description:**

Implement the `Draw()` method in the session to render the dynamic chain UI:

1. **Reorderable List UI**
   - Render 0..`ChainCount` entries
   - Per entry: montage dropdown (populated by `GetPlayableMontages()`) + `BlendIntoTime` / `PlayRate` / `StartSection` numeric fields
   - Controls: Add (disabled at 8), Remove, Move up/down buttons

2. **ChainCount Management**
   - On Add: increment `ChainCount` (disabled if already 8), mark `IsDirty`
   - On Remove: decrement `ChainCount`, reindex entries, mark `IsDirty`
   - On Move: swap entries, keep `ChainCount` stable, mark `IsDirty`
   - Any entry field edit: mark `IsDirty`

3. **Write-Back Through IEditService**
   - The session must update the node's `ChainedMontages[0..Count-1]` and zero the tail via `IEditService`
   - Use storage-agnostic patterns (works whether field is managed `int[]` or future `[InlineArray]` per DEBT D-18)
   - Write-back should occur on `Draw()` (every frame the drawer is open) or on explicit "Apply" (confirm with exemplar behavior)

4. **Montage Resolution**
   - Use `IAnimationTkbQueries.GetPlayableMontages(currentClass)` to populate dropdown
   - Resolve selected name to ID via `StableIdHasher` (deterministic name→id for compile-time consistency)
   - Display montage name in dropdown, store ID in `ChainedMontages[]`

**Design Reference:**
- **DD-5 §3.3:** Chain entries, param fields, max-length constraint (≤8)
- **DD-5 §14.5:** Drawer UI: add/remove/reorder controls, `ChainCount` consistency
- **DD-1 §6.3:** Same-slot validation (defer to 08c; just manage UI state here)
- **WhenNodeSession exemplar:** Look at how it renders a complex nested UI and manages `IsDirty`

**Tests Required:**

Add headless session-state tests (no ImGui render; verify logic only):

- ✅ `Session_Add_IncrementsChainCount` — Add action increases `ChainCount`, marks `IsDirty`
- ✅ `Session_Add_DisabledAt8` — Add when `ChainCount == 8` is a no-op, does not increment
- ✅ `Session_Remove_DecrementsChainCount` — Remove action decreases `ChainCount`, reindexes entries, marks `IsDirty`
- ✅ `Session_Remove_ZeroEntries_Succeeds` — Remove when `ChainCount == 0` is a no-op
- ✅ `Session_MoveUp_ReordersEntries` — Move-up swaps adjacent entries, keeps `ChainCount` stable
- ✅ `Session_MoveDown_ReordersEntries` — Move-down swaps adjacent entries
- ✅ `Session_MontageSelection_ResolvesToId` — Selected montage name is resolved to stable ID via `StableIdHasher`
- ✅ `Session_WriteBack_PopulatesNode` — After edit, `IEditService` call updates node's `ChainedMontages[0..Count-1]` and zeros tail
- ✅ `Session_WriteBack_TailZeroed` — Entries beyond `ChainCount` are zeroed (prevent stale data)
- ✅ `Session_RoundTrip_JsonSerialization` — Node edited → write-back → asset JSON serialized/deserialized → state preserved

**⚠️ QUALITY STANDARDS**

- **Test Coverage:** Each operation (Add/Remove/Move/Select) tested with state verification
- **No Fake Tests:** All tests verify **actual state changes** via direct field inspection or round-trip serialization
- **Edge Cases Covered:** Boundary conditions (0 entries, 8 entries, add/remove at edges, reindex correctness)
- **Storage Agnostic:** Write-back code works whether field is `int[]` (current) or `[InlineArray(8)]` (future per DEBT D-18)

**Success Criteria:**

- ✅ `Draw()` method compiles and renders UI without errors
- ✅ Add/Remove/Move controls functional (state updates reflect in `ChainCount` and `ChainedMontages`)
- ✅ Montage dropdown populated with playable montages from `GetPlayableMontages()`
- ✅ Selected montage name resolved to stable ID
- ✅ `IEditService` write-back updates node correctly (testable via round-trip)
- ✅ All 10+ session-state tests pass
- ✅ Solution builds clean
- ✅ All previous tests (08a) still pass

---

## 🧪 Testing Requirements

**Minimum Test Counts:**
- ANC-P5-08a: 6 unit tests (drawer + session lifecycle)
- ANC-P5-08b: 10+ headless session-state tests (UI logic verification)

**Test Quality Bar:**

**NOT ACCEPTABLE:**
- ❌ Tests that only check "can I create an object" or "does it exist?"
- ❌ Tests that only verify string presence in generated code
- ❌ Shallow "Handles returns boolean" without checking actual node recognition
- ❌ Missing edge cases (add at capacity, reindex on remove, tail zeroing)

**REQUIRED:**
- ✅ Tests verify actual state changes (field values, counts, IDs)
- ✅ Tests would catch broken implementation (e.g., off-by-one in reindexing)
- ✅ Tests validate round-trip (edit → write-back → reload)
- ✅ Tests check boundary conditions (0/8 entries, move at edges)
- ✅ Each test independently runnable and named after the scenario it tests

---

## 📊 Report Requirements

**Focus on Developer Insights, Not Understanding Checks**

The report should gather valuable professional feedback:

**✅ What to Include:**
- **Issues Encountered:** What problems did you run into during drawer implementation? How did you resolve dispatch-keying (Route A vs B)?
- **Design Decisions:** Did you make choices beyond the spec? Why?
- **Weak Points Spotted:** Any areas of the codebase that could be improved?
- **Edge Cases Discovered:** What scenarios weren't mentioned in the spec?
- **Test Coverage:** Explain how your tests verify correctness (not just existence).
- **Suggested Commit Message:** Summary of what was achieved.

**❌ What NOT to Include:**
- "Explain how IBlueprintNodeDrawer works" (baby-sitting)
- "What is the purpose of INodeEditSession?" (understanding check)

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **ANC-P5-08a:** Implement drawer + session skeleton → Write tests → **ALL tests pass** ✅
2. **ANC-P5-08b:** Implement dynamic UI + state management → Write tests → **ALL tests pass** ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Current task tests written
- ✅ **ALL tests passing** (including previous task tests)
- ✅ Build clean: `dotnet build IOS-IG-SimHost.sln -c Debug --no-restore -maxcpucount:4`

**Why:** Ensures each component is solid before building on top of it. Prevents cascading failures.

---

## Verification Requirements

Run and **include summary output** in your report:

1. Full test run for the new tests:
   ```
   dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Tests/ --filter "FullyQualifiedName~PlayMontageChainNodeDrawerTests" -c Debug
   ```

2. Full Blueprints test suite (regression check):
   ```
   dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj -c Debug --no-build
   ```

3. Full solution build (verify no unrelated regressions):
   ```
   dotnet build IOS-IG-SimHost.sln -c Debug --no-restore -maxcpucount:4
   ```

**Include test output:**
- Total tests passed/failed
- Any warnings or errors
- Execution time

---

## ⚠️ Common Pitfalls to Avoid

1. **Dispatch-Keying Ambiguity:** `PlayMontageChainNode` is an AiPrimitive params struct hosted on a generic node, not a standalone Blueprint node type. Confirm keying strategy (Route A/B) early; document in code.

2. **String Presence vs. Actual Behavior:** Tests must verify montage IDs are actually set in the `ChainedMontages` array, not just that a dropdown exists.

3. **Off-by-One in Reindexing:** When removing an entry, ensure remaining entries are contiguous (e.g., entries 0–2 remain, 3–7 zeroed). Test this explicitly.

4. **ChainCount vs Array Bounds:** Keep `ChainCount ≤ 8` enforced in Add control. Keep tail entries zeroed. Tests must verify tail zeroing on write-back.

5. **Write-Back Atomicity:** Ensure `IEditService` calls are all-or-nothing (chain state updates consistently). Test round-trip (edit → write-back → reload) to verify.

6. **Dependency Injection:** Ensure drawer and session inject all required services (`IAnimationTkbQueries`, `IEditService`, current-class context). Missing dependency = null reference at draw time.

---

## 📚 Reference Materials

- **Task Defs:** [TASK-DETAIL.md — Addendum A](../TASK-DETAIL.md#addendum-a--anc-p5-08-implementation-plan-playmontagechainnode-custom-drawer)
- **Design:** [DD-5_BlueprintPrimitives_v1_1.md](./DD-5_BlueprintPrimitives_v1_1.md) (section 14.5)
- **Exemplar Drawer:** `WhenNodeDrawer.cs` + `WhenNodeSession.cs`
- **Previous Review:** `.dev/anim-ctrl/reviews/BATCH-10-REVIEW.md` (AiPrimitive precedent)
- **DEBT:** D-15 (context for deferral), D-18 (managed array → InlineArray future compatibility)

---

## 📝 Files Summary

**New Files to Create:**
1. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/NodeDrawers/PlayMontageChainNodeDrawer.cs`
2. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/NodeDrawers/PlayMontageChainNodeSession.cs`
3. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Tests/NodeDrawers/PlayMontageChainNodeDrawerTests.cs`

**Files to Update:**
1. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintEditorBootstrap.cs` — Extend `CreateNodeDrawerRegistry`
2. `IOS-IG-SimHost.sln` — No project changes needed (test file added to existing test project)

---

**When complete, submit your report to:** `.dev/anim-ctrl/reports/BATCH-17-REPORT.md`
