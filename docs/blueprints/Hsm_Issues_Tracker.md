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
| `WIRING` | 0 | 1 |
| `RW-L` | 1 | 7 |
| `RW-M` | 1 | 8 |
| `RW-H` | 1 | 3 |
| **Total** | **3** | **19** |

> ⚠⚠ **The previous table (`12 open / 7 done`) was ALREADY STALE before the `2026-10-06` batch touched it** —
> `HSM-001/002/003/005/006/010` had been ticked by the `Q84` / canvas work (`CE-1000`…`CE-1004`) without the
> count being recomputed. 📌 Recomputed here with the file's own reconciliation recipe (below), three ways:
> checkbox tally **3 / 19** · column sums `0+1+1+1 = 3` open, `1+7+8+3 = 19` done · 22 rows total.
> ⭐ **Every original row is now closed**; the three open ones were all split out of this batch by measurement.

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
grep '^- \[ \]' Hsm_Issues_Tracker.md | while read -r l; do
  echo "$l" | grep -oP '(?<=`)(WIRING|RW-L|RW-M|RW-H)(?=`)' | head -1; done | sort | uniq -c
# ⚠ (was `grep -oPm1` over the whole pipe — -m1 stops after the FIRST ROW, so it printed one tag)
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

> ### ⭐⭐ RE-EVALUATED `2026-10-02`, after merging `origin/ui` (2988 commits, to 2026-10-01)
>
> ⚠ **The VM had checked out a pre-session commit.** The branch was fast-forwarded from
> `origin/claude/hsm-visual-editing-9ngei4` (`17097985e`) first, then `origin/ui` merged — clean, no
> conflicts. `origin/ui` **already contains** `claude/blueprint-authoring-status-gm0akp` in full (which
> itself stopped at `2026-09-19`) ⇒ ⭐ **`origin/ui` is now the branch to track.**
>
> ⭐ A large HSM programme landed meanwhile: `DESIGN_Hsm_Blueprint_Behaviour_Authoring.md` (1242 lines,
> items `CE-381`…`CE-401`, *"Stage 1 and Stage 2 complete, all nine acceptance rails closed"*), plus
> `DESIGN_Hsm_Storage_Model.md`, `RESUME_Hsm_Blueprint_Behaviour.md`, `RESUME_Hsm_Subtree_Authoring.md`
> and `docs/designs/btree-hsm-unif/DESIGN.md`. **+1333 lines across 20 `Hrot.Hsm.Editor` files.**
> Suite **619/619 green** (was 554).
>
> **Four rows closed, one moved to partial; the six reproduced UX rows reproduce byte-identically.**
>
> | row | verdict `2026-10-02` |
> |---|---|
> | **HSM-013, 014, 015, 019** | ✅ **CLOSED** — see each row's closure note |
> | **HSM-009** | ⚠ **PARTIAL** — the Events table is registered and rename works; **create/delete still absent** |
> | **HSM-001, 002, 004, 005, 006** | ⛔ **reproduce identically** (throwaway probe, deleted after; suite left green) |
> | **HSM-003, 007, 008, 010, 011, 012** | ⛔ still live — re-greped repo-wide |
> | **HSM-017, 018** | ⛔ still live |
>
> ⭐ **`DEBT-BF-04` moved too, partially:** `StateNodeDto` now carries `ExpressionTargetField`,
> `ActivityBlueprintAssetId` and `ActivityBlueprintName`, so a **state** can bind a parameterised
> blueprint. ⚠ **Activity slot only, and one `ExpressionTargetField` per state, not per slot** —
> `OnEntry`/`OnExit`/`Timer` have no blueprint binding ⇒ Q-1 of the opening prompt is **still open,
> narrowed**.
>
> 📌 **Two independent audits agreed three times now** (HSM-016 Batch 59, HSM-015 `CE-297`,
> HSM-019). ⭐ That is evidence the method works — but **every one of them was fixed upstream before we
> looked**, so the real lesson is the same as last time: **re-sync first.**

