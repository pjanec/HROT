<!--STATUS
state: LIVE
updated: 2026-09-18
current-answer: the whole file — it is a batch report and does not rot in place.
stale-below: nothing
known-rot: nothing
known-conflict: none
related-designs:
  - docs/DESIGN_Cluster_Load_Phase.md — the owning design. §6 carries the AS-BUILT this report points at.
  - docs/DESIGN_Node_Roles_And_Policies.md — §3.2 owns the per-role requirement table this batch reads.
  - docs/DESIGN_Terrain_Zones_And_Assets.md — §2.1e ④ is SUPERSEDED by this batch.
-->

# REPORT — the cluster LOAD PHASE batch (`L1`–`L8`)

**Branch** `claude/blueprint-macro-feature-sdmspn` · **started at** `4a428df6` · **design**
[`DESIGN_Cluster_Load_Phase.md`](../../DESIGN_Cluster_Load_Phase.md)

---

## 1. What the batch was for

🔴 A stock `--mode all` loaded **zero entities** and said `ok:true`. Root cause: `ClusterSlave` gives a
load step to the **first** matching handler and returns, so the prerequisite loaders registered as
handlers were **competing** — on CGF the terrain loader cancelled the scenario loader; on SimHost the
knowledge-base loader cancelled the terrain loader, so terrain had **never** loaded. Nothing logged.

---

## 2. Design conformance — obligation ③

⭐ The design carries a module `graph TD`, a `sequenceDiagram` and two `classDiagram`s. **What was built
matches them**, with the deviations in §4 and folded into the design's own §6.

| design element | built as |
|---|---|
| `LoadPhasePayload` + the two new names | `EditLoadHandlerPayload` / `NodeTransitionPayloadDto` |
| `LoadPhaseChain` (one claimant, one ACK) | `Hrot.Core/Services/LoadPhase/LoadPhaseChain.cs` |
| `RoleLoadRequirements` + `UniversalParts` | same name, same split |
| `ILoadPartProvider` × 3 | `KnowledgeBaseLoadStep`, `TerrainLoadStep`, `ScenarioLoadStep` |
| `TerrainResidency` with an unwired `Unload` | `Hrot.Core/Services/TerrainResidency.cs` |
| ⭐ **§7.2's `ParkedTransition` + the plan/execute split** | `ClusterMaster._parked` · `ProcessTransitionStateIntent` → `ExecuteTransitionTrajectory` · `ProcessParkedTransition` |
| ⭐ **§7.2's saga edge** | `PrefetchAckTracker.OriginRequestId` + `PrefetchDistributionCompletedEvent` |

⚠ **One design element did NOT match reality and the design was corrected, not the code:** §7.2b's claim
that the editor's offline master never parks. See §4 ⑤.

---

## 3. Gates — the contract's eight rows

| # | gate | command | result |
|---|---|---|---|
| 1 | production build | `dotnet build Hrot.ClusterRunner.csproj --no-restore` | ✅ **0 errors, 0 warnings** *(re-run after `L8`)* |
| 2 | `--no-build` column | every suite below ran `--no-build` after one build of the **test** project | ✅ |
| 3 | golden movement | — | **none**: no golden touched |
| 4 | reds proved pre-existing | ran the three suspects at base `4a428df6` **in a clean worktree** | ✅ all three pre-existing |
| 5 | clean tree after runs | `git status --porcelain` | ✅ clean |
| 6 | quarantine counts | skipped 3 (SimHost) / 1 (Editor) | unchanged |
| 7 | ids allocated | **none** — the plan's `L1`–`L7` are design items, not tracker rows | — |
| 8 | the cross-cutting integration suite | ⭐ **the real cluster**, `--mode all` — see §5 and ⭐ §5a *(re-measured after `L8`)* | ✅ **acceptance met, twice** |

### Suites — ⭐ re-run after `L8`

