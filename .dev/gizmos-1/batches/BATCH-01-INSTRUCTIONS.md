# BATCH-01: Core Primitive Protocol — Types, Tagged Union, Builder, String Interning

**Batch Number:** BATCH-01
**Tasks:** TASK-GZ001, TASK-GZ002, TASK-GZ003, TASK-GZ019
**Phase:** Phase 1 — Core Primitive Protocol
**Estimated Effort:** 14–18 hours
**Priority:** HIGH
**Dependencies:** None (first batch)

---

## 📋 Onboarding & Workflow

### Developer Instructions

You are implementing the foundational data model for the FDP Declarative Gizmo & Presentation
Framework. This batch covers the entire Phase 1 primitive protocol: the color/enum types, the
64-byte `DebugPrimitive` tagged union, the `IDebugDrawBuilder` / `DebugPrimitiveBuffer` API,
and the string interning side-channel for long text.

All code you write in this batch is pure data model and buffer logic — no ECS systems, no
rendering. Everything else in the framework will depend on what you produce here, so correctness
and layout precision are critical.

### Required Reading (IN ORDER)

1. **Onboarding:** `.dev/gizmos-1/ONBOARDING.md` — project overview and key existing types
2. **Design Document:** `.dev/gizmos-1/DESIGN.md` — read §1.1, §1.2, §1.3 in full
3. **Task Definitions:** `.dev/gizmos-1/TASK-DETAIL.md` — read TASK-GZ001, TASK-GZ002, TASK-GZ003,
   TASK-GZ019 in full (every constraint and success condition)

### Key Existing Types to Understand

Before writing any code, browse these files (do NOT skip this step):

- `FDP/Engine/Fdp.Core/Entity.cs` — `Entity`, `Entity.Null`, generational index
- `FDP/Engine/Fdp.Core/Text/FixedString32.cs` — `FixedString32` (32-byte fixed buffer, 31 chars + null)
- `FDP/Engine/Fdp.Core/Numerics/` — how `Vector3`, `Vector2` are used in the codebase
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/` — existing diagnostics files (do NOT modify)
- `FDP/Toolkits/Fdp.Toolkits/Fdp.Toolkits.csproj` — confirm `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`

### Source Code Locations

- **Primary Work Area:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/` (create this folder)
- **Test Project:** `FDP/Toolkits/Fdp.Toolkits.Tests/` (existing test project)

### Report Submission

**When done, submit your report to:**
`.dev/gizmos-1/reports/BATCH-01-REPORT.md`

