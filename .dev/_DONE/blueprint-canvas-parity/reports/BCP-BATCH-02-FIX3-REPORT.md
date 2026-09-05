# BCP-BATCH-02-FIX3 Report

unify wire-drop picker + auto-connect + duplicate-variable-name guard

## Implementation Summary

### Task 1 — wire-drop picker shows only 3 kinds AND new node doesn't auto-connect (unified root)

**Root cause confirmed exactly as diagnosed.** `BlueprintNodeCatalog.DescriptorToEntry` built the
`NodeCatalogEntry.Inputs`/`Outputs` `PinSignature` lists straight from `defaultNode.Pins`. The 24
FIX2 palette kinds construct nodes with **empty `Pins`** (pins are projected by `NodePinSchema`
at render time and never persisted — projection-only). So their catalog entries had no pin
signatures, which meant:
- `QueryForPinContext` (filters by `entry.Inputs`/`entry.Outputs`) matched only the 3
  hand-authored When/EQS kinds → the wire-drop picker showed 3 instead of the full compatible set.
- NodeEdit's wire-drop auto-connect searched the entry's signatures for a pin compatible with the
  dragged pin; with empty signatures it found none → formed no link → "doesn't connect".

**Fix (the only production change for Task 1):** in `DescriptorToEntry`, derive the pin list from
`NodePinSchema.GetCanonicalPins(defaultNode, _registry)` instead of `defaultNode.Pins`, then build
the Input/Output `PinSignature`s from that via the existing `PinToSignature` helper.
`GetCanonicalPins` already returns `node.Pins` verbatim when non-empty (When/EQS hand-authored
kinds), so those are unchanged; for the empty-`Pins` palette kinds it resolves the canonical schema
(registry descriptor → built-in fallback table, e.g. `BranchNode => BranchPins()`, exec in/out for
flow kinds). `DescriptorToEntry` was changed from `static` to an instance method so it can read the
catalog's existing `_registry` field (`PinToSignature` stays static). No other call-site change —
`BuildAll` already calls it as an instance method.

**Why auto-connect now works end-to-end (no NodeEdit core change):** with real signatures the
wire-drop picker offers every compatible kind AND the dragged wire finds a compatible target pin,
so NodeEdit forms the link to a fresh pin GUID on the freshly-created pinless node. On the next
`BlueprintGraphModel.Rebuild`, the two-pass **slow path** (node had empty `Pins`) collects the
distinct `ToPinId` GUIDs from the new node's incoming links and assigns the first one to the new
node's first input pin (in canonical declaration order). The link's `ToPinId` therefore resolves to
that input pin → `FindPin(from) != null && FindPin(to) != null` → the wire is drawn connected to
the new node. This binding chain is asserted directly in a test (below).

### Task 2 — variable creation silently appends a numeric suffix on a duplicate name

**`BlueprintDocumentFactory.CreateVariable(asset, name, type)`** now **rejects** rather than
suffixes:
- returns `VariableDecl?` — `null` (and adds nothing, fires no dirty callback) when the name is
  blank/whitespace OR collides case-insensitively with an existing variable name;
- on a unique name, adds it **verbatim** (trimmed), no numeric suffix.

Added `internal static bool IsDuplicateVariableName(asset, name)` (case-insensitive, trims) as the
authoritative predicate, reused by the modal.

The "+ Variable" quick-add path (`AddVariable`, the no-modal `editor.create-variable` handler) now
picks a free unique default name itself (`MakeUniqueVariableName("NewVar")`) before calling
`CreateVariable`, so repeated "+" clicks still produce `NewVar`, `NewVar1`, … and never hit the
rejection. The dedup/uniquify logic moved out of `CreateVariable` into this quick-add path only.

**`VariableCreateModal`** now takes the owning `BlueprintAsset` (optional, defaults null for tests).
In `Draw` it validates the live name: shows an inline orange warning
`"A variable named 'X' already exists."` on collision (or `"Name cannot be empty."` when blank) and
**disables the Create button** in both cases — no auto-rename. `BlueprintMyBlueprintWindow.Retarget`
passes the active `blueprintAsset` into the modal so production validation is live.

## Design Decisions

- **`CreateVariable` returns `VariableDecl?`** (null = rejected) rather than throwing. The modal
  already gates Confirm, so the factory-level guard is a defense-in-depth invariant, not the primary
  user-facing error path; `null` is cheaper for callers than catch/handle and existing call sites
  discard or null-check the result.
- **Quick-add ("+") keeps auto-uniquify** rather than rejecting, because there is no name field to
  warn on — rejecting it would make the "+" button silently do nothing on the second click. Only the
  named create path (modal) rejects.
- **`DescriptorToEntry` made an instance method** (vs. threading the registry through a parameter)
  to reuse the existing `_registry` field with the smallest diff.

## Deviations

- **Existing test `CreateVariable_DuplicateName_IsMadeUnique` replaced.** It asserted the old
  silent-suffix behavior, which Task 2 explicitly reverses. WHAT: renamed/rewrote it to
  `CreateVariable_DuplicateName_IsRejected` (+ added `CreateVariable_UniqueName_IsAdded`,
  `CreateVariable_BlankName_IsRejected`, `IsDuplicateVariableName_DetectsCaseInsensitiveCollision`,
  replacing the now-obsolete `CreateVariable_BlankInputs_FallBackToDefaults`). WHY: the old assertion
  directly contradicts the new required behavior. BENEFIT: tests now encode the correct contract.
  RISK: none — `CreateVariableCommand_Twice_ProducesUniqueNames` (the "+" path) still passes because
  `AddVariable` retains uniquify.

