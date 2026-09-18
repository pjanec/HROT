<!--STATUS
state: LIVE
updated: 2026-09-18
current-answer: §1 what shipped · §2 the five findings · §3 the gate table · §4 the UML check
  (obligation ③) · §5 what the design got wrong · §6 the programme-closing OPEN-questions answer.
stale-below: nothing.
known-rot: none known. ⚠ This report is EPHEMERAL — every durable fact in it is folded into
  DESIGN_Terrain_Zones_And_Assets.md §10.7/§10.8 and into the BP-539..BP-551 tracker rows. Quote those.
related-designs:
  - docs/DESIGN_Terrain_Zones_And_Assets.md — the owning design; §10.7 (E5) and §10.8 (stage H) carry
    this batch's as-built, and §10.1-§10.6 carry batches ② / ②b's.
  - docs/blueprints/PLAN_Terrain_Zones_Build.md — the stage/task breakdown and the U-register this
    batch closed U6 and U9 in.
  - docs/blueprints/batches/HANDOFF_Terrain_Zones_Batch3_Surfaces.md — the dispatch this answers.
-->

# REPORT — Terrain & zones, Batch ③ "THE SURFACES" (`C8` + `E` + `H`)

**Branch** `claude/blueprint-macro-feature-sdmspn` · **scope frozen at** `2500ced2d` ·
**ids allocated** `BP-539` … `BP-551` · **this is the last batch of the programme.**

---

## 1. What shipped — 10 items, all of them

| item | state | one line |
|---|---|---|
| **`C8`** | ✅ | a REAL `TerrainLoadClusterStateHandler` on IG and CGF. **Closes `BP-537`** |
| **`E1`** | ✅ | the zone gizmo renders load state as a stroke; staleness COMPUTED, never stored |
| **`E2`** | ✅ | "Load zone" on the shared entity context menu, **always enabled**, publishing the cluster op |
| **`E3`** | ✅ | a zones view in the existing `DetailsWindow`. **Owns `U5`** — which cost one enum member |
| **`E4`** | ✅ | the cluster-panel terrain-build control, **per-node** outcomes, never one global OK |
| **`E5`** | ✅ | ONE area-authoring mechanism. **Owns `U6`** — and exposed a live defect |
| **`H1`** | ✅ | the seeds are passed, and re-read LIVE |
| **`H2`** | ✅ | seeds resolve where the cluster looks. **Owns `U9`** — both its options measured away |
| **`H3`** | ✅ | Scenario offers no blank template; `CreateNew(null, …)` refuses and names the seeds |
| **`H4`** | ✅ | a shipped seed carrying both `tkbName` and `terrainName`. ⚠ **with a filed blocker — `BP-550`** |

---

## 2. The five findings, shortest-first

### ⑴ 🔴 `E5`'s task was not buildable as written, and that IS the finding — `BP-545`

The plan said *"move area authoring into the Map2D **role** feature set."*

| the plan assumed | 📐 measured |
|---|---|
| `NodeRole.Map2D` is where authoring features live | it is a component-**OWNERSHIP** policy (`HrotRoleComponentSets.cs:193`) — it selects no systems and registers no tools |
| *"editor included"* | 🔒 **the editor deliberately does NOT carry `Map2D`** — `EditorCapabilitiesTests.cs:232` asserts the user ruling *"CGF ∪ SimHost, and NOT ImageGenerator"* |

⇒ **a role gate would have excluded the one host the `U6` ruling names.** What `U6` actually is: a
**duplicate-mechanism** finding — the path existed twice (~35 lines editor, ~135 lines IG). Moved into
`AreaAuthoringArm`, which holds no host knowledge.

**And the move exposed a live defect.** Both bodies hard-coded `TacGraphic_Area`, and the parse read the
incoming `tkbType` **only** to compare it against `TacGraphic_Route` ⇒ a `CMD_START_AUTHORING` asking for
`TerrainZone` (`B1`) silently authored an `8803` tactical area. A shape appeared, so nothing looked
broken — and **none of stage `E`'s zone surfaces would ever have matched it.**

### ⑵ 🔴 `C8`'s real risk was ORDER, and only a rail found it — `BP-539`

`ClusterSlave` dispatches to the **first** `CanHandle`-true handler and returns (`ClusterSlave.cs:406-448`),
and `ReferenceLiveLoadHandler` claims `PrepareLive` **unconditionally**. ⇒ registering the loader after it
makes it **silently unreachable**, which is indistinguishable from a host that has no terrain. Fixed by
registering before every claimant, pinned against a **real `ClusterSlave`** rather than by inspection —
and the rail immediately caught CGF's target-typed `new()`. **The code was strengthened, not the rail
weakened.**

