# BATCH-02: HSM and Blackboard Sanitizers + D-02 Debt Fix

**Batch Number:** BATCH-02
**Tasks:** TASK-C-05, TASK-C-06, TASK-C-07 + D-02 debt fix
**Slice:** C-2 — HSM and Blackboard sanitizers
**Estimated Effort:** 12-16 hours
**Priority:** HIGH
**Dependencies:** BATCH-01 (framework interfaces and BTree sanitizer must be in place)

---

## Onboarding & Workflow

### Required Reading (IN ORDER)

1. **Developer Skill:** `.github\skills\developer\SKILL.md`
2. **Design Document:** `.dev\visual-asset-comparison\Visual_Asset_Comparison_Detailed_Design.md` — pay special attention to §3.3 (HSM-specific notes at the end), §3.4 (Blackboard sanitization), §10.3
3. **Task Details:** `.dev\visual-asset-comparison\TASK-DETAILS.md` — read TASK-C-05 through TASK-C-07 sections in full
4. **BATCH-01 Review:** `.dev\visual-asset-comparison\reviews\BATCH-01-REVIEW.md` — understand the BTree pattern you are mirroring
5. **BATCH-01 source (study these):**
   - `Hrot/Subsystems/AI/Hrot.BTree.Editor/Comparison/BTreeComparisonSanitizer.cs` — the reference implementation to follow
   - `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Comparison/BTreeComparisonSanitizerTests.cs` — test structure to follow
6. **Debt Tracker:** `.dev\visual-asset-comparison\DEBT-TRACKER.md` — see D-02 (the debt item this batch also fixes)

### Source Code Locations

| What | Path |
|------|------|
| HSM sanitizer (NEW) | `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Comparison/HsmComparisonSanitizer.cs` |
| HSM DI extension (NEW) | `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Comparison/HsmEditorComparisonServiceCollectionExtensions.cs` |
| HSM tests (NEW sub-folder) | `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Comparison/` |
| Blackboard sanitizer (NEW) | `Hrot/Editor/Hrot.Editor.AiShared/Comparison/BlackboardComparisonSanitizer.cs` |
| Blackboard tests (existing sub-folder) | `Hrot/Editor/Hrot.Editor.AiShared.Tests/Comparison/` |
| BTree subtree+sync test to upgrade (EXISTING) | `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Comparison/BTreeComparisonSanitizerTests.cs` |
| HSM layout types (study) | `Hrot/Editor/Hrot.Editor.AiShared/Layout/HsmEditorLayoutBuilder.cs`, `HsmEditorLayout.cs`, `StateLayoutEntry.cs`, `TransitionLayoutEntry.cs`, `RegionLayoutEntry.cs` |
| HSM emitter (study — understand the builder chain format) | `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Emit/HsmFluentEmitter.cs` |
| Blackboard DTO emitter (study — understand the file format) | `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/BlackboardDtoEmitter.cs` |
| HSM host services (for DI wiring guidance) | `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Host/HsmEditorHostServices.cs` |
| HSM csproj | `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Hrot.Hsm.Editor.csproj` |
| HSM tests csproj | `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Hrot.Hsm.Editor.Tests.csproj` |

### Test Execution

```powershell
# Run shared AiShared tests (Blackboard sanitizer tests)
dotnet test "Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj" -c Debug

# Run HSM editor tests
dotnet test "Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Hrot.Hsm.Editor.Tests.csproj" -c Debug

# Run BTree editor tests (for D-02 fix verification)
dotnet test "Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Hrot.BTree.Editor.Tests.csproj" -c Debug

# Build whole solution to catch integration errors
dotnet build "IOS-IG-SimHost.sln" -c Debug --no-restore -maxcpucount:4
```

### Report Submission

Submit completed report to: `.dev\visual-asset-comparison\reports\BATCH-02-REPORT.md`

---

## Context

