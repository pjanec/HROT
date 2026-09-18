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

# REPORT — the cluster LOAD PHASE batch (`L1`–`L7`)

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

---

## 3. Gates — the contract's eight rows

| # | gate | command | result |
|---|---|---|---|
| 1 | production build | `dotnet build Hrot.ClusterRunner.csproj --no-restore` | ✅ **0 errors, 0 warnings** |
| 2 | `--no-build` column | every suite below ran `--no-build` after one build of the **test** project | ✅ |
| 3 | golden movement | — | **none**: no golden touched |
| 4 | reds proved pre-existing | ran the three suspects at base `4a428df6` **in a clean worktree** | ✅ all three pre-existing |
| 5 | clean tree after runs | `git status --porcelain` | ✅ clean |
| 6 | quarantine counts | skipped 3 (SimHost) / 1 (Editor) | unchanged |
| 7 | ids allocated | **none** — the plan's `L1`–`L7` are design items, not tracker rows | — |
| 8 | the cross-cutting integration suite | ⭐ **the real cluster**, `--mode all` — see §5 | ✅ **acceptance met** |

### Suites

| suite | result |
|---|---|
| `Hrot.SimHost.Tests` | **1006 passed / 3 failed / 3 skipped** — the 3 are the pre-existing ones from row 4. **Zero new.** |
| `Hrot.Editor.Tests` | the re-pointed rail green. ⚠ two unrelated reds observed intermittently (`EditorMapPickAdapter`, `AiHotReloadCoordinator`), absent from the first run of the same commit — **flaky, not caused here** |
| `Hrot.Orchestrator.Tests` | **176 / 176** |
| doc gates | `design-digest.py --check` PASS (60 docs) · `rulings-check.py` 35/35 · `mermaid-check.mjs` 4/4 |

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
4. **`L6` is partial, deliberately.** The names no longer race; the two loaders gained the bounded wait the
   scenario step always had. ⛔ Ordering the content step after the staging acknowledgements was **not**
   done — it restructures the trajectory every transition shares, and doing it beside a node-side refactor
   would make a failure impossible to attribute.

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

---

## 6. ⚠ What a reader should NOT conclude

| | |
|---|---|
| ⛔ "the staging race is fixed" | only the **name** half. §4 ④ names what is left |
| ⛔ "the T3 conformance rails are green" | they were not re-run — §3 row 8 |
| ⛔ "a host can no longer misconfigure its load" | it can; it now **fails loudly at composition** instead of silently at runtime. That is the whole change |
| ⛔ "terrain is loaded everywhere" | it is loaded where a role **reads** it — `MuscleGround` and `NavigationSolver` only, per the accepted ruling |
