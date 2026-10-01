<!--STATUS
state: LIVE
updated: 2026-10-01
current-answer: the whole file — a FRAME handoff (coordinator frames, the session designs the detail)
stale-below: none
related-designs:
  - docs/DESIGN_Subsystem_Composition_Unification.md §4.1ad — CE-221/CE-493: a hand-wired harness is a host the capability plan cannot reach
  - docs/blueprints/DESIGN_Behaviour_Fault_And_Teardown.md — the behaviours lane's current work (not touched by this batch)
-->

# HANDOFF — the 32 remaining `Hrot.ClusterRunner.Integration.Tests` reds → **backend lane**

**From:** behaviours lane (`behaviors`). **Dispatched on top of `d3108bef3`** (the commit adding this file) — your scope is frozen there.
**User, `2026-10-01`:** *"good handoff to backend lane … they could take also your parts so that you can continue on something else."*
⇒ **all 32 are yours**, including the five that look behaviours-shaped (group ④).

## Context — what is already done

⚠ **Renumbered:** the harness fix landed as `CE-492` in `d3108bef3`, and is **`CE-493`** from the next commit on `behaviors` — your lane's `CE-492` (EQS config multi-writer ordering) keeps its number. ⭐ **Merge `behaviors` before you start**: your branch does not yet have the harness fix, which is why your EQS folder still showed 31 reds.

[`CE-493`](../Blueprint_Issues_Tracker.md) (`d3108bef3`) fixed the **31 EQS reds**: two harness gaps left by
`CE-221` (pack → capability hoist) and `0cda0caf9` (SimHost stops registering the brain set). `--filter Eqs|HideInCover`
is **84/0**. ⭐ **The pattern to suspect first below is the same one: a test-only world/harness that composition
refactors left behind** — production hosts run (live `--mode all` hill attack is green, `2026-10-01`).

## The reds — one cloud run, `--filter "FullyQualifiedName!~Eqs&FullyQualifiedName!~HideInCover"`

230 total · **195 pass · 32 fail · 3 skip** · 10 m 41 s · at `d3108bef3`.
⚠ **Only group ③'s three classes were base-compared** (identical 8 reds at `d3108bef3^` — not caused by `CE-493`).
Everything else is **un-triaged**: first job is to separate *environment* (cloud VM memory, DDS) from *real*.

| # | group | tests | message (trimmed) | lean |
|---|---|---|---|---|
| ① | **OOM** | `SimTimeSyncIntegrationTests` ×2 · `SplitAuthoritySpawnTests` ×3 · `TimeControlIntegrationTests` ×2 | `System.OutOfMemoryException` | ⚠ likely run-order/environment (the suite ran ~10 min in one process). Re-run each class **in isolation** before reading anything into it. ⭐ `SplitAuthoritySpawnTests` and `SimTimeSync…` are the integration suites for split-authority and cluster time — if they are red alone, that is serious |
| ② | **cluster never comes up / DDS** | `DistributedScenarioSaveTests` (*Failed to create participant*) · `DistributedScenarioLoadTests`, `UrbanCombatFileLifecycleTests` (*OperatingLive not reached, state 0*) · `CgfRecordingIntegrationTests` (*recording not found*) · `ClusterOpE2eScriptTests.RecordAndReplaySeek` | DDS participant / lifecycle | ⚠ may cascade from ①'s memory pressure; isolate first |
| ③ | **harness composition** *(base-identical)* | `FeatureSwitchRcuIntegrationTests` ×4 | *Module 'SimHostCoreLogicPack' is not currently installed* | ⭐ the RCU switch ejects/restores a pack by name — check what `CE-221`/composition work renamed or re-homed. Same family as `CE-493` |
| ④ | **behaviours-shaped** | `BlueprintScenarioIntegrationTests` ×3 (one: *missing `BlueprintBlackboard1024`*) · `MissionToMovementChainProbe` (*`BehaviorState` not registered*) · `HsmBehaviorIntegrationTests.E2_FullFrame_MobilityLostInterrupt_ThenBehaviorFinished` | stale expectations | ⭐ `BlueprintBlackboard1024` was **retired by P4** (blackboards → slot-resident behaviour block); the test expects a deleted component ⇒ re-home the claim to the slot-resident block. `BehaviorState` ⇒ the probe's world misses `CognitiveComponentRegistry` (same as `CE-493` ②). Ask the behaviours lane if a behaviour rule is unclear — do not guess |
| ⑤ | **movement / replication flows** | `SpawnMovingVehicleIntegrationTests` ×2 · `CgfSubsystemHeadlessTests` ×3 · `NetworkDemoPatrolAndEngageTests` ×2 · `GhostPromotionTests` · `SensorMechanismIntegrationTests` · `MapPlacementIntegrationTests` · `SelectionAndMissionIntegrationTests` | *entity did not move* / *not promoted* / *not updated* | ⚠ could be ① cascading, could be real. These are the end-to-end flows — **after** isolating ①/②, any that stay red are the most valuable finding of the batch |

## The frame

| | |
|---|---|
| **goal** | every red is either **fixed**, or **proved pre-existing/environmental** with a base sha, or **filed** as a plain-numbered `CE-` row with its root cause |
| **step 1** | isolate: run each class alone; base-compare the survivors (`git worktree` at `d3108bef3^` or older) |
| **design obligation** | a root cause that teaches something goes into its **owning design** (CLAUDE.md *"an investigation that learns something must update the owning design"*) — e.g. extend §4.1ad's hand-wired-harness note if ③/④ are the same family |
| **fences** | ⛔ no production edits in `FDP/Toolkits/Fdp.Toolkits/Behavior/**`, `Hrot.AI.Behaviors/**`, the blueprint compiler — behaviours lane is active there. Test-file edits in this project are yours |
| **acceptance** | the §Gates contract: one row per class, counts, base sha for every red you call pre-existing, clean tree, `tracker-counts.py --check`, ids allocated |
