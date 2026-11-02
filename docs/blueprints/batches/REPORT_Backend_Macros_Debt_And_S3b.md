<!--STATUS
state: LIVE
updated: 2026-11-02
current-answer: §1 Part A (built, live) · §2 gates · §3 findings · §4 Part B (BLOCKED on behaviors' rules S2 ⑧)
stale-below: nothing
related-designs:
  - docs/DESIGN_Scenario_Macros.md — OWNS the design; §15.4 carries Part A's as-built (and §15.2's open rows are marked SUPERSEDED there)
  - docs/blueprints/batches/HANDOFF_Coordinator_To_Backend_Macros_Debt_And_S3b.md — the dispatch (scope frozen at 0a4ab2f42)
  - docs/blueprints/batches/REPORT_Backend_Macros_S3a.md — the batch whose §3 "open" row this one closes
-->

# Report — backend → coordinator: Macros debt (Part A) and S3b (Part B)

Dispatch: [`HANDOFF_Coordinator_To_Backend_Macros_Debt_And_S3b.md`](HANDOFF_Coordinator_To_Backend_Macros_Debt_And_S3b.md), scope frozen at
`0a4ab2f42`; started at `f4ca03f54` (merged `coordinator`). Branch `backend`. The as-built is in
[`DESIGN_Scenario_Macros.md`](../../DESIGN_Scenario_Macros.md) §15.4; this report points there.

## 1. Part A — the S3a debt

| # | what | commit | design |
|---|---|---|---|
| A1 SR-65/188 | `RefKey` registered on every world ⇒ a runtime `@Key` arg resolves against the LIVE world; a duplicate key ⇒ the first, with a warning | `f29e74292` | §15.4 |
| A2 SR-190 | the solver reserves every slot it handed out for 600 frames ⇒ two expansions in one frame, or the next, do not overlap | `f29e74292` | §15.4 |
| A3 SR-157/180 | moving a relocatable instance re-runs placement (`PlacementSlot.Self` names the moving body); a failed re-placement is loud and moves nothing. ⭐ **found live**: the moved men are REQUESTED from their owner (split authority) | `f29e74292` + `0f8dd452d` | §15.4 |
| A4 SR-150 | a `Done` instance with no baseline (after a restore) gets `MacroExpandedWith` rebuilt ⇒ a later edit reconciles | `f29e74292` | §15.4 |
| A5 SR-41 | an EDIT load takes scenario args (`POST /scenario/load/edit {args}`, `--param` with `--load edit`), through the same `ScenarioParams` | `f29e74292` | §15.4 |
| A6 | a child's placement layer + radius from its TKB template's mobility (DIS type as fallback); rail: a wheeled child ⇒ vehicle layer, radius 3.5 | `f29e74292` | §15.4 |
| A7 | `EveryEcsHostComposesTheLoadPhaseChainRails` ×2 green — red because CE-1446 moved CGF's/the editor's node build into the bootstrappers. ⭐ ONE delegation follower, `CompositionRootSource.ReadEffectiveRoot`, now used by the four rails that read the stale files (load-phase, cluster handlers, translator set, live plan) | `f29e74292` | §15.4 |

Ids allocated: none (no new component, descriptor or tracker row; `PlacementSlot.Self` / `DdsPlacementSlot.Self` are fields on existing types).

## 2. Gates

