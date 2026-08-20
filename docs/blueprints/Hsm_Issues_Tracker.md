# HSM — Issue Tracker

> **Scope: the whole HSM stack, not just the editor.** ⭐ **Widened 2026-08-14 by user direction** —
> *"this design session needs to record also HSM infrastructure issues, not just visual editing."*
> Covers `Hrot.Hsm.Editor` and its `EditorSubsystem` wiring · `Hrot.AiEditor.Persistence` /
> `.Generators` (HSM codegen) · `Fdp.Toolkits.Behavior` (runtime) · `Fhsm.Kernel` / `Fhsm.Compiler`
> where a kernel limitation shapes what can be authored.
> *(Renamed from `Hsm_Visual_Editing_Issues_Tracker.md` at the same time. Row ids are unchanged.)*
> BTree has its own gaps; they are not tracked here except where a mechanism is shared.
> **Status:** open design session. **No code is being written against these rows yet** — the
> tracker exists so findings accumulate while the design is settled.
> **Branch:** `claude/hsm-visual-editing-9ngei4` (based on `claude/blueprint-authoring-status-gm0akp`).

**Layer** tag on each area: 🎨 authoring/editor · ⚙️ codegen · 🔧 runtime/kernel. A row's cost often
lives in a different layer from its symptom — HSM-012/013/015/016 all *surface* in the editor and are
*fixed* below it.

**ID prefix `HSM-nnn`** — deliberately distinct from `BP-nnn`. The blueprint programme's two-session
protocol has produced three ID collisions from shared blocks; a separate prefix makes that
structurally impossible here.

**Complexity:** `WIRING` = call existing code, no new logic · `RW-L` = real work, low (≲150 lines) ·
`RW-M` = real work, medium (new component / some design) · `RW-H` = new subsystem or architect
decision first.
🔴 = correctness / data-loss, not an enhancement. 📐 = needs a design ruling before it can be scoped.

| Complexity | Open | Done |
|---|---:|---:|
| `WIRING` | 1 | 0 |
| `RW-L` | 7 | 0 |
| `RW-M` | 7 | 1 |
| `RW-H` | 3 | 0 |
| **Total** | **18** | **1** |

⭐ **New here? Read [Hsm_Integration_Map.md](Hsm_Integration_Map.md) first** — how an HSM gets from
the canvas to a ticking entity, with every stage cited. These rows assume it.

📌 **Resuming this session?** [Hsm_Design_Session_RESUME.md](Hsm_Design_Session_RESUME.md) — established
facts, open rulings, and the verification discipline. This tracker stays the source of truth for the
rows themselves.

📘 **New to HSM concepts?** [Hsm_Concepts_For_Game_AI.md](Hsm_Concepts_For_Game_AI.md) explains what
history / parallel / hierarchy / actions actually mean here, grounded in the FastHSM kernel rather
than generic UML, and ranks which parts are needed first.