**If you have questions, create:**
`.dev/gizmos-1/questions/BATCH-01-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **TASK-GZ001:** Implement → Write tests → **ALL tests pass** ✅
2. **TASK-GZ002:** Implement → Write tests → **ALL tests pass** ✅
3. **TASK-GZ003:** Implement → Write tests → **ALL tests pass** ✅
4. **TASK-GZ019:** Implement → Write tests → **ALL tests pass** ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Current task tests written
- ✅ **ALL tests passing** (including previous task tests)

Do not stop and ask if it is ok to run tests, fix compilation errors, or fix failing tests. Just
do it. Complete all tasks and get everything green before writing the report. No asking for
permission on obvious steps.

---

## Context

This batch implements the lowest layer of the gizmo framework. The design's core principle is
"Evaluate Once, Present Anywhere": gizmo logic runs once, emits `DebugPrimitive` structs into a
buffer, and those structs are rendered locally (Raylib) or transported remotely (DDS). The buffer
must be zero-allocation on the hot path — all primitives are 64-byte blittable structs.

**Related Tasks:**
- [TASK-GZ001](../TASK-DETAIL.md#task-gz001--color-type-and-primitive-enums) — Foundation enums and color type
- [TASK-GZ002](../TASK-DETAIL.md#task-gz002--debugprimitive-tagged-union) — 64-byte blittable tagged union
- [TASK-GZ003](../TASK-DETAIL.md#task-gz003--idebugdrawbuilder-and-debugprimitivebuffer) — Write-side API and buffer
- [TASK-GZ019](../TASK-DETAIL.md#task-gz019--stringinternmap-and-drawtextlong) — String interning for long text

---

## 🎯 Batch Objectives

By the end of this batch:
1. All Phase 1 types exist in `Fdp.Toolkit.Diagnostics.Gizmos` namespace
2. `DebugPrimitive` is exactly 64 bytes with correct explicit field offsets
3. `DebugPrimitiveBuffer` accumulates primitives with zero allocation on the draw path
4. `DrawTextLong` interns strings >31 chars via `StringInternMap` (FNV-1a hash)
5. All success conditions from TASK-GZ001, GZ002, GZ003, GZ019 are covered by tests

---

## ✅ Tasks

### Task 1: Color Type and Primitive Enums (TASK-GZ001)

**Files to CREATE** in `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Primitives/`:
- `Rgba32.cs`
- `PipelineTarget.cs`
- `CoordinateSpace.cs`
- `SizeMode.cs`
- `DebugPrimitiveShape.cs`
- `ScreenAnchor.cs`
- `PickToken.cs`

**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-gz001--color-type-and-primitive-enums) for all constraints and exact values.

**Key points:**
- All types in namespace `Fdp.Toolkit.Diagnostics.Gizmos`
- `Rgba32`: `[StructLayout(Sequential, Size=4)]`, 4 named constants minimum (`Red`, `Green`, `Yellow`, `White`, `Black`, `Transparent`)
- `PipelineTarget`: `[Flags] enum : byte` — check `All == (Map2D | Viewport3D)`
- `PickToken`: `bool IsValid => !Target.IsNull` — zero-init must be invalid

**Tests Required (TASK-GZ001):**
- SC-GZ001-1: `Rgba32` round-trips R/G/B/A from 4-byte constructor
- SC-GZ001-2: `Marshal.SizeOf<Rgba32>() == 4`
- SC-GZ001-3: `PipelineTarget.All == (PipelineTarget.Map2D | PipelineTarget.Viewport3D)`
- SC-GZ001-4: `PickToken` with `Entity.Null` → `IsValid == false`
- SC-GZ001-5: `PickToken` with non-null entity → `IsValid == true`
- SC-GZ001-6: zero-initialised `PickToken` → `IsValid == false`

---

### Task 2: DebugPrimitive Tagged Union (TASK-GZ002)

**Files to CREATE** in `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Primitives/`:
- `DebugPrimitive.cs`

**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-gz002--debugprimitive-tagged-union) for all field offsets and payload layouts.

**Key points:**
- `[StructLayout(LayoutKind.Explicit, Size = 64)]` — 64 bytes exactly, no more, no less
- All header fields and all payload fields must have explicit `[FieldOffset(n)]`
- `StringHash` overlaps `AnchorIndex` at offset 8 (shared overlay)
- `FixedString32` at offset 32 for Text/EntityBadge payloads — verify with `Marshal.SizeOf<FixedString32>() == 32`
- Provide static factory helpers: `MakeLine`, `MakeSphere`, `MakeText`, `MakeArrow` (see TASK-DETAIL.md)
- `float Thickness => ThicknessU16 * 0.1f` helper property
- `Entity Anchor` helper property reconstructing from `AnchorIndex` + `AnchorGeneration`

**Tests Required (TASK-GZ002):**
- SC-GZ002-1 through SC-GZ002-11 — all of them; see TASK-DETAIL.md for exact assertion patterns
- **Focus on offset isolation**: changing one field must not corrupt adjacent fields
- The `StringHash` overlay test (SC-GZ002-11) is critical for Phase 1 interning

---

### Task 3: IDebugDrawBuilder and DebugPrimitiveBuffer (TASK-GZ003)

**Files to CREATE** in `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/`:
- `IDebugDrawBuilder.cs`
- `DebugPrimitiveBuffer.cs`

**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-gz003--idebugdrawbuilder-and-debugprimitivebuffer) for the full interface contract and buffer semantics.

**Key points:**
- `DebugPrimitiveBuffer` is a `sealed class` implementing `IDebugDrawBuilder`
- Pre-allocated `DebugPrimitive[]` array, default capacity 4096
- Thread-safe slot reservation via `Interlocked.Increment`
- `GetFrame()` returns `ReadOnlySpan<DebugPrimitive>` — zero-copy, no allocation
- `Clear()` resets `_count = 0` (no reallocation)
- Overflow: silently drop; increment `DroppedCount`
- `DrawTextLong` will be wired in TASK-GZ019; for now, leave the method body with `throw new NotImplementedException()` in `DebugPrimitiveBuffer` (the interface must still declare it)
- `DrawEntityLocal` emits `Space = EntityLocal`, sets `AnchorIndex`/`AnchorGeneration` from entity

**Tests Required (TASK-GZ003):**
- SC-GZ003-1 through SC-GZ003-9 — all of them; see TASK-DETAIL.md
- Note: SC-GZ003-7, -8, -9 about `DrawTextLong` / `StringHash` will be completed in GZ019

---

### Task 4: StringInternMap and DrawTextLong (TASK-GZ019)

**Files to CREATE** in `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/`:
- `StringInternMap.cs`

**Files to CREATE** in `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Network/`:
- `StringInternBatch.cs`

**MODIFY** `DebugPrimitiveBuffer.cs` — replace the `NotImplementedException` in `DrawTextLong`
with the real implementation.

**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-gz019--stringinternmap-and-drawtextlong) for the full spec.

**Key points:**
- `StringInternMap`: `Dictionary<uint, string>` keyed by FNV-1a hash
- `Intern(uint hash, string fullText)` — idempotent (skip if already registered)
- `TryResolve(uint hash)` — returns null if not found (renderer falls back to FixedString32 preview)
- FNV-1a 32-bit: `uint h = 2166136261; foreach(char c in text) { h ^= c; h *= 16777619; } return h;`
- `DrawTextLong` in `DebugPrimitiveBuffer`:
  1. Compute FNV-1a hash of the string
  2. Call `_internMap.Intern(hash, text)` (pass `_internMap` reference)
  3. Fill `FixedString32` with first 31 chars (preview)
  4. Set `StringHash = hash` in the primitive header at offset 8
- `StringInternBatch` DDS topic — see TASK-DETAIL.md for the struct definition
- `DebugPrimitiveBuffer` constructor must accept `StringInternMap internMap` parameter
  (or create one internally if null passed — use an internal default)

**Tests Required (TASK-GZ019):**
- SC-GZ003-7: `DrawTextLong` with 60-char string → `StringHash != 0`, preview holds first 31 chars
- SC-GZ003-8: Same string twice → same `StringHash`
- SC-GZ003-9: `DrawText(FixedString32)` → `StringHash == 0`
- SC-GZ019-1 through all success conditions in TASK-DETAIL.md TASK-GZ019 section

---

## 🧪 Testing Requirements

**Test project:** `FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj`

**Minimum:** Cover all success conditions listed in TASK-DETAIL.md for each task (SC-GZ001-x,
SC-GZ002-x, SC-GZ003-x, SC-GZ019-x).

**Test naming convention:** `{TypeName}_{Scenario}_{ExpectedOutcome}` (e.g.
`DebugPrimitive_LinePayload_DoesNotCorruptColor`)

**Quality bar:**
- Tests must verify **actual values** — field contents, sizes, hashes — not just that the object
  was constructed.
- Tests for `DebugPrimitive` must write specific values into payloads and read them back, verifying
  that adjacent fields are not corrupted.
- Offset isolation tests are mandatory: write to one field, read back, verify other fields unchanged.
- Do NOT write tests that only check `Assert.NotNull` or `Assert.IsType`.

---

## ⚠️ Quality Standards

**❗ STRUCT LAYOUT:**
- `DebugPrimitive` must be exactly 64 bytes. Use `Marshal.SizeOf<DebugPrimitive>()` to verify in a test.
- Every `[FieldOffset]` must be declared explicitly — no implicit padding.
- `FixedString32` at offset 32: verify `Marshal.SizeOf<FixedString32>() == 32` in a test.

**❗ NO ALLOCATIONS ON HOT PATH:**
- `DebugPrimitiveBuffer.DrawLine`, `DrawSphere`, `DrawArrow`, `DrawText` must not allocate.
- `DrawTextLong` IS allowed to allocate on the intern-registration path (cold path only; subsequent
  calls with the same string must not allocate).

**❗ TEST QUALITY:**
- NOT ACCEPTABLE: Tests that only verify the object was constructed
- REQUIRED: Tests that verify field values, sizes, hash values, and cross-field isolation

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] TASK-GZ001 completed: all 7 type files created, all SC-GZ001-x tests pass
- [ ] TASK-GZ002 completed: `DebugPrimitive.cs` created, all SC-GZ002-x tests pass
- [ ] TASK-GZ003 completed: `IDebugDrawBuilder.cs` + `DebugPrimitiveBuffer.cs` created, all SC-GZ003-x tests pass
- [ ] TASK-GZ019 completed: `StringInternMap.cs` + `StringInternBatch.cs` created, `DrawTextLong` wired, all SC-GZ019-x tests pass
- [ ] `dotnet build IOS-IG-SimHost.sln` succeeds with no errors
- [ ] `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj` passes with no failures
- [ ] Report submitted to `.dev/gizmos-1/reports/BATCH-01-REPORT.md`

---

## 📊 Report Requirements

Create `.dev/gizmos-1/reports/BATCH-01-REPORT.md` with the following:

```markdown
# BATCH-01 Report

