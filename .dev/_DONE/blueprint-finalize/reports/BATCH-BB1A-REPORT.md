# BATCH-BB1A Report

## Implementation Summary

### Task B-1: Type-filtered binding picker

**`BTreeFacetFqnContext`** (new class in `BTreePickerDrawers.cs`):
A mutable shared cell written by `BTreeFacetMapper.GetFacet()` and read by `BlackboardFieldPickerDrawer.GetItems()`. One instance per open BTree asset, created alongside the drawer factory.

**`BlackboardFieldPickerDrawer`** (upgraded, backward-compatible):
- New 3-arg constructor: `(BehaviorTreeAsset, IActionSchemaExporter?, Func<string?>?)` alongside the preserved 1-arg `(BehaviorTreeAsset)` no-filter overload.
- `GetItems()` now calls `BlackboardFieldPickerAttribute.GetCompatibleVariables()` when exporter + accessor are configured, returning only variables where `FieldType == entry.DtoType`.
- `HasNoCompatibleVariables` property: true when FQN is known but no variable matches — surfaces the Promote affordance to the inspector.
- `TriggerPromote()` / `ResetPromoteRequest()` / `PromoteRequested` for headless-testable Promote entry-point state.
- `Promote(string facetVisualId)`: creates `_auto_{guid:N}` variable with `IsAutoManaged=true` and the action's `DtoType`; returns name; idempotent if already exists; returns null if FQN unresolvable.

**`BTreeFacetMapper`** (new overload):
`(BehaviorTreeAsset, BTreeFacetFqnContext?)` — sets `ctx.CurrentActionFqn` before returning `BTreeActionFacet`/`BTreeConditionFacet`; clears it for non-action/condition nodes so unrelated facets don't bias the picker.

**`BTreePickerDrawerFactory.BuildDrawers`** — added optional `IActionSchemaExporter?` and `BTreeFacetFqnContext?` params (default null, fully backward-compatible).

**HSM counterpart** (new in `HsmPickerDrawers.cs`):
- `HsmFacetFqnContext` — same pattern as BTree.
- `HsmBlackboardFieldPickerDrawer` — full mirror: type-filtering, `HasNoCompatibleVariables`, `Promote(string facetVisualId)` using transition/global-transition VisualId.
- `HsmPickerDrawerFactory.BuildDrawers` — added optional `IActionSchemaExporter?` and `HsmFacetFqnContext?` params.

**`HsmFacetMapper`** — updated `GetTransitionFacet()` and `GetGlobalTransitionFacet()` to write the transition's `ActionFunction` to the context before returning.

**`HsmFacetDispatcher`** — new overload accepting `HsmFacetFqnContext?`; propagates it to `HsmFacetMapper`.

**`HsmFacets.cs`** — added `ExpressionTargetField` (string?) to `TransitionFacet` and `GlobalTransitionFacet` with `[BlackboardFieldPicker]` attribute.

**`TransitionNode` model** — added `ExpressionTargetField` string? property.

**`TransitionNodeDto` / `GlobalTransitionNodeDto`** — added `ExpressionTargetField` with `[JsonIgnore(WhenWritingNull)]`.

**`HsmAssetMapper`** — carries `ExpressionTargetField` in both ToDto/FromDto directions for transitions and global transitions.

**`HsmFacetDispatcher.ApplyTransitionFacet` / `ApplyGlobalTransitionFacet`** — write back `ExpressionTargetField` to the model.

### Task B-2: `IsAutoManaged` + Promote

**`BlackboardVariableEntry`** — added `bool IsAutoManaged = false` positional param (backward-compatible; all existing `new BlackboardVariableEntry(name, type, comment)` calls compile unchanged).

**`BlackboardVariableDto`** — added `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool IsAutoManaged { get; set; }` — omitted from JSON when false, preserving byte-identical serialization of existing assets.

**`HsmBlackboardVariableDto`** — same pattern.

**`BehaviorTreeAssetMapper.BlackboardToDto` / `BlackboardFromDto`** — carry `IsAutoManaged` both ways.

