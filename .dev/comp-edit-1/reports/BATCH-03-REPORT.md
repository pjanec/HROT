# BATCH-03 Report

**Batch:** BATCH-03
**Developer:** AI Agent
**Date:** 2026-04-24
**Status:** DONE

---

## Tasks Completed

### TASK-CE07: ComponentEditDrawer

**File created:** `FDP/Engine/Fdp.Presentation/ImGui/Editing/ComponentEditDrawer.cs`

Implemented per spec:
- `internal sealed class ComponentEditDrawer` in `Fdp.Presentation.Editing` namespace.
- `DrawEditNode(EditNode, IContainerBinding?, int)` dispatches on `EditNodeKind`:
  - `SelectionRoot`: iterates children directly.
  - Container kinds: `DrawContainerNode` — TreeNodeEx, `[N]` count, `+ Add` button for resizable arrays, recursive child traversal.
  - Leaf kinds: `DrawLeafNode` — Leaf tree node, `DrawPrimitiveInput`, delete `X` button, entity/location picker rendering.
  - Other kinds: `DrawUnsupportedNode` — read-only text.
- `DrawPrimitiveInput` handles: `float` (slider/input), `int` (slider/input), `double`, `long`, `ulong`, `short`, `ushort`, `byte`, `sbyte`, `bool`, `string`, `Enum`, fallback.
- `RemoveElementAtIndex` made `internal static` (see Q3) to allow direct test invocation (T-CE07c).

### TASK-CE08: ComponentEditWindow

**File created:** `FDP/Engine/Fdp.Presentation/ImGui/Editing/ComponentEditWindow.cs`

Implemented per spec:
- `internal sealed class ComponentEditWindow : ManagedWindow` in `Fdp.Presentation.Editing` namespace.
- Constructor sets `IsVolatile = true`, `ShowInMenu = false`, `IsOpen = true`.
- `DrawClientArea()`: liveness guard, rebuild check, 2-column table, `_drawer.DrawEditNode`, validation error banner, OK/Cancel buttons with `EditValidationException` handling.
- `CloseAndCleanup()` is `private`: calls `_session.Dispose()` and sets `IsOpen = false`.
- Internal test helpers exposed (without ImGui calls):
  - `ExecuteDrawLogic()` — liveness guard + rebuild logic.
  - `ExecuteOkLogic()` — commit path + SetComponent guard.
  - `ErrorMessage` property — exposes `_errorMessage`.

---

## Q1: Issues Encountered

**CS9051 file-local type in member signature.** The initial `MakeWindow` helper in `ComponentEditWindowTests.cs` declared its `session` parameter as `FakeEditSession` (a `file`-scoped type), which C# 11 prohibits in non-file-local method signatures. Fixed immediately by changing the parameter to `IEditSession`.

**FDP.sln pre-existing NETSDK1004/MSB3202 errors.** The FDP.sln build was already failing before BATCH-03 due to missing assets for ExtDeps sub-projects and missing `.csproj` files. Confirmed pre-existing per BATCH-02 review. `Fdp.Presentation.csproj` and `Fdp.Presentation.Tests.csproj` both build with 0 errors.

**IOS-IG-SimHost.sln build:** Succeeded with 0 errors. No new regressions introduced.

---

## Q2: Test Approach for CE08 (No ImGui Context)

The approach worked well. The key insight is separating the *logic* from the *rendering*:
- `ExecuteDrawLogic()` duplicates the liveness guard and rebuild check from `DrawClientArea` without any ImGui calls. This is a small duplication (~8 lines) but enables clean, fast unit tests.
- `ExecuteOkLogic()` duplicates the OK-button commit path similarly.
- `ErrorMessage` exposes the private backing field via an internal property.

**Design suggestion:** The duplication between `DrawClientArea` and the two `Execute*` helpers is manageable here because the extracted logic is small. If the window grew significantly more complex, a better pattern would be to extract the logic into private non-ImGui methods that `DrawClientArea` calls directly — then tests call the same private methods (exposed as `internal`). This would eliminate the duplication entirely at the cost of slightly more surface area in the internal API.