Reconciliation — all three must agree (checkbox tally, column sum, per-tag counts):
```bash
grep -c '^- \[ \]' Hsm_Issues_Tracker.md   # open -> Total row
grep -c '^- \[x\]' Hsm_Issues_Tracker.md   # done -> Total row
# per-complexity: take the FIRST tag on each row — rows discuss other classes in their prose
grep '^- \[ \]' Hsm_Issues_Tracker.md \
  | grep -oPm1 '(?<=`)(WIRING|RW-L|RW-M|RW-H)(?=`)' | sort | uniq -c
```
⚠ `grep -c` over the whole row over-counts — HSM-009 names both `WIRING` and `RW-M`. First tag wins;
this is the same trap the blueprint tracker documents at its Batch 27 note.

---

## How these were found

Docs read: `HSM_Editor_NodeEditor_Host_Design.md` (feature/UX target),
`BTree_HSM_JSON_Persistence_Detailed_Design.md` (substrate of record),
`BTree_HSM_Editor_State_And_Forward_Plan.md` (plan — **stale**, see HSM-011),
`docs/projects/Hrot/AI/Hrot.Hsm.Editor.md`.

Rows marked **✅ reproduced** were confirmed by a throwaway xUnit probe run against the real
assemblies, quoted inline, then deleted.

> ### ⭐ RE-VERIFIED `2026-08-20`, after merging 428 coordinator commits (Batch 46 → 98)
>
> The original audit ran on a base that had gone **428 commits stale**, and the coordinator branch
> had done substantial HSM work in between (Batches 58, 59, 67, 71–78, 92). **Every reproduced row
> was re-run on the merged tree.**
>
> | | |
> |---|---|
> | **HSM-001, 002, 004, 005, 006, 013** | ⭐ **reproduce identically** — same outputs, same values |
> | **HSM-007, 009, 012, 014** | ⭐ still live — re-greped repo-wide |
> | **HSM-016** | ✅ **FIXED upstream in Batch 59** — closed, see the row |
> | **HSM-015** | still live: `AiPrimitiveEmitter:420,448` still `ref var p = ref *(Params*)instance` |
> | **HSM-013** | still live: `CSharpEmitter:383,385` still keys on `(ushort)BlueprintId` |
>
> Baseline on the merged tree: `Hrot.Hsm.Editor.Tests` **554/554 green** (was 510 — the merge added
> 44), solution build **0 errors**.
>
> 📌 **Lesson recorded:** a tracker built off a stale base carries rows the world has already closed.
> Re-sync and re-verify **before** presenting findings, not after — this is `.claude/CLAUDE.md`
> rule 7 (*re-sync from the coordinator branch at the START of every run*), and skipping it cost one
> row's credibility here.

> ⚠ **A "nothing calls X" claim needs the whole repo and the graph, not one grep.** During this
> session it was asserted that *"no HROT runtime code drives an HSM instance"*. **That was wrong.**
> `HsmTickSystem<T>` is a real registered ECS system (`SystemPhase.Simulation`, wired in
> `CognitiveRuntimeModule` for `BrainHsm64` and `BrainHsm128`). The claim came from a grep scoped to
> `Hrot/` — but the HSM runtime lives in `FDP/Toolkits/` — which additionally piped through
> `grep -v Editor`, and whose non-empty output was misread as empty.
> **Rule adopted:** every negative claim in this tracker must be backed by (a) a repo-wide search
> with no directory filter, and (b) a `codebase-memory-mcp` graph query over `CALLS` edges. Both are
> cited on the rows that make such claims (HSM-007, HSM-009, HSM-012).

---

## Area A 🎨 — The initial-state model

The single root cause behind HSM-001 and HSM-002: **initial state has two sources of truth.**

| Container | Where "initial" lives | Who writes it |
|---|---|---|
| Normal composite | `StateNode.IsInitial` on the child | `StateFacet.Flags` checkbox |
| Parallel region | `RegionNode.InitialChild` reference | `RegionFacet.InitialChildName` |

Nothing synchronises them. `HsmFacetDispatcher` sets `r.InitialChild` and never touches
`child.IsInitial`. `HsmValidator` reads only `IsInitial`. `HsmInitialArrowRenderer` reads
`RegionNode.InitialChild` for parallel and `IsInitial` for composite — so the canvas and the
validator can disagree about the same machine.

- [ ] **HSM-001** 🔴 · `RW-L` — **Every UML-correct parallel composite is reported as an error.**
  `HsmValidator.CheckInitialChildren` counts `IsInitial` across *all* children of a state, with no
  awareness of regions. A parallel state with 2 regions, each with its own initial child — correct
  by UML and by the kernel — yields `initialCount == 2`. ✅ **reproduced:**
  `Error MultipleInitialChildrenInSameParent: Composite state 'ParallelWork' has 2 children marked as initial; only one is allowed.`
  ⚠ **`HsmShowcase.hsm.json` was authored around this defect:** `RegionB.InitialChild = WorkB` but
  `WorkB.IsInitial = false`, and likewise for `WorkC`. The showcase passes validation by being
  semantically under-specified, which is why 510 green tests never caught it. Blocked on the
  Area-A ruling (HSM-003).

- [ ] **HSM-002** 🔴 · `RW-L` — **A parallel region with no initial child at all passes clean.** The
  mirror of HSM-001. Region 0's initial child satisfies the whole-state count, so region 1 having
  `InitialChildStableId: null` produces **zero diagnostics** — the check that matters most for
  parallel states does not exist in any form. ✅ **reproduced:** validator returned an empty
  collection for a 2-region parallel state whose region 1 had no initial child. Blocked on HSM-003.

- [ ] **HSM-003** 📐 · `RW-H` — **Decide the initial-state model.** Two candidate shapes:
  **(A)** `RegionNode.InitialChild` becomes the single source of truth for *both* container kinds
  (a normal composite is modelled as an implicit single region); `IsInitial` becomes derived/display
  only. **(B)** `IsInitial` stays authoritative and the validator, renderer and persistence all
  become region-scoped. (A) unifies the two paths and matches how the kernel stores it
  (`StateDef.InitialChildIndex` / `RegionDef.InitialStateIndex`); (B) is a smaller diff but keeps
  two writers on one fact. Touches model, persistence, validator, facets and renderer — **this is
  the ruling that unblocks HSM-001, HSM-002 and shapes HSM-004.**

---

## Area B 🎨 — Region persistence and editing

- [ ] **HSM-004** 🔴 · `RW-M` — **A region with no initial child is silently destroyed on reload.**
  `RegionNodeDto` carries **no owner-state reference** (`StableId, RegionIndex, Name, Priority,
  InitialChildStableId, Comment, ColorOverride`). `HsmAssetMapper` recovers the owner via
  `region.InitialChild?.Parent`, and the code comment calls this *"the unambiguous owner"* — it is
  neither unambiguous (it breaks when the initial child is reparented) nor total (it fails when
  `InitialChild` is null). ✅ **reproduced:**
  ```
  AllRegions           = 2
  parallel.RegionNodes = 1 -> [RegionA]
  ```
  **This is on the primary authoring path:** `ApplyAddRegion` creates a region with no initial
  child, so *add region → save → reopen → the region is gone*. `HsmAssetMapperRegionAttachTests`
  covers 2/3/4 regions but every fixture gives each region an initial child, so the null case is
  untested. ⚠ `HsmShowcase.hsm.json` already loses `TopRegion` this way — its `InitialChild` is the
  synthetic `__Root`, whose `Parent` is null. **Fix direction:** add an owner field to the DTO
  (a schema change — needs a migration stance for existing assets).

- [ ] **HSM-005** 🔴 · `RW-L` — **Removing a region corrupts the surviving children's region
  indices.** `ApplyRemoveRegion` re-indexes `state.RegionNodes` but never re-maps the children
  pointing into that list; only children *of the removed region* are touched. ✅ **reproduced**,
  removing the middle of three regions:
  ```
  regions now: [Region0@0, Region2@1]
    child WorkA -> RegionIndex 0
    child WorkB -> RegionIndex 0
    child WorkC -> RegionIndex 2   <- out of range; only 2 regions remain
  ```
  Every child with an index above the removed one is left stale. Self-contained fix; no design
  ruling needed.

---

## Area C 🎨⚙️ — Identity and emit

- [ ] **HSM-006** 🔴 · `RW-M` — **Palette-created states all get the same name, and names are load
  bearing.** `ApplyAddNode` hard-codes `"State"` (`"Parallel"`, `"Final"`, … per kind) and nothing
  anywhere enforces uniqueness. ✅ **reproduced:** two palette placements → `names: [State, State]`.
  This is **not cosmetic**: `HsmEmitCore` resolves transition targets *by name* —
  `.GoTo("State", visualId: …)` — so two same-named states mean the fluent builder binds to
  whichever it resolves first. **Silently wrong machine, no diagnostic, at build time.**
  Two candidate fixes, and they are not exclusive: (1) auto-uniquify on create (`State`, `State1`,
  …) — cheap; (2) a `DuplicateStateName` validator rule — catches renames and hand-edited JSON too.
  ⚠ (1) alone does not close it, because the Inspector lets you rename a state to an existing name.

---

## Area D 🎨 — Built but never fed

Each of these is a component that exists, is tested in isolation, and has **zero production
callers** — the pipe is wired, nothing fills it.

- [ ] **HSM-007** · `WIRING` — **`OutputLaneMask` is never computed on the JSON path, so the whole
  lane-conflict feature is inert.** `HsmOutputLaneMaskInferrer.ApplyToAsset` / `BuildLaneDictionary`
  have no callers outside their own tests. `StateNodeDto` has no lane field, and the only production
  writer is `HsmAssetProjector:53` — the *legacy reflection* path, used for hand-authored assets.
  For every editor-owned asset the mask stays `0`, therefore:
  `HsmValidator.CheckOutputLaneConflicts` can never fire · `HsmRegionConflictsRenderer` stays dark
  despite being correctly fed by `HsmDocumentFactory:99` · the inspector's read-only
  "Output lanes (inferred)" summary is always blank. The design's §10.3 inference is implemented and
  unreachable. ⚠ Design decision embedded here: is the mask **inferred at load** (reflect the
  assembly each open) or **persisted in the DTO** (fast, but a second source of truth that drifts
  from the `[HsmAction].Lane` attributes)? Host doc §19 Q2 asks the analogous question about
  emitting it and leans "keep it inferred".

- [ ] **HSM-008** · `RW-L` — **Lane-conflict detection only looks at direct children.**
  `CheckOutputLaneConflicts` ORs masks from `s.Children` only; design §12.2 specifies *"the union of
  `OutputLaneMask` across all **leaf states** in R1"*. Any conflict one level down is invisible.
  Rules 8/8b (`ConcurrentStatefulSubtree`, `ConcurrentSharedScopeKey`) share the same direct-children
  restriction and say so in their comments. Gated behind HSM-007 — until masks are populated this
  rule cannot fire at all, so fix them together.

- [ ] **HSM-009** · `RW-M` — **The Events table and Globals strip are built but never registered,
  and there is no way to author an event.** `HsmEventsWindow` and `HsmGlobalsStrip` have zero
  production callers; `EditorSubsystem` registers only the canvas
  (`_hsmRegistrar.RegisterExtraWindow(windowManager, hsmCanvasWindow)`). Separately and more
  seriously, **no event-authoring path exists at all**: `EventDefinition` is only ever constructed
  by `HsmAssetProjector` (from a compiled blob) and `HsmAssetMapper` (from disk) — there is no
  add / rename / delete. Consequence: a transition's `[HsmEventPicker] EventId` has nothing to pick
  from, and authoring a triggered transition requires **hand-editing the `.hsm.json`** — which
  directly fails the Phase-1 bar of *"build a working machine without touching C#"*. This is the
  largest functional hole in the editor. Registering the window is `WIRING`; the authoring
  commands behind it are `RW-M`.

---

## Area E 🎨🔧 — Design contradictions

- [ ] **HSM-010** 🔴📐 · `RW-M` — **History is modelled as the wrong kind of thing; the palette
  produces states the kernel cannot act on.** ⭐ **Upgraded 2026-08-14 after reading the kernel —
  this is not a validator strictness issue, it is a modelling mismatch.**
  **What FastHSM actually does:** history is a **flag on the composite that owns the children** —
  `builder.State("Engaging").History().Child("Chasing"…).Child("Attacking"…)`
  ([Fhsm.Compiler.md §Example 3](../projects/FDP/ExtDeps/FastHSM/Fhsm.Compiler.md)), backed by
  `StateDef.HistorySlotIndex` on that composite. **There is no history pseudo-state in this kernel** —
  nothing to draw as a node, nothing to transition into.
  **What the editor does:** `HSM_Editor_NodeEditor_Host_Design.md` §8.2 chose UML Option B —
  *"distinct palette entries that produce small dedicated state nodes"* — so `hsm.state.history` /
  `hsm.state.deepHistory` create a **separate childless `StateNode` with `IsHistory = true`**.
  `HsmEmitCore:649` then emits `.History()` on it, i.e. *"a state with no children remembers its
  last active child"* — semantically null. ✅ **`HsmShowcase.hsm.json` contains exactly this:**
  `HistoryPseudo`, `ChildStableIds: []`, `IsHistory: true`, sitting as a *sibling* of `Idle` /
  `Scanning` / `AlertState` inside `GuardComposite` — where the correct modelling is
  `GuardComposite.History()`.
  **Second-order symptom** (what this row originally recorded): `HsmLinkValidator` blanket-rejects
  transitions targeting `IsHistory || IsDeepHistory`, so the nodes are placeable, render their
  `H` / `H*` glyphs, and are unreachable. The design's own §5.3 sketch had an
  `IsExplicitHistoryEntry(...)` escape hatch that was never implemented — but under the correct
  model **no such hatch is needed**, because history stops being a transition target at all.
  **Ruling needed** (host doc §19 open question #4 asked for exactly this review and never got it —
  the kernel has since answered it): withdraw the two palette entries and expose history as a
  **checkbox on the composite's `StateFacet`** (kernel-faithful, one writer, deletes the
  `hsm.history_glyphs` rendering bypass for the H/H\* case), versus keeping the UML pseudo-state as
  an *editor-side sugar* that lowers onto the parent flag at emit. ⚠ Migration: `HsmShowcase`'s
  `HistoryPseudo` node has to be rewritten either way.