| suite | result |
|---|---|
| `Hrot.Orchestrator.Tests` | ⭐ **181 / 181** *(was 176; **+5** — the `L8` rails, §7.7's table)* |
| `Hrot.SimHost.Tests` | **1000 passed / 4 failed / 3 skipped.** ⚠ The red SET is **unstable across runs of the same commit** — three runs gave 3, 2 and 4. 📐 Checked in a clean worktree at base `4a428df6`: `FullBranchPipelineTests.BranchedRecording_CapturesHistoricalStateAsKeyframe` and `NodeRolePersistenceRails.TheSaveHandlerSetIsStillComplete` are **red at base too** ⇒ pre-existing. ⚠ `EcsRecordReplayControllerTests.PrepareRecordingAsync_InstallsRecordingModule` **passed at base** and appeared only in the run taken while the acceptance cluster was still running — recorded as **contention-flaky, NOT proved pre-existing**. **Zero new.** |
| `Hrot.Editor.Tests` | **408 passed / 1 failed / 1 skipped** — `AiHotReloadCoordinatorTests.TwoReloadCycles_OldAlcIsCollected`, a GC-timing rail already recorded as flaky here and untouched by this batch |
| doc gates | `design-digest.py --check` PASS (60 docs) · `rulings-check.py` 35/35 · `mermaid-check.mjs` **6/6** |

⚠ **Row 2, stated honestly rather than rounded off:** two of those three are recording/replay timing rails
and one is a source-scanning rail whose expectation lists **save** handlers — none of which this batch
touched *(it deleted five **load** handlers)*. ⛔ The instability is itself a finding, not a result: a suite
whose red set changes run to run cannot certify anything, and it is reported as such rather than quoted as
a clean number.

⚠ **Row 8, honestly:** the `ClusterConformanceRails` cases that pinned this defect live in the **T3**
suite, which is the slow lane and was **not** re-run inside this batch. ⭐ The direct cluster run in §5
asserts the same property more strongly (it checks the outcome, not just the entity sets), but the T3
rails themselves have not been observed green since the fix. **Stated, not implied.**

---

## 4. Deviations from the plan — obligation ⑤

⭐ All four are folded into [`DESIGN_Cluster_Load_Phase.md` §6](../../DESIGN_Cluster_Load_Phase.md); in brief:

1. **`GenesisIntentComponents` had to move down to `Hrot.Core`.** 🔴 This is *why* the editor's readiness
   predicate was missing a condition: `Hrot.Presentation` cannot see `Hrot.Common`, so that copy could
   never have had it. An assembly wall, not carelessness — the design's §4.1c said "one copy lost a line",
   and the truer statement is now recorded.
2. **`IScenarioEntityExtractor` gained a remapper overload** (default implementation), so one step serves
   every host through the interface.
3. **The knowledge-base provider is supplied by the host** when a caller passes no database, rather than
   demanded of every caller — the host supplying a *HOW*, not the requirement being relaxed.
4. **`L6` was partial, deliberately — and `L8` CLOSED it in the same batch.** The names stopped racing
   first (`L1`). ⭐ `L8` then removed the ordering defect itself: `ClusterMaster` **parks** a scenario-carrying
   transition until the prefetch saga reports every node has acknowledged its files, and fans out only then.
   ⇒ **all three waits and `StagedArtifactWait` are deleted** — nothing in the load path waits on a clock.
   📄 [`DESIGN_Cluster_Load_Phase.md` §7](../../DESIGN_Cluster_Load_Phase.md) (`build-state: BUILT`, §7.7 the
   as-built). 🔒 User: *"it cannot depend on timeouts where can easily wait deterministically."*
5. 🔴 **`L8`'s own design was WRONG about the editor, and the build caught it.** §7.2b had claimed the
   editor's offline master *"stages nothing, so it never parks"* — measured false: it constructs and ticks
   an `AssetPrefetchProcessManager` (`EditorSubsystem.cs:2150`, `:2661`), registers `ReferencePrefetchHandler`
   on its own one-node slave (`:1422`) and heartbeats as node 0 (`:1035`). ⇒ it parks and unparks on exactly
   the same path, which is **why** it is safe. The design carries the correction with its `file:line`s; had
   the claim been true, every scenario open in the editor would have hung for the liveness bound.

---

## 5. Acceptance — measured, stock build, no probe

```
[LoadPhase] SimHost composed for roles [MuscleGround, Perception, NavigationSolver]: KnowledgeBase -> Terrain.
[LoadPhase] IG      composed for roles [Map2D]:                                      KnowledgeBase.
[LoadPhase] CGF     composed for roles [Brain]:                                      KnowledgeBase -> ScenarioEntities.

POST /scenario/load/live hill-attack-close → { entityCount: 8, sawWorldChange: true }
Scenario 8 · SimHost 8 · IG 9
```

| at `t ≈ 106` | force | HP | locomotion | ammo | distance to its OWN destination |
|---|---|---|---|---|---|
| 1006 · 1007 M1 Abrams | Hostile | **0/50** (both by t≈46) | Failure | 42 | — |
| 1001–1004 Tank Platoon | Friend | 50/50 | **Success** | 41 | **0.8 – 1.5 m** |

⇒ ✅ **both targets destroyed, all four attackers home** — the question this whole programme started from.

### 5a. ⭐⭐⭐ RE-MEASURED AFTER `L8` — same acceptance, and the wait is now visible in the log

📐 Stock `--mode all`, `hill-attack-close`, no probe, `2026-09-18`:

```
19:07:37.4153  ClusterMaster  L8: transition 86ba65c9… PARKED until the staging of 'hill-attack-close' is on every node.
19:07:37.6259  AssetPrefetch  L8: distribution of 'hill-attack-close' for request 86ba65c9… completed (success).
19:07:37.6539  ClusterMaster  L8: the staging of 'hill-attack-close' is on every node — transition 86ba65c9… resumes.
```

⇒ **parked for 210 ms, then the ordinary fan-out.** `clusterState: OperatingLive`, `entityCount: 8`,
network ids **1000–1007** (so the `HN-037` reset still fires, on the execute side).

| at `t ≈ 111` | HP | navigation | distance to its OWN destination |
|---|---|---|---|
| 1006 · 1007 (hostile) | **0/50** | `InProgress`, never arrived | — |
| 1001–1004 (friendly platoon) | 3000/3000 | **`Arrived`**, `HasArrived: 1` | **0.67 – 1.51 m** |

⇒ ✅ **acceptance unchanged by `L8`** — which is the point: the fan-out is the same pass, run later.

---

## 6. ⚠ What a reader should NOT conclude

| | |
|---|---|
| ✅ "the staging race is fixed" | ⭐ **now genuinely yes**, after `L8` — the names moved to the message (`L1`) and the transition itself is ordered after every node's acknowledgement. ⛔ An earlier version of this row said *"only the name half"*; that was true of `L1`–`L7` and is **superseded** |
| ⛔ "the `L8` rails prove a node gets its files" | they exercise `ClusterMaster` + the saga over a real bus, **not a real node**. The node-side proof is the measured cluster run in §5a |
| ⛔ "the T3 conformance rails are green" | they were not re-run — §3 row 8 |
| ⛔ "a host can no longer misconfigure its load" | it can; it now **fails loudly at composition** instead of silently at runtime. That is the whole change |
| ⛔ "terrain is loaded everywhere" | it is loaded where a role **reads** it — `MuscleGround` and `NavigationSolver` only, per the accepted ruling |