No other deviations. Projection-only honored: no `Pin` schema field, no `.bp.json`/
`BlueprintJsonServices` change.

## Test Results

Files changed for tests: `BlueprintNodeCatalogTests.cs` (Task 1), `BcpBatch02BlueprintTests.cs`
(Task 1 + Task 2).

**New Task 1 tests (all pass):**
- `QueryForPinContext_ExecOutputSource_ReturnsFullFlowSet_WithCompatibleExecInput` — full palette;
  asserts `results.Count > 3`, that `Branch`/`Sequence`/`ChannelCommand` are each present **and**
  each exposes an exec-input pin, and that every returned entry has an exec input.
- `QueryForPinContext_ExecOutputSource_ExcludesPureDataOutputKinds` — `GetVariable` (pure data-out)
  is excluded for an exec-output source (proves real compatibility filtering, not "return all").
- `DescriptorToEntry_EmptyPinsPaletteKind_DerivesCanonicalPinSignatures` — Branch entry has exec In
  + 2 exec Outs; Sequence has exec In + exec Out (derived via NodePinSchema, not empty `defaultNode.Pins`).
- `WireDrop_AddPinlessNode_PlusLinkToFreshPin_ResolvesAndConnectsAfterRebuild` — source node with a
  real exec-out pin + a pinless `BranchNode` + an asset `Link` to a **fresh** `ToPinId`; after
  `Rebuild`, both endpoints resolve (`FindPin` non-null), the resolved To-pin is the new node's
  exec **input** pin (`OwnerNodeId == newNode`, `Kind==Exec`, `Direction==Input`), and the model
  link wires source-out → new-node-in. This is the auto-connect slow-path binding, asserted on real
  values.

**New Task 2 tests (all pass):**
- `CreateVariable_DuplicateName_IsRejected` — exact + case-insensitive duplicate both return null,
  nothing added, original `VariableDecl` (name + type) unchanged.
- `CreateVariable_UniqueName_IsAdded` — two distinct names → both added verbatim, count == 2.
- `CreateVariable_BlankName_IsRejected` — whitespace name → null, no variable added.
- `IsDuplicateVariableName_DetectsCaseInsensitiveCollision` — true for `Speed`/`speed`/`  SPEED  `,
  false for `Health`.

**Suite results:**
- `Hrot.Blueprints.Tests` — full run: Failed 11, Passed 1103, Skipped 8 (Total 1122). The 11
  failures are pre-existing debt, verified identical on a clean stash of this batch's files
  (baseline: Failed 11, Passed 1097 — the +6 passing are this batch's new tests): the 10 DEBT-006
  golden/demo/allocation/EQS-summary snapshots (`AiPrimitiveEmitGoldenTests` ×2,
  `InstanceEmitGoldenTests` ×3, `LibraryEmitGoldenTests`, `LibraryMathDemoTests`,
  `MoveToAndFireDemoTests`, `ConditionSummaryAttachmentTests`, `AllocationFreeTests`) plus the flaky
  perf `WhenNodePerfTests.WhenNode_ConditionMet_Under200ns_perTick` (timing-only, env-dependent).
  Catalog + Batch02 classes alone: **48 passed, 0 failed**. No new failures from this batch.
- `Hrot.Editor.AiShared.Tests` — **761 passed, 0 failed**.
- `Hrot.BTree.Editor.Tests` — **382 passed, 0 failed**.
- `Hrot.Hsm.Editor.Tests` — **333 passed, 0 failed**.
- `Hrot.ClusterRunner.Integration.Tests --filter ~EditorSubsystemBoot` — **10 passed, 0 failed**.
- Byte-stability / round-trip (`~Stability|~RoundTrip`) — **79 passed, 0 failed**.

**Build:** `dotnet build IOS-IG-SimHost.sln` → **0 errors**. The 6 files I changed produce **0
warnings** (verified by grepping the warning output for each filename). The 26 solution warnings are
pre-existing debt in untouched files (`SpawnEqsSensorRuntimeTests`, `CoverAwarePatrolEndToEndTest`,
`IBlueprintTimeController` obsolete usages, `BlueprintTestFixture`, `ProbeOverheadBenchmarks`) — none
are in my diff.

**Byte-stability + compiler golden:** unchanged. The 7 golden/demo failures are identical on the
baseline stash (DEBT-006), i.e. not introduced or worsened here. Byte-stability suite is fully green.
This is a projection-only change (signatures derived at catalog-build time; no asset/serialization
touch).

## Developer Insights

- The fix is elegant precisely because `NodePinSchema.GetCanonicalPins` and the two-pass slow path
  already existed (BCP-A). Task 1 was a one-line source swap in `DescriptorToEntry`
  (`defaultNode.Pins` → `NodePinSchema.GetCanonicalPins(defaultNode, _registry)`); everything
  downstream (picker filter, auto-connect binding) just started working.
- `GetCanonicalPins` resolves the palette kinds via its **built-in fallback table** (Pass 2), not
  the registry descriptor — because the registry's `CreateInstance().Pins` is also empty for those
  kinds (that is the whole bug). The registry is still passed so the When/EQS short-name lookup path
  remains correct.
- Edge case beyond spec: a kind whose canonical schema is `Array.Empty<Pin>()` (e.g. `ReadEqsResult`
  in the fallback table) correctly contributes no signatures and is filtered out of pin-context
  queries — verified indirectly by the "exclude pure data-output" test pattern.

## Known Issues

None introduced. The pre-existing DEBT-006 golden/demo failures and the flaky sub-200ns perf test
remain (out of scope for this batch).

## Suggested Commit Message

fix(blueprints): wire-drop picker offers full compatible set + auto-connects; reject duplicate variable names (BCP-BATCH-02-FIX3)
