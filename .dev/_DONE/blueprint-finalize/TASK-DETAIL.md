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
- **Status:** DONE -- committed. Report: `reports/BATCH-03C-REPORT.md`. Added `containingGraph` param +
  4 helpers (`EventEntryNodePins`/`ReturnNodePins`/`FunctionCallPinsDispatch`/`FunctionGraphCallPins`);
  call sites in `BlueprintGraphModel`/`BlueprintCommandSink` pass `_graph`.
- **Gate:** NodePinSchema 31/31; full suite 7 fail / 0 new / no golden changed; boot 10/10.

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
- **Status:** DONE -- committed. Report: `reports/BATCH-03C2-REPORT.md`. `BlueprintSignature` gained
  `ExportedFunctions` (`BlueprintFunctionSig`/`BlueprintParamSig`); `ExportedFunctionNames` kept as a
  computed property (no breaking change); builder + parser project Inputs/Outputs; `GetCanonicalPins`/
  `BlueprintGraphModel` gained a `Func<Guid,BlueprintSignature?> peerSignatureLookup`;
  `BlueprintDocumentFactory` builds a disk-backed lookup; **live-wired** in `EditorSubsystem` (a
  `FileSystemAssetCatalog` over `blueprints/` passed to the factory). 4 test construction sites updated.
- **Gate:** NodePinSchema 39/39; whole-solution build 0 warnings (contract change); full suite 7 / 0 new /
  no golden changed; boot 10/10.

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
- **Split into 03D1 (FunctionCall drawer) + 03D2 (graph-signature panel).** The node-drawer pump already
  exists: `BlueprintDetailsWindow` resolves drawers from `BlueprintNodeDrawerRegistry` and calls
  `session.Draw()` (headless `ResolveSession()` seam). A full reflection-based CLR method *browser* is
  deferred (no method catalog exists; `StaticTypeRegistry` lists primitives only).
- **BATCH-03D1 status:** DONE -- committed. `FunctionCallNodeDrawer` + `FunctionCallNodeSession`
  (CLR type/method text fields + in-blueprint Function-graph picker + IsPure; mutually-exclusive modes;
  edits mark the asset dirty via `IEditService`). Registered in `CreateNodeDrawerRegistry`. Lead added a
  mode-persistence fix so the graph picker doesn't flicker back to CLR before a graph is chosen. 19 headless
  tests; `Draw()` body still needs a manual visual smoke. Report: `reports/BATCH-03D1-REPORT.md`.
- **BATCH-03D2 status:** DONE -- committed. `GraphSignatureEditModel` (headless: Add/Remove/Rename/Retype/
  Move on a graph's Inputs or Outputs, fires `onChanged`) + `GraphSignatureWindow` (graph-picker combo over
  Function graphs; a **bespoke** 3-column ImGui rows panel — `VariablesPanelControl` was rejected as it
  carries blackboard byte-budget/pack-warning UI inappropriate for a function signature; headless
  `ResolveEditModels` seam). Wired in `EditorSubsystem` via the legacy selection bridge + `RegisterExtraWindow`
  + `Retarget`. 26 tests incl. a round-trip proving an added input projects a matching Entry data-OUT pin
  (BATCH-03C). `Draw()` needs manual smoke. Report: `reports/BATCH-03D2-REPORT.md`.
- **Gate (each):** drawer/schema unit tests green; full suite subset of 7 / 0 new; boot 10/10; manual smoke.

---

## BATCH-05 -- Task 6: canvas-authorable counting demo
- **Goal:** Produce a hand-authored `.bp.json` whose Tick increments a blackboard `Count` and that
  compiles + runs + shows a climbing value in the runtime inspector -- so a manual editor test is
  convincing (replaces the code-defined `CounterDemoBlueprint` workaround).
