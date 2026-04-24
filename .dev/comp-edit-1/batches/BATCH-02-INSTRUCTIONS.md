# BATCH-02: Picker Infrastructure + Project References

**Batch Number:** BATCH-02
**Tasks:** TASK-CE04, TASK-CE05, TASK-CE06
**Phase:** Phase 2 — Picker Infrastructure + Phase 3 Project Reference
**Estimated Effort:** 3-4 hours
**Priority:** HIGH
**Dependencies:** BATCH-01 (completed, committed)

---

## Onboarding & Workflow

### Developer Instructions

Three small, loosely coupled tasks:
1. **CE04** — Two new attribute classes that component authors place on ECS fields to opt into picker UI.
2. **CE05** — The `IComponentPickerContext` interface that brokers async map/entity picks.
3. **CE06** — Add `StructEdit.Core` and `StructEdit.Reflection` project references to `Fdp.Presentation.csproj` so Phase 3 work can use StructEdit types.

CE04 and CE05 create a new sub-folder (`Editing/`) inside `Fdp.Presentation/ImGui/`. CE06 is a one-line `.csproj` edit.

### Required Reading (IN ORDER)

1. **Developer Workflow:** `.github/skills/developer/SKILL.md`
2. **Code Standards:** `.github/skills/CODE-STANDARDS.md`
3. **Onboarding:** `.dev/comp-edit-1/ONBOARDING.md`
4. **Previous Review:** `.dev/comp-edit-1/reviews/BATCH-01-REVIEW.md`
5. **Design (Phase 2 + Phase 3 §3.1):** `.dev/comp-edit-1/DESIGN.md` — §§ "Phase 2: Picker Infrastructure" and "Phase 3 §3.1 Project Reference"
6. **Task Detail:** `.dev/comp-edit-1/TASK-DETAIL.md` — TASK-CE04, TASK-CE05, TASK-CE06

### Source Code Locations

- **New folder to create:** `FDP/Engine/Fdp.Presentation/ImGui/Editing/`
  - `PickerAttributes.cs` (CE04)
  - `IComponentPickerContext.cs` (CE05)
- **csproj to modify:** `FDP/Engine/Fdp.Presentation/Fdp.Presentation.csproj` (CE06)
- **Test project:** `FDP/Engine/Fdp.Presentation.Tests/` (CE04 and CE05 tests go here)

### Build Commands

```powershell
# Build FDP only (faster)
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet build FDP/FDP.sln --no-restore

# Run Fdp.Presentation tests
dotnet test FDP/Engine/Fdp.Presentation.Tests/Fdp.Presentation.Tests.csproj

# Verify full solution still builds (do this before submitting report)
dotnet build IOS-IG-SimHost.sln --no-restore
dotnet test IOS-IG-SimHost.sln
```

### Report Submission

When done: `.dev/comp-edit-1/reports/BATCH-02-REPORT.md`

---

## MANDATORY WORKFLOW

1. **TASK-CE06** → add project references → `dotnet build FDP/FDP.sln --no-restore` succeeds ✅
2. **TASK-CE04** → create picker attributes → write tests → **ALL tests pass** ✅
3. **TASK-CE05** → create `IComponentPickerContext` → write tests → **ALL tests pass** ✅

Do CE06 first (it unblocks `using StructEdit.Core;` in `Fdp.Presentation`). Do not stop to ask for permission at any point. Fix compilation errors before proceeding. Submit only when all tests pass.

---

## Context

After BATCH-01, `StructEdit.Core` and `StructEdit.Reflection` are fully extended. This batch wires the picker abstraction into `Fdp.Presentation` and makes StructEdit types available there. No rendering logic is written yet — that is BATCH-03.

---

## Tasks

### TASK-CE06: Add StructEdit Project References