### ⑶ 🔴 `U9`: both of its options are impossible — `BP-549`

| `U9`'s option | 📐 what is measured |
|---|---|
| *"`AvailableRecipes()` hands back full paths the existing load accepts"* | **nothing anywhere accepts a path** — `OpenForEdit` (`EditorScenarioSession.cs:147`) takes a NAME and publishes a `TransitionStateIntent` |
| *"widen `IScenarioCreationSession` with a root-aware load"* | resolution is **per node**, against that node's NAS scenarios root. A root would cross the wire to nodes where `Recipes/Scenarios` — a local output path — **does not exist** |

⇒ the third shape costs nothing: `OpenForEdit` already accepts a relative name, so seeds are staged into
a reserved `Recipes/` subfolder and loaded as `Recipes/<seed>`. **No seam changed.**
⭐ `AssetRoots`' own header had already flagged the reason: *"Scenario has **no** Assets root — Scenarios
are orchestrator/NAS-backed."*

### ⑷ 🔴 `QA-008` was live in a SECOND assembly — `BP-547`

`Hrot.Presentation.Tests` failed **3 of 4** full runs with **six distinct victims**, every one green under
`--filter`. Three classes there clear the process-global `ComponentTypeRegistry` with no serial collection.
**Why `QA-008`'s fix could never have reached it:** an xUnit collection is per-assembly, and its gate is a
source scan rooted at its own `.csproj`. Fixed with this assembly's own collection + gate: **3-red-of-4 →
0-red-of-5 at 252/252.**
⚠ **Stated honestly:** the batch did not cause it — base failed 1 of 4; 11 new test methods widened an
existing window.

### ⑸ 🔴 A correction to batch ②b — `BP-551`

②b's gate row 7 reported `Hrot.Orchestrator.Tests` clean. It was not: **my own rail asserted the defect**
(`Assert.NotEqual` on two transaction ids that `D3` had deliberately made equal), and the gate run missed
it because `--no-build` followed a build of the **production** project only. Relayed to the coordinator
immediately rather than held for this report.

> ⭐ **The checkable lesson:** *build the TEST project, not the production one, before a `--no-build`
> run* — a test project's build copies the production assembly, the reverse does not.

---

## 3. §Gates — the gate-report contract (rule 8)

**Base commit for every pre-existing claim: `2500ced2d`.**

| # | gate — verbatim command | `--no-build`? | result | Δ vs base |
|---|---|---|---|---|
| 1 | `dotnet build <each affected project> --no-restore` | n/a | **0 errors** on all 12 touched projects | — |
| 2 | `dotnet test Hrot/Engine/Hrot.Presentation.Tests/… --no-build` | ✅ after building the TEST project | **252 passed / 0 failed**, ⭐ **5 consecutive clean runs** | base 240; +12 *(11 `AreaAuthoringArmTests` + 1 `QA-008` gate)*. 🔴 **base itself failed 1 of 4 runs — see `BP-547`** |
| 3 | `dotnet test Hrot/Subsystems/Hrot.IG.Tests/… --filter AreaAuthoring` | ✅ | **13 passed / 0 failed** | base **11/11** ⇒ +2 `E5` rails. ⭐ `T-1`: run BEFORE the change too |
| 4 | `dotnet test Hrot/Subsystems/Hrot.Editor.Tests/… --filter EditorSpawnAdapterTests` | ✅ | **9 passed / 0 failed** | +2 `E5` rails |
| 5 | `dotnet test Hrot/Subsystems/Hrot.Editor.Tests/… --filter ScenarioNewAsset\|TheShippedScenarioSeed` | ✅ | **13 passed / 0 failed** | +5 stage-`H` rails, 3 claims **re-homed** |
| 6 | `dotnet test Hrot/Subsystems/Hrot.Editor.Tests/…` *(full)* | ✅ | **412 passed / 1 failed / 1 skipped** | 🔴 **the 1 red is PRE-EXISTING and NON-DETERMINISTIC — `BP-548`** |
| 7 | `dotnet test Hrot/Subsystems/Hrot.ExCon.Tests/… --filter ExConLogicTests` | ✅ | **40 passed / 0 failed** | +1 `E5` rail |
| 8 | `dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests/…` *(full)* | ✅ | **2075 passed / 0 failed / 1 skipped** | clean |
| 9 | `python3 scripts/tracker-counts.py --check` | n/a | ✅ **OK — open 111 / done 373** | counts table updated with the 13 rows |
| 10 | `python3 scripts/rulings-check.py` | n/a | ✅ **35/35 verified** | — |
| 11 | `python3 scripts/design-digest.py --check` | n/a | ✅ **59 docs OK**, every buildable design carries both diagrams | — |
| 12 | `MERMAID_PREFIX=/tmp/mm node scripts/mermaid-check.mjs docs/DESIGN_Terrain_Zones_And_Assets.md` | n/a | ✅ **all 7 blocks parse** | +2 *(§10.7 `classDiagram`, §10.8 `graph TD`)* |

