# BATCH-08: Remaining Debt Resolution (D-05, D-06, D-07, D-16)

**Batch Number:** BATCH-08
**Tasks:** D-05 fix, D-06 fix, D-07 fix, D-16 fix
**Estimated Effort:** 6-10 hours
**Priority:** P3 + P2 debt items
**Dependencies:** All prior batches (BATCH-01 through BATCH-07)

---

## Onboarding & Workflow

### Required Reading (IN ORDER)

1. **Developer Skill:** `.github\skills\developer\SKILL.md`
2. **Debt tracker:** `.dev\visual-asset-comparison\DEBT-TRACKER.md`
3. **Existing HSM sanitizer + tests:**
   - `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Comparison/HsmComparisonSanitizer.cs`
   - `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Comparison/HsmComparisonSanitizerTests.cs`
4. **Existing Blackboard sanitizer + tests:**
   - `Hrot/Editor/Hrot.Editor.AiShared/Comparison/BlackboardComparisonSanitizer.cs`
   - `Hrot/Editor/Hrot.Editor.AiShared.Tests/Comparison/BlackboardComparisonSanitizerTests.cs`
5. **VariablesPanelControl (D-16 target):**
   - `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/VariablesPanelControl.cs`
6. **BlackboardComparisonDecorator (D-16 helper):**
   - `Hrot/Editor/Hrot.Editor.AiShared/Comparison/BlackboardComparisonDecorator.cs`
7. **BlackboardAuthoringWindow (D-16 integration point):**
   - `Hrot/Editor/Hrot.Editor.AiShared/Windows/BlackboardAuthoringWindow.cs`

### Test Execution

```powershell
dotnet test "Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj" -c Debug
dotnet test "Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Hrot.Hsm.Editor.Tests.csproj" -c Debug
dotnet build "IOS-IG-SimHost.sln" -c Debug --no-restore -maxcpucount:4
```

### Report Submission

Submit to: `.dev\visual-asset-comparison\reports\BATCH-08-REPORT.md`

---

## D-05 FIX — HSM state with stableId comment AND visualId transition test

**Source:** BATCH-02 developer note  
**Priority:** P3

**Background:** The HSM sanitizer injects stable-ID comments and handles visualId transitions. The existing tests do not cover the case where a single state has BOTH a stableId comment injected AND a visualId-based transition on that same state — verifying that neither injection is confused with the other or causes duplication.

**Test to add (extend `HsmComparisonSanitizerTests.cs`):**

```
StableId_And_VisualId_SameState_NeitherConfused
```

**Setup:** An HSM C# fixture (write inline or as a small string) with:
- A state `Patrol` that has both:
  - An explicit `stableId = "patrol-01"` comment (or attribute — check how existing fixtures define stableIds)
  - An outgoing transition `-> Chase` where the transition uses a `visualId` or visual weight parameter

**Assertions:**
1. The sanitized output contains exactly one `stableId` comment for `Patrol` (no duplication).
2. The sanitized output contains the `Chase` transition exactly once.
3. Sanitize twice — output is byte-identical (determinism property).

Note: Read the existing HSM fixture files and tests to understand the exact fixture format expected by `HsmComparisonSanitizer`. Use the same format.

---

## D-06 FIX — Blackboard `AssetId:` header form untested

**Source:** BATCH-02 developer note  
**Priority:** P3

**Background:** `BlackboardComparisonSanitizer` handles two header forms: `AssetId:` and `OwningAssetId:`. Existing tests only cover `OwningAssetId:`. The `AssetId:` form must also be tested.

**Test to add (extend `BlackboardComparisonSanitizerTests.cs`):**

```
AssetIdHeader_Form_SanitizesCorrectly
```

**Setup:** A Blackboard C# fixture (inline string) that uses the `// AssetId: {guid}` header format instead of `// OwningAssetId: {guid}`. This is the main-asset form of the header (as opposed to the companion form).

**Assertions:**
1. `result.SanitizedText` is non-empty (sanitizer did not fail).
2. `result.SanitizedText` does NOT contain the raw GUID string (it is normalized/stripped or kept as-is — check what `BlackboardComparisonSanitizer` does with the header; verify the output matches).
3. No exception thrown.

---

## D-07 FIX — HSM 3-level nested Child call test

**Source:** BATCH-02 developer note  
**Priority:** P3

**Background:** The brace-depth scanner in `HsmComparisonSanitizer` (or `BTreeComparisonSanitizer`) could misidentify the opener in deeply nested cases with 3 levels of `Child { ... }` nesting. Existing tests have at most 2 levels.

**Test to add (extend `HsmComparisonSanitizerTests.cs`):**

```
ThreeLevelNestedChild_AllLevelsExtracted
```

