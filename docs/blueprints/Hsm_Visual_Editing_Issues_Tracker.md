# HSM Visual Editing — Issue Tracker

> **Scope:** the HSM visual editor only (`Hrot.Hsm.Editor` + its wiring in `EditorSubsystem`).
> BTree has its own gaps; they are not tracked here.
> **Status:** open design session. **No code is being written against these rows yet** — the
> tracker exists so findings accumulate while the design is settled.
> **Branch:** `claude/hsm-visual-editing-9ngei4` (based on `claude/blueprint-authoring-status-gm0akp`).

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
| `RW-L` | 5 | 0 |
| `RW-M` | 3 | 0 |
| `RW-H` | 2 | 0 |
| **Total** | **11** | **0** |

Reconciliation — all three must agree (checkbox tally, column sum, per-tag counts):
```bash
grep -c '^- \[ \]' Hsm_Visual_Editing_Issues_Tracker.md   # open -> Total row
grep -c '^- \[x\]' Hsm_Visual_Editing_Issues_Tracker.md   # done -> Total row
# per-complexity: take the FIRST tag on each row — rows discuss other classes in their prose
grep '^- \[ \]' Hsm_Visual_Editing_Issues_Tracker.md \
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
assemblies, quoted inline, then deleted. Baseline at time of audit:
`Hrot.Hsm.Editor.Tests` **510/510 green**, solution build **0 errors**.

---

## Area A — The initial-state model

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

## Area B — Region persistence and editing

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

## Area C — Identity and emit

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

## Area D — Built but never fed

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

## Area E — Design contradictions

- [ ] **HSM-010** 📐 · `RW-H` — **History pseudo-states can be placed but never wired.**
  `HsmLinkValidator` blanket-rejects any transition whose target `IsHistory || IsDeepHistory`
  (*"Transitions into a History pseudo-state are not allowed."*). The host design's own §5.3 sketch
  carries an `IsExplicitHistoryEntry(sourceState, targetState)` escape hatch that was not
  implemented. **In UML a transition into a history pseudo-state is precisely how "resume where we
  left off" is expressed** — so as written, the History / DeepHistory palette entries (HSM-`hsm.state.history`,
  `…deepHistory`) are placeable, render their `H` / `H*` glyphs, and are unreachable. Needs a
  ruling: does this editor model history as an *entry target* (UML) or purely as a *flag consulted
  on parent re-entry* (which is how `Fhsm.Kernel` stores it)? The answer decides whether the
  validator gains an exception or the palette entries are withdrawn.

---

## Area F — Documentation accuracy

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