| gate | command | result | vs base |
|---|---|---|---|
| SimHost (touched classes + every rail over the changed seams) | `dotnet test Hrot/Subsystems/Hrot.SimHost.Tests --no-build --filter "MacroExpander\|ScenarioLoadStep\|EveryEcsHost\|MapPresentationParity\|LiveSimulationPlan\|OneClusterHandlerSet\|NodeRolePersistence\|EntityGenesisHazard\|SquadManeuver\|TheDebugProviders\|StrictNetworkSeparation\|EntityCreationPack\|LoadPhase"` | ✅ 459/0 (then `MacroExpanderTests` 15/0 after `0f8dd452d`) | the 2 A7 reds and 5 stale composition rails are green |
| toolkits | `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests --no-build --filter "Placement\|ScenarioLoadSequence\|ScenarioParams\|MacroValidator\|ParamBinder\|Orchestration\|MacroTypes"` | ✅ 180/0 | — |
| NED (whole) | `dotnet test Hrot/Network/Hrot.Network.NED.Tests --no-build` | 176/1 | the 1 red `AllTopicStructsHaveDdsTopicAttribute` is red at base; `coordinator`'s `3ee71c998` fixes it (not yet merged here) |
| editor + runner build | `dotnet build Hrot/Subsystems/Hrot.Editor` · `Hrot/Runner/Hrot.ClusterRunner` | ✅ 0 errors | — |
| `DistributedScenarioLoadTests` alone | `dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests --no-build --filter DistributedScenarioLoadTests` | ✅ 1/0 — §2b | — |
| live `--mode all` | §2a | ✅ | — |
| docs | `tracker-counts.py --check` · `rulings-check.py` · `design-digest.py --check` · `mermaid-check.mjs` | OK · 496/496 · OK · 15/15 | — |

Red-proofs (each a compiling mutation, restored): A2 the solver ignores its reservations ⇒ `A2_TwoInstances…` red · A3 a move applied without
asking ⇒ both A3 rails red · A3-live the transform written locally regardless of ownership ⇒ `A3_UnderSplitAuthority…` red · A4 the rebuild
skipped ⇒ `A4_AfterARestore…` red · A6 the template ignored ⇒ `A6_AVehicleChild…` red.
⚠ Two of the first mutations proved nothing and were redone: A2's kept the reservation through the other half of an `||`, A4's did not compile.

### 2a. The live run (`--mode all`, `scenarios/test-macro-two-teams --load live --param side=Friend`)

| step | observed |
|---|---|
| load | 2 instances `Done`, 8 men, each `MacroOrigin` its own instance, per-instance sides `Friend` / `Hostile` |
| move Team East (60,100) → (100,60) | 🔴 first binary: "reconciled: 4 kept" and **no man moved** (§3) · ✅ after `0f8dd452d`: leader at (100,60), riflemen around it |
| checkpoint `macro-b` → restore | every node resumed; both instances `Done` **with a baseline** (A4) |
| move Team Turned (60,140) → (140,140) after the restore | reconciled, 4 kept, its men followed to (140,140) · no error in the log |

### 2b. `DistributedScenarioLoadTests`

✅ `DistributedLoad_TranslatesNetworkIds_AndSpawnsEntitiesWithRemappedMissionPlan` passed (a real 3-node boot + load; the summary's `< 1 ms` is the runner's known misreport).

## 3. Findings

| | |
|---|---|
| 🔴 found LIVE, fixed (`0f8dd452d`) | a re-placed man's body is the SIMULATION node's (split authority), so the instance node's `SetComponent` wrote a replica the owner overwrote. Now the engine's one "ask the owner" route (`EntityAttributeChangeRequests.TryPublishJsonPatch`, the one `POST /entities/{id}/attribute` uses). ⚠ The unit rails run ONE world and could not see it — the new rail sets the authority bit off by hand |
| 🔴 found by a rail, fixed (`f29e74292`) | the reconcile's early return was `ReferenceEquals(record) \|\| SameAnchor` ⇒ a move with the record unchanged never reconciled |
| ⚠ found, not fixed (ui's surface) | `POST /entities/{id}/component` on `MacroInstance` with `{"Args":{…}}` answers `ok` and changes nothing — the StructEdit patch does not reach the args dictionary. ⇒ the live A4 proof used a MOVE after the restore, not an args edit (the args-edit path is the A4 unit rail) |
| ⚠ A2 scope | reservations live in one solver instance; two solvers answering one cluster would not share them (one answers today) |

## 4. Part B — S3b: **BLOCKED**, not started

The handoff gates Part B on behaviors' rules S2 (the trigger runtime) being on `behaviors`. At `origin/behaviors@ecedba770` S2 is **partly**
built: ⑤ `2487aa4c4`, ⑥ `593138a39`, ⑦ the Trigger behaviour `4302e2d97` — ⑧ (the verbs, END, PlayerMessage) is still in build
(`RESUME_Behaviors_Command_And_Platoon.md` §R0). `InsertMacro` (B1) is one of those verbs' kind, so B1–B3 wait for ⑧.
