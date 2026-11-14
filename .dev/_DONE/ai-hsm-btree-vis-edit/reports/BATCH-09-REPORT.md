# BATCH-09 REPORT

**Batch:** BATCH-09
**Status:** COMPLETED
**Developer:** Coder Sub-agent

---

## Summary

All four tasks (1d-03, 1d-04, 1f-03, 1f-04) implemented, tested, and passing.

| Project | Tests Before | Tests After |
|---------|-------------|-------------|
| Hrot.BTree.Editor.Tests | 212 | 218 (+6) |
| Hrot.Hsm.Editor.Tests | 206 | 211 (+5) |
| Hrot.Editor.AiShared.Tests | ~337 | 365 (+28) |
| **Total new** | | **+39** |

Build: 0 errors, 0 warnings.

---

## Tasks Implemented

### TASK-BB-1d-03 — BTree Orchestrator Emit

**File created:** `Hrot/Subsystems/AI/Hrot.BTree.Editor/Emit/BTreeOrchestratorEmitter.cs`

- `public static string? Emit(BehaviorTreeAsset asset)` — pure, stateless, no I/O.
- Iterates `BlackboardVariables`, calls `GetAliasesFor` per variable.
- Deduplicates on `(variableName, subTreeName)` using a `HashSet<string>` with a `\x1F` separator.
- Collects usings deterministically via `FluentCSharpEmitterBase.SortUsings`.
- Emits four-line header: standard marker + two info lines + OwningAssetId + OwningAssetName.
- Returns `null` when no aliases exist.
- `WriteOrchestratorFile(BehaviorTreeAsset, string?)` static helper sits in the same file; uses `WriteAtomic`; no-ops on null content.

**Tests:** `BTreeOrchestratorEmitterTests.cs` — 6 tests covering: null-on-no-aliases, method presence, deduplication, two-distinct-methods, determinism, editor-marker prefix.

---

### TASK-BB-1d-04 — HSM Orchestrator Emit

**File created:** `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Emit/HsmOrchestratorEmitter.cs`

- Same structural pattern as `BTreeOrchestratorEmitter` but uses `[HsmAction]` attribute and includes `Fhsm.Kernel.Attributes` using.
- `BlackboardTypeName` property added to `HsmAsset`:
  ```csharp
  public string BlackboardTypeName { get; set; }
  ```
  Initialized in the constructor as `$"{SanitizeIdentifier(Name)}_Blackboard"`.
- Sub-tree tick signature uses `BehaviorTreeState` and `BTreeContext` (identical to BTree flavor) because the sub-asset is always a BTree.

**Tests:** `HsmOrchestratorEmitterTests.cs` — 5 tests: null-on-no-aliases, HsmAction attribute present, determinism, BlackboardTypeName default value, two-distinct-methods.

---

### TASK-BB-1f-03 — Unused-Variable Diagnostic + Glyph

**File created:** `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/BlackboardDiagnosticCode.cs`
- `UnusedVariable` and `VariableTypeNotFound` codes only. No premature additions.

**Modified:** `Hrot/Editor/Hrot.Editor.AiShared/Windows/BlackboardAuthoringWindow.cs`
- `VariableViewModel` record extended with `bool IsUnused` as the last property (positional).
- `BuildViewModel` populates `IsUnused` via `bbAsset.CountNodesReferencingVariable(v.Name) == 0`.
- `DrawClientArea` row-drawing loop: unused variables render with ASCII `o ` glyph at 60% alpha (`ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.6f)` / `PopStyleVar()`). Tooltip: `"Not referenced by any node -- consider removing."`.

**Tests:** `UnusedVariableDiagnosticTests.cs` — 12 tests: `IsUnused` set/cleared, multi-var partial marking, ImGui render path validation via test-double asset.

---

### TASK-BB-1f-04 — "Remove Unused" Toolbar Action