---

## Area G ⚙️🔧 — Blueprint AiPrimitives as HSM actions / guards

The whole path is built: `AiPrimitiveHosting` has `HsmAction` and `HsmGuard`;
`AiPrimitiveEmitter` emits `HsmActivity` / `HsmGuard` thunks; `CSharpEmitter:353-356` emits the
registration; `Stage2_Validate.V_DispatchKindCompatibility` pairs `BTreeAction↔HsmAction` and
`BTreeCondition↔HsmGuard`. Two things break it in practice.

- [ ] **HSM-013** 🔴 · `RW-M` — **The id an AiPrimitive registers under can never equal the id the
  machine looks up — two hashes over different inputs.**
  Registration (`CSharpEmitter.cs:354`):
  `HsmActionDispatcher.RegisterAction(unchecked((ushort)ClassName.BlueprintId), &HsmActivity)`, where
  `BlueprintId = FNV-1a-32 over the asset GUID's 16 bytes` (`BlueprintIdHash.Compute`).
  Lookup: the machine's `StateDef.ActivityActionId` comes from
  `HsmFlattener.BuildActionTable` → `ComputeHash(actionName)` = **FNV-1a-32 over the UTF-16 chars of
  the action-name string**, truncated to 16 bits (`HsmFlattener.cs:385-394`).
  A GUID hash and a name hash coincide only by accident (~1/65536).
  ✅ **reproduced** — registrar key for asset `a3f2c5d8-…` is **62025**; flattening a machine whose
  state carries that primitive in its Activity slot gives:
  ```
  .Activity("HsmTestAction")                            -> 50045   no
  .Activity("Hrot.AI.Behaviors.Blueprints.HsmTestAction")-> 46437   no
  .Activity("HsmTestAction_A3F2C5D8_Bp.HsmActivity")     -> 22562   no
  .Activity("a3f2c5d8-9c01-4b2e-8d7a-1f6e5c4b3a29")      -> 38138   no
  ```
  **No spelling of the name can reach the thunk.**
  ⚠ **Both failure modes are silent, and the guard one is dangerous:**
  `HsmActionDispatcher.ExecuteAction` does nothing when the id is absent, and `EvaluateGuard`
  **returns `true`** (`// No guard = always pass`). So a blueprint-backed HSM action never runs, and
  a blueprint-backed HSM *guard* is treated as permanently satisfied — the transition always fires.
  ⚠ **The existing tests do not cover this.** `HsmInvokeHelpersTests` proves the thunk is registered
  and invocable, but `BlueprintTestFixture.InvokeHsmAction` computes the id as
  `(ushort)BlueprintIdHash.Compute(asset.AssetId)` (`:565`) — the same key the registrar used. It
  never goes through a machine's name → `ComputeHash` path, which is the only path an author has.
  **Fix direction.** ⭐⭐ **ANSWERED upstream — the canonical form is already ruled.** Coordinator
  ledger **`R-88`**: *"offsets come from the packer and the thunk key is `MethodFqn@offset`"*, stated
  for **the BTree/HSM `Role=Input` params case jointly**. ⇒ this is no longer an open design
  question, it is an **unimplemented ruling**. 📐 **Measured on the merged tree `2026-08-20`:**
  `BTreeBridgeEmitCore` contains **50** `MethodFqn` references; `HsmBridgeEmitCore` contains **0**
  ⇒ BTree ships the binding half, HSM does not — exactly the coordinator's own ruling *"on HSM,
  absent NEVER means unwanted — it is behind, not scoped out"* (`2026-08-16`).

