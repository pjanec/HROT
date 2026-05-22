# BATCH-01: Solution Scaffolding + Kernel Foundation

**Batch Number:** BATCH-01  
**Tasks:** TASK-P0-001, TASK-P1-001, TASK-P1-002, TASK-P1-003, TASK-P1-004, TASK-P1-005, TASK-P1-006, TASK-P1-007  
**Phase:** Phase 0 (Infrastructure) + Phase 1 (Kernel / Foundation)  
**Estimated Effort:** 8–12 hours  
**Priority:** HIGH  
**Dependencies:** None (first batch)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This is the first batch on a brand-new C# .NET 8 node-graph editor library. You are
building the entire solution structure and all pure-logic foundation code. No ImGui,
no windowing — just types, algorithms, and interfaces. Everything in this batch must
be unit-testable without a UI.

### Required Reading (IN ORDER)

1. **Workflow Guide:** `.dev/.guides/DEV-GUIDE.md` — How to work with batches
2. **START HERE:** `.dev/final/instructions/00-START-HERE.md` — Ground rules, conventions, solution layout
3. **Spec Brief:** `.dev/final/instructions/01-spec-brief.md` — Quick reference for behavioral context
4. **Scaffolding task:** `.dev/final/instructions/03-01-task-scaffolding.md` — TASK-P0-001 full brief
5. **Kernel layer tasks:** `.dev/final/instructions/03-02-tasks-kernel-layer.md` — TASK-P1-001 through TASK-P1-007 full briefs
6. **Kernel files (verbatim source):**
   - `.dev/final/kernel/00-primitives.md` — IDs, enums, IdGenerator, RectF
   - `.dev/final/kernel/01-interfaces.md` — Host contract interfaces
   - `.dev/final/kernel/02-commands-and-undo.md` — Command hierarchy, UndoStack
   - `.dev/final/kernel/03-search-spatial-constants.md` — FuzzyMatcher, ExpressionEvaluator, SpatialIndex, TimingConstants, DefaultTypeColors, CommandCatalog
   - `.dev/final/kernel/04-my-blueprint-and-rest.md` — IMyBlueprintModel and remaining interfaces; IEditorCommands, IEditorIndicators

### Source Code Location

- **Solution Root:** `D:\Work\BlueprintEdit` (or whatever your VS Code workspace root is)
- **Primary Work Area:** `src/` — create this folder at workspace root
- **Test Projects:** `tests/` — create this folder at workspace root

### Report Submission

**When done, submit your report to:**  
`.dev/final/reports/BATCH-01-REPORT.md`

**If you have questions, create:**  
`.dev/final/questions/BATCH-01-QUESTIONS.md`

---

## Context

This batch builds the entire skeleton the rest of the project depends on.
Phase 0 creates the solution/projects so the solution compiles.
Phase 1 fills in all pure-logic types, interfaces, algorithms and constants
verbatim from kernel files. After this batch, unit tests for FuzzyMatcher,
ExpressionEvaluator, SpatialIndex, UndoStack, and Primitives all pass.

---

## 🎯 Batch Objectives

1. **Compilable solution** — `dotnet build` green, `dotnet test` green (no failures).
2. **Verbatim kernel code** — every kernel file copied exactly, public surface unchanged.
3. **Tests passing** — all unit tests specified in the task briefs pass.

---

## ✅ Tasks

### Task 1: Solution Scaffolding (TASK-P0-001)

**Task Definition:** See `.dev/final/instructions/03-01-task-scaffolding.md`

Create the `.sln` and all six `.csproj` files plus `Directory.Build.props`.
The project spec, package versions, and demo `Program.cs` placeholder are all
in the task brief — follow it verbatim.

**Acceptance:**
- `dotnet build` succeeds
- `dotnet test` succeeds (no test failures; no tests yet at this point)
- All projects have `GenerateDocumentationFile` and `TreatWarningsAsErrors`

---

### Task 2: Primitives (TASK-P1-001)

**Task Definition:** See `.dev/final/instructions/03-02-tasks-kernel-layer.md#t-02--primitives`  
**Kernel source:** `.dev/final/kernel/00-primitives.md`

Copy verbatim into `src/NodeEditor.Primitives/`.  
Add the smoke test in `tests/NodeEditor.Core.Tests/Primitives/IdGeneratorTests.cs`
(exact test code is in the task brief).

---

### Task 3: Host Contract Interfaces (TASK-P1-002)

**Task Definition:** See `.dev/final/instructions/03-02-tasks-kernel-layer.md#t-03--host-contract-interfaces`  
**Kernel source:** `.dev/final/kernel/01-interfaces.md`

