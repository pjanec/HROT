<!--STATUS
state: LIVE
updated: 2026-09-18
current-answer: §1 what shipped · §2 the findings · §3 V1's measurement and the call · §4 the gate
  table · §5 T3's verdict · §6 the UML check · §7 what the design got wrong.
stale-below: nothing.
known-rot: none known. ⚠ This report is EPHEMERAL — every durable fact is folded into
  DESIGN_Artifact_Staging.md §9 and into the BP-552..BP-558 tracker rows. Quote those.
related-designs:
  - docs/DESIGN_Artifact_Staging.md — THE owning design; §9 carries this batch's as-built.
  - docs/blueprints/PLAN_Artifact_Staging_Build.md — the 8-task breakdown this answers.
  - docs/blueprints/batches/HANDOFF_Artifact_Staging.md — the dispatch.
  - docs/DESIGN_Terrain_Zones_And_Assets.md — BP-550 is the row this closes for the TKB.
-->

# REPORT — **Artifact staging: getting the named TKB to the nodes**

**Branch** `claude/blueprint-macro-feature-sdmspn` · **scope frozen at** `a5e9a5278` ·
**ids allocated** `BP-552` … `BP-558` · **`BP-550` half-closed** *(TKB done, terrain still open)*.

---

## 1. What shipped — 8 tasks

| # | state | one line |
|---|---|---|
| **`S1a`** | ✅ | the TKB path constants; the hand-built `"TKB"` literal is gone from production |
| **`S1b`** | ✅ | **`V1` settled — and the lean FLIPPED.** See §3 |
| **`S2a`** | ✅ | the consensus check returns what it already computed — ⚠ **both names**, see §2 ⑴ |
| **`S2b`** | ✅ | the named zip reaches every node's TKB directory |
| **`S2c`** | ✅ | the staged header is **written**, in the shape the node actually parses |
| **`S2d`** | ✅ | `IsAlreadyCurrent` on `(length, mtime)`, with the `File.Copy` property railed |
| **`S3a`** | ✅ | length joins the node's cache key, so both sides evaluate one predicate |
| **`S3b`** | ✅ | the compose rail, **all three cases, including ③** |
| **`S4`** | ⚠ | **confirmation — and it is BOTH halves.** See §2 ⑷ |

⭐ **Plus one thing that was not on the list and had to come first** — see §2 ⑸.

---

## 2. The findings

### ⑴ 🔴 `S2c` had to carry BOTH names, or `S4` was impossible — `BP-553`

The design and plan name only the TKB name. 📐 `ScenarioTerrainName.Read` reads `TerrainName` out of **that
same staged header file** (`ScenarioTerrainName.cs:42`). ⇒ writing only `TkbName` would have left terrain
with nothing to inherit, and `S4`'s *"no new code"* unreachable. Both names now reach consensus and are
written; disagreement on **either** fails loud.

### ⑵ ⚠ The staged header is **not** a copy of the scenario's header block — `BP-553`

📐 Both node-side readers take a **flat, PascalCase** object — `ValueTextEquals("TkbName")` /
`("TerrainName")`, no nesting. A scenario file carries `header: { tkbName, terrainName }`, **nested and
camelCase**. ⇒ *"write the agreed header"* read as *"copy the block"* would produce a file **neither reader
can parse**. `BuildStagedHeaderJson` builds the reader's shape and omits absent names rather than writing
`null`.

### ⑶ 🔴 A design question the build surfaced, answered by three existing rails — `BP-555`

Is *"scenario names a TKB that is not published"* a prefetch failure? The design never said. The first
draft counted it — and `PrefetchScenario_SameTkbName_AllFiles_Succeeds` plus two siblings went red, because
they assert `IsFullSuccess` for exactly that case.

⇒ **They encode the existing contract.** Counting it would change the orchestrator's **transition outcome**
for a condition this design never said should block one, and silently redefine `IsFullSuccess` for every
caller. ⭐ The loud failure already has a designed home — the node's handler throws
`FileNotFoundException` naming the path. ⇒ the gateway logs an error naming the missing NAS path **and where
to publish it**, and nothing is swallowed.