**File:** `FDP/Engine/Fdp.Presentation/Fdp.Presentation.csproj` (MODIFY)
**Task Detail:** See [TASK-DETAIL.md §TASK-CE06](../TASK-DETAIL.md#task-ce06-add-structedit-project-references)

Add two `<ProjectReference>` entries inside the existing `<ItemGroup>` that already contains the `Fdp.Toolkits` reference:

```xml
<ProjectReference Include="..\..\ExtDeps\StructEdit\src\StructEdit.Core\StructEdit.Core.csproj" />
<ProjectReference Include="..\..\ExtDeps\StructEdit\src\StructEdit.Reflection\StructEdit.Reflection.csproj" />
```

Paths are relative to `FDP/Engine/Fdp.Presentation/`. The `Fdp.Toolkits.csproj` reference already in that group confirms the relative-path style.

No tests needed for this task — `dotnet build` succeeding is the verification.

### TASK-CE04: Picker Attributes

**File:** `FDP/Engine/Fdp.Presentation/ImGui/Editing/PickerAttributes.cs` (NEW FILE)
**Task Detail:** See [TASK-DETAIL.md §TASK-CE04](../TASK-DETAIL.md#task-ce04-picker-attributes)
**Design Reference:** [DESIGN.md §2.1](../DESIGN.md#21-picker-attributes)

Two `public sealed` attribute classes in namespace `Fdp.Presentation.Editing`:

- `MapPickableEntityAttribute`: `[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]`, constructor `(params string[] filterPresets)`, property `string[] FilterPresets`.
- `MapPickableWorldLocationAttribute`: `[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]`, no constructor parameters.

**Tests to write** (new file in `FDP/Engine/Fdp.Presentation.Tests/` under `ImGui/Editing/`):
- `T-CE04a`: `[MapPickableEntity("tanks", "infantry")]` → `FilterPresets` equals `["tanks", "infantry"]`
- `T-CE04b`: `[MapPickableEntity]` (no args) → `FilterPresets.Length == 0`
- `T-CE04c`: `[MapPickableWorldLocation]` applied to a field compiles and attribute is present via reflection

### TASK-CE05: IComponentPickerContext

**File:** `FDP/Engine/Fdp.Presentation/ImGui/Editing/IComponentPickerContext.cs` (NEW FILE)
**Task Detail:** See [TASK-DETAIL.md §TASK-CE05](../TASK-DETAIL.md#task-ce05-icomponentpickercontext)
**Design Reference:** [DESIGN.md §2.2](../DESIGN.md#22-icomponentpickercontext)

Interface `IComponentPickerContext` in namespace `Fdp.Presentation.Editing`. All five methods exactly as specified — using `string jsonPath` (NOT `int nodeId`):

```csharp
bool IsPickPendingFor(string jsonPath);

void RequestEntityPick(string jsonPath, string[]? filterPresets);
void RequestLocationPick(string jsonPath);

bool TryConsumeEntityPick(string jsonPath, out Entity pickedEntity);
bool TryConsumeLocationPick(string jsonPath, out Vector3 location);
```

`Entity` is `Fdp.Core.Entity`. `Vector3` is `System.Numerics.Vector3`.
The interface carries no state and no default implementations.

**Tests to write** (same test file as CE04, or a separate `IComponentPickerContextTests.cs`):
- `T-CE05a`: a `NopPickerContext : IComponentPickerContext` mock compiles and all five methods can be invoked without error
- `T-CE05b`: `NopPickerContext.TryConsumeEntityPick("$.Targets[0]", out var e)` returns `false` and `e == default(Entity)`

---

## Testing Requirements

- **Minimum new tests:** 5 (3 CE04 + 2 CE05)
- All pre-existing `Fdp.Presentation.Tests` tests must continue to pass
- Tests must verify actual behavior — not just "no exception"

---

## Quality Standards

- `MapPickableEntityAttribute` and `MapPickableWorldLocationAttribute` must be `public sealed`
- `IComponentPickerContext` must be a pure interface — no default implementations, no state
- No magic numbers, no unnecessary abstraction
- Follow existing code style in `Fdp.Presentation`

---

## Report Requirements

Submit `.dev/comp-edit-1/reports/BATCH-02-REPORT.md` covering:

**Q1:** Any issues encountered and how resolved?

**Q2:** Did the `IComponentPickerContext` design feel right for the async pick use-case? Any concerns?

**Q3:** Any edge cases or design questions discovered?

**Q4:** Suggested commit message.

---

## Success Criteria

This batch is DONE when:
- [ ] Two StructEdit `ProjectReference` entries added; `dotnet build FDP/FDP.sln --no-restore` succeeds
- [ ] `PickerAttributes.cs` created, all CE04 tests pass
- [ ] `IComponentPickerContext.cs` created, all CE05 tests pass
- [ ] All pre-existing tests still pass
- [ ] `dotnet test IOS-IG-SimHost.sln` exits with 0 failures
- [ ] Report submitted

---

## Reference

- **Task Detail:** `.dev/comp-edit-1/TASK-DETAIL.md` §§ CE04, CE05, CE06
- **Design:** `.dev/comp-edit-1/DESIGN.md` §§ 2.1, 2.2, 3.1
- **Existing csproj to study:** `FDP/Engine/Fdp.Presentation/Fdp.Presentation.csproj`
- **Existing Editing tests dir:** `FDP/Engine/Fdp.Presentation.Tests/ImGui/` (create `Editing/` sub-folder)