Copy verbatim into `src/NodeEditor.Core/Interfaces/`.  
No tests required for this task.

---

### Task 4: Commands and Undo Stack (TASK-P1-003)

**Task Definition:** See `.dev/final/instructions/03-02-tasks-kernel-layer.md#t-04--commands-and-undo`  
**Kernel source:** `.dev/final/kernel/02-commands-and-undo.md`

Copy verbatim into `src/NodeEditor.Core/Commands/`.  
Add `UndoStackTests.cs` with the four scenarios from the task brief.

---

### Task 5: Fuzzy Matcher (TASK-P1-004)

**Task Definition:** See `.dev/final/instructions/03-02-tasks-kernel-layer.md#t-05--fuzzy-matcher`  
**Kernel source:** `.dev/final/kernel/03-search-spatial-constants.md` (FuzzyMatcher section)

Copy verbatim into `src/NodeEditor.Core/Search/FuzzyMatcher.cs`.  
Add `FuzzyMatcherTests.cs` with all theory/fact tests from the task brief.

---

### Task 6: Expression Evaluator (TASK-P1-005)

**Task Definition:** See `.dev/final/instructions/03-02-tasks-kernel-layer.md#t-06--expression-evaluator`  
**Kernel source:** `.dev/final/kernel/03-search-spatial-constants.md` (ExpressionEvaluator section)

Copy verbatim into `src/NodeEditor.Core/Expression/ExpressionEvaluator.cs`.  
Add `ExpressionEvaluatorTests.cs` with all theory cases from the task brief.

---

### Task 7: Spatial Index (TASK-P1-006)

**Task Definition:** See `.dev/final/instructions/03-02-tasks-kernel-layer.md#t-07--spatial-index`  
**Kernel source:** `.dev/final/kernel/03-search-spatial-constants.md` (SpatialIndex section)

Copy verbatim into `src/NodeEditor.Core/Spatial/SpatialIndex.cs`.  
Add `SpatialIndexTests.cs` with the six fact tests from the task brief.

---

### Task 8: Timing / Theme / Catalog Constants (TASK-P1-007)

**Task Definition:** See `.dev/final/instructions/03-02-tasks-kernel-layer.md#t-08--timing--theme--catalog-constants`  
**Kernel sources:**
- `.dev/final/kernel/03-search-spatial-constants.md` (TimingConstants, DefaultTypeColors, DefaultTheme, CommandCatalog)
- `.dev/final/kernel/04-my-blueprint-and-rest.md` (IMyBlueprintModel, IEnumValueProvider, IGraphSearchProvider, IPinDefaultValueEditorRegistry, IDetailsViewProvider, IEditorCommands, IEditorIndicators)

Copy verbatim into `src/NodeEditor.Core/`.  
Add the sanity test for `DefaultTypeColors` from the task brief.

---

## 🧪 Testing Requirements

Minimum tests for this batch (all from task briefs):

| Test File | Tests |
|-----------|-------|
| `Primitives/IdGeneratorTests.cs` | 3 facts |
| `Commands/UndoStackTests.cs` | 4 facts |
| `Search/FuzzyMatcherTests.cs` | ~10 theory cases + 3 facts |
| `Expression/ExpressionEvaluatorTests.cs` | ~14 theory (success) + 6 theory (failure) |
| `Spatial/SpatialIndexTests.cs` | 6 facts |
| `Constants/DefaultTypeColorsTests.cs` | 1 fact (3 assertions) |

**Quality bar:**
- Tests verify actual values/behavior, not just compilation.
- No `Assert.True(true)` or trivial tautologies.
- Every test has a meaningful assertion with `FluentAssertions`.

---

## 📊 Report Requirements

Submit `.dev/final/reports/BATCH-01-REPORT.md` with:

```markdown
# BATCH-01 Report

## Tasks Completed
[List each TASK-PX-XXX with ✅ or ❌ and one-line status]

## Test Results
[Paste the output of `dotnet test` — exact counts]

## Developer Insights
1. **Issues encountered:** [What was hard, any kernel ambiguities?]
2. **Weak points spotted:** [Anything that looks fragile or likely to cause problems later?]
3. **Design decisions beyond spec:** [Any gaps you filled? Any choices you made?]

## Files Created
[List every new file]
```

---

## ✅ Success Criteria

Batch is complete when:
- [ ] `dotnet build NodeEditor.sln` exits 0 with zero errors and zero warnings
- [ ] `dotnet test` exits 0, all tests listed above pass
- [ ] Every kernel file is copied verbatim (public API unchanged)
- [ ] `BATCH-01-REPORT.md` submitted