**Modified:** `Hrot/Editor/Hrot.Editor.AiShared/Windows/BlackboardAuthoringWindow.cs`
- `[ Remove unused ]` button rendered in panel header when at least one `IsUnused == true`.
- Click opens `ImGui.OpenPopup("confirm_remove_unused")`.
- Confirmation modal shows `Remove N unused variables? This will free X bytes from the blackboard. This cannot be undone.` with `[ Remove ]` / `[ Cancel ]`.
- On confirm: calls `bbAsset.RemoveVariables(unusedNames)` — single call, fires `Changed` once.

**Modified:** `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/IBlackboardManagedAsset.cs`
- `void RemoveVariables(IReadOnlyList<string> names)` added with XML doc.

**Modified:** `Hrot/Subsystems/AI/Hrot.BTree.Editor/Model/BehaviorTreeAsset.cs`
- `RemoveVariables` implemented: removes each named variable, fires `Changed` once at end (or not at all if list is empty or no matches).

**Modified:** `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Model/HsmAsset.cs`
- Same implementation as `BehaviorTreeAsset.RemoveVariables`.

**Tests:** `RemoveVariablesTests.cs` — 16 tests covering removal, single-Changed-fire, empty-list no-op, alias-key cleanup, tested on both `BehaviorTreeAsset` and `HsmAsset` instances.

---

## Issues Encountered

1. **Stub visibility with `file sealed class`** — Several existing test files used `file sealed class` stubs which created method visibility issues when `IBlackboardManagedAsset` gained the `RemoveVariables` method. Fixed by updating all five existing stub implementations (`BlackboardAddRemoveTests.cs`, `BlackboardAliasingTests.cs`, `BlackboardAuthoringWindowTests.cs`, `BlackboardVariableWiringTests.cs`, `Windows/BlackboardAuthoringWindowTests.cs`) to implement the new method. The stubs remain as minimal no-ops for the new method where not being tested.

2. **xUnit2013 analyzer as hard error** — `TreatWarningsAsErrors=true` means `Assert.True(list.Count > 0)` fails to compile. All count assertions use `Assert.Empty` / `Assert.Single` / `Assert.Equal(N, ...)` to avoid `xUnit2013`.

3. **Unassigned struct fields** — A test helper struct had uninitialized fields; resolved by using field initializers.

---

## Design Decisions Beyond the Spec

1. **`\x1F` separator for dedup key** — The unit-separator character is not a legal C# identifier character, making the concatenated key unambiguous. This is safer than using `/` or `.` which could hypothetically appear in mangled names.

2. **`WriteOrchestratorFile` co-located with emitter** — The spec allowed it in `FluentCSharpEmitterBase` or in the emitter file. Co-location was chosen to keep the BTree-specific behavior in one file; no cross-cutting concern is introduced.

3. **`BlackboardTypeName` added to `HsmAsset` as `{ get; set; }`** — Spec required non-null with a computed default. Made it a settable property (matching the pattern on `BehaviorTreeAsset`) so editor tooling can override it if needed.

---

## Weak Points Spotted

1. **`CountNodesReferencingVariable` always returns 0 on stubs** — The real implementation is per-asset but untested at the integration level. The "unused" detection is only as good as the node graph traversal in the concrete assets, which currently returns 0 everywhere (stub-like). This is a pre-existing issue, not introduced in this batch.

2. **No cascade: removing a variable does not remove its alias bindings from *other* assets** — When `x` is removed from asset A, assets B and C that have alias bindings pointing to A's `x` are not notified. This is noted but out of scope for this batch.

3. **`Unsafe.As` projection in generated orchestrator** — The emitter writes `Unsafe.As<T, T>(ref v)` where both type arguments are the same DTO type. This is technically a no-op cast (correct for projection-by-layout), but a future type-alias or layout-change would silently produce wrong behavior. Worth a comment in the generated code, but spec says marker comment only.

---

## Test Count Summary

- `BTreeOrchestratorEmitterTests`: 6 tests
- `HsmOrchestratorEmitterTests`: 5 tests
- `UnusedVariableDiagnosticTests`: 12 tests
- `RemoveVariablesTests`: 16 tests
- **Total new:** 39 tests

All 218 BTree, 211 HSM, and 365 AiShared tests pass. Full solution build: 0 errors.
