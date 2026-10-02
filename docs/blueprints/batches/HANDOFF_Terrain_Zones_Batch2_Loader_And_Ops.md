<!--STATUS
state: LIVE
build-state: DISPATCH — a pointer, not a design. ⛔ Carries NO UML and NO design content by rule
  ("THE DIAGRAMS LIVE IN THE DESIGN, NEVER IN THE BATCH"). Every item names its owning chapter.
updated: 2026-09-17
current-answer: §2 is the item list. §1 and §3 are the two things that make this batch different from ①.
stale-below: nothing — new document.
known-conflict: F touches the same scenario/merge surface B5 touched in batch ①; that work is MERGED,
  not in flight, so this is sequencing, not a conflict.
related-designs:
  - ../PLAN_Terrain_Zones_Build.md — the plan this dispatches (stages C, D, F; grouping in §5).
  - ../../DESIGN_Terrain_Zones_And_Assets.md — THE owning design. §5.4 is AS-BUILT after batch ①.
  - ../../designs/mgmt-1/DESIGN.md — §11 owns the PrepareZone/CommitZone 2PC protocol.
  - ../../DESIGN_Distributed_Scenario_Persistence.md — §6a owns what Stage F retires.
  - REPORT_Terrain_Zones_Batch1.md — what batch ① measured, and the two hazards it handed you.
-->

# HANDOFF — **Terrain & zones, Batch ② THE LOADER AND THE OPS** *(stages C + D + F)*

**Dispatched at `af6b1a071`.**
⛔⛔ **Your scope is FROZEN at that sha.** Documents that change after it are **FYI ONLY**. If a later
document **invalidates** an item: **STOP that item and REPORT it** — do not adapt, do not revert, and
⛔ **do not stop the batch** (`R-106`).

## 1. ⭐⭐⭐ WHY C, D AND F ARE ONE BATCH — read this before proposing to split

**The terrain loader (`C5`) is what makes retiring the zone bundle (`F`) safe.** Land them apart and the
tree is left in one of the two states the design exists to prevent:

| split | what the tree looks like |
|---|---|
| C without F | ⛔ **two producers for the road network** (`R-132`) — the retiring `LoadZones` and the new loader, both live |
| F without C | ⛔ **a retirement with nothing loading roads** — navigation silently loses its graph |

⭐ **The one legal split is `F` LAST and ONLY after `C5` is green and reported** (plan §5, batch ④).
⛔ Never the reverse.

## 2. Before you write a line

| | |
|---|---|
| **①** | `git fetch origin claude/blueprint-authoring-status-6sr5ld` then `git merge --ff-only` it (**rule 7**) |
| **②** | ⭐⭐ **Push an empty `chore: started terrain batch 2 at af6b1a071` commit IMMEDIATELY** (**rule 1b**) |
| **③** | ⭐⭐⭐ **T-1 (`R-142`)**: find and run each feature's OWN suite first. ⛔ Do not open a new rail class where a suite exists |
| **④** | ⭐ **Read the owning chapter, not this file.** Success conditions live in `PLAN_Terrain_Zones_Build.md` §2, one row per item |
| **⑤** | ⛔ **The coordinator allocated NO ids** (rule 3). Batch ① used `BP-519`…`BP-527` — ⭐ **continue from `BP-528`**, plain numbers, no letter suffixes (rule 3a-id) |
| **⑥** | ⭐ **Rule 4**: before your final commit, pull the coordinator branch again and read anything that changed |

## 3. ⭐⭐ WHAT BATCH ① HANDED YOU — three inheritances, all measured

Read **`REPORT_Terrain_Zones_Batch1.md`** first. Three of its findings land on you:

| # | inheritance |
|---|---|
| **`C6`** | ⭐⭐⭐ **BLOB LIFETIME is unsolved and it is yours.** `U1` proved `NavigationSolverModule` runs `SlowBackground(10)`, so a reader may be **mid-traversal inside the old blob's native arrays** when you publish a new graph. ⛔ **`RoadNetworkHolder`'s volatile write makes the REFERENCE atomic and says NOTHING about the memory.** The commit/swap path must keep the previous blob alive |
| **`C7`** | ⭐⭐ **The holder must be PASSED.** A host composing `NavigationSolverModule` without one still never sees a reload. ⭐ `NavigationSolverModule` has **zero production constructions** today (test sites only) — that is why this is cheap now and expensive after role-based composition switches it on |
| **`BP-527`** | ⚠ **`[DataPolicy]` is SILENTLY IGNORED for a managed singleton** set without an explicit registration — the attribute is read only by `RegisterComponent`/`RegisterManagedComponent`, and `SetSingletonManaged` auto-registers through a path that never reads it. ⇒ **if any item here adds a managed singleton, the attribute alone does not exclude it**; assert at the gates the engine actually applies (`GetSaveableMask`, `GetRecordableMask`, `ComponentTypeRegistry.IsRecordable`) |