### ⑷ 🔴 `S4` — terrain inherits the HEADER and cannot inherit the ARTIFACT — `BP-557`

| half | inherits? | 📐 |
|---|---|---|
| the **name**, from the staged header | ✅ **with no new code** | one file, two readers; `S2c` writes both names. ⭐ Railed through the orchestrator's **own** `BuildStagedHeaderJson`, ⛔ not a fixture — a fixture cannot catch the writer emitting an unparseable shape |
| the **artifact** | 🔴 **no, and it cannot** | TKB is `{node}/TKB/{name}.zip`; terrain is `{node}/Terrain/{name}.json` — different directory, different extension. `S2b` copies one named zip out of `{nas}/tkb` and cannot serve it |

⛔ **Not built, deliberately** — the dispatch ruled a terrain-specific path is a finding, and a second
artifact **kind** is the wider asset-management model that is out of scope. A rail pins the gap.
⇒ **`BP-550` closes for the TKB, stays open for terrain.**

### ⑸ 🔴 `T-1` found the prefetch suites RED before any code, and two shared one cause — `BP-556`

📐 Baseline, run before writing anything: `TkbLoadClusterStateHandlerTests` **13/13**,
`StorageGatewayTests` **8/10**, `ClusterMasterPrefetchTests` **1/2**.
⭐ **These are the same three reds this lane has reported as "pre-existing" for three batches without ever
diagnosing them.**

**The shared cause:** production resolves `{nasBasePath}/scenarios/{scenarioId}`
(`StorageGatewayModule.cs:238`); two fixtures built the layout **without the `scenarios/` segment**, so the
call threw `DirectoryNotFoundException` at its first statement.
⛔⛔ **So the empty-directory guard, `CheckTkbNameConsensus` and the whole parallel copy loop had never been
exercised by any test** — precisely the method `S2` extends. Fixing it first is what made `S2`'s success
conditions assertable.

**The third, separately:** the *"invalid on any OS"* target was a UNC path; on **Linux a backslash is an
ordinary filename character**, so it is a legal relative path, the copy succeeds, and the count reads 3
where 2 is expected. Replaced with a destination whose parent is an existing **file**.

⇒ ⭐ **`Hrot.Orchestrator.Tests` is now 176/176**, where this lane had been reporting *"166/3 pre-existing"*.

### ⑹ ⚠ A tooling defect that nearly produced a false finding — `BP-558`

`scripts/find.sh` with a regex **alternation** returns `graph 0 · grep 0` and *"The two agree on the file
set"* — for classes that exist. `CLAUDE.md` promises it *"degrades LOUDLY … never an empty result that
reads like an absence"*, and this is exactly that, **with both halves appearing to corroborate**. The first
conclusion drawn from it was *"the plan names three `T-1` suites and none exists"* — the opposite of true.

---

## 3. `V1` — the measurement, and which way I went — `BP-552`

> **Decision: the TKB handler keeps the bare root it already gets; `GetTkbStagingRoot` takes NO node id.**
> The plan's lean was the other way, and its own cited precedent is what flipped it.

| the lean rests on | 📐 code — how it IS | design basis |
|---|---|---|
| the node handler gets a **bare, shared** root | ⛔ **FALSE.** Every production host passes a **per-node** root: `SimHostApp.cs:362` `Combine(base,"nodes",$"node-{id}")` · `CgfSubsystem.cs:612` identical · `OrchestratorSubsystem.cs:137` `GetNodeStagingRoot(id)` | ✅ plan §3 named this as the flip condition |
| `ReferenceArchiveHandler` is a precedent for a **node-aware root** | ⛔ **FALSE, and it argues the other way.** It uses `nodeId` to build a **filename** — `node_{id}.fdp` under the **shared** `exercises/` dir (`ReferenceArchiveHandler.cs:73-74`) | ⛔ searched design + plan; the precedent is cited but never characterised |
| the orchestrator writes `{root}/nodes/node-N/TKB` | ✅ `AssetPrefetchProcessManager.cs:193` + `GetNodeTkbStagingRoot` | ✅ design §3 |