### Row 4 of the contract — **every red confirmed pre-existing, against `2500ced2d`, by running it there**

| red | 📐 the proof |
|---|---|
| `AiHotReloadCoordinatorTests.TwoReloadCycles_OldAlcIsCollected` *(gate 6)* | built and ran the suite in a worktree at `2500ced2d`: **it fails there too** in a full run *(base pass/fail over 2 runs; that base run produced a **second** victim, `EditorMapPickAdapterTests.CompletingAPickResumesTheToolUnderneath`)*. In my tree it goes **fail/pass/fail** over 3 full runs and **passes 3/3 under `--filter` on both trees**. ⇒ pre-existing, rotating, **not a regression** — filed as `BP-548`. ⚠ **NOT diagnosed**, and deliberately not assumed to share `BP-547`'s cause: the assertion is that an `AssemblyLoadContext` was **collected**, which is sensitive to concurrent memory pressure |
| the six `Hrot.Presentation.Tests` victims | same method: base failed **1 of 4** full runs. ⇒ the race pre-existed; the batch widened it. ⭐ **Now fixed** (`BP-547`), so gate 2 is genuinely clean rather than "pre-existing red" |

### Rows 3 and 5 — diff shape, and a clean tree

**Golden movement: NONE.** No golden file was touched, generated or regenerated by this batch.
**The working tree is CLEAN after every suite run** — verified with `git status --short` after each.
**Quarantine counts: unchanged.** ⛔ **No test was skipped, filtered out or quarantined by this batch**;
the one `Skipped: 1` in gates 6 and 8 is pre-existing and identical at base.

### Row 8 — the cross-cutting integration obligation

`C8` and `E2` are cross-node changes, so the contract requires naming the integration suite that would
break if the invariant broke, and reporting **running** it.

| | |
|---|---|
| ⭐ **named and run** | `TerrainLoaderIsComposedOnEveryEcsHostRails` — **3 composition roots × 3 claims**, plus `RegistrationOrderDecidesTheDispatchWinner` against a **real `ClusterSlave`**. ⭐⭐ This is the suite that would break if `C8`'s invariant broke, and it is the one that **caught CGF's target-typed `new()`** |
| ⭐ **named and run** | `ClusterMasterZoneRoundTests` — **12/12**, the round `E2`'s menu item starts *(and where `BP-551`'s stale assertion lived)* |
| ⛔ **CANNOT gate, with evidence** | `Hrot.ClusterRunner.Integration.Tests` — the **pre-existing DDS-allocator crash** makes the suite un-gateable, exactly as batches ① and ② reported. ⚠ Reported as a finding, not silently omitted |
| ⛔ **NOT run — `T3`, async by rule** | `run-system-tests.sh`. ⚠ **And `BP-550` says what it would find:** the shipped seed names a TKB and a terrain that **nothing stages**, so an E2E create-from-seed **throws by design** (§8.3 `N4`). ⇒ ⛔ a green there would have required building a staging mechanism no stage of this plan owns |

---

## 4. Obligation ③ — the UML check