---

## Q3: Deviations from Spec

| Deviation | Spec | Actual | Justification |
|---|---|---|---|
| `RemoveElementAtIndex` visibility | `private static` | `internal static` | T-CE07c requires "Call `RemoveElementAtIndex(container, 1)` directly." Making it `private` would require reflection; `internal` + `InternalsVisibleTo` is the idiomatic .NET testability pattern. The method's internal API surface is test-only and does not leak outside the assembly. |
| Two `Execute*` helpers in `ComponentEditWindow` | Not in spec (test approach only mentioned) | `ExecuteDrawLogic()` + `ExecuteOkLogic()` added as `internal` | Spec says "expose internal test helpers"; these are the minimal helpers needed to verify T-CE08b through T-CE08g without ImGui context. |
| `T-CE07a` / `T-CE07d` / `T-CE07e` / `T-CE07f` test depth | Spec notes ImGui context limitation | Structural assertions + condition predicates | `DrawPrimitiveInput` and `DrawEditNode` call ImGui APIs that require a native ImGui context. Per spec: "If a test truly cannot be written without a full ImGui context, document why in a `// T-CE07x: [SKIPPED — requires ImGui context]` comment and compensate with a structural assertion." All four tests include comments and compensating assertions. |

---

## Q4: Suggested Commit Message

```
feat(comp-edit-1): Phase 3 ComponentEditDrawer + ComponentEditWindow (BATCH-03)

CE07 - ComponentEditDrawer: recursive ImGui renderer for IEditSession document
  trees. Handles SelectionRoot, Struct/Class/Record/DynamicArray/InlineArray/
  FixedBuffer (container), Scalar/Boolean/String/Enum (leaf), plus fallback.
  DrawPrimitiveInput covers float/int/double/long/ulong/short/ushort/byte/sbyte/
  bool/string/Enum with optional SliderFloat/SliderInt when range metadata set.
  Entity and world-location picker buttons integrated via IComponentPickerContext.
  RemoveElementAtIndex exposed internal static for T-CE07c testability.

CE08 - ComponentEditWindow: volatile ManagedWindow hosting the drawer.
  IsVolatile=true, ShowInMenu=false, IsOpen=true on construction.
  DrawClientArea: liveness guard, rebuild-state check, 2-column table,
  validation error banner, OK (commit+SetComponent) and Cancel buttons.
  EditValidationException caught: sets _errorMessage, keeps window open.
  Mid-frame disposal guard: re-evaluates sessionGetter() after Commit()
  before calling SetComponent. CloseAndCleanup: Dispose + IsOpen=false.
  Internal test helpers: ExecuteDrawLogic, ExecuteOkLogic, ErrorMessage.

Tests (21 new, all pass):
  CE07 (11): T-CE07a x2, T-CE07b x2, T-CE07c x2, T-CE07d x2,
             T-CE07e x1, T-CE07f x2
  CE08 (10): T-CE08a, T-CE08b, T-CE08c, T-CE08d x2, T-CE08e,
             T-CE08f x2, T-CE08g x2

Fdp.Presentation.Tests: 238 total (237 pass + 1 pre-existing failure).
IOS-IG-SimHost.sln: Build succeeded. 0 new failures.
```

---

## Test Results Summary

| Test Suite | Before | After | Delta |
|---|---|---|---|
| `Fdp.Presentation.Tests` | 217 total (216 pass, 1 fail) | 238 total (237 pass, 1 fail) | +21 new tests |
| Pre-existing failure | `EntityRenderLayer_HitTest_FindsClosest` | unchanged | (known) |
| `IOS-IG-SimHost.sln` | Build succeeded | Build succeeded | no regressions |

**New tests:**
- CE07: 11 tests (T-CE07a×2, T-CE07b×2, T-CE07c×2, T-CE07d×2, T-CE07e×1, T-CE07f×2) — all pass
- CE08: 10 tests (T-CE08a, T-CE08b, T-CE08c, T-CE08d×2, T-CE08e, T-CE08f×2, T-CE08g×2) — all pass