**`HsmAssetMapper.BlackboardToDto` / `BlackboardFromDto`** — carry `IsAutoManaged` both ways.

**Promote** implementation lives in `BlackboardFieldPickerDrawer.Promote(string facetVisualId)` (BTree) and `HsmBlackboardFieldPickerDrawer.Promote(string facetVisualId)` (HSM). Both: resolve `DtoType` from exporter, create `_auto_{guid:N}` variable with `IsAutoManaged=true`, return the name; return null and create nothing if FQN unresolvable. Idempotent on the same visualId (no duplicate).

---

## Design Decisions

### FQN threading mechanism: shared mutable context object (`BTreeFacetFqnContext`)

**Alternatives considered:**
1. Per-node drawer factory (call `BuildDrawers` per render) — rejected: the factory is asset-scoped, not node-scoped; per-frame factory creation is wasteful and breaks the SE2 rebuild model.
2. `EditNode`-sibling navigation (read `MethodFqn` from a peer field) — rejected: `EditNode` has no parent document reference so sibling fields are not accessible from within `DrawInput`.
3. Separate Inspector callback that updates the drawer when selection changes — rejected: too much ceremony and requires InspectorWindow cooperation.
4. **Chosen: shared mutable cell** (`BTreeFacetFqnContext`) written by the mapper's `GetFacet()` and read by the drawer's `GetItems()`. This works cleanly because StructEdit calls `GetFacet` to build the document and then renders each field's drawer; the context is set before any field renders. Tested headlessly by mutating the context and confirming `GetItems()` changes.

### `[JsonIgnore(WhenWritingDefault)]` omission for `IsAutoManaged`
Using `JsonIgnoreCondition.WhenWritingDefault` means `false` is omitted from JSON, keeping serialized output byte-identical to pre-batch assets. `true` is emitted explicitly. This is the byte-stability contract noted in the hard rules.

### HSM `ExpressionTargetField` scope: TransitionFacet + GlobalTransitionFacet only
States carry multiple action function fields (OnEntry/OnExit/Activity/Timer). Adding a single `ExpressionTargetField` to StateFacet would create ambiguity — which action does it bind to? The spec's primary use case (action-parameter binding) maps cleanly onto transitions (which have a single `ActionFunction`). `ExpressionTargetField` on `StateFacet` is deferred; B-4 (lifecycle) will revisit.

### `Promote` idempotency
When called twice with the same visualId, `Promote` returns the existing name without creating a duplicate variable. This is safer than failing loudly on a double-click and matches the expected editor UX.

---

## Deviations

**1. HSM `ExpressionTargetField` added to transitions/global-transitions only, not states.**
- WHAT: `StateFacet` does NOT get `ExpressionTargetField` in this batch.
- WHY: States carry four independent action fields (Entry/Exit/Activity/Timer); a single `ExpressionTargetField` would be ambiguous about which action it binds.
- BENEFIT: Avoids a design ambiguity that the spec doesn't resolve; keeps the HSM model clean.
- RISK: HSM state actions can't use the type-filtered picker yet. This is a known limitation for B-4.

**2. `Promote` returns existing name rather than throwing when variable already exists.**
- WHAT: Idempotent on double-promote.
- WHY: Safer for UI double-click; the spec says "creates... binds..." which implies it should succeed; doesn't say what to do on duplicate.
- BENEFIT: No confusing exceptions on re-click.
- RISK: Tiny — a genuine second promote on a different action with the same node ID (impossible by construction since VisualIds are stable GUIDs) would silently reuse the existing variable.

---

## Test Results

| Test project | Total | Failed | New tests added |
|---|---|---|---|
| `Hrot.BTree.Editor.Tests` | 420 | 0 | 12 (`BlackboardFieldPickerDrawerTests.cs`) |
| `Hrot.Hsm.Editor.Tests` | 368 | 0 | 11 (`HsmBlackboardFieldPickerDrawerTests.cs`) |
| `Hrot.AiEditor.Persistence.Tests` | 98 | 0 | 13 (`IsAutoManagedRoundTripTests.cs`) |
| `Hrot.Editor.AiShared.Tests` | 1025 | 0 | 0 (pre-existing; validates no regressions) |