**Setup:** An HSM C# fixture (inline string) with 3-level nesting:
```
// State A
//   Child:
//     State B
//       Child:
//         State C
//           Child:
//             State D
```
(Use the actual HSM C# syntax — see existing HSM fixtures for the pattern.)

**Assertions:**
1. Sanitized text contains entries for State A, B, C, and D (all levels extracted without confusion).
2. No exception thrown.
3. Byte-identical across two sanitize calls.

---

## D-16 FIX — BlackboardAuthoringWindow per-row comparison decoration

**Source:** BATCH-06 developer debt  
**Priority:** P2

**Background:** In BATCH-06, the Blackboard Variables panel integration (C-26) added a `BlackboardComparisonDecorator` that can determine per-field decorations. However, the decorations are currently shown in a separate section below the main variables table, not inline per row as §6.7 specifies.

**Required change:** Add an optional decoration callback to `VariablesPanelControl.DrawSingle` and thread it through to the per-row table rendering.

### Step 1: Add callback parameter to VariablesPanelControl

In `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/VariablesPanelControl.cs`:

1. Add an optional parameter to `DrawSingle`:
```csharp
public void DrawSingle(VariablesPanelSection section,
    Func<string, FieldDecoration?>? rowDecoration = null)
```

2. Pass `rowDecoration` through to `DrawSection` and `DrawTable`:
```csharp
private void DrawSection(VariablesPanelSection section,
    Func<string, FieldDecoration?>? rowDecoration = null)
```
```csharp
private void DrawTable(VariablesPanelSection section,
    Func<string, FieldDecoration?>? rowDecoration = null)
```

3. In `DrawTable`, inside the per-row loop, after `ImGui.TableNextRow()` and before drawing the name column:
```csharp
FieldDecoration? dec = rowDecoration?.Invoke(row.Name);
if (dec != null)
{
    // Apply row background tint for decorated variables.
    // Use TableSetBgColor to color the row.
    uint rowColor = 0;
    if (dec.IsAdded)        rowColor = ImGui.GetColorU32(new Vector4(0.2f, 0.8f, 0.2f, 0.15f));
    else if (dec.IsRemoved) rowColor = ImGui.GetColorU32(new Vector4(0.9f, 0.2f, 0.2f, 0.15f));
    else if (dec.IsRetyped) rowColor = ImGui.GetColorU32(new Vector4(0.3f, 0.5f, 1.0f, 0.15f));
    else if (dec.IsRenamed) rowColor = ImGui.GetColorU32(new Vector4(1.0f, 0.85f, 0.3f, 0.15f));
    if (rowColor != 0)
        ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, rowColor);
}
```

Also update `DrawDual` to accept and pass through `rowDecoration`:
```csharp
public void DrawDual(VariablesPanelSection topSection, VariablesPanelSection bottomSection,
    Func<string, FieldDecoration?>? rowDecoration = null)
```

### Step 2: Update BlackboardAuthoringWindow to pass the callback

In `Hrot/Editor/Hrot.Editor.AiShared/Windows/BlackboardAuthoringWindow.cs`:

Find the call to `_variablesControl.DrawSingle(section)` (or `DrawDual`). Update it to pass a decoration callback:

```csharp
Func<string, FieldDecoration?>? dec = null;
var session = _sessionRegistry?.GetSession(CurrentAssetId);
if (session != null)
    dec = fieldName => BlackboardComparisonDecorator.GetDecoration(fieldName, session);

_variablesControl.DrawSingle(section, dec);
```

Where `CurrentAssetId` is the Guid property added in BATCH-05.

### Step 3: Remove the old separate decoration section

The BATCH-06 integration added a separate section below the variables table. Find that code block in `BlackboardAuthoringWindow.cs` (it was added after `_variablesControl.DrawSingle(section)`) and remove it. The per-row coloring from Step 1 replaces it.

**IMPORTANT:** The `FieldDecoration` type lives in `Hrot.Editor.AiShared.Comparison.BlackboardComparisonDecorator`. Make sure the `using` directive for that namespace is already present in `VariablesPanelControl.cs`. If not, add:
```csharp
using Hrot.Editor.AiShared.Comparison;
```

### Step 4: Tests for D-16

**New tests to add (`Hrot.Editor.AiShared.Tests/Comparison/BlackboardAuthoringWindowComparisonTests.cs`):**

The existing tests already test the `BlackboardComparisonDecorator.GetDecoration` static method. Add tests that verify the decoration data would produce correct row coloring:

- **Added_Decoration_HasGreenRowColor:** `GetDecoration("AmmoCount", session_with_variable_added)` returns `IsAdded == true`. When `IsAdded`, the expected row color is green-ish (verify the color logic in the decorator is called correctly — this is already covered by existing tests). Instead, add a NEW test:

  **`VariablesPanelControl_DrawSingle_NullDecoration_DoesNotThrow`**
  - Create a stub `VariablesPanelSection` with one variable.
  - Call `DrawSingle(section, rowDecoration: null)` — confirm it does not throw.
  - This is a compile-check + smoke test.

- **`VariablesPanelControl_DrawSingle_DecorationCallback_Invoked`**
  - Create a stub section with 2 variables named "Alpha" and "Beta".
  - Pass a `rowDecoration` callback that records which names were queried.
  - Call `DrawSingle(section, rowDecoration: name => { recordedNames.Add(name); return null; })`.
  - But wait — `DrawSingle` requires ImGui context for rendering. Use a test that verifies the `FieldDecoration` lookup logic instead.
  
  **Revised test:** Since `DrawSingle` calls `ImGui.*` methods which require an ImGui context (not available in unit tests), do NOT test `DrawSingle` directly. Instead, verify the `BlackboardComparisonDecorator.GetDecoration` results in the correct decorations (already done by existing tests). The "D-16 resolved" condition is met when:
  1. `DrawSingle` accepts the optional callback parameter (compile-time verification).
  2. The old separate section below the table is removed from `BlackboardAuthoringWindow`.

So for D-16 tests, just add:

**`DrawSingle_WithNullCallback_CompilationVerification`** — a trivial test asserting a `VariablesPanelControl` instance can be created (smoke test that the constructor signatures are correct after the refactor). This verifies the change doesn't break DI or existing callers.

Actually: DON'T add a trivial test. The test coverage for D-16 is already provided by the existing `BlackboardAuthoringWindowComparisonTests.cs` decorator tests (they test the logic). The D-16 change is structural (remove separate section, add callback parameter). No new test file needed — just verify the build succeeds and existing tests still pass.

---

## Mandatory Workflow

1. **D-05:** Read existing HSM fixture format → write test → run HSM tests ✅
2. **D-06:** Read existing Blackboard fixture format → write test → run Blackboard tests ✅
3. **D-07:** Read existing HSM fixture format → write 3-level nested test → run HSM tests ✅
4. **D-16:** 
   a. Read `VariablesPanelControl.cs` fully to understand `DrawTable` row loop
   b. Add `rowDecoration` callback parameter to `DrawSingle`, `DrawDual`, `DrawSection`, `DrawTable`
   c. Add `ImGui.TableSetBgColor` call inside the per-row loop when decoration is non-null
   d. Update `BlackboardAuthoringWindow` to build and pass the callback from the session registry
   e. Remove the old separate decoration section from `BlackboardAuthoringWindow`
   f. Run full solution build → 0 errors
5. Run all tests ✅

---

## Developer Insights (Answer in Report)

**Q1:** For D-05 (HSM stableId+visualId), what exact syntax does the HSM C# format use for stableId comments and visualId transitions? Show a snippet of the fixture you wrote.

**Q2:** For D-06 (Blackboard AssetId header), how does the `BlackboardComparisonSanitizer` treat the `AssetId:` header differently from `OwningAssetId:`? Are both stripped, preserved, or normalized?

**Q3:** For D-07 (3-level nested Child), did the brace-depth scanner correctly handle 3 levels without confusion? Did any assertions fail initially?

**Q4:** For D-16 (per-row decoration), how many existing callers of `DrawSingle` / `DrawDual` did you find? Did any of them need to be updated to use the new optional parameter?

**Q5:** Any new debt items discovered?

---

## Success Criteria

- [ ] D-05: 1 new test in `HsmComparisonSanitizerTests` passes
- [ ] D-06: 1 new test in `BlackboardComparisonSanitizerTests` passes
- [ ] D-07: 1 new test in `HsmComparisonSanitizerTests` passes
- [ ] D-16: `VariablesPanelControl.DrawSingle` + `DrawDual` accept optional `Func<string, FieldDecoration?>` callback; `BlackboardAuthoringWindow` passes callback from session; old separate section removed; full build 0 errors
- [ ] All existing tests still pass (no regressions)
- [ ] `dotnet build "IOS-IG-SimHost.sln" -c Debug --no-restore -maxcpucount:4` — 0 errors
- [ ] Report submitted to `.dev\visual-asset-comparison\reports\BATCH-08-REPORT.md`

---

## Reference Materials

- **Debt items:** `.dev\visual-asset-comparison\DEBT-TRACKER.md`
- **HSM sanitizer:** `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Comparison/HsmComparisonSanitizer.cs`
- **Blackboard sanitizer:** `Hrot/Editor/Hrot.Editor.AiShared/Comparison/BlackboardComparisonSanitizer.cs`
- **VariablesPanelControl:** `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/VariablesPanelControl.cs`
- **BlackboardComparisonDecorator:** `Hrot/Editor/Hrot.Editor.AiShared/Comparison/BlackboardComparisonDecorator.cs`
- **BlackboardAuthoringWindow:** `Hrot/Editor/Hrot.Editor.AiShared/Windows/BlackboardAuthoringWindow.cs`
