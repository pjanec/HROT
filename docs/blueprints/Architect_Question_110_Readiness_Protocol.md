<!--STATUS
state: LIVE — ✅ APPROVED by the user 2026-10-29 ("approved, write it up and update the plan and present it"); R-3072
updated: 2026-10-29
build-state: READY-TO-BUILD — the diagrams live in DESIGN_Cluster_Load_Phase.md §11.2 (classes, cancel sequence, modules) and §11.3 (the readiness report + the two levels + the policies)
current-answer: §3 (A–E, each APPROVED with its lean) · §4 the slices
stale-below: nothing
known-rot: nothing known
related-designs:
  - ../DESIGN_Player_Join.md — the join starts once a Player is admitted (AdmissionPolicy) and caught up
  - ../DESIGN_Cluster_Load_Phase.md — §11 owns cluster ready / play ready (R-3069–R-3071) and the UML this question builds on
  - Architect_Question_91_Player_Nodes.md — owns the late joiner (I1 in sync, K6 "a best-effort node never gates", C7 catch-up, L2′ the join gate); this question only makes its admission an explicit policy
  - ../DESIGN_Cluster_View.md — owns ClusterView, the one read model every gate reads (R-3066)
  - Architect_Question_108_Run_Mode_Picker.md — R-3068: a host-kind default as a bootstrap parameter of shared code, the pattern §3 D reuses
  - ../DESIGN_Node_Roles_And_Policies.md — §3.4 roles are what a host DOES; the gate policy keys on the host kind, never on a network fact
-->

# Architect Question 110 — one readiness protocol: each node reports, the master combines participants, policies decide who waits

