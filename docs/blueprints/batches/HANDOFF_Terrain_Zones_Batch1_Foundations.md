<!--STATUS
state: LIVE
build-state: DISPATCH — a pointer, not a design. ⛔ Carries NO UML and NO design content by rule
  ("THE DIAGRAMS LIVE IN THE DESIGN, NEVER IN THE BATCH"). Every item names its owning chapter.
updated: 2026-09-17
current-answer: §2 is the item list. Read §1 before touching anything.
stale-below: nothing — new document.
known-conflict: none measured. Stage G touches routes (ROUTES1-DESIGN), a different owning design from
  A/B — disjoint files, listed here only because this is the lightest batch.
related-designs:
  - ../PLAN_Terrain_Zones_Build.md — the plan this dispatches (stages A, B, G; grouping in §5).
  - ../../DESIGN_Terrain_Zones_And_Assets.md — THE owning design. Read the cited chapters, not a summary.
  - ../Architect_Question_71_Terrain_Zones_And_The_Asset_Build.md — why the design is shaped this way.
  - ../../designs/routes-1/ROUTES1-DESIGN.md — owns Stage G (§16 is the as-built persistence gap).
-->

# HANDOFF — **Terrain & zones, Batch ① FOUNDATIONS** *(stages A + B + G)*

**Dispatched at `b1751c382`.**
⛔⛔ **Your scope is FROZEN at that sha.** Documents that change after it are **FYI ONLY**. If a later
document **invalidates** an item: **STOP that item and REPORT it** — ⛔ do not adapt, do not revert, and
⛔ **do not stop the batch** (`R-106`: every item that is not blocked still ships).

## 1. Before you write a line

| | |
|---|---|
| **①** | `git fetch origin claude/blueprint-authoring-status-6sr5ld` then `git merge --ff-only` it (**rule 7**) |
| **②** | ⭐⭐ **Push an empty `chore: started terrain batch 1 at b1751c382` commit IMMEDIATELY** (**rule 1b**) — before any code. The coordinator's ancestry check is blind until you do |
| **③** | ⭐⭐⭐ **T-1 (`R-142`): find and run each feature's OWN suite first.** ⛔ Do not open a new rail class where a suite exists. Named per item below; `scripts/find.sh <FeatureName>` finds the rest |
| **④** | ⭐ **Read the owning chapter, not this file.** This handoff restates no design — if an item seems under-specified, the chapter is where the answer is |
| **⑤** | ⛔ **The coordinator allocated NO ids** (rule 3). You number these into the tracker and **state every id** in your report (rule 5) |
| **⑥** | ⭐ **Before your final commit, pull the coordinator branch again** and read any handoff/design that changed (**rule 4**) |

## 2. The items — 11, in this order

⭐ **A must land before B**: A1's double-render would make every later visual check lie.

| # | plan row | owning chapter |
|---|---|---|
| **A1** | fix `TacticalAreaGizmo`'s missing `SimTransform` origin (draw **and** `EmitPickSegments`) | design **§2.2**; defect `BP-517` |
| **A2** | correct `EditablePolyline`'s header — points are RELATIVE (doc-only) | design **§2.2** |
| **A3** | navigation consumers re-read `ZoneEnvironmentData` per tick | design **§5.4** ⚠ **owns `U1`** |
| **A4** | fix `ClusterOpIntents.cs:133`'s comment (doc-only) | design **§4**, dead edge 1 |
| **B1** | `TkbType.TerrainZone = 8804` | design **§2.1**; plan **§3-U2** |
| **B2** | `TerrainAssetLoadState` with `[DataPolicy(NoScenario \| NoReplay)]` | design **§2**, **§9.1** |
| **B3** | the footprint hash `hash(SimTransform ⊕ Points)`, ONE shared helper | design **§9.7 ③c** |
| **B4** | zone entities save/load through the ordinary gate | design **§2**; `DESIGN_Distributed_Scenario_Persistence` §6 |
| **B5** | the terrain **NAME** in the scenario header | design **§2.1e ①** |
| **B6** | the terrain **definition file** + the parsed singleton | design **§2.1e ①a ②** |
| **G1** | `RoutePlanTranslator` (independent; different owning design) | `ROUTES1-DESIGN` **§16**, **§4**; defect `BP-518` |