- **Scope / key changes:** Author a real `.bp.json` (projection-only `"Pins": []`) with
  GetVariable(Count) -> increment (via a CLR/in-blueprint Add FunctionCall once BATCH-03A/C land) ->
  SetVariable(Count). Add a test that compiles it and asserts `Count` climbs via `TryGetField`
  (BATCH-04 StateFields make this observable). Depends on BATCH-03A/C (authorable increment).
- **Status:** DONE -- committed. Report: `reports/BATCH-05-REPORT.md`. Added `BlueprintMath`
  (`FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintMath.cs`, 38 pure fns: float/int arithmetic, comparisons,
  bool logic, Vector3 ops — div/mod-by-zero → 0) in `Fdp.Toolkits` (auto in the Roslyn reference set);
  `CountingDemo.bp.json` (Instance; Tick: `EventEntry → SetVariable(Count) ← AddInt(GetVariable(Count),
  Literal(1))`); proof tests (Count==0 after attach, ==5 after 5 ticks). 69 BlueprintMath tests.
- **Pin authoring finding:** the compiler reads `node.Pins` directly (Stage4 `TypeRef.TypeId`, Stage5
  `SetVariableNode`); there is NO hydration pass. Every existing `.bp.json` with a real Tick is actually a
  variable-only stub (`"Graphs": []`) — `CountingDemo` is the FIRST compilable data-flow `.bp.json`, so it
  uses **explicit** pins. This compiles+runs and does not perturb goldens.
- **OPEN (DEBT-MVE-004?):** the projection-only invariant says editor-saved `.bp.json` store `"Pins": []`,
  but the compiler needs pins. Whether the editor's in-memory compile path hydrates pins into the asset
  before `Compiler.Compile` (making canvas-authored data-flow graphs compile) is UNVERIFIED — flagged for
  investigation. The hand-authored demo satisfies "compiles + runs + counts up"; the full canvas
  authoring→save→compile round-trip for data-flow graphs is the open piece.
- **Gate:** demo compiles + runs (0→5); 71/71 math+demo tests; full suite 7 / 0 new / no golden changed; boot 10/10.

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

# Phase 4.6 -- Build-break + live-editor fixes (DONE; recorded for history)

## NODESTATUS -- emit Fbt.NodeStatus
- **Done** `908b8a2f`. AiPrimitive/function-graph emit referenced compiler-only `Hrot.Blueprints.Core.Assets.NodeStatus`
  (only an analyzer ref in the game asm) -> CS0234; also ordinal-inverted vs `Fbt.NodeStatus`. Fixed: emit
  `global::Fbt.NodeStatus` in AiPrimitiveEmitter (x5 + drop the `(int)` cast), LibraryEmitter, TerminatorEmitter,
  StatementEmitter (WaitLowering literal). Goldens re-baselined. Compiler's internal enum unchanged.

## UX1 -- live-editor usability
- **Done** `3a53c235`. (A) Gate blueprint auto-quick-reload behind `_blueprintAutoReloadOnEdit=false` in
  `EditorSubsystem` flushAction -> no Roslyn recompile on node move/edit (use toolbar buttons). (B) `BlueprintCommandSink`
  takes `IChannelCommandCatalog`; `ApplyPinIds` passes it -> ChannelCommand projects param pins (was exec-only). (C)
  `BlueprintSelectionBridgeHelper` + `AiGraphCanvasWindow.AfterDraw` -> publishes `BlueprintNodeSelection` so Details
  resolves (bridge was never wired in prod). (D) deleted stub `GraphEditorWindow` + registration + tests. **Needs
  running-editor re-test.**

## FIXEDSTRING -- Fdp.Core.FixedString32/64 pin types
- **Done** `2bc9ae11`. StaticTypeRegistry (Unmanaged 32/64) + BlueprintTypeSystem constants/colors/SelectableTypeIds
  + host-side `StringPinEditor` registration in BlueprintDocumentFactory + ParseValue cases + EditorTypesDemo pin +
  11 tests. NOTE: inline defaults don't compile yet for ANY type (Stage3 stub) -> see AN1.

---