> ### ⭐⭐ RE-EVALUATED `2026-10-02` (later), on `ui` after fast-forwarding `origin/behaviors` (+44 commits, `CE-417` one action binding · `CE-503` HSM resolver · `CE-504` · `CE-506` global-transition guard/action reach the kernel)
>
> ⚠ **Two rows the earlier passes kept "live" were already fixed upstream — BEFORE this tracker first
> recorded them as reproduced.** Both were closed by reading the current write path, not by re-running
> the old probe — ⛔ the old probe evidently exercised the legacy fallback, not the save path.
>
> | row | verdict |
> |---|---|
> | **HSM-004** | ✅ **CLOSED — `BP-299` (`267897ebb`, 2026-08-17).** `RegionNodeDto.OwnerStableId` is written by `ToDto` and is authoritative on load; `InitialChild.Parent` survives only as the pre-field fallback. Railed by `HsmSubtreeAssetIdPersistenceTests.ARegionWithNoInitialChild_KeepsItsOwner_AndTheRuleFires` |
> | **HSM-018** | ✅ **CLOSED — ruling 14 (`a801dccae`, 2026-08-16).** The live variable write is field-surgical: `BlueprintLiveValueWriter.TryWrite` → `BlueprintDebugSession.TryWriteWorkingStateField` → `DataBreakpointManager.StageFieldMutation` → `SetComponentFieldRaw` at the component-absolute offset. ⚠ **The coordinator ledger's `R-52` (and the comment at `CgfSubsystem.cs:2124`) still describe the defect as live** — a stale STATE claim, reported to the coordinator, not edited here |
> | **HSM-013 residue (`DEBT-BF-04`)** | ⭐ **narrowed again by `CE-417`:** every one of the **eight** sites now carries its own `BehaviorActionBinding` with its own `ExpressionTargetField` ⇒ **per-slot ETF exists.** A blueprint pick is offered on **Activity and transition Guard only**, by design ([`DESIGN_Behavior_Action_Binding.md`](DESIGN_Behavior_Action_Binding.md) §9 ③) |
> | **HSM-014** | ✅ still closed — the two drawers are now one `HsmBindingMethods.For` (`CE-417` 4b), still unioning `IActionSchemaExporter.All` with the asset's own names |
> | **HSM-001, 002, 005, 006, 007, 008, 009 (partial), 010, 011, 012, 017** | ⛔ **still live** — current sites below each row's text; `HsmValidator.CheckInitialChildren` (`:114`), `HsmCommandSink.ApplyRemoveRegion` (`:359`) and `ApplyAddNode` (`:148`) are unchanged by the merge |
>
> 📐 **Method:** grep + `codebase-memory` `search_code` (re-indexed after the merge) for every "no caller / no
> producer" claim (HSM-007, 009). ⚠ `check_index_coverage` is not reachable through the CLI, so those
> negative claims carry grep + graph agreement, not a coverage proof.

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

- [x] **HSM-001** 🔴 · `RW-L` — **Every UML-correct parallel composite is reported as an error.** ✅ **CLOSED `2026-10-06` (CE-1003)** — a parallel state no longer counts the per-child flag; its regions own their start states.
  `HsmValidator.CheckInitialChildren` counts `IsInitial` across *all* children of a state, with no
  awareness of regions. A parallel state with 2 regions, each with its own initial child — correct
  by UML and by the kernel — yields `initialCount == 2`. ✅ **reproduced:**
  `Error MultipleInitialChildrenInSameParent: Composite state 'ParallelWork' has 2 children marked as initial; only one is allowed.`
  ⚠ **`HsmShowcase.hsm.json` was authored around this defect:** `RegionB.InitialChild = WorkB` but
  `WorkB.IsInitial = false`, and likewise for `WorkC`. The showcase passes validation by being
  semantically under-specified, which is why 510 green tests never caught it. Blocked on the
  Area-A ruling (HSM-003).

- [x] **HSM-002** 🔴 · `RW-L` — **A parallel region with no initial child at all passes clean.** The ✅ **CLOSED `2026-10-06` (CE-1003)** — `RegionWithoutInitialState` (Error).
  mirror of HSM-001. Region 0's initial child satisfies the whole-state count, so region 1 having
  `InitialChildStableId: null` produces **zero diagnostics** — the check that matters most for
  parallel states does not exist in any form. ✅ **reproduced:** validator returned an empty
  collection for a 2-region parallel state whose region 1 had no initial child. Blocked on HSM-003.

- [x] **HSM-003** 📐 · `RW-H` — **Decide the initial-state model.** Two candidate shapes: ✅ **CLOSED `2026-10-06` (CE-1003, Q84 B)** — one writer, `HsmAsset.SetStartState`: a parallel state's REGION owns it, other containers' children carry `IsInitial`; the emitter follows the same rule.
  **(A)** `RegionNode.InitialChild` becomes the single source of truth for *both* container kinds
  (a normal composite is modelled as an implicit single region); `IsInitial` becomes derived/display
  only. **(B)** `IsInitial` stays authoritative and the validator, renderer and persistence all
  become region-scoped. (A) unifies the two paths and matches how the kernel stores it
  (`StateDef.InitialChildIndex` / `RegionDef.InitialStateIndex`); (B) is a smaller diff but keeps
  two writers on one fact. Touches model, persistence, validator, facets and renderer — **this is
  the ruling that unblocks HSM-001, HSM-002 and shapes HSM-004.**

---

## Area B 🎨 — Region persistence and editing

- [x] **HSM-004** 🔴 · `RW-M` — **A region with no initial child is silently destroyed on reload.**
  ✅ **CLOSED `2026-10-02` — already fixed by `BP-299` on 2026-08-17** (before this row was first
  re-verified). The "fix direction" below is exactly what shipped: `RegionNodeDto.OwnerStableId`,
  written by `HsmAssetMapper.ToDto` (`:107`), authoritative on load (`:324`); the
  `InitialChild?.Parent` derivation is kept only as the fallback for files saved before the field
  (`:326`). Railed: `ARegionWithNoInitialChild_KeepsItsOwner_AndTheRuleFires` +
  `APreFieldAsset_StillLoads_ViaTheInitialChildFallback`. ⚠ The original text below is history.
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