**Build: 0 errors, 0 warnings** (`dotnet build IOS-IG-SimHost.sln -c Debug`).

**Stability filter used:** `--filter "Stability!=Flaky&Stability!=Environment&Stability!=Broken"` on all 4 projects.

### Key scenarios verified

**B-1 filtering:**
- BTree drawer with known FQN + asset vars of T and U → `GetItems()` returns only T vars (real drawer path, not static helper)
- BTree drawer with unknown FQN → fallback to all vars
- BTree drawer with null FQN → fallback to all vars
- BTree drawer without exporter configured → unfiltered (backward-compat)
- Empty-state: known FQN, no matching vars → `GetItems()` empty + `HasNoCompatibleVariables==true`
- `HasNoCompatibleVariables==false` when FQN unknown (not a Promote-worthy state)
- Context-threading test: `BTreeFacetFqnContext.CurrentActionFqn` set externally → drawer immediately reflects filtered list
- HSM variants of all the above

**B-2 Promote:**
- `Promote(visualId)` creates `_auto_{N}` var of correct CLR type with `IsAutoManaged==true`
- Idempotency: second Promote with same ID returns same name, no duplicate
- Two distinct IDs → two distinct vars
- Promote with unresolvable FQN → null returned, no variable created
- Promote with null FQN → null returned

**B-2 persistence:**
- `IsAutoManaged=true` round-trips through model→DTO→model (BTree + HSM)
- `IsAutoManaged=false` (default) round-trips (BTree + HSM)
- Default record construction: `IsAutoManaged` defaults to false
- `false` omitted from JSON (byte-stability, `WhenWritingDefault`)
- `true` present in JSON
- Back-compat: legacy JSON without `IsAutoManaged` property → defaults to false via real `JsonSerializer.Deserialize` (BTree + HSM)

---

## Developer Insights

- **`GetCompatibleVariables` was already correct and well-tested** in `BlackboardFieldPickerAttributeTests`. The gap was exactly as described: the live drawer returned all names, not filtered. The fix was to plumb exporter + FQN into the existing drawer rather than re-implementing filtering.
- **HSM ExpressionTargetField gap**: The HSM model had no `ExpressionTargetField` at all before this batch. Adding it to transitions required touching `TransitionNode` model, `TransitionNodeDto`, `HsmAssetMapper` (both directions), `HsmFacetMapper`, `HsmFacetDispatcher`, and `HsmFacets.cs`. Each file had to be read before editing.
- **`[JsonIgnore(WhenWritingDefault)]`**: The `JsonIgnoreCondition` enum lives in `System.Text.Json.Serialization` which was already imported in both DTO files. Zero friction.
- **BTreeFacetMapper backward-compat**: The original single-arg constructor is preserved as a delegating overload to `this(asset, null)`, so all existing callers (SE2 tests, EditorSubsystem, selection bridge) compile without change.
- **Byte-stability test**: The `ByteStabilityTests` suite (in `Hrot.AiEditor.Persistence.Tests`) re-serializes real assets. Since `IsAutoManaged=false` is omitted from JSON, existing serialized assets produce identical output.

---

## Known Issues

- `StateFacet` (HSM) does not have `ExpressionTargetField` — state actions (OnEntry/Exit/Activity/Timer) cannot use the type-filtered picker yet. Deferred to B-4 scope or a follow-up once the design resolves which action field a state's `ExpressionTargetField` maps to.
- The `Promote` method is on the drawer but the caller (InspectorWindow / SE1 StructEdit render path) must handle the returned name to set `ExpressionTargetField` on the facet. The wiring from `PromoteRequested==true` → call `Promote(facet.VisualId)` → write `facet.ExpressionTargetField = name` → `ApplyFacet` is Inspector-side work (B-3/B-4 scope; requires running-editor integration).

---

## Suggested Commit Message

`feat(ai-editor): B-1 type-filtered blackboard picker + B-2 IsAutoManaged + Promote (BTree & HSM)`
