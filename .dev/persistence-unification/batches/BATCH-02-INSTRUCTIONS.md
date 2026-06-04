# BATCH-02: Emit-core extraction (netstandard2.0) + emit-test re-base
**Tasks:** PU-101, PU-105 (emit-core re-base portion)  **Phase:** 1 (JSON substrate; keystone)  **Est:** ~11h
**Dependencies:** BATCH-01 (the persisted DTOs + mappers in `Hrot.AiEditor.Persistence` — the emit core consumes the DTO).

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — your working contract.
2. `.dev/persistence-unification/BTree_HSM_JSON_Persistence_Detailed_Design.md` — read **§2.2** (emitters consume the in-memory model; `BTreeFluentEmitter.cs:441 EmitBuild`, `HsmFluentEmitter.cs:319 EmitCompile`; `FluentCSharpEmitterBase` holds the `HROT_EDITOR_GENERATED` marker + `WriteAtomic`), **§6.1** (emit core relocation — takes the persisted DTO, returns the C# string for `CreateBuilder()` + the `[BTreeDefinition]`/`[HsmDefinition]` thunk + `[*Layout]`), **§6.4** (determinism & test reuse). Cite it; don't re-derive.
3. `.dev/persistence-unification/TASK-DETAIL.md` — **PU-101** success conditions (read verbatim — the byte-identical gate is exact), and PU-105.
4. `.dev/persistence-unification/reviews/BATCH-01-REVIEW.md` + `reports/BATCH-01-REPORT.md` — the DTO/mapper shape you build on.
5. Codebase Memory MCP first; never `search_code`.

## Goal (PU-101)
Extract the deterministic C# emission logic out of the net8 editor emitters into a **`netstandard2.0` emit core** that takes a **persisted DTO** (`Hrot.AiEditor.Persistence` `BehaviorTreeAssetDto`/`HsmAssetDto`, BATCH-01) and returns the C# string for `CreateBuilder()` + the `[BTreeDefinition]`/`[HsmDefinition]` thunk + the `[BTreeLayout]`/`[HsmLayout]` method. **This is a relocation of existing, tested logic — not a rewrite.** The net8 editor emitters (`BTreeFluentEmitter`/`HsmFluentEmitter`) become **thin adapters** that map their model → DTO (BATCH-01 mapper) → call the core. Output must be **byte-identical** to today.

## Verify-first (cite findings in the report)
- Read `BTreeFluentEmitter` / `HsmFluentEmitter` / `FluentCSharpEmitterBase` / `IFluentCSharpEmitter<T>` in full. Identify EVERY field the emitters read from the model. **Confirm the BATCH-01 DTO carries all of them** (it should — the emitter uses only persisted fields). If a field needed for byte-identical output is missing from the DTO/mapper, **extend the DTO + mapper** (in `Hrot.AiEditor.Persistence` + the editor mapper) AND add a field-by-field round-trip assertion for it (this is an allowed re-touch of BATCH-01 files; note it in the report).
- Decide the emit-core home: a `netstandard2.0` lib with no editor/ImGui dep. Recommended: place it in `Hrot.AiEditor.Persistence` (DTO + JSON + emit together, all generator-consumable) OR a sibling `Hrot.AiEditor.EmitCore` that references `Hrot.AiEditor.Persistence`. `FluentCSharpEmitterBase` (marker constant + `WriteAtomic`) relocates into the core too (it has no ImGui dep — verify). Record the choice + why.

## Tasks (complete in sequence; do NOT start the next until the current's tests pass.)

### Task 1 — PU-101: relocate emit logic to a `netstandard2.0` emit core — files: new core lib + refactored `BTreeFluentEmitter.cs`/`HsmFluentEmitter.cs`/`FluentCSharpEmitterBase.cs`
- Move the deterministic emission (CreateBuilder + thunk + `[*Layout]` + the `HROT_EDITOR_GENERATED` marker + `WriteAtomic` byte-identical-skip) into the core, parameterized by the persisted DTO.
- Editor emitters become adapters: `Emit(model)` → `mapper.ToDto(model)` → `core.Emit(dto)`. Public API/behavior of the editor emitters is unchanged (callers — `AiAssetEmitService` etc. — keep working).
- The core must reproduce the **exact** current text including the `[HsmDefinition(... AssetId = AssetId)]` const form and the `[*Layout]` method.
**Tests required (the byte-identical gate — this is the crux):** a parametrized test over **every** existing editor-owned fixture asset (`Trees/*.cs` + `Machines/*.cs` under `Hrot.AI.Behaviors`): load the model (current reflection/projector path), then assert `core.Emit(mapper.ToDto(model))` is **byte-identical** to the current `BTreeFluentEmitter.Emit(model)` / `HsmFluentEmitter.Emit(model)` output (including `[*Layout]` and the const `AssetId` form). Also assert `WriteAtomic` returns `false` (no write) when content is byte-identical.

### Task 2 — PU-105 (remainder): re-base `SaveBTreeEmitTests` / `SaveHsmEmitTests` onto the emit core — files: those test files (UPDATE)
Re-point `SaveBTreeEmitTests`/`SaveHsmEmitTests` to assert the **emit core**'s output (via the adapter or the core directly). They must stay green (or, if a deliberate re-baseline is unavoidable, document exactly what changed and why in the report — but byte-identical is the target, so they should NOT need re-baselining).
**Tests required:** the existing determinism scenarios pass against the core; no loss of coverage.

## Success Criteria
- [ ] PU-101: emit core is `netstandard2.0`, no editor/net8/ImGui ref (verify project refs — record). Editor emitters are thin adapters; their public behavior unchanged.
- [ ] **Byte-identical gate:** `core.Emit(ToDto(model))` == current emitter output for **every** `Trees/*.cs` + `Machines/*.cs` fixture, incl. `[*Layout]` + const `AssetId`. `WriteAtomic` no-op preserved.
- [ ] PU-105: `SaveBTreeEmitTests`/`SaveHsmEmitTests` green against the core (or documented reviewed re-baseline).
- [ ] Any DTO/mapper extension needed for byte-identity is added with a round-trip assertion (BATCH-01 tests still green).
- [ ] Global gate: `dotnet build IOS-IG-SimHost.sln` 0 errors / 0 new warnings (touched); `EditorSubsystemBoot` 10/10; `Hrot.AiEditor.Persistence.Tests` green (BATCH-01, 75+); `Hrot.Editor.AiShared.Tests` green; `Hrot.Blueprints.Tests` only the pre-existing failures (0 new). **Report exact counts + classification.**
- [ ] Report → `.dev/persistence-unification/reports/BATCH-02-REPORT.md`.

## Report Requirements
The emit-core home + why; every model field the emitters read and confirmation the DTO covers it (or which DTO/mapper extensions you made + the added round-trip assertions); whether the byte-identical gate passed first try or required adjustments (what + why); whether `SaveBTree/HsmEmitTests` needed any re-baseline; weak points; suggested commit message. No comprehension questions.

## Constraints
Branch `blueprint-integ-1`. GizmoMap.Contracts 0.2.2. No `Hrot.IG`/DDS/`Stride/`. No `editor_stride`. **Zero behavior change** — relocation only; no load-path switch, no generator, no `.cs` decommit (those are PU-201+/PU-301+). Don't touch the Blueprint path. Do NOT commit (the lead commits).
