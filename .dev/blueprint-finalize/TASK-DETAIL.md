# Blueprint Integration Finalization -- Task Detail

Full per-batch detail for the `blueprint-finalize` thread (branch `blueprint-integ-1`, anchor `42aab24c`).
Companion checklist: [TASK-TRACKER.md](./TASK-TRACKER.md). Mission/context: [ONBOARDING.md](./ONBOARDING.md).
Per-batch instructions: `batches/BATCH-XX-INSTRUCTIONS.md`. Per-batch reports: `reports/BATCH-XX-REPORT.md`.

Each batch entry: **Goal**, **Scope / key changes**, **Status** (+ commit when done), **Verification gate**.
Completed batches reference their report for full file:line detail rather than duplicating it here.

---

## BATCH-01 -- DEBT-MVE-003 multi-blueprint quick-reload safety
- **Goal:** Fix the P1 production blocker where quick-reloading one editor-compiled blueprint wiped its
  siblings from the registry and dangled their ALC delegates (access violation on next tick).
- **Scope / key changes:** `BlueprintRegistry.CommitStagingMerge` (atomic upsert; `CommitStaging`
  full-replace left intact for the file-watcher path); `BlueprintRegistryStaging.StagedBlueprintIds`;
  `Fdp.Toolkit.Behavior.AiHotReloadCoordinator` `_currentAlc` -> `Dictionary<int,AssemblyLoadContext>`
  with selective unload; multi-blueprint regression proof test + 5 merge unit tests.
- **Status:** DONE -- commit `2d06f741`. Report: `reports/BATCH-01-REPORT.md`.
- **Gate:** targeted 22/22; full suite 1161 pass / 10 pre-existing fail / 0 new; EditorSubsystemBoot 10/10.

## BATCH-04 -- DEBT-MVE-002 emit StateFields in codegen
- **Goal:** Make a *compiled* Instance blueprint's working state readable by field name via
  `BlueprintStateView.TryGetField` (was only possible with hand-built defs / DebugMap workaround).
- **Scope / key changes:** `CSharpEmitter.EmitInstanceRegistration` emits a `StateFields` dictionary from
  `asset.Variables` (offset = `f.Offset` directly; already absolute from byte 0). Synthesized reactive-state
  structs (`_*` local types, e.g. WhenNode PrevState) are SKIPPED (`IsReferencableStateFieldType`) because
  their generated type name isn't referencable in the registrar scope (would be `CS0246`). Regenerated 3
  Instance goldens (additive `StateFields` only). End-to-end proof test (no hand-built def / no DebugMap).
- **Status:** DONE -- committed. Report: `reports/BATCH-04-REPORT.md`. (Lead review caught + fixed an
  initial regression where the un-filtered `typeof()` broke WhenNode/EQS blueprint compilation.)
- **Gate:** full suite 7 fail (3 Instance goldens resolved from the prior 10), 0 new; boot 10/10.