# Phase 5 -- Unified behavior-action nodes + enums (NOT STARTED)
Design: [ENUM-DESIGN.md](./ENUM-DESIGN.md) §RESOLVED, [ACTION-NODE-DESIGN.md](./ACTION-NODE-DESIGN.md) §ROUND-2.
Phase 5A is intended as ONE large autonomous (headless-verifiable) push; 5B is the running-editor review gate.

## AN1 -- Stage-3 default-literal materialization
- **Goal (architect gotcha):** make unconnected In-data pin defaults actually reach generated C#. Today
  `Stage3_Normalize.MaterializeDefaultPinLiterals` is a **no-op stub** — authored literals (int/float/FixedString/
  enum) render+persist (`Node.PinDefaults`/`Pin.DefaultValue`) but never compile.
- **Do:** implement the pass to synthesize literal sources for unconnected data-IN pins: primitives -> raw literal;
  `Fdp.Core.FixedString32/64` -> `new global::Fdp.Core.FixedStringNN("...")`; **enum -> `(global::FQN)N`** (integer
  cast, per architect Q3). Feeds `IrOp_Const` (StatementEmitter emits `CSharpLiteral` verbatim). Respect projection-only.
- **Files:** `Compiler/Stages/Stage3_Normalize.cs`, `Compiler/Emit/StatementEmitter.cs` (IrOp_Const), `Assets/GraphTypes.cs` (Pin.DefaultValue), `Assets/Nodes.cs` (Node.PinDefaults).
- **Verify (autonomous):** golden + e2e compile tests for a blueprint with enum/FixedString/int literal defaults;
  build 0/0; 0 new failures.

## AN2 -- StaticTypeRegistry enum-FQN acceptance
- **Goal:** enum-typed pins/params/vars resolve + pack in the reflection-less compiler. The compiler can't reflect,
  so the **editor stamps** the enum's metadata into the persisted `BlueprintTypeRef` (`FullName`=enum FQN,
  `IsUnmanaged=true`, `SizeBytes`=underlying size via `Enum.GetUnderlyingType` at edit time, default 4).
- **Do:** make `StaticTypeRegistry.TryResolve` / the type-resolve path accept such a TypeRef (or treat an unknown
  FQN carrying IsUnmanaged+SizeBytes as valid). Confirm Stage-4 (`CheckUnmanagedConstraint`) passes enums in
  Variables/WorkingState. (First, a small investigation: confirm how a pin/var TypeRef flows vs StaticTypeRegistry
  lookup, so the stamped TypeRef is honored.)
- **Files:** `Compiler/Catalogs/StaticTypeRegistry.cs`, `Compiler/Stages/Stage4_TypeResolve.cs`, editor type-stamping
  site (where a pin/var TypeRef is built — `BlueprintTypeSystem`/`NodePinSchema`/variable-create).
- **Verify (autonomous):** resolve an enum TypeRef (unmanaged, size 4); enum var passes BP1503; headless tests.

## AN3 -- Unified behavior-action catalog
- **Goal:** one facade enumerating ALL behavior actions for palette/inspector generation.
- **Do:** `IBehaviorActionCatalog` returning entries `{ FqnOrId, DisplayName, Category/Channel, ParamsTypeFqn,
  ValidHosts(Blueprint/BTree/HSM), Source(ChannelCommand|Hardcoded|AiPrimitive) }`, composing `IChannelCommandCatalog`
  + `IActionSchemaExporter` (which already reflects `[BTreeAction]`/`[HsmAction]`/`[SharedAiAction]` + AiPrimitives).
  Canonical identity = generated **FQN** (`{Namespace}.{Type}.{Method}`), not AssetId (architect AQ2).
- **Files:** new in `Hrot.Editor.AiShared` (near `Blackboard/ActionSchemaExporter.cs`) + `IChannelCommandCatalog`
  (Hrot.Blueprints.Compiler/Compiler/Catalogs). Rebuild on catalog `Changed` (post-reload).