BATCH-02 completes the three sanitizers for the C#-emitted asset kinds (BTree done in BATCH-01; HSM and Blackboard done here). After this batch, all four asset kinds will have working sanitizers. The Blueprint sanitizer (JSON-based) comes in Slice C-3.

The key differences from the BTree sanitizer:
- **HSM:** Uses `stableId: new Guid("...")` for states/regions and `visualId: new Guid("...")` for transitions. The builder chain is imperative-style (`builder.State(...)`, `builder.GlobalTransition(...)`, `state.On("Event").GoTo("Target", visualId: ...)`) not fluent-chained like BTree.
- **Blackboard:** The simplest sanitizer — just concatenates the inline and heavy files verbatim with header labels. No hoist needed (XML `///` comments are already inline).

---

## Tasks

### D-02 Debt Fix — Upgrade Subtree+Sync Test to Line-Order Assertions

**Existing file to modify:** `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Comparison/BTreeComparisonSanitizerTests.cs`

**Context:** The BATCH-01 review identified that `Sanitize_SubtreeWithSyncAndCatalog_HoistsCommentSyncAndHumanizesGuid` uses only `Assert.Contains` checks. The design §3.3 specifies a precise order: (1) node comment, (2) sync-in binding, (3) sync-out binding, all appearing BEFORE the builder call. Update the test to also verify this ordering via line-number comparison.