⇒ threading a node id into the handler would have **double-applied the node segment**.
`GetNodeTkbStagingRoot(base, id)` exists for the orchestrator and is defined by **composing** the two
existing helpers; rails in **`TheHostsAgreeOnTheScenarioRootTests`** — the suite whose whole purpose is
*"two sides agree on a root"* — assert the two sides resolve the same directory and that the node segment
appears exactly once.

⚠ **One precision the plan got wrong:** it phrased the condition as *"if `ResolveStagingRoot()` is already
per-node"*. It is **not** — it is the base. What is per-node is the value each host **passes**.

---

## 4. §Gates — the gate-report contract (rule 8)

**Base commit for every pre-existing claim: `a5e9a5278`.**

| # | gate — verbatim command | `--no-build`? | result | Δ vs base |
|---|---|---|---|---|
| 1 | `dotnet build <each affected project> --no-restore` | n/a | **0 errors** on all 7 touched projects | — |
| 2 | `dotnet test Hrot/Subsystems/Hrot.Orchestrator.Tests/…` *(full)* | ✅ after building the TEST project | ✅ **176 passed / 0 failed** | 🔴 **base 166 passed / 3 FAILED** — all three fixed by `BP-556` |
| 3 | `dotnet test …Hrot.SimHost.Tests/…` *(full)* | ✅ | **989 passed / 4 failed / 3 skipped** | base **983 / 3 / 3**; +7 new rails. **3 reds identical to base; the 4th is `BP-534`** — see row 4 |
| 4 | `dotnet test …Hrot.Editor.Tests/…` *(full)* | ✅ | **417 passed / 1 failed / 1 skipped** | the 1 red is `BP-548`, filed pre-existing in batch ③ |
| 5 | `dotnet test …Hrot.Editor.Tests/… --filter TheHostsAgreeOnTheScenarioRootTests` | ✅ | ✅ **12 passed** | +3 `S1b` rails |
| 6 | `dotnet test …Hrot.Orchestrator.Tests/… --filter StorageGateway\|ClusterMasterPrefetch` | ✅ | ✅ **26 passed** | +7 `S2` rails |
| 7 | `dotnet test …Hrot.SimHost.Tests/… --filter TkbLoadClusterStateHandlerTests` | ✅ | ✅ **17 passed** | +4 `S3` rails incl. the compose cases |
| 8 | `dotnet test …Hrot.SimHost.Tests/… --filter TerrainLoadClusterStateHandlerTests` | ✅ | ✅ **13 passed** | +3 `S4` rails |
| 9 | `python3 scripts/tracker-counts.py --check` | n/a | ✅ **OK — open 112 / done 379** | 7 new rows + `BP-550` amended |
| 10 | `python3 scripts/rulings-check.py` | n/a | ✅ **35/35 verified** | — |
| 11 | `python3 scripts/design-digest.py --check` | n/a | ✅ **61 docs OK** | — |
| 12 | `MERMAID_PREFIX=/tmp/mm node scripts/mermaid-check.mjs docs/DESIGN_Artifact_Staging.md` | n/a | ✅ **all 3 blocks parse** | — |

### Row 4 — every red confirmed pre-existing, **by running it at the base sha**

Built and ran `Hrot.SimHost.Tests` in a worktree at `a5e9a5278`: **983 / 3 / 3**, and the three are
*identical by name* to three of mine — `NodeRolePersistenceRails.TheSaveHandlerSetIsStillComplete`,
`MapPresentationParityRails.EveryTkbSpawningHost_ObtainsTheSharedTranslatorSet(…EditorStrideSubsystem.cs)`,
`FullBranchPipelineTests.BranchedRecording_CapturesHistoricalStateAsKeyframe`.
My fourth, `EcsRecordReplayControllerTests.PrepareRecordingAsync_InstallsRecordingModule`, is **`BP-534`** —
filed in batch ②b as a rotating non-deterministic flake; **verified 5/5 green under `--filter`, three
consecutive runs**, which is exactly what that row predicts.

### Rows 3 and 5 — diff shape, clean tree, no new skips

**Golden movement: NONE** — no golden file was touched or regenerated.
**Tree clean after every suite run.** **Quarantine counts unchanged; no test skipped or filtered by this
batch** — the `Skipped: 3` / `Skipped: 1` are identical at base.