- [x] **HSM-016** ✅ **CLOSED — already fixed upstream, found independently.** · `RW-M` —
  *The `[BlueprintRegistrar]` bridge registered no-op stubs at placeholder ids 100+/200+.*
  ⭐ **Fixed in Batch 59 (`W3`) on the coordinator branch, before this session ever saw it** — this
  row was raised against a base that was 428 commits stale. Verified on the merged tree
  `2026-08-20`: `HsmBridgeEmitCore:162-188` now carries the deletion rationale as a comment, and it
  reaches **the same two conclusions this session derived independently** — (1) nothing ever looked
  the ids up, because the blob addresses `ComputeHash(name)` only; (2) they could silently overwrite
  a real action, because `RegisterAction` is `ActionTable[id] = a`, last-writer-wins, and
  `ComputeHash` ranges over all of `0…65535` including those windows.
  ⭐ **Regression-railed:** `BHU_020` (Batch 58) ranges over the final id set, so a reintroduced
  counter-allocated registration that collides with a hashed one fails the build.
  📌 **Kept as a row, not deleted** — it is the evidence that two independent audits reached the same
  root cause, and the rail is worth knowing about.

- [ ] **HSM-015** 🔴📐 · `RW-H` — **The generated HSM thunk reads its parameters out of the live HSM
  instance memory. There is nowhere else for them to live.**
  `AiPrimitiveEmitter.EmitHsmActivityThunk` / `EmitHsmGuardThunk` both emit:
  ```csharp
  ref var p = ref *(Params*)instance;
  ```
  but `instance` is the pointer the kernel passes —
  `HsmActionDispatcher.ExecuteAction(actionId, instancePtr, contextPtr, writerPtr)`
  (`HsmKernelCore.cs:763`), i.e. the **`HsmInstance64/128/256` runtime memory**: `InstanceHeader`
  (MachineId, Flags, Phase, Generation), `ActiveLeafIds`, `TimerDeadlines`, `HistorySlots`,
  `EventQueue`. The primitive's `Params` struct is reinterpreted over that.
  ⇒ every parameter reads bytes of state-machine bookkeeping. And because it is a **`ref`**, any
  write `TickCore` performs through `p` lands in the live instance — active leaf ids, phase and the
  event queue are within the first bytes.
  **Contrast the BTree sibling, which is correct:**
  ```csharp
  ref var p = ref Unsafe.As<byte, Params>(ref bb.BehaviorParameters[paramIndex * sizeof(Params)]);
  ```
  — params come from the `BrainBlackboard` param region, indexed by a per-node `paramIndex`.
  **The HSM side has no equivalent *field*.** `StateDef` is a full 32 bytes with only `Reserved29`
  (1 byte) spare and no param field; `TransitionDef` is a full 16 bytes with none either.
  ⭐ **REFINED 2026-08-14 — a ROM change is probably NOT required.** BTree does not carry the offset
  in a ROM field either: it bakes it into the **action key string**,
  `{MethodFqn}@{offset}` (and `{MethodFqn}@{offset}@{slotKey}` for stateful), per
  `BTreeBridgeEmitCore.cs:460,488,622`. HSM action ids are `ComputeHash(<arbitrary name string>)`,
  so the identical trick applies: the editor writes the slot's action name as `Ns.Type.Method@40`,
  `HsmFlattener` hashes the whole string into `ActivityActionId`, and the generated bridge registers
  a thunk under `ComputeHash("Ns.Type.Method@40")` that projects the DTO at offset 40.
  **The offset rides in the hashed identity — no new `StateDef` field, no blob format change.**
  ⇒ HSM-013 (identity) and HSM-015 (params) are then **one fix**, and it is the fix BTree already
  shipped. This is the central proposal to put to the blueprint session — see
  [Hsm_Parameters_And_Variables_OPENING_PROMPT.md](Hsm_Parameters_And_Variables_OPENING_PROMPT.md).
  What genuinely remains open is *per-slot* granularity: a BTree node has **one** action, an HSM
  state has **four** (Entry/Exit/Activity/Timer) plus a transition's two — so slot identity must
  include which slot, and `ExpressionTargetField` must exist per slot (today `StateNode` has none at
  all; only `TransitionNode`/`GlobalTransitionNode` carry one — this is `DEBT-BF-04`).
  ✅ **Not currently live:** no shipped `.bp.json` declares `HsmAction`/`HsmGuard` hosting, so
  nothing is corrupting an instance today. ⚠ **And the existing test cannot catch it** —
  `BuildHsmAiPrimitive` uses `.WithGraph("Main", g => g.Entry().Return())` with **no parameters**, so
  `Params` is empty and the bad cast is harmless. The first parameterised primitive hosted on an HSM
  is the one that bites.
  Also worth noting (**not** a defect): the generated thunk ignores the `HsmCommandWriter* writer`
  argument — but so does the shipped hand-written `ApcHsmActions`, which writes channel components
  directly through the repo. Writer-less actions are the house pattern.