- [x] **HSM-005** 🔴 · `RW-L` — **Removing a region corrupts the surviving children's region ✅ **CLOSED `2026-10-06` (CE-1003)** — later regions' children shift on remove AND insert.
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

- [x] **HSM-006** 🔴 · `RW-M` — **Palette-created states all get the same name, and names are load ✅ **CLOSED `2026-10-06` (CE-1001)** — unique names on create + `DuplicateStateName`.
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

- [x] **HSM-007** · `WIRING` — ✅ **THE EDITOR HALF IS BUILT `2026-10-06`; the RUNTIME half is split out as
  `HSM-020`.** `HsmDocumentFactory.Build` step 0b now calls `HsmOutputLaneMaskInferrer.ApplyToAsset` with a
  dictionary built from the loaded assemblies — §10.3 step 2's *"at asset open, the editor reflects each
  `[HsmAction]`'s `Lane`"* — so rule 7, the `hsm.region_conflicts` renderer and the inspector's
  "Output lanes (inferred)" summary are reachable for the first time. ⭐ Chosen there because it is the one place
  with the asset in hand and it re-runs after a hot reload, which the derived mask needs. ⚠ The mask is derived
  and does NOT `MarkDirty`. ⭐ **And step 4's summary had to be built too** — `HsmFacetMapper` hard-coded
  `OutputLanesSummary = ""`, harmless only while the mask was always 0, and the one consumer that would still have
  lied once it was not; it now names the lanes in the mask *(names, not §10.3 step 4's per-action attribution — a
  bitfield does not remember which action set which bit)*.
  ⚠ **Reflection is guarded at three levels** (assembly / type / per-method attribute
  read) mirroring `ActionSchemaExporter.ScanAssembly` — 📌 without the per-method one, 15 rails went red with a
  `TypeLoadException` from `GetCustomAttribute` on an unrelated loaded host assembly.
  🔴🔴 **AND THE ROW'S OWN FRAMING WAS TOO NARROW:** §10.3 step 5 says the editor need not emit the mask because
  *"the kernel computes it at compile time"* — 📐 **measured FALSE.** Nothing in `Fhsm.Compiler` ever sets
  `StateNode.OutputLaneMask` but `Fhsm.Tests`; `HsmBuilder`/`StateBuilder` expose no lane API; the Hrot emit chain
  mentions lanes zero times. ⇒ `HsmKernelCore.ArbitrateOutputLanes` arbitrates on **zeros** for every
  editor-authored machine, so runtime lane arbitration was inert too. ⭐ Folded into
  `HSM_Editor_NodeEditor_Host_Design.md` §10.3a and §19 Q2; the remaining work is **`HSM-020`**.
  ⛔ Original text: **`OutputLaneMask` is never computed on the JSON path, so the whole
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

- [x] **HSM-008** · `RW-L` — ✅ **BUILT `2026-10-06`.** `CheckOutputLaneConflicts` now unions `OutputLaneMask`
  over **every leaf** at or under each direct child (`UnionOfLeafLaneMasks`), which is design §12.2's own wording
  and is what the kernel arbitrates — `ArbitrateOutputLanes` reads the ACTIVE LEAF (`HsmKernelCore.cs:971`).
  ⭐ The region index still comes from the top-level child and is carried down, because a nested parallel
  composite's `RegionIndex` means the INNER composite's region *(the same reasoning as rule 8's
  `SubtreeHostsUnder`)*. 3 new rails incl. a composite whose own stale mask must NOT create a conflict its leaves
  do not have. ⚠⚠ **The row's claim that rules 8/8b share the restriction was STALE** — `DEBT-AIB-029` (Batch 76)
  already gave both the full-subtree walk; only the lane rule was shallow. ⚠ And this rule could not fire at all
  until `HSM-007` populated the masks — **a green validator was not evidence**. Design: §12.2a.
  ⛔ Original text: **Lane-conflict detection only looks at direct children.**
  `CheckOutputLaneConflicts` ORs masks from `s.Children` only; design §12.2 specifies *"the union of
  `OutputLaneMask` across all **leaf states** in R1"*. Any conflict one level down is invisible.
  Rules 8/8b (`ConcurrentStatefulSubtree`, `ConcurrentSharedScopeKey`) share the same direct-children
  restriction and say so in their comments. Gated behind HSM-007 — until masks are populated this
  rule cannot fire at all, so fix them together.