**Batch:** BATCH-01
**Developer:** [Your identifier]
**Date:** [Date]
**Status:** COMPLETED

## Tasks Completed
- [ ] TASK-GZ001
- [ ] TASK-GZ002
- [ ] TASK-GZ003
- [ ] TASK-GZ019

## Test Results
[Paste dotnet test output showing pass count]

## Developer Insights

**Q1:** What issues did you encounter during implementation? How did you resolve them?

**Q2:** Did you spot any weak points or design ambiguities? What would you change?

**Q3:** What design decisions did you make beyond the spec? What alternatives did you consider?

**Q4:** What edge cases did you discover that weren't mentioned in the spec?

**Q5:** Are there any performance concerns or optimization opportunities you noticed?

**Suggested commit message:** [your suggestion]
```

---

## 📚 Reference Materials

- **Task Defs:** `.dev/gizmos-1/TASK-DETAIL.md` — TASK-GZ001, TASK-GZ002, TASK-GZ003, TASK-GZ019
- **Design:** `.dev/gizmos-1/DESIGN.md` — §1.1, §1.2, §1.3
- **Onboarding:** `.dev/gizmos-1/ONBOARDING.md` — folder layout, build commands
- **Existing types:** `FDP/Engine/Fdp.Core/Entity.cs`, `FDP/Engine/Fdp.Core/Text/FixedString32.cs`
- **DDS topic pattern:** Browse `FDP/Network/Fdp.Network.Cyclone/` for existing `[DdsTopic]` examples
- **Test patterns:** Browse `FDP/Toolkits/Fdp.Toolkits.Tests/` for existing test file conventions