- [ ] **HSM-014** 🔴 · `RW-M` — **The HSM action/guard picker is circular: it can only offer names
  the asset already uses.** `HsmActionPickerDrawer.GetItems()` walks `_asset.AllTransitions` /
  `AllStates` / `AllGlobalTransitions` and returns the distinct `OnEntry/OnExit/Activity/Timer/
  ActionFunction` strings **already stored in this machine**. It never queries `HsmActionDispatcher`,
  `IActionSchemaExporter`, or `IBehaviorActionCatalog`. ⇒ **on a fresh machine the picker is empty**,
  and no hand-written `[HsmAction]` or blueprint AiPrimitive can ever be selected — the only way to
  populate it is to have already typed the name somewhere else.
  This directly contradicts the design (`HSM_Editor_NodeEditor_Host_Design.md` §10.1): *"a dropdown
  over `HsmActionDispatcher.AllActions`, grouped by declaring type; fuzzy search"*, and §5.1's
  dynamic action/guard catalog entries.
  ⭐ **This is the HSM twin of the BTree `EB-C` gap** (static node catalog / no specific actions in
  the palette) recorded in `BTree_HSM_Editor_State_And_Forward_Plan.md` §2.2 — and
  `BehaviorActionCatalog` **already maps `ActionHosting.Hsm → BehaviorActionHosts.Hsm`** (`:200`),
  so the catalog side is built and just not consumed here. Likely `WIRING`-sized once HSM-013 settles
  what identity the picker should write.