**The design carried 5 diagrams at dispatch** *(§2 `classDiagram`, §3.1/§3.2 `sequenceDiagram`s, §4's
module `graph TD`, and §9.5's view sketch)*. Checked before building. **Matches, with three deviations,
each argued in the report AND folded into the design** *(obligation ⑤)*:

| deviation | where the design now says so |
|---|---|
| `E5` is **not** a Map2D-role feature set — it is a shared CLASS any host composes | **§10.7**, with the role-vs-tools table and a new `classDiagram` |
| stage `H`'s seeds live in **three** places, only one of which a cluster load can reach | **§10.8**, with a new module `graph TD` — prose could not say which root does which job |
| `E1`'s outline loop is now shared (`DrawClosedPolylineOutline`); `E3`'s progress is tracked by the **requester** | **§10.7 / §10.8** and `BP-540` / `BP-542` |

⭐ **The two new diagrams both earned their place by the diagram-first test:** drawing §10.8's module view
is what forced the question *"which of these three copies can a cluster load actually resolve?"* — and
answering it is what killed both of `U9`'s options.

---

## 5. What the design got wrong

| # | the design said | 📐 measured |
|---|---|---|
| 1 | `E5`: *"move it into the **Map2D role** feature set… editor included"* | the role is an ownership policy and **the editor is deliberately outside it** ⇒ the instruction is self-contradicting. `U6` is a duplicate-mechanism finding |
| 2 | `U9`: *"two clean ways… widen the seam, or return full paths"* | **both impossible.** A scenario resolves as a NAME through the cluster, not as a file |
| 3 | `U5`: *"selection-as-context is new"* | it existed end to end; **one enum member** was missing |
| 4 | `H4`: *"creating from it yields a scenario whose header round-trips both names"* | true of the CARRYING; ⛔ **an end-to-end load throws**, because nothing stages a named TKB or terrain — `BP-550` |
| 5 | §2.1e ⑤c `G1`: *"three wirings + one deletion"* | accurate for `H1`/`H3`, ⛔ **`H2` needed a staging step** the design did not anticipate |

---

## 6. The programme-closing question — **§8.4 / §9's OPEN questions, answered by what now exists**

§8.4 says *"See §9. That is the only thing now standing between this document and `READY-TO-BUILD`."*
⇒ **§8.4 is answered exactly insofar as §9 is.**

### ✅ ANSWERED BY WHAT EXISTS — 6 of 8

| # | §9's question | ✅ what answers it now |
|---|---|---|
| **`U1`** | seeing a zone is stale after an edit | `TerrainZoneGizmo` (`E1`) — stroke per state, staleness **computed** via `ZoneFootprint.Compute`, so a reshape changes stroke with no reload. ⭐ The design's own lean was right and is now code |
| **`U2`** | invoking a load | `SharedContextMenuPopulator` + `IEntityActionController.LoadZone` (`E2`) — the exact seam the design named, **always enabled** per §9.7 ③b |
| **`U3`** | forcing all changed zones | `ZonesDetailsView` (`E3`) — a view on the existing details shell, with `LoadAllStale`. ⭐ The design's *"PURE REUSE"* claim held: no new shell, no new window |
| **`U4`** | multi-zone at once | **ONE OP PER ZONE**, as ruled — `LoadAllStale` issues one op per zone and `_pendingTransactions` already supports concurrent rounds |
| **`U5`** | *"the details shell has no map-background CONTEXT"* | `SelectionOrigin.MapBackground` — ⭐⭐ **one enum member.** `NotifySurfaceFocused`/`DetailsContext.Focus`/`FocusIs` already existed |
| **`U6`** | zone creation is IG-only | `AreaAuthoringArm` + `PlaceZone` + `StartZoneAuthoringMode` + the DRAW ZONE button (`E5`). ⭐ A zone is now **authorable on every host that composes the shared adapter or calls the arm** |

### ⚠ GENUINELY STILL OPEN — 2, plus 2 the programme newly opened

| # | what remains | why it is not closable here |
|---|---|---|
| **`U3` tile lifetime** (§9.8 *"two lifetimes"*) | zone invalidation vs **tile eviction** are still one concept in code, because ⛔ **tiles are FAKED by ruling** (§6/§7 — *"fake is ok"*). ⇒ there is nothing to evict yet. **Scope, not an open question** — but it becomes one the moment tiles are real |
| **`U7` static-obstacle bake** | ⛔ ruled postponed; `B1` deliberately allocated **no** static kind, so no wire id was burned (`R-42`) for unbuilt behaviour |
| 🔴 **NEW — `BP-550`: nothing stages a named TKB or terrain artifact** | this is the **consumption** end of §2.1e ①a. The loaders are built and correct; the artifacts they name have no distribution path. ⭐⭐ **This is now the single thing standing between the programme and a working end-to-end zone load**, and it is bigger than any remaining `U` row |
| ⚠ **NEW — `BP-546`: the `Place*` tool ids are registered by ONE adapter** | the last of `UXI-07` step `3b`'s unfinished half. Folding them into `MapInteractionPack` would make authoring role-composable — which is what `U6`'s wording was reaching for and what the role could never deliver |

### ⇒ The honest closing statement

⭐⭐⭐ **Every `U` row §9 opened is answered, and `E`/`H` shipped the surfaces §8.4 was waiting for.**
⛔⛔ **But the programme does NOT end with a working zone load**, and the reason is precise and newly
measured: **the loaders resolve artifacts that nothing distributes** (`BP-550`). ⚠ Before this batch that
was invisible, because no scenario in the repository had ever named a TKB or a terrain — `H4` is the first
artifact that does, and it is what turned a latent gap into a measurable one.

⇒ ⭐ **Recommended next programme, one line:** artifact staging for named TKB and terrain assets, which
would make `BP-550` closable and `T3` meaningful for this feature for the first time.