- **Verify (autonomous):** enumerates channel + hardcoded + (post-build) AiPrimitive actions; headless tests with fakes.

## AN4 -- Per-action palette generation
- **Goal:** "one action = one node" via the palette, single underlying node kind.
- **Do:** generate one palette entry per `IBehaviorActionCatalog` action over the single `ChannelCommandNode` kind
  (replace the single generic entry at `BlueprintNodePaletteEntries.cs:108`); each entry presets `(ChannelType,
  ActionId)`/action id; on drop, the node bakes those props and `NodePinSchema.GetCanonicalPins` projects pins from
  `ParamsTypeFqn`.
- **Files:** `NodeDrawers/BlueprintNodePaletteEntries.cs`, `BlueprintEditorBootstrap.CreatePaletteRegistry`.
- **Verify (autonomous):** one entry per catalog action; placement bakes props + projects param pins (headless).

## AN5 -- Immutable action selection
- **Goal:** no runtime action-swap (chameleon hazard). Action fixed at create.
- **Do:** `ChannelCommandNodeDrawer` renders ChannelType/ActionId as **read-only labels** (remove the editable
  Combo) once the node exists; selection happens only via the AN4 palette at create-time. No JSON migration
  (fields already persisted).
- **Files:** `NodeDrawers/ChannelCommandNodeDrawer.cs`.
- **Verify:** headless logic (drawer exposes no mutation for action id); read-only render confirmed in REVIEW-V1.

## AN6 -- Blueprint enum data pins
- **Goal:** enum-typed Blueprint data pins get a combo editor (System B).
- **Do:** implement an `IEnumValueProvider` that reflects project enums (net8.0 editor) -> `EnumValueEntry[]`;
  register `EnumPinEditor(provider)` for enum TypeKeys in `BlueprintDocumentFactory` (after CreateWithBuiltins; do
  NOT edit the framework factory); `BlueprintPinModel.ParseValue` enum case (parse/persist as **int/long**);
  `BlueprintTypeSystem` enum color/name (grey fallback already exists). Pairs with AN1 (compile) + AN2 (resolve).
- **Files:** new `IEnumValueProvider` impl (Hrot.Blueprints.Editor.Host), `Host/BlueprintDocumentFactory.cs`,
  `Host/BlueprintPinModel.cs`, `Host/BlueprintTypeSystem.cs`.
- **Verify (autonomous):** provider returns members for a test enum; registry returns EnumPinEditor for an enum
  TypeKey; ParseValue round-trips an int; headless. Combo render confirmed in REVIEW-V1.

## REVIEW-V1 (Phase 5B gate)
- Running editor: per-action palette lists actions; drop -> immutable node, baked param pins, read-only action
  labels; enum pin shows combo; set an enum default + compile -> `(global::FQN)N` in generated code, runs.

---

# Phase 6 -- BTree/HSM StructEdit inspector + param binding (NOT STARTED; Blackboard Slice 1.5)

## SE1 -- Wire InspectorWindow StructEdit
- **Goal (architect gotcha, foundational):** replace the stubbed `InspectorWindow.DrawClientArea` "Apply" button
  with the active `StructEdit IComponentEditService` dispatch over the mapped facets. BTree/HSM facet fields then
  render + edit; **enum combos come free** (ComponentEditDrawer reflection).
- **Files:** `Hrot.Editor.AiShared/Windows/InspectorWindow.cs` (~208-213 stub), the StructEdit `IComponentEditService`
  wiring, `HsmFacetDispatcher`/`BTreeFacets` (already wired).
- **Verify:** facet structs render headlessly where possible (service builds an EditDocument); REVIEW-V2 visual.

## REVIEW-V2 (gate)
- Running editor: BTree/HSM facet fields render + edit in the Inspector; enum fields show combos.