---

## Area E2 🔧 — Kernel features the editor exposes but the runtime does not implement

- [ ] **HSM-012** 🔴📐 · `RW-H` — **The editor authors timers; the kernel never arms one.**
  `StateFacet` exposes a `TimerAction` picker, `HsmEmitCore:656` emits `.TimerAction(...)`,
  `HsmFlattener:175` packs it into `StateDef.TimerActionId`, and `HsmEmitter` writes it into the
  blob. **`HsmKernelCore` never reads `TimerActionId`, and never writes a non-zero
  `TimerDeadlines[i]`.** Every production write is `= 0` — timer *cancellation* on state exit
  (`HsmKernelCore:1126-1138`) and hot-reload reset (`HotReloadManager:139-167`). The only non-zero
  writes in the repo are `Fhsm.Tests` fixtures arming deadlines by hand
  (`TimerCancellationTests:43`). There is no `SetTimer`/`StartTimer`/`Arm` API anywhere in
  `Fhsm.Kernel`. `ProcessTimerPhase` therefore decrements a counter that is always zero, and
  `FireTimerEvent` (which would post `TimerEventId = 0xFFFE`) is unreachable.
  ⇒ **an author can wire a timer action in the editor, save it, build it, and it will silently never
  fire.** Needs a ruling on layering: is arming a *kernel* addition (an `OnEntry`-time arm driven by
  a per-state duration in `StateDef` — which needs a new ROM field and a builder param), or does the
  editor stop offering `TimerAction` until the kernel supports it? Until one or the other, the
  facet field is a trap. ⚠ Related: the design doc's `StateFacet` (§11.1) lists `TimerAction` with
  no duration field at all, so even the *design* has no way to say "how long".

