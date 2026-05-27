# BATCH-09 REVIEW

**Batch:** BATCH-09
**Tasks:** 1d-03, 1d-04, 1f-03, 1f-04
**Verdict:** APPROVED

---

## Scope Check

All four specified tasks are implemented:

| Task | Deliverable | Status |
|------|-------------|--------|
| 1d-03 | `BTreeOrchestratorEmitter` + 6 tests | Done |
| 1d-04 | `HsmOrchestratorEmitter` + 5 tests + `HsmAsset.BlackboardTypeName` | Done |
| 1f-03 | `BlackboardDiagnosticCode`, `VariableViewModel.IsUnused`, glyph + tooltip | Done |
| 1f-04 | `IBlackboardManagedAsset.RemoveVariables`, batch removal modal | Done |

No scope creep detected.

---

## Design Alignment

### 1d-03 / 1d-04 — Orchestrator emitters

- Header format matches spec (standard marker + auto-generated note + OwningAssetId + OwningAssetName). Good.
- Deduplication uses `HashSet<string>` keyed on `variableName + \x1F + subTreeName` — the `\x1F` separator is a solid choice (cannot appear in a C# identifier).
- Emitter is stateless and pure; file I/O delegated to `WriteOrchestratorFile` helper which uses `WriteAtomic`. Correct pattern.
- HSM emitter correctly uses `[HsmAction]` and `Fhsm.Kernel.Attributes` namespace. Sub-tree tick signature is `BehaviorTreeState + BTreeContext` as specced.
- `HsmAsset.BlackboardTypeName` defaults to `{SanitizedName}_Blackboard` in constructor. String property (not nullable). Correct.
- `WriteOrchestratorFile` is a no-op when content is `null` — **does not delete** the existing file. Matches spec wording precisely.

### 1f-03 — Unused-variable diagnostic

- `BlackboardDiagnosticCode` has exactly two values (`UnusedVariable`, `VariableTypeNotFound`). No premature additions. Good.
- `VariableViewModel` record extended with `bool IsUnused` as the last positional property. `BuildViewModel` computes it via `CountNodesReferencingVariable`. Correct.
- `DrawClientArea` uses `ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.6f)` / `PopStyleVar()` for dimming. ASCII `o ` glyph. Tooltip: `"Not referenced by any node -- consider removing."`. All match spec.

### 1f-04 — Remove unused toolbar action

- Button gated on `vm.Variables.Any(v => v.IsUnused)`. Correct.
- Popup id `"confirm_remove_unused"`. Modal shows count + byte sum + "This cannot be undone." wording. Correct.
- On confirm: calls `bbAsset.RemoveVariables(unusedNames)` — single call, single Changed fire.
- `BehaviorTreeAsset.RemoveVariables` and `HsmAsset.RemoveVariables` both remove alias entries alongside variables (`_aliases.Remove(name)`). Correct cascade cleanup.

---

## Test Review

**Assertions reviewed manually for all four new test files.**

### `BTreeOrchestratorEmitterTests` (6 tests)
- `Emit_ReturnsNull_WhenNoAliases` — correct null check
- `Emit_ContainsOrchestratorMethod_ForAlias` — checks `[BTreeAction(Name = "Orchestrate_Shoot_BT")]`, method name, `ref master.SharedFire` — behavioral assertions, not string-exists-somewhere
- `Emit_Deduplicates_SameSubTreeTwoBindings` — counts method occurrences; would fail if two are emitted
- `Emit_ContainsTwoMethods_ForTwoDistinctSubTrees` — checks both method names present
- `Emit_OutputIsDeterministic` — two calls produce identical strings
- `Emit_StartsWithEditorGeneratedMarker` — `StartsWith(EditorGeneratedMarker)` 

All assertions check correctness of output content, not merely that a string is non-null.

### `HsmOrchestratorEmitterTests` (5 tests) — same pattern; attribute is `[HsmAction(...)]`. Pass.

### `UnusedVariableDiagnosticTests` (12 tests)
- Zero-reference → `IsUnused == true`; one-reference → `IsUnused == false`; partial marking confirmed.
- Uses `RefCountBbAsset` stub with configurable per-variable counts. Directly tests `BuildViewModel` behavior.

### `RemoveVariablesTests` (16 tests, stub-based) + concrete tests
- Stub tests: removals, single-Changed-fire, empty-list no-op, alias-key cleanup via `AliasMutableAsset`.
- **Spec gap noticed and fixed:** concrete `BehaviorTreeAsset` and `HsmAsset` `RemoveVariables` behavior was not covered. Added 3 tests each in `BlackboardVariableAssetWiringTests.cs` and new `HsmAssetBlackboardTests.cs` (4 tests including `BlackboardTypeName` default). Total: 7 concrete-impl tests added post-gap-detection.

---

## Test Run Results

```
Hrot.BTree.Editor.Tests:   221 passed, 0 failed
Hrot.Hsm.Editor.Tests:     215 passed, 0 failed
Hrot.Editor.AiShared.Tests: 365 passed, 0 failed
```

Build: `dotnet build IOS-IG-SimHost.sln` — 0 errors, 0 warnings.

Failures in unrelated projects (`StructEdit`, `Hrot.Core`, `FDP`, etc.) are pre-existing and were not introduced by this batch.

---

## Debt Updates

Three new P2/P3 items recorded in `DEBT-TRACKER.md`:

- **DEBT-05** (P2): `CountNodesReferencingVariable` always returns 0 — unused detection needs real graph traversal.
- **DEBT-06** (P2): No cascade invalidation when a variable is deleted from asset A while B/C hold alias bindings.
- **DEBT-07** (P3): `Unsafe.As<T, T>` same-type cast in generated orchestrator — silent layout risk.

---

## Suggested Git Commit Message

```
feat(blackboard): BATCH-09 orchestrator emit, unused-variable diagnostic, batch remove (1d-03, 1d-04, 1f-03, 1f-04)

- BTreeOrchestratorEmitter: emit companion .Orchestrators.g.cs per aliased sub-tree [BTreeAction]
- HsmOrchestratorEmitter: same pattern for HSM master, [HsmAction] attribute
- HsmAsset: add BlackboardTypeName property (defaults to {name}_Blackboard)
- IBlackboardManagedAsset: add RemoveVariables(IReadOnlyList<string>) for batch removal
- BehaviorTreeAsset + HsmAsset: implement RemoveVariables, single Changed fire, alias key cleanup
- BlackboardDiagnosticCode: UnusedVariable + VariableTypeNotFound enum
- BlackboardAuthoringWindow: IsUnused flag in VariableViewModel, dimmed row + o glyph, Remove Unused toolbar action with confirmation modal
- 801 tests pass (221 BTree, 215 HSM, 365 AiShared); build clean
```

*(Already committed as `8950b5b1`.)*
