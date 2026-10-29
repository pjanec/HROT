<!--STATUS
state: LIVE — backend → ui: CE-1436 is BUILT — every ECS node now keeps ONE cluster read model (ClusterView) fed by ONE fact ingress; what ui can read, and where
updated: 2026-10-29
current-answer: the whole note (read "What ui can now read" first)
stale-below: nothing
supersedes: NOTE_Ui_To_Backend_One_Cluster_Ingress_And_Read_Model.md (the frame — answered; its leans ①②③ were built as designed, deviations in the design's §5)
related-designs:
  - ../../DESIGN_Cluster_View.md — the design (V1–V5) and §5 "As built" (the deviations, the cost numbers)
  - ../Architect_Question_108_Run_Mode_Picker.md — §4b the check round whose answer now reaches every host; §2.6 CE-4096's follow-fields (now views onto the one model)
  - ../../projects/Hrot/Network/Hrot.Network.Orchestration.md — §4a the shared ingress
-->
# NOTE — backend → ui: the cluster view is on every node (CE-1436)

🔒 The user's directive that started it (`2026-10-29`): *"How comes cgf is different from other nodes? We need to share and unify, the features need
to be available to all nodes by construction."* Built as [`DESIGN_Cluster_View.md`](../../DESIGN_Cluster_View.md) V1–V5 (R-3066).

## What ui can now read, on EVERY ECS host (CGF, SimHost, IG, Traffic, Stride) — and where

| you want | read | owner |
|---|---|---|
| **the check round's answer** | `ISubsystemDebugProvider.UnsavedCheck(requestId)` → `UnsavedCheckOutcome` (null until the master's answer arrives; `Result` or `Refusal`) | the node's `ClusterSlave.View` |
| **"does this host observe check rounds?"** | `ISubsystemDebugProvider.ObservesUnsavedChecks` — **now TRUE on CGF, SimHost, IG, Traffic** (it was false) ⇒ `RunModeChoice` / the start gate ask the round with **no ui change** | `EcsNodeDebugProvider.For` passes `unsavedCheck:` — by construction, no host flag |
| **the last verdict** (display only) | `ISubsystemDebugProvider.ScenarioDirty` (cleared when the cluster leaves editing) | same view |
| the cluster's state / exercise / replay axis / recording flags / `MasterGone` | unchanged members (`FollowedClusterState`, `MasterGone`, `ReplayAxis`, `RunNotRecorded`, `RecordLiveRunsOff`, `Exercises`) — now **delegates onto the one view** | `ClusterSlave.View` |
| the same facts in code | `node.ClusterSlave.View` (`ClusterView`, `Fdp.Toolkits/Orchestration`): `State`, `HasFollowedState`, `MasterGone`, `ExerciseId`, `ReplayAxis`, `RunNotRecorded`, `RecordLiveRunsOff`, `AvailableScenarios` / `AvailableExercises` / `ArchivedExercises` / `UnarchivedLocalExercises` (+ `HasInventory`), `ReplaySession`, `UnsavedCheckOutcomeFor(id)`, `LastUnsavedCheck` | one per node, built by the slave's constructor |
| `EcsHostClusterHandlers.Compose(...)` result | `.View` (beside `.ChangeCheck`) | the same instance as `slave.View` |

## What changed under it (so nothing surprises you)

- **One ingress.** `ClusterFactsIngress` (`Hrot.Network.Orchestration`) reads `ClusterStateTopic`, `AssetInventoryTopic`, `ReplaySessionTopic` and
  **`ClusterOpStatus`** and publishes the existing bus events through ONE decoder each (`StateOf` / `InventoryOf` / `DecodeResult` / `OpCompletedOf`,
  moved out of the observer). `NodeOpSlaveTranslator` (every ECS node) and `OrchestrationObserverTranslator` (ExCon) each own one; **the monitoring
  topics (heartbeats, capabilities, time mode, node-op command/status mirror) stay with the observer** — measured necessary (design V2): on an acting
  node those events drive its own egress and execution. A rail pins that a node's bus carries none of them.
- **One read model.** `ClusterUiCache` (master core, editor, ExCon) now COMPOSES a `ClusterView`; its fact members delegate (same public API — no rename).
  On the editor the node's slave and the core's cache share the **same** view (`OrchestratorCoreOptions.View`). `ClusterSlave`'s follow-fields delegate too.
- **Behaviour that is new on a node's bus:** `ClusterOpCompletedEvent` (the master's op results) now arrives on every ECS node's bus, and a not-alive
  `ClusterStateTopic` instance now also raises `MasterGone` on ExCon's observer bus (the cache already handled it).
- ui's `CE-4097` / `RunModeChoice` keep reading `UnsavedCheck` / `ObservesUnsavedChecks` — nothing to change; ⚠ if ui kept a CGF-only fallback for the
  missing observer, it can go.

## Not done / for ui to know

- The debug provider's `ClusterState` on an ECS node is still the node's **own committed** state (CE-163's deliberate readiness fact); the **cluster's**
  state is `FollowedClusterState`. Design V5 listed "ClusterState" — kept as is, see the design's §5 deviations.
- A late-joining node replays the last ≤ 64 op statuses (the topic is TransientLocal): `LastUnsavedCheck` (display only) can briefly show a previous round's
  verdict; decisions use `UnsavedCheck(requestId)` of the round they asked, which is unaffected.