---

---

## Area H ⚙️🔧 — Surfaced by the coordinator branch, HSM half unowned

⭐ **Added 2026-08-20 after merging 428 coordinator commits (Batch 46 → 98).** The blueprint
programme measured these; each has an **HSM half that nobody owns**. Their ledger entry is cited so
the two trackers do not drift.

- [ ] **HSM-017** 🔴 · `RW-M` — **Renaming a bound variable dangles the binding — and on HSM nothing
  catches it at build.** ✅ Verified on the merged tree `2026-08-20`: `HsmAsset.RenameVariable:244`
  renames the entry and fixes up `_aliases`, and **never touches `ExpressionTargetField`** — the
  binding still names the old string. This is the HSM half of the coordinator's `M-15`/`M-16`:
  Blueprint is safe because declarations carry a persisted `Guid Id` and references store
  `VariableId`; **BTree/HSM store the NAME STRING**.
  ⭐⭐ **But HSM is worse than BTree, and that half is not in their ledger.** BTree's dangling
  binding is caught at build — `BTreeJsonGenerator` skips the whole asset with a `BTREE0002`
  **Warning** (*"never a partial/silent emit"*). `HsmJsonGenerator` has **only `HSM0001`**, and it is
  a *parse/emit error* code — there is **no dangling-binding diagnostic on the HSM path at all**
  (verified: the only `ReportDiagnostic` calls are `MakeParseErrorDiagnostic`). ⇒ on HSM a rename
  produces a **silently wrong machine**, where on BTree it produces a build warning.
  📌 Coordinator ledger `M-15` · `M-16` · `R-88`.