# Phase 7 -- BB1: Action-parameter authoring + node-owned variables
**Design (APPROVED):** `docs/blueprints/Blackboard_Authoring_Addendum_v3_ActionParamAuthoring.md` + ACTION-NODE-DESIGN.md
"BB1 MODEL RESOLVED". Action node binds its WHOLE param DTO to ONE blackboard variable (`ExpressionTargetField`);
per-field binding REJECTED (breaks the kernel's contiguous zero-alloc projection). "+ Promote to new variable"
auto-creates a node-owned variable for the blueprint-like node-local feel. Static defaults baked into the generated
`ParseParamsDelegate` at assignment; dynamic via Approach A (alias) / B (Subtree sync). Builds on SE1/SE2.

## B-1 -- Type-filtered binding picker
- **Goal:** the action's binding dropdown shows only compatible variables.
- **Do:** `[BlackboardFieldPicker]` (BTree `BlackboardFieldPickerDrawer` / HSM equivalents) consults the action's
  schema `DtoType` (`IActionSchemaExporter`) and lists only blackboard variables of that type; show
  `(no compatible variables)` + the Promote affordance (B-2) when none. (DD §11.2)
- **Files:** `BTreePickerDrawers.cs` (BlackboardFieldPickerDrawer), HSM picker drawers, the facet mapping.
- **Verify:** headless — given an action with DtoType T and a blackboard with vars of T and U, the picker offers only
  the T vars.

## B-2 -- Promote to new variable + IsAutoManaged
- **Goal:** in-context creation of a correctly-typed, node-owned variable.
- **Do:** add **`IsAutoManaged`** (bool) to `BlackboardVariableDto` (persisted JSON) + `BlackboardVariableEntry`
  (editor). The picker's inline "+ Promote to new variable" creates a variable named `_auto_{VisualId:N}` (BTree) /
  `_auto_{StableId:N}` (HSM) of the action's DtoType, sets `IsAutoManaged=true`, binds the node's
  `ExpressionTargetField` to it. Downstream (generator/bin-packer/ParseParamsDelegate) ignores the flag. (DD §11.3,
  Addendum §3)
- **Verify:** headless — Promote yields a uniquely-named auto var of the right type, bound; round-trips through JSON
  with `IsAutoManaged=true`.

## B-3 -- StructEdit editing of the variable default
- **Goal:** author the static param values (the bound variable's `DefaultValueJson`) in-context.
- **Do:** render the bound variable's default via the SE1 StructEdit surface (DTO fields → enums combos, vectors,
  FixedString, etc.); writes back to `DefaultValueJson`. Works for both node-owned and shared variables.
- **Verify:** headless — set a DTO field's default via the edit service → persisted in the variable's
  `DefaultValueJson`; an enum field round-trips by name.

## B-4 -- Node-owned variable presentation + lifecycle
- **Goal:** keep the panel clean + the auto var node-local; no orphans.
- **Do:** `VariablesPanelControl` filters `IsAutoManaged==true` OUT of the main "Defined Variables" list → renders a
  dimmed, read-only **"Node-Owned Allocations"** sub-group (or behind a toggle). **EXCLUDE** node-owned vars from the
  Approach-A alias drop-target list. `BTreeCommandSink`/`HsmCommandSink`: on owning-action-node delete, remove the
  node-owned variable + trigger re-pack. (Addendum §3.5–§3.7)
- **Verify:** headless — auto var filtered from the defined list + excluded from alias targets; deleting the owning
  node removes the auto var.

## B-5 -- Static-vs-dynamic tooltip
- **Goal:** prevent designer surprise about timing.
- **Do:** one-line Inspector tooltip on the param-binding row: BTree/HSM static value = applied once at behavior
  assignment; bind a variable for live/dynamic values. (Addendum §4.1)
- **Verify:** present (visual); no functional test needed.

## REVIEW-BB1 (gate)
- Running editor: type-filtered picker; Promote → set static params in-context → assign/compile uses them; node-owned
  var dimmed/hidden + auto-deleted with the node.

---

# Phase 5C -- Generalize to non-channel behavior actions (ROUND-3; ACTION-NODE-DESIGN.md §ROUND-3)

## ENUM-SAMPLE -- enum-param action for live testing
- **Goal:** give the user a behavior action whose param DTO has an ENUM field, so the AN6 enum pin combo is
  live-testable (render + persist + compile). No existing channel-command DTO has an enum field, and the
  variable-type picker doesn't offer enums — so there is no live enum surface today.
- **Approach (testable NOW with AN1/AN2/AN6 + the AN4 channel palette):** add a small DEMO enum + a blittable
  demo param struct (with the enum field + maybe one primitive) in a reflectable assembly (Hrot.AI.Behaviors or a
  toolkit), and a `BuiltInChannelCommandCatalog` entry referencing it (reuse an existing channel FQN, e.g.
  LocomotionChannel, with an unused ActionId). The per-action palette then surfaces it; dropping it projects an
  enum data-IN pin (combo); setting a value persists to PinDefaults and compiles to `(global::FQN)N` (AN1). Mark
  clearly as a DEMO (removable). Runtime no-op (no executor for the demo ActionId) — that's fine; this is an
  authoring/compile test.
- **Verify:** palette shows the demo action; NodePinSchema projects the enum pin with a `global::` TypeId; the
  recipe/asset compiles; headless tests. Live combo render = REVIEW.

## AN7 -- Generalize node + palette to non-channel actions
- **Goal:** the generalized behavior-action node dispatches non-channel actions too (`[SharedAiAction]` etc.).
- **Do (editor):** give the node (ChannelCommandNode, repurposed) a non-channel **action FQN** identity alongside
  the channel `(ChannelType, ActionId)`; generate palette entries from the AN3 unified catalog's NON-channel
  actions (named by FQN); `NodePinSchema.GetCanonicalPins` projects pins from the action's `ParamsTypeFqn`
  (reflect the DTO; enum fields per AN6); drawer shows the action identity read-only (AN5 pattern). Bake identity
  at create (immutable, D-B).
- **Verify:** palette lists non-channel actions by FQN; placement bakes the FQN + projects its param pins;
  headless. Compile of such a node depends on AN8.

## AN8 -- Compiler lowering for non-channel behavior-action invocation
- **Goal (LARGE):** lower a non-channel behavior-action node in a Blueprint graph. Unlike a channel command (CQRS
  write) or FunctionCall (inline), it invokes the action with the state-machine signature
  `(self, ECS context, params DTO) -> global::Fbt.NodeStatus` via `BehaviorRegistry` routing, with the params
  built from the data-IN pins; Success/Failure drive exec-out; **Running suspends** (mirror the
  channel-command + `WaitForChannel` latent/`BlueprintLatentCursor` path — dispatch-aware per the architect's
  WaitForChannel note). Confirm how BehaviorRegistry exposes an invokable thunk for an action FQN at runtime and
  how the blueprint obtains/calls it.
- **Verify:** e2e compile + (where feasible) execute a blueprint invoking a non-channel action; golden/emit tests;
  0 new failures. Sequence after AN7.

## AN9 -- "Wait Until Completed" static metadata
- **Goal (ROUND-5):** make channel + non-channel action nodes block-by-default consistent, WITHOUT a runtime pin
  (latency must be compile-time-static) and WITHOUT a fused node.
- **Do:** add a STATIC bool `WaitUntilCompleted` (default **true**) to the generalized action node (persisted),
  rendered as a Details checkbox. Stage-5 fuses by the static value: channel + true → emit `IrOp_ChannelCommand`
  then split the block + `IrOp_WaitForChannel` (reuse the existing WaitForChannel latent lowering); channel + false →
  ChannelCommand only (fire-and-forget); non-channel + true → inline-latent (AN8); non-channel + false → **forbidden**.
  UI: checkbox disabled+locked-true for non-channel actions (Inspector reads the action schema). Stage-2 `Validate`
  emits **BP1405** if a non-channel action has WaitUntilCompleted=false in JSON. `WaitForChannelNode` REMAINS a
  separate palette node for the manual fire-then-sync-later path.
- **Files:** the generalized action node model + drawer; `Stage5_Schedule` (fuse), `Stage2_Validate` (BP1405),
  ChannelCommandNodeDrawer/Inspector (checkbox).
- **Verify:** golden/emit — channel+true emits ChannelCommand+WaitForChannel; channel+false emits ChannelCommand
  only; BP1405 fires for non-channel+false; existing tests green.

---

# Phase 6.1 -- Enum/JSON polish + morning fixes (DONE; recorded for history)

## ENUM-NAME -- enum persisted + emitted by member name
- **Done** `7c9b7189`. PinDefaults stores the enum member NAME ("Crouching"); codegen emits `global::FQN.Member`
  (integer back-compat kept). Conversion at the editor seam (ParseValue name→long via the provider; FormatEnumValue
  long→name). Reorder-robust; rename → CS error (compiler safety net).

## JSON-PRETTY -- pretty-print .bp.json saves
- **Done** `5e1b97be`. `SaveActiveBlueprintCommand.Save` applies `JsonAestheticFormatter.FlattenNumericArrays`
  (indented + numeric arrays inlined). Compiler `Serialize` stays minified (golden tests). Committed assets
  reformatted (semantically identical). User experiment files untouched.

## FIX-A / FIX-B / FIX-C -- morning Inspector + vector fixes
- **Done** `31c9d4b1` (A+B), `8d411bf5` (C). A: BTree/HSM canvas selection→facet bridge (SetFacetDispatcher per
  asset + canvas AfterDraw + AiCanvasContext.AssetRef) — new BTree/HSM SelectionBridgeHelpers. B: vector pin-default
  invariant `[x, y, z]` (was culture-dependent `<0  4,5  0>`). C: Inspector facet session keyed by node identity
  (sub-selection), not just facet type, so same-type nodes show their own values.

# Phase 8 -- Follow-ups (smaller; after BB1 / on demand)

## HSM-TRANS -- HSM transition facets
- **Goal:** clicking an HSM **transition** (not just a state) shows its facet in the Inspector.
- **Do:** extend `HsmSelectionBridgeHelper.MapSelection` to map a selected canvas **link** (`ILinkModel`, the
  transition) → the right HSM transition sub-selection, so `HsmFacetDispatcher.GetFacet` returns the transition
  facet. FIX-A wired states only.
- **Verify:** headless map test (transition link → transition sub-selection); visual at review.

## JSON-PRETTY-BTHSM -- pretty-print BTree/HSM JSON
- **Goal:** consistency with JSON-PRETTY (blueprint).
- **Do:** apply `JsonAestheticFormatter.FlattenNumericArrays` at the BTree/HSM save path(s)
  (`Hrot.AiEditor.Persistence` / the BTree/HSM save commands), mirroring JSON-PRETTY; update any byte-stability
  tests; reformat committed `.btree.json`/`.hsm.json` (verify semantically identical).
- **Verify:** saved BTree/HSM JSON indented + arrays inlined; round-trips; suites 0 new failures.

---

## Conventions (every batch)
- Delegate implementation + test-fix to a `sonnet` coder; lead plans, reviews hard, verifies independently,
  commits per batch (message file `.git/BFxx_MSG.txt`, trailer `Co-Authored-By: Claude Opus 4.8 ...`).
- Projection-only invariant: never persist pins; keep byte-stability + compiler golden/snapshot tests green
  (re-baseline goldens only when codegen intentionally changes, with `BLUEPRINT_REGENERATE_SNAPSHOTS=1`).
- Keep the failing-test set a SUBSET of the 7 pre-existing (0 new) unless intentionally re-baselining.
- Stay on `EditorSubsystem`; no `editor_stride`; GizmoMap.Contracts stays 0.2.2; don't touch
  `Hrot.IG`/DDS/`Stride/`.
