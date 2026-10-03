<!--STATUS
state: LIVE
updated: 2026-10-01
current-answer: §1 (outcome per red) · §3 (the two decisions) · §5 (gates)
stale-below: none
related-designs:
  - docs/blueprints/batches/HANDOFF_ClusterRunner_Integration_Reds.md — the frame this answers
  - docs/DESIGN_Subsystem_Composition_Unification.md §4.1ad — the hand-wired-fixture family (extended here)
  - docs/DESIGN_Role_Affinity_Ownership.md §4.2 — CE-500, Path B publication
  - docs/blueprints/DESIGN_Time_Architecture.md §12 — CE-497
  - docs/designs/navig-2/Navigation_Design_v2_0.md §7.1 — CE-498
  - docs/DESIGN_Cluster_Load_Phase.md §6 — CE-499
-->

# REPORT — the 32 `Hrot.ClusterRunner.Integration.Tests` reds *(backend lane)*

**Answers** [`HANDOFF_ClusterRunner_Integration_Reds.md`](HANDOFF_ClusterRunner_Integration_Reds.md). **User, `2026-10-01`:**
*"those load bearing ones are worth restoring, others can be filtered out or removed completely"* → verdict table approved:
*"approved, go ahead with all steps"* (S1 remove · S2 domain · S3 shared causes · S4 product defects · S5 hand back).

## 1. Outcome per red

| class | was | now | cause → fix |
|---|---|---|---|
| `SimTimeSync` ×2 · `SplitAuthoritySpawn` ×3 · `TimeControl` ×2 | OOM | ✅ green alone *(6/6 · 3/3 · 9/9)* | environmental: run order in one ~10-min process — not a defect |
| `MissionToMovementChainProbe` · `NetworkDemoPatrolAndEngage` ×2 | red | 🗑 **removed (S1)** | a probe and a duplicate of covered flows — not load-bearing |
| `DistributedScenarioSave` | red | ✅ | domain 271 ⇒ port ≥ 65536 (limit ≈232) → `81` |
| `DistributedScenarioLoad` | state 0 | ✅ | scenario staged where nothing prefetches → shared `NasScenarioStaging`; offline ids moved off the live range, which exposed **`CE-499`** *(product)* |
| `CgfRecording` | no file | ✅ | CGF records under its node staging root — test path |
| `ClusterOpE2e.RecordAndReplaySeek` | x = 0 | ✅ 4/4 | **`CE-497`** *(product: seek-to-end overflowed to frame 0)* + the fixture never pressed play (`CE-101` boot-paused) |
| `SpawnMovingVehicle` ×2 · `CgfSubsystemHeadless` wander + ghost | no motion | ✅ | **`CE-498`** *(product: an intent written while Constructing was lost)* |
| `CgfSubsystemHeadless.MoveToLocationMission` | no motion | 🔴 **`CE-500` — decision, §3.1** | brain runs, cannot publish (Path B) |
| `GhostPromotion` | not promoted | ✅ | test never delivered `EntityInfo` (`CE-265` gate) — **`CE-157` resolved** |
| `MapPlacement.DirectCreationTool` | tool inactive | 🟡 activation ✅, next stage 🔴 **`CE-501`** | **`CE-159`** *(product: gizmo manager passed before assigned)* |
| `SelectionAndMission` | not selected | ✅ 2/2 | test hook stale since `UXI-11 S-6` — now selects as a real click |
| `SensorMechanism` | no tracks | 🔴 **`CE-158`** progressed | stimulus was an end state; now a sighting stream — still red one hop earlier (§4) |
| `UrbanCombat` | state 0 | 🔴 **`CE-502`** | three fixture defects fixed (staging · `NetworkIdentity` · latches on the brain world); latches still never fire |
| `FeatureSwitchRcu` ×4 | throws | 🔴 **`CE-154` — decision, §3.2** | unchanged, deliberately |
| `BlueprintScenario` ×3 · `Hsm` E2 | red | ↩ **handed back (S5)** | behaviours lane — §4 |