- [ ] **HSM-018** 🔴 · `RW-L` — **Editing one blueprint variable reverts a tick of HSM state.**
  The coordinator's `R-52`, recorded verbatim as *"A LIVE DATA-CORRUPTION DEFECT ON NO WORK LIST"*:
  the staged variable write takes a **whole component** and writes it with `SetComponentRaw` (no
  offset), and `Blackboard1024` is **one component shared by BTree, HSM and Blueprint at disjoint
  offsets** (`R-65`) ⇒ writing the whole thing **clobbers the other subsystems' state**. Batches
  79/80 made that editing reachable. Needs `SetComponentFieldRaw`.
  📌 **Recorded here because the HSM state is what gets clobbered**, and the row sits on no work
  list in either programme. ⚠ Fix belongs to whoever owns the variable-edit write path, not to us.
  📌 Coordinator ledger `R-52` · `R-65`.

- [ ] **HSM-019** · `RW-L` — **HSM cannot produce a subtree sync binding, and that is by design —
  but the validator still blames a field that exists.** Coordinator `M-24` measured it: `HsmAsset`
  implements `IEditableAsset, IBlackboardManagedAsset, IStitchableAsset` — **not
  `IBTreeSyncableAsset`** — and the Inspector gate is `is BTreeNodeSelection` while HSM emits
  `HsmStateSelection`, so the path is **doubly closed**. ⇒ the missing `SubtreeSyncBindings` mapper
  field is a **consequence, not an omission**, and `Q45-F` recommends **not** adding it.
  ⚠ **The actionable residue is a rotted comment:** `HsmValidator:395` blames a missing
  `StateNodeDto.SubtreeAssetId` — **false since Batch 75**; the field is at `HsmAssetDto.cs:37`
  (verified on the merged tree) and `DEBT-AIB-028(a)` is resolved. A stale comment that names a
  non-existent gap will send the next reader down a dead end.
  📌 Coordinator ledger `M-24`.

## Area F 📄 — Documentation accuracy

- [ ] **HSM-011** · `RW-L` — **`BTree_HSM_Editor_State_And_Forward_Plan.md` is materially stale and
  will mis-plan the work.** Refreshed 2026-06-12; it states HSM *"cannot author at all"* and that
  four command-sink methods are `TODO` stubs at `HsmCommandSink.cs:139,151`. In this tree
  `HsmCommandSink` is 457 lines with **every** create-op implemented, and EH-01…EH-05 have
  substantially landed (create/delete state, transitions, initial arrows incl. LCA highlight,
  validators passed to the registrar at `EditorSubsystem.cs:2141`, conflict-renderer feed,
  `HsmShowcase.hsm.json`, 510 tests). Its §6 residual verify item *"confirm Events-table/Globals-strip
  are registered into the HSM perspective"* now resolves to **no** (→ HSM-009). Either refresh it or
  mark it superseded — leaving it is the trap, because it reads as authoritative.

---

## Not yet audited

Stated so no one mistakes silence for a clean bill:

- **BTree editor** — untouched this pass; the plan doc's BTree gaps (EB-A…EB-E) are unverified.
- **Phase-2 debug/trace surface** — `HsmDebugSession`, runtime overlay, heatmap, trace lanes,
  step controls. Deferred by design; not checked against §13/§14.
- **JSON round-trip byte-stability** and the migration-equivalence gate (§6.4 of the JSON DD).
- **Anything visual** — renderer geometry, container/divider layout, transition label placement,
  internal-transition dashed loops (§7.4, an open question in the host doc's §19 Q3). These cannot
  be judged headlessly and need a running editor.
- **`DEBT-BF-04`** — BB1 param binding covers HSM transitions/globals but not a state's four action
  slots. Called out in the plan doc as needing an architect call; not re-verified here.

---

## Change log

| Date | Change |
|---|---|
| 2026-08-14 | Created. HSM-001…HSM-011 from the first docs-vs-code audit. Five rows reproduced with throwaway probes; probes deleted, suite left green at 510/510. |