🔒 **User, `2026-10-29`** (the CE-4103 thread — an open in the editor's first second):
- *"is 'ready to play' a signal from each host monitored by master? some hosts like joining players needs just their local 'ready to play' gate for their UI only (joining player into already running scenario does not block other players) but some other situation like 'loading scenario in cluster' might not want to allow the players to join before the scenario is even loaded (although not always - player might wait in the "lobby"). I would like to be ready for different simular situation and prepare the protocol for that"*
- on the leans below: *"approved, write it up and update the plan and present it"*

## 1. INVENTORY — measured `2026-10-29`

Queries run: `search_graph` name `.*Heartbeat.*` (Class) → 4 (`NodeHeartbeat` topic `OrchestrationMessages.cs:290`, `NodeHeartbeatEvent` `ClusterCqrsEvents.cs:75`, two non-production); `.*Readiness.*` → `SimulatedInitReadinessParticipant` (the reliable-init barrier — entity init, not node readiness) + design sections; `.*Admission.*|.*Admit.*` → `ClusterMaster.AdmitJoinedNode` (`InSync.cs:64`) + unrelated behaviour admission; `.*Lobby.*` → **0**; `.*GatePolicy.*|.*HostKind.*` → blackboard/asset host kinds only, none for cluster gating. Plus grep and the read-only sweep recorded in `DESIGN_Cluster_Load_Phase.md` §11.1. ⚠ `check_index_coverage` is unavailable through the CLI.

| piece | exists? | where |
|---|---|---|
| a per-node report every node sees | ✅ the 1 Hz heartbeat — but it carries only the followed state | `ClusterSlave.cs:213`; read by `ClusterUiCache.ActiveNodes` |
| players never gate others | ✅ R-4014 (K6) | AQ91 §4-K |
| a joiner during a parked load joins it; after a load it is caught up | ✅ | `ClusterMaster.InSync.cs:66`, `:96` |
| a per-node phase / load step / failure reason | ⛔ none | — |
| a "lobby" or an admission choice | ⛔ none (0 graph hits) | — |
| a per-host-kind gate | ⛔ none — only the Orchestrator panel gates, on its own flag | `ClusterScenarioPanel.cs:315` |

## 2. The protocol in one picture

The diagram is `DESIGN_Cluster_Load_Phase.md` §11.3 — ⛔ not redrawn here.

## 3. Sub-questions — each ✅ APPROVED with its lean

| # | question | ✅ answer | rejected (one line each) |
|---|---|---|---|
| **A** | Where does a node's readiness travel? | ⭐ **on the heartbeat it already sends** — `Phase` (Booting · Ready · Loading · CatchingUp · PlayReady · Failed), `Step` (the load step's name), `Reason` (the last failure's). A fact about the node only, no rule in it | a new readiness topic — the heartbeat already reaches every node once a second · a new `Starting` cluster state — the capability advert already is the explicit boot report (§11.2) |
| **B** | What is derived from it? | ⭐ **two levels**: **local** — *this* node's `Phase == PlayReady`, read only by this host · **cluster** — the master combines the **participants only** (in sync ∧ not an observer, K6) and publishes `ClusterPlayReady` + `WaitingFor` with the cluster state | the master gating every host on every node — contradicts R-4014 |
| **C** | Does a joiner ever enter the cluster level? | ⭐ **no** — a node outside the in-sync set is not a participant, so its loading never holds anyone else; it gates only its own UI | counting every roster node — a slow Player would freeze the room |
| **D** | Who waits on what? | ⭐ **a gate policy per host kind, a bootstrap parameter of the shared code** (the R-3068 pattern): operator hosts (Editor, CGF, Orchestrator) = local ∧ cluster ready ∧ cluster play-ready; Player = local only. `ClusterView` exposes the facts; the host's policy combines them — ⛔ never re-derived per host | hard-coding per host — N copies drift (ruling 9) |
| **E** | A node joining while the cluster LOADS? | ⭐ **an admission policy per deployment**, three values: **JoinTheLoad** (today, while the load is parked for files) · **Lobby** (in the roster, not in sync, its screen says *"waiting for the scenario"*, caught up when the load completes — today's C7 made explicit) · **RefuseUntilLoaded**. Default = today's behaviour (JoinTheLoad while parked, else Lobby); RefuseUntilLoaded is built only when a deployment needs it | hard-coding "players wait in a lobby" — it is a deployment choice |

## 4. The slices (the plan: `PLAN_Run_Modes_And_Content_Build.md` §6)

| slice | what | lane | id |
|---|---|---|---|
| **R1** | cluster ready: the latch over names AND roles; role loss ⇒ Degraded; `WaitingFor` on the cluster state; `ClusterView.CanOperate`; HTTP cluster routes 409 + `WaitingFor`; `ClusterSlave.HoldReady/MarkReady` opt-in | backend | CE-3344 |
| **R2** | the readiness report (A) + the two levels (B, C): heartbeat `Phase`/`Step`/`Reason`; `ClusterView` local + cluster play-ready + load progress; `ClusterSlave.CancelLoad` (real token, `AbortLoad`, failure WITH reason); a failed load round ⇒ round to Idle; the gate-policy parameter (D) | backend | CE-3345 |
| **R3** | the 3-D map rebuild as a load step on map hosts, so PlayReady includes it | backend + ui | CE-4078 (extended) |
| **R4** | the admission policy (E) as an explicit parameter with today's two values; RefuseUntilLoaded only on demand | backend | CE-3346 |
| **U1** | greyed controls + *"waiting for …"*, the loading screen with progress and Cancel, the lobby screen, pickers *"waiting for the cluster's list…"* | ui | ui allocates |

**As-built (backend, `2026-10-29`).**
- **R4 / CE-3346** — `ClusterConfiguration.AdmissionPolicy` (code-only, `[JsonIgnore]`; enum `Fdp.Toolkit.Orchestration.AdmissionPolicy { JoinTheLoadWhenParked = 0 (default, today), Lobby = 1 }`). `ClusterMaster.AdmitJoinedNode`: `Lobby` does not admit a node joining while a load is PARKED — it is outside the load round's targets and caught up by C7 once the cluster operates; at rest in Idle both admit. `RefuseUntilLoaded` NOT built (only when a deployment needs it). Rails: `ClusterMasterCatchUpTests.Under_Lobby_*` (red-proof: `AdmitJoinedNode` ignoring the policy).
- **Late-join rail** — `PlayerJoinsARunningScenarioTests` (`Hrot.ClusterRunner.Integration.Tests`): the cluster (SimHost + ExCon + CGF) loads a 2-unit scenario and runs; THEN a real IG starts on the same DDS domain (`HrotRunnerHarness.StartLate(ig, nodeId)` — through the one `SubsystemOrchestrator.Initialize`). Asserts the IG passes `CatchingUp` → `PlayReady`, its Player gate opens, `ClusterPlayReady` (seen by ExCon) never drops (sampled every frame), the master lists it in `InSyncNodeIds`, and its world holds both brain units. Red-proof: `SendCatchUpOps` without the operating-state step ⇒ the IG stays `CatchingUp`. ⚠ the late node needs its OWN node id — the IG default (300) is the Orchestrator's own in the one-process harness.
