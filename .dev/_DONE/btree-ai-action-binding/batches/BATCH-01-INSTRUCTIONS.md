# BATCH-01: bool MarshalAs fix + Category-1 DTO reflection
**Tasks:** S1-0, S1-1   **Phase:** Slice 1   **Est:** ~10h
**Dependencies:** none (both editor-side, no codegen)

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — your working contract.
2. `docs/blueprints/BTree_AiActionParameterBinding_Detailed_Design.md` ("AIB-DD") §3.1 and §3.2 — the spec. Do not re-derive.
3. `.dev/_DONE/btree-ai-action-binding/TASK-DETAIL.md` §S1-0 and §S1-1 — the exact success conditions / named test specs you must implement. **Implement those tests with those assertions exactly; do not invent your own acceptance criteria.**
4. `.dev/_DONE/btree-ai-action-binding/SLICE1-DESIGN.md` §3.1, §9 (Q6).

Use the codebase-memory MCP (`list_projects` → `get_architecture` → `search_graph` / `get_code_snippet`) FIRST for exploration, per `.claude/CLAUDE.md`. Use `read_file` only to read exact content you will edit.

**Complete tasks in sequence; do NOT start Task 2 until Task 1's implementation is done, its tests are written, and ALL tests (including prior projects') pass.**

---

## Task 1: bool `[MarshalAs(UnmanagedType.I1)]` fix (S1-0) — file: `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/BlackboardDtoEmitter.cs` (UPDATE)

**Problem (latent bug):** `EmitEditorManagedField` (around line 309) emits a bare `public bool B;`. When such a struct is projected at runtime via `Marshal.OffsetOf`, `bool` defaults to a 4-byte Win32 `BOOL`. But the editor bin-packer (`BlackboardBinPacker.cs`, `PrimitiveSizes`) already counts `bool` as **1 byte**. So the emitted struct's real field offsets silently drift from the bin-packer's advisory offsets → broken projection + replay schemas. AIB-DD §3.2.

**Fix:** in `EmitEditorManagedField`, when `field.FieldType == typeof(bool)`, emit a line `    [MarshalAs(UnmanagedType.I1)]` immediately before the field declaration (after any `/// <summary>` comment, before the `public bool ...` line). `System.Runtime.InteropServices` is already in the using set (added unconditionally at line 141). Do NOT touch `ReadOnlyFieldEntry` (verbatim passthrough — author owns its attributes). Apply the same to the heavy path (`EmitHeavy`) since it reuses `EmitEditorManagedField`.

**Edge cases:** only `bool` needs this (other primitives marshal at their managed size). Keep emission deterministic. Do not change field ordering or the marker block.

**Tests required** (`Hrot.Editor.AiShared.Tests`, new file `Blackboard/BlackboardDtoEmitterMarshalTests.cs` or add to existing `BlackboardDtoEmitterTests`):
- `Emit_BoolField_CarriesMarshalAsI1`: build a `BlackboardDtoModel` with fields `{ int A; bool B; int C }` (use `EditorManagedFieldEntry`). Assert the emitted source contains `[MarshalAs(UnmanagedType.I1)]` on the line immediately preceding `public bool B;`. Then **compile the emitted struct in-test with Roslyn** (mirror however existing emitter tests compile generated source — check `Emit/` test helpers), reflect the compiled type, and assert: `Marshal.OffsetOf(t, "C") == BlackboardBinPacker` offset for `C`, and `Marshal.SizeOf(t)` equals the packer's total packed size for `{A,B,C}`. The point is to prove `B` occupies 1 byte (offset of `C` == 8), not 4 (would be 12).
- Keep all existing `BlackboardDtoEmitterTests` green (a non-bool struct must emit byte-identically to before — no stray attribute).

---

## Task 2: Category-1 DTO read-only reflection in Variables panel (S1-1) — files under `Hrot/Editor/Hrot.Editor.AiShared` (+ `Hrot.BTree.Editor` consumers) (UPDATE)

**Goal:** when a node binds a *hardcoded* action whose param-0 DTO is NOT an editor-managed variable, surface that DTO's fields **read-only** in the Variables panel. Editor-only; NO codegen. AIB-DD §3.1; SLICE1-DESIGN §3.1, §9 (Q6).

**Steps:**
1. Locate `ActionSchemaExporter` and `ActionSchemaEntry` (search the graph). Confirm/derive that the **first `ref` param type** of a registered action method `M(ref FooDto, ref BehaviorTreeState, ref BTreeContext)` is exposed as `ActionSchemaEntry.DtoType`. If the exporter does not yet expose `DtoType` + enumerated public fields, add that (minimal, read-only).
2. In the Variables panel view-model (`VariablesPanelControl` / its VM in `Hrot.Editor.AiShared`), when an asset's node binds such an action, list that DTO's public fields with `IsReadOnly == true`, kept **separate** from the editable managed-variable set (do not let them enter the editable collection or round-trip into the emitter).

**Edge cases:** an action whose DTO type IS already an editor-managed variable must NOT be double-listed (managed wins). Actions with no ref-DTO param (offset-less) contribute nothing. Read-only fields must never be persisted/emitted.

**Tests required** (`Hrot.Editor.AiShared.Tests`):
- `ActionSchema_ReflectsFirstRefParamDto`: register/exercise an action `M(ref FooDto, ref BehaviorTreeState, ref BTreeContext)`; assert `ActionSchemaExporter` yields an entry with `DtoType == typeof(FooDto)` and its public fields enumerated (names + types).
- `VariablesPanel_ReflectsHardcodedDto_ReadOnly`: given an asset whose node binds such an action, assert the panel VM lists `FooDto`'s fields with `IsReadOnly == true`, and those fields are NOT present in the editable managed-variable set.

---

## Success Criteria
- [ ] S1-0: `Hrot.Editor.AiShared` builds 0 errors; `Emit_BoolField_CarriesMarshalAsI1` passes (Roslyn-compiled offset assertions); existing emitter tests green.
- [ ] S1-1: `Hrot.Editor.AiShared` + `Hrot.BTree.Editor` build 0 errors; `ActionSchema_ReflectsFirstRefParamDto` + `VariablesPanel_ReflectsHardcodedDto_ReadOnly` pass.
- [ ] Full `Hrot.Editor.AiShared.Tests` (+ `Hrot.BTree.Editor.Tests` if touched) suite: 0 net-new failures vs baseline.
- [ ] Report submitted.

You must run the tests and fix root causes to completion **without asking permission**, then report. Only stop on a breaking design flaw (a direct design↔codebase contradiction) — describe it in the report.

## Report Requirements (`.dev/_DONE/btree-ai-action-binding/reports/BATCH-01-REPORT.md`)
Answer: issues encountered; weak points spotted; any design decisions beyond spec (e.g. how you extended `ActionSchemaExporter`); edge cases discovered; how you compiled the emitted struct in-test; whether the bin-packer's `bool=1` assumption now matches `Marshal.OffsetOf` of the emitted struct (state the measured offsets); a suggested commit message. Do NOT ask comprehension questions.