## BATCH-02 -- Task 3 node value pins for all node kinds
- **Goal:** Ensure every node kind exposes the data/value pins the COMPILER actually consumes (the
  user's top-priority authoring item). Compiler-grounded audit found almost all kinds already correct;
  exec-only kinds are exec-only because their config comes from node fields, not pins.
- **Scope / key changes (3 real gaps, in `NodePinSchema.cs`):** `ReadRankedResultNode` -> 3 data-OUT pins
  `IsValid:bool / Entity:long / Score:single` (names match the emitted result struct; Stage5 reads OUT
  pins by name); `CallCustomEventNode` -> exec + one data-IN per custom-event parameter (from
  `asset.CustomEvents`, graceful fallback); `CallPeerBlueprintNode` -> exec + static `Return` data-OUT
  (dynamic arg pins deferred to BATCH-03C2). All other kinds verified correct, untouched.
- **Status:** DONE -- committed. Report: `reports/BATCH-02-REPORT.md`.
- **Gate:** NodePinSchema tests 19/19; full suite golden count unchanged (projection-only invariant held);
  boot 10/10.

---

## BATCH-03A -- Compiler core: in-blueprint function-graph calls
- **Goal:** Foundation of Option B -- an Instance blueprint can define a local `GraphKind.Function` graph
  (typed Inputs/Outputs) and call it from another graph via `FunctionCallNode`.
- **Scope / key changes:** `FunctionCallNode.TargetGraphId` discriminator (empty = existing CLR call);
  `IrOp_GraphCall`; Stage5 generates `IrOp_ReadInputArg` for Entry data-OUT pins (name-matched to
  `Graph.Inputs`) -- this binding was defined in IR but never produced before; Stage5
  `FunctionCallNode`-with-`TargetGraphId` scheduling (impure + pure) with BP4004 graceful fallback;
  `IrGraph.Inputs/Outputs` propagated via `BuildIrFieldsFromGraphParams`; `InstanceEmitter` emits each
  non-Tick Function graph as `Func_{name}(ref State s, view, ecb, self, time, deltaTime, instanceVersion,
  <inputs>)`; `StatementEmitter` renders `IrOp_GraphCall`; Stage2 `V_FunctionGraphCallRules` BP1650 forbids
  latent nodes in called function graphs (single flat `BlueprintLatentCursor` -> a function method can't
  own a cursor). 3 tests incl. end-to-end compile-and-run (composes with BATCH-04 StateFields).
- **Status:** DONE -- committed. Report: `reports/BATCH-03A-REPORT.md`.
- **Gate:** 3 new tests green; full suite 7 fail / 0 new / no golden changed; boot 10/10.
- **Deferred from here:** multi-output values; recursion/arg diagnostics (BATCH-03B); editor projection
  (BATCH-03C); FunctionCall picker / signature-editing UI (BATCH-03D).

## BATCH-03B -- Compiler validation hardening
- **Goal:** Catch malformed function-graph calls at compile time instead of producing broken C# /
  infinite recursion. Extend `V_FunctionGraphCallRules`.
- **Scope / key changes:** BP1651 (target graph not found / not a Function graph); BP1652 (arg count
  mismatch: caller data-IN pin count vs target `Inputs.Count`); BP1653 (positional arg-type mismatch,
  best-effort via the existing type-resolution; conservative, no false positives); BP1654 (recursion/cycle
  detection over the Function-graph call graph -- essential since graph calls compile to synchronous C#
  method calls). Negative tests per code + a positive control.
- **Status:** DONE -- committed. Report: `reports/BATCH-03B-REPORT.md`. (BP1653 is conservative:
  Stage 2 precedes type resolution, so it compares top-level `TypeRef.TypeId` strings, treating empty /
  `System.Object` as wildcards -- no false positives; generics/array wrapping not compared.)
- **Gate:** 03A+03B tests 10/10; full suite 7 fail / 0 new / no golden changed; boot 10/10.

## BATCH-03C -- Editor projection: Entry/Return value pins + FunctionCall mirrors graph signature
- **Goal:** Editor side of the in-blueprint function-graph feature -- project the pins the canvas shows so
  they bind to exactly what the BATCH-03A compiler consumes.
- **Scope / key changes (in `Hrot.Blueprints.Editor/Host/`):** add optional `Graph? containingGraph` param
  to `NodePinSchema.GetCanonicalPins` (passed by `BlueprintGraphModel` + `BlueprintCommandSink`, which both
  hold `_graph`; catalog passes null). EventEntryNode in a Function graph -> exec-Out + one data-OUT pin
  per `Graph.Inputs` (Direction `"Out"`, Name = input name). ReturnNode -> exec-In + one data-OUT pin from
  `Outputs[0]` (Direction `"Out"` -- compiler reads `!IsExec && Direction=="Out"`; GetVariable convention).
  FunctionCall with `TargetGraphId` -> exec In/Out + data-IN per target Input (positional order) + data-OUT
  for target Output; falls back to the existing CLR-reflection path when unset/unresolved. Headless tests.
- **Status:** PENDING (spec ready: `batches/BATCH-03C-INSTRUCTIONS.md`). Queued behind 03B to avoid
  concurrent solution builds.
- **Gate:** NodePinSchema tests green; full suite subset of 7 / 0 new / no golden changed; boot 10/10.

## BATCH-03C2 -- CallPeerBlueprint arg pins via extended BlueprintSignature
- **Goal:** Resolve the BATCH-02 deferral -- project a `CallPeerBlueprintNode`'s argument data-IN pins
  (one per peer function parameter) by reading the peer's exported function signature.
- **Scope / key changes:** Extend `BlueprintSignature` to carry, per exported Function graph, its
  Inputs/Outputs (name+type) -- not just names (`ExportedFunctionNames`). Update
  `BlueprintSignatureBuilder.FromInMemoryAsset` and `BlueprintSignatureParser.ParseExportedFunctions`, and
  any `ExportedFunctionNames` consumers. Thread a sibling-signature registry (dict `Guid->BlueprintSignature`
  or a `Func<Guid,BlueprintSignature?>`) from `BlueprintDocumentFactory` -> `BlueprintGraphModel` ->
  `GetCanonicalPins`. Project `CallPeerBlueprintNode` pins: data-IN per peer-function Input + `Return`
  data-OUT (compiler consumes data-IN positionally + first data-OUT, Stage5:656-673). Headless tests with
  a stub registry.
- **Status:** PENDING. (Heavier than 03C -- changes the signature contract used by the compiler.)
- **Gate:** new tests green; full suite subset of 7 / 0 new; boot 10/10.

## BATCH-03D -- Editor UI: FunctionCall picker + graph-signature editing panel
- **Goal:** Answer "how do we configure Function Call?" with real UI, and let authors edit a graph's
  Inputs/Outputs.
- **Scope / key changes:** A `FunctionCallNodeDrawer : IBlueprintNodeDrawer` (registered in
  `BlueprintEditorBootstrap.CreateNodeDrawerRegistry`) whose `INodeEditSession.Draw()` offers a library/
  method picker (CLR: sets `TargetTypeId`/`MethodName`/`IsPure`) OR an in-blueprint function-graph picker
  (sets `TargetGraphId` from the asset's `GraphKind.Function` graphs); edits applied via `IGraphCommand`.
  A graph-signature editing panel (like `BlueprintVariablesWindow`) to add/remove/retype `Graph.Inputs`
  and `Graph.Outputs` (`ParameterDecl` rows). Reference NodeEdit demos S18_FunctionAuthoring,
  S19_MultipleReturnNodes, S30_GoToDefinition. UI rendering (`Draw()`) is not headless-testable; the
  drawer `Handles`/`CreateSession`/dirty-tracking and command `Execute`/`Undo` logic ARE.
- **Status:** PENDING. Hardest to verify (canvas/ImGui). Plan: headless-test the non-rendering logic +
  a manual editor smoke test; consider the `/run` skill to launch the editor for a visual pass.
- **Gate:** drawer/command unit tests green; full suite subset of 7 / 0 new; boot 10/10; manual smoke.

---

## BATCH-05 -- Task 6: canvas-authorable counting demo
- **Goal:** Produce a hand-authored `.bp.json` whose Tick increments a blackboard `Count` and that
  compiles + runs + shows a climbing value in the runtime inspector -- so a manual editor test is
  convincing (replaces the code-defined `CounterDemoBlueprint` workaround).
- **Scope / key changes:** Author a real `.bp.json` (projection-only `"Pins": []`) with
  GetVariable(Count) -> increment (via a CLR/in-blueprint Add FunctionCall once BATCH-03A/C land) ->
  SetVariable(Count). Add a test that compiles it and asserts `Count` climbs via `TryGetField`
  (BATCH-04 StateFields make this observable). Depends on BATCH-03A/C (authorable increment).
- **Status:** PENDING (depends on BATCH-03).
- **Gate:** demo compiles + runs; Count increments across ticks; full suite subset of 7 / 0 new.

---

## Phase 4 -- Canvas polish backlog (Task 4, lower priority, best-effort)

## BATCH-06 -- ChannelCommand param enrichment (DEBT-BCP-006)
- **Goal:** Finish/round out the ChannelCommand parameter pin enrichment. `NodePinSchema.ChannelCommandPins`
  already resolves params dynamically from the channel-command catalog; this batch closes whatever
  DEBT-BCP-006 specifically tracks (re-read the debt before scoping).
- **Status:** PENDING. Verify DEBT-BCP-006's exact remaining gap first.

## BATCH-07 -- Inline mini-editors
- **Goal:** Inline value editors on node value pins (type literals directly on the node) for common types.
- **Status:** PENDING (UI; lower priority -- the user ranked node value pins above mini-editors).

## BATCH-08 -- Fonts: multi-size atlas
- **Goal:** Engine multi-size font atlas for canvas text (see NodeEdit S05 / font handling).
- **Status:** PENDING (UI/engine).

## BATCH-09 -- Comments / reroutes / containers
- **Goal:** Canvas comments, reroute nodes, and container/grouping support (NodeEdit S06 / S26 / S27 / S35).
- **Status:** PENDING (UI).

---

## Conventions (every batch)
- Delegate implementation + test-fix to a `sonnet` coder; lead plans, reviews hard, verifies independently,
  commits per batch (message file `.git/BFxx_MSG.txt`, trailer `Co-Authored-By: Claude Opus 4.8 ...`).
- Projection-only invariant: never persist pins; keep byte-stability + compiler golden/snapshot tests green
  (re-baseline goldens only when codegen intentionally changes, with `BLUEPRINT_REGENERATE_SNAPSHOTS=1`).
- Keep the failing-test set a SUBSET of the 7 pre-existing (0 new) unless intentionally re-baselining.
- Stay on `EditorSubsystem`; no `editor_stride`; GizmoMap.Contracts stays 0.2.2; don't touch
  `Hrot.IG`/DDS/`Stride/`.