**What to change:**
- After collecting the `result.SanitizedText`, split by `\n`
- Find the line indices of `"// delegate to shoot subtree"`, `"// sync (in):"`, `"// sync (out):"`, and the `.Subtree(` call line
- Assert: commentIdx < syncInIdx < syncOutIdx < subtreeCallIdx
- Keep the existing `Contains` assertions (they're still useful as readable failure messages); add the ordering assertions after them

---

### TASK-C-05 — `HsmComparisonSanitizer` with Comment Hoist and Layout Truncation

**Full spec:** See [TASK-DETAILS.md](../TASK-DETAILS.md#task-c-05--hsmcomparisonsanitizer-with-comment-hoist-and-layout-truncation)
**Design refs:** §3.3 (HSM-specific notes at end of section), §10.3

**New files to create:**

| File | Description |
|------|-------------|
| `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Comparison/HsmComparisonSanitizer.cs` | HSM text-based sanitizer |
| `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Comparison/HsmEditorComparisonServiceCollectionExtensions.cs` | DI extension (mirror of BTree's) |
| `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Comparison/HsmComparisonSanitizerTests.cs` | Unit tests |

**Understanding the HSM C# file format (study `HsmFluentEmitter.cs`):**

The HSM emitter generates three methods:

```csharp
// HROT_EDITOR_GENERATED — managed by AI editor; manual edits to this file will be overwritten on next save.
// AssetId: <guid>

using ...;
namespace ...;

public static class MachineName
{
    public static HsmBuilder CreateBuilder()
    {
        var builder = new HsmBuilder("MachineName");
        // Events:
        builder.Event("EventName", eventId, payloadSize, isIndirect, isDeferrable);
        // States (top-level):
        builder.State("StateName", stableId: new Guid("stableId-string"));
        // or with variable for children:
        var s1 = builder.State("StateName", stableId: new Guid("stableId-string"));
        s1.Child("ChildName", sb2 =>
        {
            ...
        }, stableId: new Guid("childStableId-string"));
        s1.On("EventName").GoTo("TargetName", visualId: new Guid("transitionVisualId"));
        // Global transitions:
        builder.GlobalTransition("EventName", "TargetName", visualId: new Guid("globalVisualId"));
        return builder;
    }

    [HsmDefinition("MachineName", AssetId = "...")]
    public static HsmDefinitionBlob Compile() => CreateBuilder().Build().Compile();

    [HsmLayout("assetId-string")]
    public static HsmEditorLayout Layout() => new HsmEditorLayoutBuilder()
        .State("stableId-string", position: new Vector2(x, y), comment: "...", ...)
        .Transition("visualId-string", waypoints: new Vector2[] { ... }, comment: "...")
        .Region("stableId-string", regionIndex: 0, position: new Vector2(x, y), comment: "...")
        .Build();
}
```

**Identifiers used in the HSM:**
- States/regions: `stableId: new Guid("...")` in CreateBuilder; `"stableId-string"` (first arg) in Layout
- Transitions: `visualId: new Guid("...")` in GoTo and GlobalTransition in CreateBuilder; `"visualId-string"` (first arg) in Layout

**Implementation notes:**

Follow the same structure as `BTreeComparisonSanitizer`:
1. Normalize line endings to `\n`
2. Find the `[HsmLayout(` line
3. Parse the layout body: extract per-element metadata
   - `.State("stableIdStr", ..., comment: "...", ...)` → capture stableId (normalize to GUID or keep as string) and comment
   - `.Transition("visualIdStr", ..., comment: "...", ...)` → capture visualId and comment
   - `.Region("stableIdStr", ..., comment: "...", ...)` → capture stableId and comment
4. Walk pre-layout lines for:
   - `stableId: new Guid("...")` → find the `builder.State(...)` or `sb.Child(...)` call containing this and inject the comment line above the call start
   - `visualId: new Guid("...")` on `.GoTo(...)` lines → inject comment above the `.On(...)` line (the one starting the transition chain)
   - `visualId: new Guid("...")` on `builder.GlobalTransition(...)` lines → inject comment above that call
5. Truncate from `[HsmLayout(` onward, close the class
6. Strip the header suffix (same as BTree: `; manual edits...` → `.`)
7. Normalize the `[HsmDefinition(...)]` thunk if needed (the HSM thunk `Compile() => CreateBuilder().Build().Compile()` is already compact)

**Note on `stableId` in layout:** The layout `.State("stableIdStr", ...)` uses a raw string like `"aabbccdd-eeff-0011-2233-445566778899"`. In the builder chain, `stableId: new Guid("aabbccdd-eeff-0011-2233-445566778899")` uses the same string. Match them by normalizing both to a canonical GUID string (lowercase, with dashes) for the dictionary key.

**Note on `SubtreeSyncField` in HSM:** The `HsmEditorLayoutBuilder` does NOT have a `.SubtreeSyncField(...)` method (unlike `BTreeEditorLayoutBuilder`). Do NOT attempt to parse it. If such a call appears in the file (possible in future), the parser should ignore it gracefully.

**Constructor:** `HsmComparisonSanitizer(IAssetCatalog catalog)` — catalog needed for potential future cross-asset GUID humanization (no cross-asset GUIDs in HSM yet, but keep the injector consistent with BTree pattern).

**AssetMetadataBlock:** Parse `AssetName` from `[HsmDefinition("Name", ...)]` attribute, `AssetId` from `// AssetId:` header comment.

**Tests required (`HsmComparisonSanitizerTests.cs`):**
- **Simple state machine:** A fixture with 2-3 states with comments in the layout; verify comments are hoisted as `//` lines above `builder.State(...)` calls
- **Parallel regions:** A fixture where a state is marked `.Parallel()` with two child regions; verify region-level comments hoisted above the child builder call
- **Global transitions with comment:** A fixture with a `builder.GlobalTransition(...)` call; verify transition comment hoisted above the transition builder line
- **Transition via On/GoTo with comment:** A fixture with `s1.On("Event").GoTo("Target", visualId: ...)` and the transition's comment in the layout; verify the comment appears above the `.On(...)` line
- **Determinism:** 10-run byte-identical loop on ≥2 fixtures
- **No layout method:** Returns file content + `SanitizationWarning` containing "Layout method not found"
- **Malformed file:** Returns a `SanitizationResult` with at least one warning; never throws

**DI wiring:** Create `HsmEditorComparisonServiceCollectionExtensions` with `AddHsmEditorComparison(services, registry)` that registers `HsmComparisonSanitizer` as a singleton and wires it into `SanitizerRegistry`. Mirror the BTree pattern exactly.

---

### TASK-C-06 — `BlackboardComparisonSanitizer` (Inline + Heavy Concatenation)

**Full spec:** See [TASK-DETAILS.md](../TASK-DETAILS.md#task-c-06--blackboardcomparisonsanitizer-inline--heavy-concatenation)
**Design refs:** §3.4

**New files to create:**

| File | Description |
|------|-------------|
| `Hrot/Editor/Hrot.Editor.AiShared/Comparison/BlackboardComparisonSanitizer.cs` | Blackboard sanitizer |
| `Hrot/Editor/Hrot.Editor.AiShared.Tests/Comparison/BlackboardComparisonSanitizerTests.cs` | Unit tests |

**Understanding the Blackboard file format (study `BlackboardDtoEmitter.cs`):**

Blackboard files are C# struct files:
```csharp
// HROT_EDITOR_GENERATED — managed by AI editor; manual edits to this file will be overwritten on next save.
// AssetId: <guid>
// AssetName: Foo_BT

using System.Runtime.InteropServices;

namespace Hrot.AI.Behaviors.Trees;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public partial struct Foo_BT_Blackboard
{
    /// <summary>Number of ammo shots remaining.</summary>
    public int AmmoCount;
}
```

The companion `.HeavyBlackboard.cs` file has the same structure when the blackboard has too many fields.

**Implementation:**

`BlackboardComparisonSanitizer` is the simplest sanitizer — it does almost nothing:
1. Read the inline file text (normalize line endings to `\n`)
2. Check if a companion `{BaseName}.HeavyBlackboard.cs` file exists in the same directory
3. Assemble the output:
   ```
   // === Inline blackboard ===\n
   {inline file content}
   ```
   And if heavy exists:
   ```
   \n// === Heavy blackboard (overflow) ===\n
   {heavy file content}
   ```
4. Preserve `[StructLayout]` attributes, `///` comments, all field declarations verbatim

**Companion file discovery:** The companion `.HeavyBlackboard.cs` is `{BaseName}.HeavyBlackboard.cs` where `{BaseName}` is the main filename without the `.Blackboard.cs` suffix. Look for it in the same directory as the main file (`request.AssetMainFilePath`).

**AssetKind.Blackboard:** This is now the correct kind to return from `TargetKind`. (BATCH-01 added `AssetKind.Blackboard` to the enum.)

**AssetMetadataBlock:**
- Parse `AssetName` from `// AssetName:` header comment (the Blackboard emitter writes this; see `BlackboardDtoEmitter.cs`)
- Parse `AssetId` from `// AssetId:` header comment
- `CompanionFiles`: include the heavy file path if it exists

**Constructor:** `BlackboardComparisonSanitizer()` — no dependencies needed. The Blackboard sanitizer does pure file concatenation with no catalog lookups.

**DI wiring:** Register in `SharedAiEditorServiceCollectionExtensions.AddSharedAiEditor()` in `Hrot/Editor/Hrot.Editor.AiShared/Di/SharedAiEditorServiceCollectionExtensions.cs`. The `SanitizerRegistry` and `BlackboardComparisonSanitizer` are both in `Hrot.Editor.AiShared`, so they can be wired up there.

**Tests required (`BlackboardComparisonSanitizerTests.cs`):**
- **Inline only:** Write a temp inline Blackboard `.cs` file (no heavy companion); output contains `// === Inline blackboard ===` and the inline content; NO `// === Heavy blackboard` section
- **Inline + heavy:** Write both temp files; output contains both labeled sections in the correct order (inline first, heavy second)
- **XML `///` comments preserved:** A field with `/// <summary>Some doc comment.</summary>` in the inline file appears verbatim in the output
- **Determinism:** 10-run byte-identical loop (once with inline-only, once with inline+heavy)
- **Missing main file:** Returns result with warning "File not found"; does not throw
- **AssetName and AssetId extracted:** Parsed correctly from header comments

---

### TASK-C-07 — HSM + Blackboard Determinism and Self-Comparison Tests

**Full spec:** See [TASK-DETAILS.md](../TASK-DETAILS.md#task-c-07--hsm--blackboard-sanitizer-round-trip-and-determinism-tests)
**Design refs:** §10.3

**New files to create:**

| File | Description |
|------|-------------|
| `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Comparison/HsmSanitizationDeterminismTests.cs` | HSM determinism + reorder tests |
| `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Comparison/HsmSelfComparisonTests.cs` | HSM self-comparison tests |
| `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Comparison/Fixtures/simple_machine.cs` | Simple HSM fixture (2-3 states) |
| `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Comparison/Fixtures/parallel_machine.cs` | HSM with parallel regions |
| `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Comparison/Fixtures/malformed_no_layout.cs` | HSM without layout attribute |
| `Hrot/Editor/Hrot.Editor.AiShared.Tests/Comparison/BlackboardSanitizationDeterminismTests.cs` | Blackboard determinism tests |
| `Hrot/Editor/Hrot.Editor.AiShared.Tests/Comparison/BlackboardSelfComparisonTests.cs` | Blackboard self-comparison tests |

**Tests required — HSM determinism (`HsmSanitizationDeterminismTests.cs`):**
- 10-run byte-identical loop on `simple_machine.cs` and `parallel_machine.cs`
- Layout `.State(...)` node reorder invariant (same as BTree: swap two `.State()` entries in the layout method, verify sanitized output is byte-identical to the original)
- Malformed fixture: no exception; non-empty warning list

**Tests required — HSM self-comparison (`HsmSelfComparisonTests.cs`):**
- Same file sanitized twice → byte-identical output (for `simple_machine.cs` and `parallel_machine.cs`)
- Two independent catalog instances with same content → byte-identical output

**Tests required — Blackboard determinism (`BlackboardSanitizationDeterminismTests.cs`):**
- 10-run loop on an inline-only blackboard (temp file written by the test)
- 10-run loop on a blackboard with both inline + heavy files (temp files written by the test)
- ≥3 distinct fixture shapes tested (e.g., different field counts, different comment patterns)

**Tests required — Blackboard self-comparison (`BlackboardSelfComparisonTests.cs`):**
- Same inline-only blackboard sanitized twice → byte-identical output
- Same inline+heavy blackboard sanitized twice → byte-identical output
- Two independent sanitizer instances (separate object references) on same files → byte-identical output

**Fixture notes:**
- `simple_machine.cs`: 2-3 states, at least one transition with visualId and comment in layout
- `parallel_machine.cs`: one parallel state with 2 region children; each child has a comment in layout; at least one global transition with comment
- `malformed_no_layout.cs`: valid C# syntax but no `[HsmLayout(...]` attribute

---

## Mandatory Workflow

**CRITICAL: Complete tasks in sequence with passing tests before proceeding.**

1. **D-02 fix:** Upgrade subtree+sync test → ALL BTree tests pass ✅
2. **TASK-C-05:** Implement HsmComparisonSanitizer + tests → ALL HSM tests pass ✅
3. **TASK-C-06:** Implement BlackboardComparisonSanitizer + tests → ALL AiShared tests pass ✅
4. **TASK-C-07:** Add HSM + Blackboard determinism + self-comparison tests → ALL pass ✅

After each step, run the relevant test project and fix all failures before moving on. Do NOT stop to ask for permission to fix errors. Keep going until everything is done. Build the whole solution at the end:
```powershell
dotnet build "IOS-IG-SimHost.sln" -c Debug --no-restore -maxcpucount:4
```

---

## Quality Standards

### Code Quality
- No compiler warnings (projects use `TreatWarningsAsErrors`)
- `HsmComparisonSanitizer` must never throw — catch all exceptions and return a warning
- `BlackboardComparisonSanitizer` must never throw — catch file-not-found and all exceptions
- Follow the exact same coding patterns as `BTreeComparisonSanitizer`
- Namespaces: `Hrot.Hsm.Editor.Comparison` for HSM; `Hrot.Editor.AiShared.Comparison` for Blackboard

### Test Quality
- HSM tests must verify ACTUAL BEHAVIOR — comments appear in the correct position, not just anywhere in the output
- The subtree+sync fix (D-02) MUST include ordering assertions (line index comparisons), not just `Contains`
- Blackboard tests must write real temp files and read the actual sanitizer output (same pattern as BTree tests)
- Determinism tests must use the 10-iteration loop pattern from BATCH-01, not just "run twice"

---

## Developer Insights (Answer in Report)

**Q1:** What differences did you encounter between the HSM and BTree builder chain formats when implementing the comment-hoisting logic? How did you handle them?

**Q2:** The HSM has two types of identifiers: `stableId` (for states/regions) and `visualId` (for transitions). How did you structure the parser to handle both in a single pass, and are there any edge cases around duplicate IDs (e.g., a state and a transition sharing the same GUID)?

**Q3:** The Blackboard sanitizer is trivially simple. Did you spot any scenarios where the simplicity could cause problems — for example, if the file had unusual encoding or BOM markers?

**Q4:** Did you find any weak points in the HSM sanitizer's backward-scan approach for finding call-start lines (inherited from BTree)? The HSM builder chain is more imperative and less fluent — does this affect the scan's reliability?

**Q5:** What test scenarios did you wish were covered but weren't specified in the instructions? Document them as potential P3 debt items in your report.

---

## Success Criteria

This batch is DONE when:
- [ ] D-02 debt fix: `Sanitize_SubtreeWithSyncAndCatalog_HoistsCommentSyncAndHumanizesGuid` includes line-order assertions; all BTree tests pass
- [ ] TASK-C-05: `HsmComparisonSanitizer` + DI wiring created; all `HsmComparisonSanitizerTests` pass
- [ ] TASK-C-06: `BlackboardComparisonSanitizer` + DI wiring created; all `BlackboardComparisonSanitizerTests` pass
- [ ] TASK-C-07: All 4 determinism/self-comparison test classes created; all tests pass
- [ ] `dotnet build "IOS-IG-SimHost.sln" -c Debug --no-restore -maxcpucount:4` — 0 errors
- [ ] `dotnet test "Hrot/Editor/Hrot.Editor.AiShared.Tests/..."` passes
- [ ] `dotnet test "Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/..."` passes
- [ ] `dotnet test "Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/..."` passes
- [ ] Report submitted to `.dev\visual-asset-comparison\reports\BATCH-02-REPORT.md`

---

## Reference Materials

- **BTree sanitizer (pattern to follow):** `Hrot/Subsystems/AI/Hrot.BTree.Editor/Comparison/BTreeComparisonSanitizer.cs`
- **BTree DI extension (pattern to follow):** `Hrot/Subsystems/AI/Hrot.BTree.Editor/Comparison/BTreeEditorComparisonServiceCollectionExtensions.cs`
- **HSM emitter (understand builder chain format):** `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Emit/HsmFluentEmitter.cs`
- **HSM layout types:** `Hrot/Editor/Hrot.Editor.AiShared/Layout/HsmEditorLayoutBuilder.cs`, `StateLayoutEntry.cs`, `TransitionLayoutEntry.cs`, `RegionLayoutEntry.cs`
- **Blackboard DTO emitter (understand file format):** `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/BlackboardDtoEmitter.cs`
- **Shared DI (for Blackboard wiring):** `Hrot/Editor/Hrot.Editor.AiShared/Di/SharedAiEditorServiceCollectionExtensions.cs`
- **Design:** `.dev\visual-asset-comparison\Visual_Asset_Comparison_Detailed_Design.md` — §3.3, §3.4, §10.3
- **Task specs:** `.dev\visual-asset-comparison\TASK-DETAILS.md` — TASK-C-05 through TASK-C-07
