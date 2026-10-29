<!--STATUS
state: LIVE — ui → backend: a FRAME (CE-1436): one cluster ingress and one cluster read model on every node, by construction
updated: 2026-10-29
current-answer: the whole note
stale-below: nothing
supersedes: NOTE_Ui_To_Backend_Op_Status_On_Nodes.md (the narrow "add one reader" ask — a patch on the split this note removes)
related-designs:
  - ../Architect_Question_108_Run_Mode_Picker.md — §2.6 (CE-4096 start gate: added FollowedClusterState / MasterGone to ClusterSlave), §2.8a (the gap that exposed this)
  - ../../projects/Hrot/Network/Hrot.Network.Orchestration.md — §5 the observer path (ExCon's passive reader)
  - ../../projects/Hrot/Subsystems/Hrot.ExCon.md — the observer bus + ClusterUiCache on ExCon
-->
# NOTE — ui → backend: one cluster ingress, one cluster read model, on every node (CE-1436)

🔒 **The principle (CLAUDE.md, user `2026-10-26`):** *"our desire in everything what we do is to do it in a shared and unified way wherever
possible instead of duplications"* — ONE implementation per concept, ONE owner per piece of state, hosts agree BY CONSTRUCTION.
🔒 User, on this finding (`2026-10-29`): *"How comes cgf is different from other nodes? We need to share and unify, the features need to be
available to all nodes by construction."*

## What is duplicated (measured `2026-10-29`, ui; grep + graph)

| concept | copy 1 | copy 2 | effect |
|---|---|---|---|
| **reading the cluster's DDS traffic onto a node's bus** | `NodeOpSlaveTranslator` — EVERY ECS node (CGF, SimHost, IG, Traffic): 4 topics (`NodeOpCommand`, `ClusterStateTopic`, `AssetInventoryTopic`, `ReplaySessionTopic`) — `NodeOpSlaveTranslator.cs:30-39` | `OrchestrationObserverTranslator` — ExCon only: 9 topics (the same 3 + `ClusterOpStatus`, `NodeHeartbeat`, `SwitchTimeModeWireDto`, `NodeOpCommand`/`NodeOpStatus`, `NodeCapabilitiesTopic`) — `OrchestrationObserverTranslator.cs:30-38` | two topic LISTS that drift; the decoders are already shared (`StateOf` / `InventoryOf`), the set is not |
| **what a node knows about the cluster** | `ClusterUiCache` — the CQRS read model: built only by the master (`OrchestratorCore.cs:103`, so the Orchestrator and the editor's in-process core) and ExCon (`ExConSubsystem.cs:445`) | partial follow-fields on `ClusterSlave` on every other node (`_clusterState`, `KnownExercises`, `ReplayAxis`, ⚠ and ui's CE-4096 `FollowedClusterState` / `MasterGone` — ui added to the split) | a host's answer to "what is the cluster doing?" depends on WHICH copy it happens to have |

📌 **How it surfaced:** CE-4097's unsaved-changes check (CE-4093's round) — the master's answer comes back on `ClusterOpStatus`; the editor and
ExCon read it, **CGF / SimHost / IG cannot** (no reader, no read model) ⇒ a CGF host goes live without the *Save and go live* prompt. ⚠ It is
not a CGF special case: every ECS node is in it, and the next cluster-wide feature will hit the same split.

## ⭐ The frame — you design the details (UML in the owning design)

| | |
|---|---|
| **goal** | every node hears the SAME cluster topics and keeps the SAME read model, so any cluster-wide feature (the unsaved check, the inventory, the session, heartbeats, op results) works on every host with no per-host wiring |
| **lean ①** | ONE ingress translator reading every cluster topic, composed where every node is assembled (the shared node frame — `HrotNodeContext` / the one slave-translator factory `NedSlaveOrchestrationTranslator`); ExCon's observer becomes that same ingress on its node, ⛔ not a second translator |
| **lean ②** | ONE read model (`ClusterUiCache`, or it renamed to say it is the node's cluster view) built for EVERY node by the same frame; `ClusterSlave`'s follow-fields (incl. ui's CE-4096 ones) fold into it or read from it — ⛔ two owners of "the master's last state" stay a defect |
| **lean ③** | the debug providers read that model — `EcsNodeDebugProvider` then supplies `ClusterState` / `UnsavedCheck` / inventory like the editor's does, by construction |
| **fences** | the wire stays as it is (topics, QoS); the master keeps its own in-process path; ⛔ no per-host flag that turns the read model on |
| **cost to watch** | a read model on every node holds the tx history / heartbeats — measure memory and per-frame drain on a 1000-entity scenario; if a part is ExCon-only by NEED, say the measured reason (CLAUDE.md: "a difference must be NECESSARY, and say why") |
| **rejected** | add `ClusterOpStatus` to the node reader only (ui's superseded note) — patches one gap, keeps the split · per-host wiring — what produced this |
| **acceptance** | on CGF, SimHost and IG hosts `ObservesUnsavedChecks` is true and the unsaved round's answer arrives (a rail per host family through the real composition); `ClusterSlave`'s duplicate follow-fields gone or delegating; ExCon's monitoring unchanged; the existing `ClusterUiCache` and slave rails green |

When it lands, ui removes nothing extra: `RunModeChoice` already asks through the provider, and the start gate's `ClusterRestReader` reads the
providers — both start answering on every host.

_ui lane, 2026-10-29_