### Row 8 — the cross-cutting integration obligation

This changes what every node holds before a load, so row 8 binds.

| | |
|---|---|
| ⭐ **named and run** | **`TkbLoadClusterStateHandlerTests`' compose rails** — the suite that would break if the invariant broke, exercising the orchestrator's skip and the node's ingest **together**, across a simulated node restart. 17/17 |
| ⭐ **named and run** | `StorageGatewayTkbConsensusTests` + `ClusterMasterPrefetchTests` — the prefetch saga end of the same invariant. 26/26 |
| ⛔ **CANNOT gate, with evidence** | `Hrot.ClusterRunner.Integration.Tests` — the **pre-existing DDS-allocator crash**, reported un-gateable by batches ①–③ and unchanged here |

---

## 5. ⚠ `T3` — **it cannot gate in this environment, and that is the honest answer**

The dispatch asked whether an end-to-end create-from-seed is green **for the first time**.

📐 **Measured:** `scripts/run-system-tests.sh` builds, then every case fails identically with
`McpRequestException: … could not reach the editor at http://localhost:<port>/: Connection refused`.
⇒ ⛔ **the harness never boots an editor in this container, so no product assertion was ever evaluated** —
not one failure is an assertion about staging. The failing set spans `DeterminismRails`,
`ClusterConformanceRails`, `TheMapsAgreeOnBothHostsRails`, `VariableAddressingTests` and others, i.e. the
**whole** suite, including cases with nothing to do with this batch.

⇒ ⭐⭐ **This is a row-8-shaped finding, not a result:** `T3` is un-gateable here for the same class of
reason as the `ClusterRunner.Integration.Tests` DDS crash, and reporting it as a red would be as wrong as
reporting it green. ⛔ **I did not run it at the base sha to "prove pre-existing"** — the failure mode is a
refused TCP connection at harness start-up, which cannot be a regression from a file-copy change.

⚠ **So the question the dispatch actually cares about is still open**, and it is the one thing this batch
could not answer. ⭐ What *can* be said from the unit and integration level: the header and the zip now
arrive, the node resolves the name, and the two skips compose — which is everything `T3` would have
exercised **except the real process boundary**.

---

## 6. Obligation ③ — the UML check

The design carried **three** diagrams at dispatch (§3 `classDiagram`, §4 `sequenceDiagram`, §5 module
`graph TD`). Checked before building. **Matches as drawn, with three deviations, each argued above AND
folded into the design** (obligation ⑤, `DESIGN_Artifact_Staging.md` §9):

| deviation | folded into |
|---|---|
| `V1` answered the other way; `GetTkbStagingRoot` takes no node id | **§9.1** |
| the staged header carries **both** names, and is flat/PascalCase | **§9.2**, **§9.5** |
| a named-but-unpublished artifact logs and does not fail | **§9.3** — a new arm §4's sequence did not draw |
| terrain inherits the header, not the artifact | **§9.4** — §5's *"inherits everything unchanged"* narrowed |

⭐ §5's module diagram was the load-bearing one again: its caption calls the `NedTkbCatalog` fallback edge
*"TODAY: always taken, because nothing writes NTKB"*. That edge is now dead **only when a scenario names a
TKB** — which is what made the `S2c`-is-the-important-half framing correct.

---

## 7. What the design got wrong

| # | the design said | 📐 measured |
|---|---|---|
| 1 | `V1`: *"lean — make the TKB handler node-aware, mirroring `ReferenceArchiveHandler`"* | both halves false; the precedent is a **filename** discriminator in a shared directory |
| 2 | §2/§4: the staged header carries the **TKB** name | the **terrain** name rides the same file and had to be carried too, or `S4` was impossible |
| 3 | §5 / `related-designs`: terrain *"inherits everything here unchanged"* | true of the **header**, false of the **artifact** |
| 4 | *(silent)* on a named-but-unpublished artifact | three existing rails already answered it; the first draft contradicted them |
| 5 | plan §0: *"the feature's own suites exist … run them BEFORE writing code"* | ✅ correct and valuable — **and three of them were already red**, two for a cause that made the target method untested |