⚠ **Also inherited, as a caution rather than a task:** batch ① reddened three untouched test classes by
calling `ComponentTypeRegistry.Clear()` from a new class outside the serial collection. If you add a class
that mutates that registry, **join `ComponentTypeRegistryMutatorCollection`** — the `QA-008` rail exists to
catch it, and catching it costs a round.

## 4. The items — 16

⭐ Order: **C → D → F**. ⛔ F does not start until C5 is green.

| stage | items |
|---|---|
| **C** — the loader and the two invocation paths | `C1` `TerrainLoadService` · `C2` `ZoneTileLoader` announcing fake · `C3` local scenario-load invocation · `C4` `LoadZoneIntent` consumer · `C5` the terrain loader · **`C6` blob lifetime** · **`C7` pass the holder** |
| **D** — the cluster ops | `D1` the three wire values · `D2` purpose-built payload DTOs · `D3` ONE shared `TerrainAssetHandler` · `D4` retire `IgZoneDummyHandler` · `D5` terrain-identity check |
| **F** — retirement | `F1` retire `ZoneDefinitionDto` + the `Zones` section + `ZoneMembership` · `F2` remove the merge I4 guard · `F3` re-home the five suites · `F4` close `CE-277(a)` will-not-build |

## 5. The wire values are RULED and PERMANENT (`R-42`)

`ClusterOpType.BuildTerrainAsset = 17` · `NodeOpType.PrepareTerrainAsset = 29` · `CommitTerrainAsset = 30`.
⛔ **Do NOT fill the `NodeOpType` gaps at 6/17/18/19** — measured absent from the authoritative NED enum
too, so they are historical holes, not reservations. ⚠ **Both enums are wire contracts**: the FDP copy is a
mirror whose *"integer values must remain identical to the NED counterpart (verified by unit tests)"*, so
`D1` lands in **both**. *(`TkbType.TerrainZone = 8804` already shipped in batch ①.)*

## 6. The four traps

| ⚠ | |
|---|---|
| **`F` is NOT a deletion — it is a RE-HOMING** | ⭐⭐ `HN-037`, measured on this exact shape: a deletion verified safe on *production* callers cost a batch because **8 test callers each asserted a claim that had to be re-homed to a different owning component**. `F3` names five suites and two doubles. ⛔ **Every claim is re-homed or deleted with a stated reason; none silently disappears** |
| **`D3`'s ACK is UNCONDITIONAL** | ⛔ no role×kind filter matrix — ruled. A host with nothing to do **ACKs at once** rather than blocking the round (design §8.3). ⚠ `D4` then deletes `IgZoneDummyHandler`, and the rail is that **IG still never stalls a round** |
| **`C4` is ONE OP PER ZONE** | ⛔ not a multi-zone payload. `ClusterMaster._pendingTransactions` is a keyed dict and already supports concurrent rounds; `_activeTransaction` is the single slot that does not. ⭐ Fan out one op per zone and let the manager serialise at will |
| **`C5`'s rail is the MUSCLE node** | ⛔ the one that matters is not "it loads" but *"a node with **no authoring deps** still loads terrain"* — `NodeBootstrapper.cs:316-318` registers scenario LOAD handlers inside a conditional, so a pure MuscleGround node may have **none** (design §2.1e ④) |

## 7. Gates — the report contract

⭐⭐⭐ **The coordinator does NOT re-run your gates (rule 8); the report SUBSTITUTES for the run.** Rows 1–7
of `CLAUDE.md` §"THE GATE REPORT CONTRACT" in full — verbatim commands, pass/fail/skip, delta vs baseline,
a `--no-build` column, golden movement as a **diff shape**, every RED confirmed pre-existing against
`af6b1a071` and named, tree clean after every suite run, both quarantine counts, `tracker-counts.py --check`
and every id allocated.

⛔⛔ **ROW 8 BINDS THIS BATCH — it is not a judgement call.** C/D are cross-node by construction and F
changes the merge core. **Name the integration suite that would break if the invariant broke, and report
RUNNING it** — or state, with base-sha evidence, why it cannot gate. ⭐ Batch ① set the precedent well:
it named `ScenarioMergeCoreTests` and argued in one line why `ClusterRunner.Integration.Tests` added no
signal (`R-131`'s quarantined reds). That argument is acceptable; **silence is not.**

⭐ Build the **affected project** (~8 s), never the solution (~115 s), in the fix loop; `--no-build` after.
⭐ Anything slow runs in the **background**.

## 8. What to send back

`docs/blueprints/batches/REPORT_Terrain_Zones_Batch2.md`, and **inline** through your report trigger
(the coordinator cannot read your files until you push): the **gate table** · **every id allocated** ·
**row 8's named suite and its result** · the **UML check** (obligation ③) with any deviation **folded back
into the owning design** and the design edit cited (obligation ⑤) · **what `C6` actually concluded about
blob lifetime** · **anything the design got wrong** — ⭐ batch ① found six such things and that section was
the most valuable part of its report · the **sha you pushed**.