- [x] **HSM-009** · `RW-M` — ✅ **BUILT `2026-10-06` — AN EVENT CAN BE AUTHORED.** `HsmAsset.CreateEvent` /
  `RemoveEvent` / `RenameEvent` + the window's "+ Add Event" modal, a Delete item that states how many transitions
  will dangle, and a Rename that **applies** *(it previously only PREVIEWED — the modal had no Apply at all)*.
  ⭐ "+ Add Event" is drawn before the empty-state early-return, because an empty machine is exactly where it is
  needed. ⭐ Two id rules, both measured: an engine-raised name takes its **reserved** id (`HsmEventIds` would
  rebind it at emit anyway, so a sequential id would make editor and emitter disagree), every other event takes the
  lowest free id **from 1** (`0` means "no event"; the high band is reserved per `R-45`); renaming TO a built-in
  name is refused. ⭐ A delete leaves referencing transitions ALONE — rewriting `EventId` to 0 would silently turn a
  triggered transition into a completion transition; the existing `EventReferenceDangling` rule reports it, which
  is §9.1's "flags warning". ⭐ **Find References was also always empty** and is fixed: it passed the bare name
  where the contributor publishes `{AssetId:D}::{EventName}`. 13 new rails. Design: §9.1a.
  ⚠ **Still open, deliberately:** `HsmGlobalsStrip` has zero production callers; and these operations are not on
  the undo stack — the same as the variables surface they follow, and one decision for both *(see §9.1a)*.
  ⛔ Original text: **The Events table and Globals strip are built but never registered,
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
  ⚠ **PARTIAL `2026-10-02` — the table arrived, creation did not.**
  ✅ `HsmEventsDetailsView` is a **new** 131-line view and **is registered in production**
  (`EditorSubsystem:3366`); `HsmEventsWindow` gained a **rename** modal (`:141-177`) routed through the
  refactor service. Events are now visible and renameable.
  ⛔ **Still missing, and it is the blocking half: no create, no delete.** Verified repo-wide —
  `EventDefinition` is constructed in exactly **three** places, all loaders: `HsmAssetProjector:242`
  (compiled blob) and `HsmAssetMapper:165,401` (JSON). A machine with no events can never acquire one,
  so `[HsmEventPicker]` on a transition still has nothing to offer.
  ⭐ **The remaining work is smaller and better shaped** — table, row model, modal pattern and refactor
  hook all exist; what is left is an *add/delete command*, not a window.
  ⚠ `HsmGlobalsStrip` is **still** unregistered (no production caller).
  📐 **Re-checked after `origin/behaviors` (`2026-10-02`, later):** unchanged. `EventDefinition` is now
  constructed in **two** Hrot places, both loaders — `HsmAssetProjector:243`, `HsmAssetMapper:393`
  (plus FastHSM's own `HsmBuilder:60`); `HsmAsset.AllEvents` is read-only and no
  `Add/Remove/Delete/CreateEvent` exists anywhere in the editor or CGF (grep + graph `search_code`).
  `HsmGlobalsStrip` is still referenced only by its own file and a doc-comment.
---

## Area E 🎨🔧 — Design contradictions

- [x] **HSM-010** 🔴📐 · `RW-M` — **History is modelled as the wrong kind of thing; the palette ✅ **CLOSED `2026-10-06` (Q84 C1/D1)** — the palette entries are gone; history is the composite's "On re-entry" setting (start at initial / resume last child / resume last leaf) = its `IsHistory`/`IsDeepHistory` flags; `HsmHistoryMigration` folds an old pseudo-node into its parent on load and before emit; `HsmShowcase` migrated.
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

- [x] **HSM-013** 🔴 · `RW-M` — **The id an AiPrimitive registers under can never equal the id the
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

  ---
  ✅ **CLOSED `2026-10-02`.** `HsmEmitCore` gained a **`blueprintIdResolver`** delegate (plus a
  `blueprintClassNameResolver` overload — `CE-388` / `Q74 D-B1`) so a state whose activity is a
  blueprint is emitted with **the identity the blueprint emitter registers**; the comment notes it had
  to be a delegate because the `BlueprintId` lives in the other compiler. ⭐ And
  `FDP/Toolkits/Fdp.Toolkits.Analyzers/Shared/HsmActionKey.cs` now exists with `ForActionName(fqn)`,
  `ForCompoundKey(key)` and **`CompoundKeyName(fqn, byteOffset)`** ⇒ the `MethodFqn@offset` form
  `R-88` ruled is **implemented on the HSM side** — which is exactly what this row and the opening
  prompt's Q-A asked for.
  ⚠ **Residual, deliberately not re-opened as a new row:** `StateNodeDto` carries
  `ActivityBlueprintAssetId` / `ActivityBlueprintName` for the **Activity slot only**, and one
  `ExpressionTargetField` per *state* rather than per slot. That is the **narrowed `DEBT-BF-04`**,
  tracked as Q-1 of the opening prompt.
  ⭐ **SUPERSEDED in part by `CE-417` (2026-10-01):** the flat fields are gone; each of the **eight**
  sites (state OnEntry/OnExit/Activity/Timer, transition Guard/Action, global Guard/Action) holds its
  own `BehaviorActionBinding` (`Hrot.Editor.AiShared`) with its **own** `ExpressionTargetField` ⇒
  per-slot ETF now exists. A blueprint is pickable on **Activity and transition Guard only** — a
  design decision, not a gap ([`DESIGN_Behavior_Action_Binding.md`](DESIGN_Behavior_Action_Binding.md)
  §9 ③). `StateNode.StateWideTargetField` is derived (Activity, else OnEntry, OnExit, Timer).
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

- [x] **HSM-015** 🔴📐 · `RW-H` — **The generated HSM thunk reads its parameters out of the live HSM
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

  ---
  ✅ **CLOSED `2026-10-02` — `CE-297`, which names this exact defect.**
  `AiPrimitiveEmitter.EmitHsmOccurrenceBody` replaced the bad cast, and its own comment states the
  diagnosis independently: *"the params were read as `*(Params*)instance`, but the kernel passes the
  **HSM INSTANCE** there — the sibling generator ignores that pointer and projects from
  `BrainBlackboard`, which is what this now does, **matching the BTree path**."*
  ⭐⭐ **It also fixed a defect this session missed** — `BP-297`/`E3`: working state was
  `GetComponentRW<Blackboard1024>(self)` at a hard-coded `memory + 8`, **one per entity**, so two
  concurrently-active parallel regions aliased it. Now occurrence-keyed through
  `HsmOccurrence.ResolveOrAttach`.
  ⇒ ⭐ **The "no param slot without a ROM change" worry is settled — no ROM change was needed.**
  Params live in `BrainBlackboard` at a per-site offset; the occurrence holds only working state and
  the host offset.
- [x] **HSM-014** 🔴 · `RW-M` — **The HSM action/guard picker is circular: it can only offer names
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
  ✅ **CLOSED `2026-10-02` — `CE-386`.** `HsmActionPickerDrawer` now takes an `IActionSchemaExporter`
  and iterates `_schema.All` (*"the catalog half"*). `HsmPickerDrawerFactory.BuildDrawers` is reached
  by the new `AiFacetPickerBinder.Rebuild`, which **both** production hosts call with a real exporter
  **and** catalogue — `EditorSubsystem:3615` and `CgfSubsystem:2381`. ⭐ The binder exists because the
  user asked *"why hosts differ in … picker drawer"*: CGF had made **0** such calls against the
  editor's 13. ⚠ **Checked as flow, not wiring** — the exporter is *passed* at both sites.
---

## Area E2 🔧 — Kernel features the editor exposes but the runtime does not implement

- [x] **HSM-012** 🔴📐 · `RW-H` — ✅ **THE TRAP IS CLOSED `2026-10-06`; the ARMING is split out as `HSM-021`.**
  The `Timer` binding facet is removed from `StateFacet` (and from the mapper/dispatcher), so the editor no longer
  offers a binding that is emitted and can never fire. ⭐ **What is kept:** `StateNode.Timer`, the mapper
  round-trip, the emit path and the blackboard aggregator's requirement — a hand-authored asset still round-trips
  untouched *(no rush removals)*. ⭐ **And the withdrawal does not create a NEW silence:** a new validator rule
  `TimerActionNotImplemented` (Warning) reports any state that still carries one. ⚠ Measured: **zero** authored
  `.hsm.json` carries a Timer binding, so the withdrawal costs nothing today. 2 new rails, and ONE EXISTING RAIL MOVED WITH IT — `Hrot.Editor.AiShared.Tests` `SE1_StructEditFacetRenderTests.EditService_HsmFacets_MarkTheBlueprintCapableSlots` asserts the HSM facet's binding slots by name, so it now expects three instead of four *(it caught the removal, which is the rail working)*. Design: §11.1b.
  📐 **The decisive measurement for the remaining half:** every production write of `TimerDeadlines[i]` is `= 0`
  (cancel-on-exit `HsmKernelCore.cs:1319/1325/1331`, hot-reload reset `HotReloadManager.cs:139-167`), and
  **no duration field exists anywhere** — not in `StateDef`, not in the facet, not in the design. ⇒ arming is a new
  ROM field + builder param + kernel phase, not a wiring gap: **`HSM-021`**.
  ⛔ Original text: **The editor authors timers; the kernel never arms one.**
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

- [x] **HSM-017** 🔴 · `RW-M` — ✅ **BUILT `2026-10-06`, and the root cause was NOT the one this row names.**
  📐 The re-verification's reframe (*"HSM contributes no variable references"*) is true and was only HALF the
  story: the rename ROUTE cannot move an HSM binding either. `BTreeHsmSchemaSource.GetRefactorKey` returns `null`,
  so `VariableRenameCommit` falls back to the composite key `{assetId:D}::{name}`, and
  `RefactorService.PreviewRename` then looks for that literal string **in the asset's source file**
  (`line.Contains(fromKey)`) — while a `.hsm.json` stores the bare name (`"ExpressionTargetField": "EngageTarget"`).
  ⇒ **zero line edits, nothing written, declaration renamed alone.** ⛔ A contributor alone would have left the
  rename just as broken, with a green rail over it.
  ✅ **Both halves built:** ① `HsmReferenceContributor` now enumerates blackboard variables and every
  `ExpressionTargetField` reference (states ×4 slots, transitions, globals) — **extended rather than mirrored**,
  because the site walk and BOTH composition-root registrations already existed there; ② `HsmAsset.RenameVariable`
  retargets every binding that names the variable, walking the same sites `CountNodesReferencingVariable` counts.
  ⭐ `BlackboardVariableSubElement` moved to `Hrot.Editor.AiShared.References` (cross-lane: one line in
  `Hrot.BTree.Editor`) so both hosts spell the key identically — two spellings is how a rename reports success and
  dangles the other host. 9 new rails incl. the `speed`/`speedLimit` substring trap a text rewrite would fall into.
  ⚠ **Scope (`R-88`):** this repairs the EDITOR's bindings; a name is separately load-bearing at runtime for
  `Scope=Behavior`/`Scope=Entity` slot keys and scenario overrides, unchanged here.
  ⚠ **The row's second half needs a wording fix, not a build:** it says HSM has "only `HSM0001`" — `HsmJsonGenerator`
  now has `HSM0001`–`HSM0004`, but **none is a dangling-binding check**, so the asymmetry with BTree's
  `BTREE0002` whole-asset skip stands. ⭐ The BTree twin of the model defect is filed as **`HSM-022`**.
  ⛔ Original text: **Renaming a bound variable dangles the binding — and on HSM nothing
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

- [x] **HSM-018** 🔴 · `RW-L` — **Editing one blueprint variable reverts a tick of HSM state.**
  ✅ **CLOSED `2026-10-02` — already fixed by ruling 14 (`a801dccae`, 2026-08-16)**, four days
  before this row copied `R-52` from the ledger. The path today: `BlueprintLiveValueWriter.TryWrite`
  (`:196`) → `BlueprintDebugSession.TryWriteWorkingStateField` (`:1009`, offset arrives fully
  resolved) → `DataBreakpointManager.StageFieldMutation` → drain calls **`SetComponentFieldRaw`** with
  that offset (`DataBreakpointManager.cs:920`). Only the changed bytes are written; the rest of
  `Blackboard1024` survives. ⚠ **`R-52` in `RULINGS.md` is the stale half** — it is a state claim the
  code has overtaken (and `CgfSubsystem.cs:2124` repeats it as a reason). Reported, not edited here.
  ⚠ The original text below is history.
  The coordinator's `R-52`, recorded verbatim as *"A LIVE DATA-CORRUPTION DEFECT ON NO WORK LIST"*:
  the staged variable write takes a **whole component** and writes it with `SetComponentRaw` (no
  offset), and `Blackboard1024` is **one component shared by BTree, HSM and Blueprint at disjoint
  offsets** (`R-65`) ⇒ writing the whole thing **clobbers the other subsystems' state**. Batches
  79/80 made that editing reachable. Needs `SetComponentFieldRaw`.
  📌 **Recorded here because the HSM state is what gets clobbered**, and the row sits on no work
  list in either programme. ⚠ Fix belongs to whoever owns the variable-edit write path, not to us.
  📌 Coordinator ledger `R-52` · `R-65`.

- [x] **HSM-019** · `RW-L` — **HSM cannot produce a subtree sync binding, and that is by design —
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

  ---
  ✅ **CLOSED `2026-10-02`.** The comment is corrected in place — `HsmValidator:596` now reads
  *"…has no counterpart on `StateNodeDto`". ⛔ That is false. 📐 The field exists at …"*. And
  `SubtreeAssetId` is now actively **used** (`:264-277`) by the new `SubtreeAssetCycle` /
  `SubtreeReferenceDangling` rules.
## Area F 📄 — Documentation accuracy

- [x] **HSM-011** · `RW-L` — ✅ **DONE `2026-10-06` — marked, not rewritten.** The forward-plan doc now carries a
  STATUS block (`state: HISTORICAL`) and a banner at the top. ⭐ **Scoped deliberately:** its §1 substrate
  reconciliation (JSON, not C#, is the source of truth) is still LIVE and is cited by this very host design's own
  STATUS, so the file is NOT superseded wholesale — `current-answer` names §1 as the only quotable part and
  `stale-below` condemns every status/progress claim under it. Each rotted claim is named with its measurement:
  `HsmCommandSink` is 533 lines with `ApplyAddNode:160`/`ApplyAddLink:300` implemented and no TODO (it claimed
  four TODO stubs); `HsmInitialArrowRenderer` is 212 lines with no TODO; and its §6 open question *"are the Events
  table / Globals strip registered?"* is now answered as a SPLIT — the Events table is (as a details view), the
  Globals strip is not. ⭐ Reciprocal `related-designs` links added in both directions.
  ⛔ Original text: **`BTree_HSM_Editor_State_And_Forward_Plan.md` is materially stale and
  will mis-plan the work.** Refreshed 2026-06-12; it states HSM *"cannot author at all"* and that
  four command-sink methods are `TODO` stubs at `HsmCommandSink.cs:139,151`. In this tree
  `HsmCommandSink` is 457 lines with **every** create-op implemented, and EH-01…EH-05 have
  substantially landed (create/delete state, transitions, initial arrows incl. LCA highlight,
  validators passed to the registrar at `EditorSubsystem.cs:2141`, conflict-renderer feed,
  `HsmShowcase.hsm.json`, 510 tests). Its §6 residual verify item *"confirm Events-table/Globals-strip
  are registered into the HSM perspective"* now resolves to **no** (→ HSM-009). Either refresh it or
  mark it superseded — leaving it is the trap, because it reads as authoritative.

---

## Area I ⚙️🔧 — Split out of HSM-007/012/017 by measurement *(new `2026-10-06`, ui lane)*

- [ ] **HSM-020** 🔴📐 · `RW-M` — **The output-lane mask never reaches the blob, so the KERNEL's lane arbitration
  is inert for every editor-authored machine.** 📐 Measured `2026-10-06` while building `HSM-007`:
  `HsmKernelCore.ArbitrateOutputLanes` (`:955-1000`) reads `StateDef.OutputLaneMask` of each region's active leaf
  and suppresses the second writer — ⛔ but **nothing ever fills that field.** `HsmFlattener.cs:165` copies
  `node.OutputLaneMask` from the compiler's `StateNode`, and the only writers of that field in the repo are
  `Fhsm.Tests` fixtures; `HsmBuilder`/`StateBuilder` expose **no lane API**; `Hrot.AiEditor.Generators` mentions
  lanes **zero times**. ⇒ a parallel machine authored in the editor runs with both regions writing the same lane and
  the kernel silent about it. ⚠⚠ **The design says this is fine and the design is wrong**:
  `HSM_Editor_NodeEditor_Host_Design.md` §10.3 step 5 justifies not emitting the mask because *"the kernel computes
  it at compile time"* — refuted above; §19 Q2's *"lean toward keep it inferred"* rests on the same premise, and both
  are marked in §10.3a / §19. **Work:** carry the inferred mask into the emitted machine — a `StateBuilder` lane
  parameter, `HsmEmitCore` emitting it, `HsmFlattener` keeping the copy it already does. ⚠ It touches **FastHSM**
  (`FDP/ExtDeps`, vendored-as-source, `R-48` no stable ABI) ⇒ 🔒 **needs a user decision, with the alternative being
  to accept that lane arbitration only works for hand-written machines and to say so in the editor.**
  *— split out of `HSM-007`, which built only the editor half*

- [ ] **HSM-021** 🔴📐 · `RW-H` — **An HSM timer cannot be armed, and there is nowhere to say "how long".**
  📐 Measured `2026-10-06`: every PRODUCTION write of `HsmInstance*.TimerDeadlines[i]` is `= 0` — cancel-on-exit
  (`HsmKernelCore.cs:1319/1325/1331`) and hot-reload reset (`HotReloadManager.cs:139-167`); the only non-zero writes
  in the tree are `Fhsm.Tests` fixtures. There is no `SetTimer`/`StartTimer`/`Arm` in `Fhsm.Kernel`, so
  `ProcessTimerPhase` decrements a counter that is always zero and `FireTimerEvent` (`TimerEventId = 0xFFFE`) is
  unreachable. ⛔⛔ **And no duration field exists anywhere** — not in `StateDef`, not in the facet, not in
  §11.1 — so even an arming kernel would have nothing to arm WITH. **Work:** a per-state duration in `StateDef`
  (new ROM field) + a builder parameter + an `OnEntry`-time arm in the kernel; then restore the facet slot
  (`HsmFacets.StateFacet`, removed by `HSM-012`) and retire `TimerActionNotImplemented`. ⚠ FastHSM change, same
  `R-48` caution as `HSM-020`; 🔒 architect/user call on layering. ⭐ Until then the editor correctly offers nothing.
  *— split out of `HSM-012`, which closed the authoring trap*

- [ ] **HSM-022** 🔴 · `RW-L` — **The BTree twin of `HSM-017`: `BehaviorTreeAsset.RenameVariable` almost certainly
  leaves `ExpressionTargetField` dangling too, and the shared rename ROUTE cannot save it.** 📐 Measured on the HSM
  side and the mechanism is shared: `BTreeHsmSchemaSource.GetRefactorKey` returns `null` for **both** hosts, so
  `VariableRenameCommit` falls back to `{assetId:D}::{name}` and `RefactorService.PreviewRename` searches the asset
  FILE for that literal — which a `.btree.json` does not contain (it stores the bare name, e.g.
  `"ExpressionTargetField": "hitSense"`). ⇒ the refactor half is inert on BTree as well, and
  `BTreeBlackboardVariableContributor`'s rails only assert the contributor's OUTPUT — **no rail drives a rename
  end to end.** ⚠ **Less severe than the HSM case and that is why it is a row, not a fix here:** BTree catches the
  dangle at build (`BTreeJsonGenerator` skips the whole asset with a `BTREE0002` warning) where HSM had nothing.
  **Work:** mirror HSM's model-level fix-up in `BehaviorTreeAsset.RenameVariable`, plus one end-to-end rail.
  ⛔ **Not done here on purpose: `Hrot.BTree.Editor` is the behaviors lane's model.** *— found while building `HSM-017`*

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
| 2026-10-02 | Re-evaluated after `origin/behaviors` (+44). **HSM-004 and HSM-018 closed** — both were already fixed upstream (`BP-299` 2026-08-17, ruling 14 2026-08-16) before earlier passes called them live. HSM-013's `DEBT-BF-04` residue narrowed by `CE-417` (per-slot ETF). Reconciliation recipe fixed. `Hrot.Hsm.Editor.Tests` 622/622. |
| 2026-10-06 | **Re-verified on `ui` (graph + grep, no code changed) — see the section below.** Still open: HSM-001/002/003/005/006/007/008/010/011/012/017. HSM-009 partial (`CE-2088`). Q84 opened for the region / initial / history model; canvas design `DESIGN_Hsm_Canvas_Authoring.md` (CE-1000..1002). |
| 2026-10-06 (later) | **HSM-007 / 008 / 009 / 011 / 012 / 017 BUILT** on `ui` — the editor half of the lane masks, the leaf-state lane union, event create/delete/rename, the forward-plan doc marked HISTORICAL, the Timer facet withdrawn, and the variable-rename fix-up + HSM variable references. 32 new test cases (28 methods); `Hrot.Hsm.Editor.Tests` 634 → **666/666**, `Hrot.BTree.Editor.Tests` **646/646**, `Hrot.Editor.AiShared.Tests` **2126/2127** (1 pre-existing skip), `Hrot.Blueprints.Tests` **4206/4223** (17 pre-existing skips). ⚠ One EXISTING rail reddened and was right to — `SE1_StructEditFacetRenderTests` asserts the HSM facet's binding slots by name and caught the Timer removal; updated with the reason, not quietly. ⭐ **Three rows split out by measurement, not deferred by preference: `HSM-020`** (the mask never reaches the blob ⇒ the KERNEL's arbitration is inert too, which refutes design §10.3 step 5), **`HSM-021`** (no timer arming and no duration field anywhere), **`HSM-022`** (the BTree twin of the rename defect). Counts recomputed — the table had been stale since the `Q84` work. As-built folded into `HSM_Editor_NodeEditor_Host_Design.md` §9.1a / §10.3a / §11.1b / §12.2a. |

## Re-verification `2026-10-06` (ui lane)

| row | state on `ui` at `c96aebc9` | evidence | where it goes |
|---|---|---|---|
| HSM-001 / 002 | open | `HsmValidator.CheckInitialChildren:115` still counts `IsInitial` over all children, parallel or not | [`Q84`](Architect_Question_84_Hsm_Region_Initial_History_Model.md) A/B |
| HSM-003 | open — ⚠ **reframed** | the COMPILER ignores declared regions — every child of a parallel state becomes a region (`HsmFlattener.cs:381-394`), contrary to the FastHSM design (§2.2–§2.4: named regions, each with its own initial state); "initial" reaches it only as the child's `.Initial()` (`HsmEmitCore.cs:780`). Region's `InitialChild` is the right owner once the compiler is fixed | [`Q84`](Architect_Question_84_Hsm_Region_Initial_History_Model.md) B |
| HSM-005 | open | `HsmCommandSink.ApplyRemoveRegion:359` unchanged | [`Q84`](Architect_Question_84_Hsm_Region_Initial_History_Model.md) A |
| HSM-006 | open | `HsmCommandSink.cs:178` still names every state `"State"`; no duplicate-name rule in `HsmDiagnosticCode` | `CE-1001` |
| HSM-007 / 008 | ✅ **built later the same day** | `HsmOutputLaneMaskInferrer.ApplyToAsset` has only test callers | ⇒ now called from `HsmDocumentFactory.Build`; the lane rule unions leaves. ⛔ The RUNTIME half is `HSM-020` |
| HSM-009 | ✅ **built later the same day** | `CE-2088`: `EnsureEvent` only; author-defined events could not be created or deleted | ⇒ `CreateEvent`/`RemoveEvent`/`RenameEvent` + the window's modals. ⚠ `HsmGlobalsStrip` is STILL unregistered |
| HSM-010 | open | `HsmNodeCatalog` still offers both history entries; kernel history is a flag on the re-entered composite (`HsmKernelCore.cs:846,877`) | [`Q84`](Architect_Question_84_Hsm_Region_Initial_History_Model.md) C |
| HSM-011 | ✅ **done later the same day** | the forward-plan doc is still stale | ⇒ STATUS block + banner; §1 kept LIVE, every status claim marked with its measurement |
| HSM-012 | ✅ **built later the same day** | `StateDef.TimerActionId` is written (`HsmFlattener.cs:182`), never read by the kernel | ⇒ facet withdrawn + a warning rule for assets that still carry one. ⛔ The ARMING is `HSM-021` — and there is no duration field anywhere |
| HSM-017 | ✅ **built later the same day** — ⚠ and the reframe was only HALF right | rename runs through `VariableRenameCommit` → refactor service, but HSM contributes no variable references | ⇒ contributor added **and** `HsmAsset.RenameVariable` retargets the bindings. 📐 The route could not have worked anyway: the refactor service text-matches `{assetId:D}::{name}` against the asset FILE, which stores the bare name ⇒ zero edits. The BTree twin is `HSM-022` |
| *new* | — | `CE-1003`: `HsmCuratedBindingDemo` draws 2 regions, the kernel runs 3 | [`Q84`](Architect_Question_84_Hsm_Region_Initial_History_Model.md) |