⭐ **Success conditions are in [`PLAN_Terrain_Zones_Build.md`](../PLAN_Terrain_Zones_Build.md) §2, one row
per item.** ⛔ They are not copied here — a second copy is a second source.

## 3. The four values are RULED and PERMANENT (`R-42`)

`TkbType.TerrainZone = 8804` · `NodeOpType.PrepareTerrainAsset = 29` · `CommitTerrainAsset = 30` ·
`ClusterOpType.BuildTerrainAsset = 17`.
⭐ **This batch allocates only `8804`** — the three op values belong to batch ②, listed here so nothing
guesses them. ⛔ **Do NOT fill the `NodeOpType` gaps at 6/17/18/19**: measured absent from the
authoritative NED enum too ⇒ historical holes, not reservations (design §6).

## 4. The three traps this batch walks into

| ⚠ | |
|---|---|
| **A1 is a LIVE double-render, not a theory** | 📐 entity `5525100c` (`hill-attack`): `TkbType 8803` + `MapOverlayStyle` + `SimTransform [670, 473.5, 0]` + points `(-53, -88.5)` ⇒ **two draws ~820 m apart**, picking off by the same amount. ⭐ **Red-proof first**: assert the two gizmos' emitted vertices coincide, watch it fail, then fix |
| **A3 owns `U1`, and it may flip the fix** | plan §3.2: whether any navigation module runs on a background thread where `DataPolicy` constrains singleton access. ⭐ **Measure it and say so in the report** — if it does, a per-tick singleton read is illegal and a holder object is the answer instead. ⛔ Do not assume either way |
| **B2/B6 must prove ABSENCE, not presence** | ⭐ a save **and** a replay round-trip that show `TerrainAssetLoadState` and the terrain singleton are **not in either output**. ⛔ "It has the attribute" is not the rail |

## 5. Gates — the report contract

⭐⭐⭐ **The coordinator does NOT re-run your gates (rule 8). The report SUBSTITUTES for the run — but only
if it carries what a run would have said.** Rows 1–7 of `CLAUDE.md` §"THE GATE REPORT CONTRACT", in full:
one row per gate with the **verbatim command**, pass/fail/skip counts and the **delta vs baseline** · a
`--no-build` column · golden movement as a **diff shape** · every RED confirmed **pre-existing against the
base sha**, named · the working tree **clean** after every suite run · both quarantine counts · and
`tracker-counts.py --check` plus **every id you allocated**.

⭐ **Row 8 does not bind this batch** — A/B/G are local model and gizmo changes, not cross-node. ⚠ If your
B4/B5 work turns out to touch the merge path, row 8 **does** apply and you name the integration suite.

⭐ **Build the AFFECTED PROJECT (~8 s), never the solution (~115 s), inside the fix loop**; `--no-build`
for every run after the first. ⭐ `scripts/quick-check.sh <proj> [filter]`.

## 6. What to send back

A `docs/blueprints/batches/REPORT_Terrain_Zones_Batch1.md`, and in chat:

1. the **gate table** (§5);
2. **the ids you allocated**;
3. **`U1`'s measurement** and what it did to A3;
4. ⭐⭐ **the UML check (obligation ③)** — *"the design carries N classes and M sequences; what I built
   matches / deviates HERE and why"*. ⛔ **A deviation is a finding argued in the report AND folded back
   into the owning design** before the batch closes, prior state marked SUPERSEDED (obligation ⑤) — cite
   the design edit in the report;
5. anything the design got **wrong**. ⭐ That is the most valuable line in the report, not a failure.