⭐ **Five product defects fixed** (`CE-497` · `CE-498` · `CE-499` · `CE-159` · the `ClusterSlave` prepare-fault log now carries the
stack). ⭐ **Five fixtures** were a test-only composition a product change never reached — folded into
[`DESIGN_Subsystem_Composition_Unification.md`](../../DESIGN_Subsystem_Composition_Unification.md) §4.1ad as one family.

## 2. Red-proofs *(inverse edit ⇒ the rail reddens)*

| rail | inverse edit | result |
|---|---|---|
| `NavigationIntentBridgeSystemTests.Intent_WrittenWhileConstructing_…` | restore the Active-only query | 1 fail / 17 pass → restored 18/18 |
| `ReferenceHandlerTests.ReplaySeek_ToTheEnd_DoesNotWrapNegative` ×2 | unsaturated sum | 2 fail / 4 pass → restored 6/6 |
| `StagingEntityExtractorTests.Extract_ThroughTheInterface_…` | drop the explicit member | 1 fail / 21 pass → restored 22/22 |

## 3. The two decisions *(not patched — engine contracts)*

### 3.1 `CE-500` — which authority face gates a brain's egress on Path B

| the lean rests on | code — how it IS | design — how it was MEANT |
|---|---|---|
| CGF's brain RUNS for a SimHost-created entity | ✅ measured: `NavigationIntent` written, real target | ✅ Role-Affinity §4.2 promote leg |
| egress reads `DescriptorOwnership`/`NetworkAuthority`, never the mask | ✅ `NavigationIntentEgressTranslator` → `AuthorityExtensions.cs:16-56` | ✅ §3.6 says so |
| on Path B the ghost has no entity authority | ✅ measured `HasAuthority=false, PrimaryOwnerId=-1` | ⛔ §4.2 never says what publishes — now noted there |
| a mask-gated cognitive egress already exists | ✅ `TacticalIntentEgressTranslator.cs:72` (`HasAuthority<BehaviorState>`) | ⛔ searched `docs/`, no ruling either way |

