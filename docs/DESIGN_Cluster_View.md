<!--STATUS
state: LIVE — backend design for ui's frame CE-1436 (one cluster ingress, one cluster read model, on every node)
updated: 2026-10-29
build-state: BUILT 2026-10-29 (backend) — V1–V5 APPROVED by the user 2026-10-29 (R-3066); §5 is the as-built (three deviations, all named there)
current-answer: §5 AS BUILT (what exists and where) · §2 the picture · §3 decisions V1–V5 · §4 build slices (built, in order)
stale-below: §4 is the plan, now done — quote §5 for what exists; §3 V4's "built in Compose" is SUPERSEDED by §5 deviation D1 (the slave constructs it)
known-rot: none
known-conflict: none
related-designs:
  - blueprints/batches/NOTE_Ui_To_Backend_One_Cluster_Ingress_And_Read_Model.md — the FRAME (ui, CE-1436): goal, leans, fences, acceptance
  - blueprints/batches/NOTE_Backend_To_Ui_ClusterView_Built.md — the hand-back: what ui can now read on every host
  - blueprints/Architect_Question_108_Run_Mode_Picker.md — §4b the check round whose answer must reach every host; §2.6 CE-4096's ClusterSlave follow-fields; §2.8a deviation 12 OWNS the ui half: the time facade (`ClusterTimeTransportAdapter`) and the editor's `EditorScenarioSession` READ the node's `ClusterView` and fold none of its facts
  - projects/Hrot/Network/Hrot.Network.Orchestration.md — §5 the observer path (ExCon's passive reader) this design folds into one ingress
  - blueprints/Architect_Question_109_Retire_ExCon.md — ExCon's retirement (proposed 2026-10-29) would remove V2's difference and the observer translator
  - projects/Hrot/Subsystems/Hrot.ExCon.md — the observer bus + ClusterUiCache on ExCon (the monitoring half stays there, V2)
-->

# DESIGN — one cluster view on every node (CE-1436)

🔒 ui frame, quoting the user (`2026-10-29`): *"How comes cgf is different from other nodes? We need to share and unify, the features need to be
available to all nodes by construction."*

## 1. INVENTORY (`2026-10-29`; graph `search_graph` on `.*(ClusterUiCache|ClusterSlave|ObserverTranslator|SlaveTranslator|SlaveOrchestration|ClusterView|ClusterReadModel|EcsNodeDebugProvider|NodeOrchestration).*` Class → 13 rows; grep for every reader of the events below)

| what | where | reads / publishes |
|---|---|---|
| `NodeOpSlaveTranslator` — every ECS node (CGF, SimHost, IG, Traffic) | `Hrot.Network.Orchestration/NodeOpSlaveTranslator.cs:28` | reads `NodeOpCommand` (filtered to its own node, `:119`), `ClusterStateTopic`, `AssetInventoryTopic`, `ReplaySessionTopic`; ⚠ WRITES its own heartbeat / capabilities / `NodeOpStatus` FROM bus events (`NodeHeartbeatEvent`, `NodeCapabilitiesEvent`, `NodeOpCompletedEvent`) |
| `OrchestrationObserverTranslator` — ExCon only | `…/OrchestrationObserverTranslator.cs:28` | the same 3 fact topics (shared decoders `StateOf`/`InventoryOf`) + `ClusterOpStatus`, `NodeHeartbeat`, `NodeCapabilitiesTopic`, `SwitchTimeModeWireDto`, and EVERY node's `NodeOpCommand`/`NodeOpStatus` (republished as `ExecuteNodeOpIntent`/`NodeOpCompletedEvent` for the transaction history) |
| `ClusterUiCache` — the master's core + ExCon | `Hrot.Orchestrator/Panels/ClusterUiCache.cs:27` | facts (state, exercise, recording flags, inventory, session, op outcomes, last unsaved check) **and** monitoring (heartbeats, tx history, planner reachability, diagnostics manifest) |
| `ClusterSlave` follow-fields — every ECS node | `Fdp.Toolkits/Orchestration/ClusterSlave.cs:615-650` | `HasFollowedState`, `MasterGone`, `KnownExercises`, `ReplayAxis`, `RunNotRecorded`, `RecordLiveRunsOff` — a SECOND owner of the same facts |
| `EcsNodeDebugProvider` | `Hrot.Presentation/DebugApi/EcsNodeDebugProvider.cs:23` | no read model ⇒ `ObservesUnsavedChecks` false on CGF / SimHost / IG |
| buses | `Program.cs:278` | ⭐ every subsystem in a runner process has its OWN bus — no node shares a bus with the master |

📐 **Who consumes the observer-only events** (grep, production): `NodeHeartbeatEvent` → ClusterMaster, ClusterUiCache, **NodeOpSlaveTranslator**;
`NodeCapabilitiesEvent` → ClusterMaster, **NodeOpSlaveTranslator**; `NodeOpCompletedEvent` → master, AssetPrefetch, ClusterUiCache,
**NodeOpSlaveTranslator**; `ExecuteNodeOpIntent` → **ClusterSlave**, NodeOpMasterTranslator, ClusterUiCache; `SwitchTimeModeEvent` → already reaches
nodes through `SwitchTimeModeDescriptorTranslator` (SlaveSyncController). `ClusterOpCompletedEvent` → master-side process managers and ClusterUiCache
only — none live on an ECS node's bus.

## 2. The picture

```mermaid
classDiagram
  class ClusterFactsIngress { <<NEW, Hrot.Network.Orchestration>> reads ClusterStateTopic · AssetInventoryTopic · ReplaySessionTopic · ClusterOpStatus → publishes the existing events (one decoder each) }
  class NodeOpSlaveTranslator { <<existing>> own commands · own heartbeat/capabilities/status egress · HAS-A ClusterFactsIngress }
  class OrchestrationObserverTranslator { <<existing>> HAS-A ClusterFactsIngress · + monitoring topics (heartbeat, capabilities, node-op mirror) }
  class ClusterView { <<NEW, Fdp.Toolkits.Orchestration>> State · MasterGone · ExerciseId · RunNotRecorded · RecordLiveRunsOff · ArmedMode (CE-3341) · Inventory · Session · ReplayAxis · OutcomeFor(requestId) · LastUnsavedCheck · Update() }
  class ClusterUiCache { <<existing>> HAS-A ClusterView (facts delegate) · keeps heartbeats, tx history, reachable targets }
  class ClusterSlave { <<existing>> follow-fields DELEGATE to ClusterView }
  class EcsHostClusterHandlers { <<existing>> Compose(): builds the node's ClusterView }
  class EcsNodeDebugProvider { <<existing>> reads ClusterView: ClusterState · UnsavedCheck · ScenarioDirty · ObservesUnsavedChecks }
  NodeOpSlaveTranslator *-- ClusterFactsIngress
  OrchestrationObserverTranslator *-- ClusterFactsIngress
  ClusterUiCache *-- ClusterView
  EcsHostClusterHandlers --> ClusterView : one per ECS node
  ClusterSlave --> ClusterView
  EcsNodeDebugProvider --> ClusterView
```

*What the picture shows that prose hid:* the split is two pairs, and only the FACTS half is unifiable as-is. The monitoring topics mirror other
nodes' ACTION topics; on a node whose own egress is driven by those very events they would be re-sent as its own (§1 consumers) — that is the
measured reason they stay where monitoring is the job.

```mermaid
sequenceDiagram
  participant M as ClusterMaster
  participant D as DDS
  participant I as ClusterFactsIngress (any node)
  participant B as the node's bus
  participant V as ClusterView (the node's)
  participant P as EcsNodeDebugProvider / RunModeChoice
  M->>D: ClusterOpStatus {request, verdict}
  D->>I: sample
  I->>B: ClusterOpCompletedEvent (the ONE decoder, DecodeResult)
  B->>V: Update() each frame — fold the outcome
  P->>V: UnsavedCheck(request) / ScenarioDirty
```

```mermaid
graph TD
  ECS["every ECS host: CGF · SimHost · IG · Traffic · Stride · editor"] --> COMP["EcsHostClusterHandlers.Compose"]
  COMP --> VIEW["ClusterView (one per node)"]
  NB["NodeBootstrapper / NedSlaveOrchestrationTranslator"] --> SLV["NodeOpSlaveTranslator + ClusterFactsIngress"]
  EXC["ExCon"] --> OBS["OrchestrationObserverTranslator + ClusterFactsIngress"]
  MASTER["OrchestratorCore (master)"] --> UIC["ClusterUiCache + its ClusterView"]
  SLV --> VIEW
  VIEW --> PROV["EcsNodeDebugProvider (HTTP, RunModeChoice, start gate)"]
```

*What the module picture shows:* the view is built where every ECS host already passes (`Compose`, the CE-3299 D16 rule), so no host can lack it;
the editor gets it from the same call.

## 3. Decisions (✅ APPROVED, R-3066)

🔒 User, `2026-10-29`: *"We need to pause any development until we enforce the unification and sharing."* · *"Approved both."*

| # | lean | rejected |
|---|---|---|
| **V1** | ONE `ClusterFactsIngress` reads the four FACT topics (state, inventory, session, op status) and publishes today's events through today's shared decoders (`StateOf`, `InventoryOf`, `DecodeResult`); BOTH translators own one — the slave gains op status by construction | add `ClusterOpStatus` to the slave only (ui's superseded note) — patches one topic, keeps two lists |
| **V2** | the four MONITORING topics (heartbeats, capabilities, the node-op command/status mirror) stay with the observer — **a necessary difference, measured** (§1): on an acting node those events drive its own egress and execution | publish them on every node (would re-send other nodes' heartbeats/statuses as its own and execute their commands); rename them to observation events now (touches the master's in-process ClusterUiCache inputs — a later slice if a node ever needs monitoring) |
| **V3** | ONE read model `ClusterView` (Fdp.Toolkits, beside `ClusterSlave`) owns the facts; `ClusterUiCache` composes one and its fact members delegate; `ClusterSlave`'s follow-fields (incl. CE-4096's `HasFollowedState` / `MasterGone`) delegate to the node's view — one owner | `ClusterUiCache` itself on every node (drags tx history, heartbeats and the planner — Hrot.Orchestrator types — onto nodes that need none) |
| **V4** | built once per ECS node in `EcsHostClusterHandlers.Compose` and drained where the slave ticks; ⛔ no per-host flag | per-host wiring (what produced the gap) |
| **V5** | `EcsNodeDebugProvider` reads the view ⇒ `ClusterState`, `UnsavedCheck`, `ScenarioDirty`, `ObservesUnsavedChecks` true on CGF / SimHost / IG; ui's `RunModeChoice` and start gate start answering with no ui change | — |

**Cost:** the view holds facts only — the last state, inventory arrays already held by `ClusterSlave`, 32 op outcomes — no tx history, no
heartbeats. To report on build: memory and per-frame drain on a 1000-entity scenario.

## 4. Build slices (after approval)

1. `ClusterView` + `ClusterFactsIngress`, both translators composing the ingress; `ClusterUiCache` delegating facts (its rails unchanged).
2. `Compose` builds the view; `ClusterSlave` follow-fields delegate; `EcsNodeDebugProvider` reads it.
3. Acceptance (ui's frame): on CGF, SimHost and IG hosts `ObservesUnsavedChecks` is true and an unsaved round's answer arrives — a rail per host
   family through the real composition; ExCon monitoring unchanged; `ClusterUiCache` and slave rails green.

## 5. As built (`2026-10-29`, backend)

```mermaid
classDiagram
  class ClusterFactsIngress { <<Hrot.Network.Orchestration, NEW>> Tick() · StateOf · InventoryOf · DecodeResult · OpCompletedOf · MasterGone on a dead state writer }
  class NodeOpSlaveTranslator { <<existing>> own ClusterFactsIngress (readers passed by NedSlaveOrchestrationTranslator, incl. opStatusReader) · own-node commands · egress }
  class OrchestrationObserverTranslator { <<existing>> For(participant) ClusterFactsIngress · monitoring topics only }
  class ClusterView { <<Fdp.Toolkits/Orchestration, NEW>> facts + 32-op outcome ring · Update() idempotent per frame }
  class ClusterSlave { <<existing>> View built in ctor · Tick() calls View.Update() · follow-fields delegate }
  class ClusterUiCache { <<existing>> ctor(bus, time, view?) · facts delegate · monitoring kept · ReachableTargets keyed on View.State }
  class OrchestratorCoreOptions { View? — the editor passes its node slave's view }
  class EcsNodeDebugProvider { unsavedCheck / scenarioDirty read ClusterSlave.View }
  NodeOpSlaveTranslator *-- ClusterFactsIngress
  OrchestrationObserverTranslator *-- ClusterFactsIngress
  ClusterSlave *-- ClusterView
  ClusterUiCache o-- ClusterView
  OrchestratorCoreOptions ..> ClusterUiCache : View
  EcsNodeDebugProvider --> ClusterView
```

*What the picture shows:* the view is owned by the slave, so a node cannot lack it; the editor's cache and its node share ONE instance.

| # | deviation from §3/§4 | prior state — SUPERSEDED | why |
|---|---|---|---|
| **D1** | `ClusterSlave`'s constructor builds the view (`slave.View`); `Compose` exposes it on the record (`.View`) rather than building it | V4: "built once per ECS node in `Compose`" | the slave is the one object every node (and ExCon, outside `Compose`) has; `Compose` has no bus in its inputs; one owner by construction |
| **D2** | the provider's `ClusterState` stays the node's own COMMITTED state; the cluster's is `FollowedClusterState` (already view-backed). `unsavedCheck` / `scenarioDirty` added | V5 listed "ClusterState" | CE-163 deliberately made it the readiness fact (`ClusterStateFrom` doc); changing it alters `waitForReady` |
| **D3** | `OrchestratorCoreOptions.View` added so the editor's cache and slave share one view; ExCon still has TWO (slave's on its main bus, cache's on the observer bus), each fed by its own ingress | (not in the design) | the editor shares one bus; ExCon's observer bus is quarantined on purpose. ⚠ Residual duplicate: fold by passing `slave.View` to ExCon's cache and dropping the observer's ingress — a later slice |

Side effects (intended): `ClusterOpCompletedEvent` now reaches every ECS node's bus (and ExCon's main bus, so its `AssetOpStatusTracker` resolves); the observer now also raises `MasterGone`; `ReachableTargets` is recomputed from the view's state, not per event.

**Rails:** `ClusterViewTests` (+`CE1436` in `ClusterSlaveTests`), `ClusterUiCacheTests.CE1436…`, `NodeOpSlaveTranslatorTests.CE1436…` (×2), `EveryEcsHostComposesOneClusterHandlerSetRails.CE1436…`, `HrotNodeBuilderTests.CE1436…` (role per host family), `DistributedScenarioSaveTests.CE1436…` (live cluster).

**Cost:** one view with a full 32-outcome ring ≈ 6.4 KB (payload objects included; inventory arrays were already held by the slave); `Update()` ≈ 160 ns on an idle frame, ≈ 2.1 µs with a state + an op outcome (publish+swap+fold). On a 1000-entity headless node (`ctx.Tick`, which runs `ClusterSlave.Tick` → `View.Update`): ≈ 206 µs per frame, of which `View.Update` is ≈ 0.1 µs (≈ 0.05 %) — the view reads four event streams and never touches the world, so it does not scale with entities.