**Lean: gate the brain-owned cognitive egress translators on the MASK of their source component** (`HasAuthority<NavigationIntent>`),
as `TacticalIntentEgressTranslator` already does. Path A is unchanged (the creator's mask holds it); a Muscle never publishes it (it is in
the Muscle's READ set, not its owned set). Blast radius: the brain-owned egress translators only (`NavigationIntent` first).
**Would change it:** a ruling that publication must follow the descriptor face everywhere.
Rejected: *send `OwnershipUpdate` on promotion* — a second ownership mechanism, which §0a rejects · *patch the test to spawn on CGF* — hides a real Path B gap.

### 3.2 `CE-154` — what "Go External" ejects

| the lean rests on | code | design |
|---|---|---|
| the default arm never `RegisterModule`s the packs, yet lists them in `logicPacks` | ✅ the CE-154 row's measurements | ✅ `cgf-scn-3` §3.1 chose group wiring **and** kept the list — the contradiction is in the design |
| External installs nothing in production | ✅ `translatorPacks` has no production supplier | ✅ `Architect_Question_63` §8.2 |
| the pack simulation systems already run inside one module | ✅ `EditorSimulationModule(cgf…, simHost…)` | — |

**Lean: the switch ejects what the host actually composed** — the module wrapping the packs' simulation systems — instead of the packs it never
installed; no phase move for Internal mode. ⚠ External's translator half stays unbuilt (`Q63` §8.2) — a separate item.
**Would change it:** a ruling that External mode is retired on the editor. Rejected: *`RegisterModule` the packs* — moves them into DISPATCH,
reordering the whole editor · *toggle the `Togglable*Group`s* — a replay facility, and would kill ingress.

## 4. Handed back / still open

| item | measured | next measurement |
|---|---|---|
| `CE-158` sensor | sightings published on SimHost's world bus every frame; `SensorContactList.Count` stays 0, **0** samples on the wire | which world/bus `SensorTrackDebounceSystem` reads (perception runs in a module with a scoped bus) |
| `CE-501` placement | tool arms; no `CreateEntityRequest` on DDS | is the test's DDS expectation stale after host-f, or the routing wrong |
| `CE-502` urban | entities on both nodes; `ambush` never sets in 600 frames on CGF | does the insurgent's brain run at all |
| ↩ `BlueprintScenario` ×3 | alone: `DemoScenario_Loads_…` and `Test4_Resilience_…` — `Assert.True` · `ParamPersistence_…` — *"Entity(1, v1) missing `BlueprintBlackboard1024`"* | behaviours lane — ⚠ the handoff's *"`BlueprintBlackboard1024` retired"* is wrong: `PLAN_Occurrence_Storage_Build.md` E5 keeps `BlueprintBlackboard*` as THE store; only legacy `Blackboard1024` went |
| ↩ `Hsm` E2 `MobilityLostInterrupt_ThenBehaviorFinished` | alone: `Assert.True` at the finish step | behaviours lane — check against the `CE-482`/`CE-449` finish change |

## 5. Gates *(merged tree `4c7543b35` + this report)*

| # | gate *(verbatim, `--no-build` after one build per project)* | result | vs base |
|---|---|---|---|
| 1 | `dotnet build` ×4 — `Hrot.ClusterRunner.Integration.Tests` · `Fdp.Toolkits.Tests` · `Hrot.SimHost.Tests` · `Hrot.IG.Tests` | 0 errors each | — |
| 2 | `dotnet test Fdp.Toolkits.Tests --filter "…Fdp.Toolkit.Navigation\|…Orchestration\|…Replication"` | **446 / 0** | ⭐ incl. the 3 new rails (§2) |
| 3 | `dotnet test Hrot.SimHost.Tests` | 1033 / **3** / 3 skip | ✅ all 3 on the known pre-existing list (`MapPresentationParityRails` Stride path · `NodeRolePersistenceRails.TheSaveHandlerSetIsStillComplete` · `EcsRecordReplayControllerTests.PrepareRecordingAsync_…`); ⭐ `FullBranchPipelineTests` was the 4th and is now green |
| 4 | `dotnet test Hrot.IG.Tests` | 431 / **7** / 1 skip | ✅ 6 red at base **`b6d5dd602`** (worktree, `EntityInfoTranslatorTests` ×4 · `EntityDamageTranslatorTests` ×2); the 7th (`EntityMasterTranslatorTests.…SetsOwnerId`, DDS) is **9/9 alone** |
| 5 | ⭐ `dotnet test Hrot.ClusterRunner.Integration.Tests` *(full, unfiltered — the integration suite for this batch)* | **287 / 21 / 3 skip** · 10 m 21 s | was **195 / 32** of 230 filtered at `d3108bef3` |
| 5a | the 21, classified | — | 🔴 open, filed: `CE-158` · `CE-500` · `CE-501` · `CE-502` · `CE-154` ×4 = **8** · ↩ behaviours: `BlueprintScenario` ×6 in-run (**3 alone**) · `Hsm` E2 = **7** · ⚠ environmental — **green alone**: `TimeControl` ×3 (9/9) · `HeadlessGizmoStreaming` ×2 (2/2) · `MissionControl` (1/1) = **6** |
| 6 | `python3 scripts/tracker-counts.py --check` | OK | ids allocated: **`CE-497` · `CE-498` · `CE-499` · `CE-500` · `CE-501` · `CE-502`** *(my first `CE-496` collided with the behaviours lane's and was renumbered `CE-499` before push)* |
| 7 | `rulings-check.py` · `design-digest.py --check` | 45/45 · OK | — |
| 8 | working tree after every run | clean | — |

⚠ **The full integration run still kills ~6 classes late in one ~10-min process** (memory/DDS) — they pass alone. That is the handoff's group ① and it is unchanged by this batch.
