<!--STATUS
state: LIVE — RE-DISPATCHED (rule 1a) at the commit that merges the P5 steer into this file (2026-10-15); scope frozen there.
  ⚠ First dispatched at 1d2eac460, before any run started (user: "i havent started the other session yet").
updated: 2026-10-15
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none. (P5 was rewritten before any run started — R-4003: IG records like every ECS host. The separate
  STEER file that first carried it is folded in here and deleted.)
related-designs:
  - docs/blueprints/Architect_Question_91_Player_Nodes.md — THE design (approved 2026-10-15): every item cites its section; the UML lives there
  - docs/DESIGN_Node_Roles_And_Policies.md — §3.3 R-4000 (one code path on every node) and §7.1's SUPERSEDED banner (R-4003, the basis of P5)
  - docs/DESIGN_Unified_Cluster_Handler_Registration.md — CE-279's deferred A2 (move shared pieces down) is what P5a needs
  - docs/DESIGN_Cluster_Load_Phase.md — the load chain P4 reworks and P7 replays to one node
  - docs/designs/mgmt-1/DESIGN.md — §12.5/§12.6 the Storage Gateway that P6 puts behind the provider
  - docs/blueprints/DESIGN_Time_Architecture.md — the step barrier P3 changes
  - docs/DESIGN_Asset_Management.md — the (length, mtime) diff P6 reuses as "already cached?"
-->

# HANDOFF — the Player node, the backend half *(an implementation session on `coordinator`)*

**For:** an implementation session working on the **`coordinator`** branch. ⭐ **Re-dispatched** (rule 1a) at the commit that
merges the P5 steer — the first stamp (`1d2eac460`) never had a run. **From:** the coordinator session that wrote
[Q91](../Architect_Question_91_Player_Nodes.md). ⭐ **That session is ON HOLD until you return** — nobody else pushes to
`coordinator` while you run, so you own the branch. ⭐ **Dispatched at the commit that adds this file**: your scope is FROZEN
there; documents that change after it are FYI. If a later document invalidates an item, **STOP that item and report it**; do
every item that is not blocked (R-106).

⭐ **This is NOT a frame — the design exists and is approved.** Q91 carries the decisions, the class and sequence diagrams
(§2, §4-B2, §4-E2) and the measurements. ⭐ Obligation ③: check those diagrams before building and report *"matches / deviates
HERE and why"*. ⭐ Obligation ⑤: fold every deviation back into Q91 (prior state marked SUPERSEDED) before you report.

## 0. Why

The user wants the system to run as a multiplayer game: SimHost and CGF as servers, **any number of disposable Player nodes**.
A Player is an ordinary **IG node** — no new node type, no new role (R-4000). Today a late node is never brought up to the
cluster's state, a node that dies mid-round hangs that round, a load blocks the node's own heartbeat, and files reach a node
only through a shared folder. Q91 fixes all four without inventing new cluster events.

🔒 The rulings you build under — read the rows in [`RULINGS.md`](../RULINGS.md) and the quotes in Q91 §0:
**R-4000** one code path on every node, behaviour differs by ROLE, never by a missing handler · **R-4001** the heartbeat is
the ONLY time limit; nothing blocks the frame thread; the frame thread coordinates · **R-4002** file transfer behind one
provider interface · **R-4003** IG records like every ECS host — unification over optimisation.

## 1. Before anything

1. `git fetch origin coordinator && git checkout coordinator && git merge --ff-only origin/coordinator` — the dispatch commit
   or later. Push an empty `chore: started HANDOFF_Coordinator_Player_Nodes at <sha>` marker (rule 1b).
2. **Ids:** you are the coordinator lane — allocate tracker ids from **`CE-4000`…** (the tracker's "ID blocks" table; update
   its *next free*). New rows go at the END of the coordinator area. State every id in your report.
3. **Index the graph** (`/opt/codebase-memory-mcp/codebase-memory-mcp cli index_repository …`) — every item below starts with
   its feature's OWN suite (T-1), named in the table.

## 2. The items — in this order

| # | item | Q91 | measure FIRST | acceptance — rails go in the FEATURE'S suite |
|---|---|---|---|---|
| **P1** | **A node that leaves is removed from every in-flight round.** `EjectNode` (`ClusterMaster.cs:690`) today returns early for a non-mandatory node and the round's `Expected` stays fixed (`:1828`) ⇒ the round waits forever. Remove the node from every `GenericTransactionTracker` (`Expected--`, drop it from `Targets`) and complete the round when it was the last missing ACK. ⭐ A bug fix for EVERY node | §4-B B1 | — | `Hrot.Orchestrator.Tests` (`ClusterMasterFanOutTests` or the bootstrap/ejection suite): a 3-node round, one node ejected before ACKing ⇒ the round completes. Red-proof with the fix reverted |
| **P2** | **Authority comes from the role.** A node holding any simulation role (`Brain`, `MuscleGround`, `Perception`, `NavigationSolver`) is *authoritative*; any other node (IG `Map2D`, ExCon `None`) is *best-effort*. A best-effort node's ERROR is recorded in the round's responses but does not fail the round — wire the declared-and-unread `DistributedTransaction.AllowPartialSuccess` | §4-A, §4-C | the role mask per node is already in the roster (`ClusterMaster.cs:551`, `NodeRoster.NodesWithRole`) | an IG-role node returning `Failure` ⇒ the round still succeeds and its response is recorded; a SimHost-role node returning `Failure` ⇒ the round fails (unchanged) |
| **P3** | **The clock's step barrier waits for authoritative nodes only, and drops a node that leaves.** Replace the name match `"SimHost" or "IG" or "CGF"` (`ClusterMaster.cs:1897`) with the P2 predicate; remove a departed node from `MasterSyncController._pendingAcks` | §4-D | — | `Fdp.Toolkits.Tests/Time/MasterSyncControllerTests` + `PauseBarrierSyncTests`: a step with a node removed mid-step completes; ⭐ **integration:** `TimeControlIntegrationTests`, `TheBarrierHaltsEveryNodeTests` (gate row 8) |
| **P4** | **The load never blocks the frame thread; the frame thread coordinates.** ① `LoadPhaseChain` starts the PURE work of its steps as a task and returns (the frame thread already polls an unfinished prepare: `ClusterSlave.cs:259-300`). ② ⭐ `KnowledgeBaseLoadStep` parses the TKB zip into a **NEW** `TkbDatabase` in `Prepare` and swaps it in at `Commit` — today it `Clear()`s and refills the LIVE database, which is not safe off the frame thread. Terrain already has this shape (`TerrainResidency.Prepare` → `Staged` → `Commit`). The scenario-entities step stays ticked on the frame thread. ③ Progress: the chain reports `{part, done, total, code, message}`; the slave sends it as `NodeOpStatus` with code **`InProgress`**, rate-limited ⇒ ④ ⭐ the master reads `InProgress` as PROGRESS and does NOT count it as the node's ACK (today every status increments `Received`: `ClusterMaster.cs:1809-1829`) and surfaces it on `ClusterOpStatus(InProgress, …)` | §4-B2 (the sequence diagram) | ⭐ who reads `TkbDatabase` and how the instance is shared — a swap must reach every holder (`search_graph` + grep for the injected instance); ⛔ and whether `ClusterOpType.CancelOperation` reaches a load prepare — REPORT it, do not build a timer | `Hrot.SimHost.Tests/LoadPhaseChainTests`, `KnowledgeBaseLoadStepTests`, `TerrainLoadStepTests`: a step whose pure work is held open ⇒ the slave's `Tick` keeps returning and the heartbeat keeps beating; the live database is untouched until `Commit`. Master: an `InProgress` status does not complete a round |
| **P5a** | **IG records like every ECS host (R-4003)** — `EcsRecordReplayController` replaces `ListenerRecordReplayController("IG")` (`IgNodeBootstrapper.cs:374`). 📐 SimHost, CGF, the Editor and Stride already use it (`NodeBootstrapper.cs:272`, `CgfApplication.cs:163`, `EditorSubsystem.cs:1512`, `EditorStrideSubsystem.cs:851`). ⭐ ExCon keeps the listener controller: it holds no ECS world, so there is nothing to record — a missing DEPENDENCY, not a role exception | §4-G | ⭐ **where the controller can live.** It is in `Hrot.SimHost`, which IG does NOT reference (CGF, the Editor and Stride do). ⭐ **Lean: move it down** to an assembly IG and the others already reference (`Hrot.Common`, or `Hrot.Network.Orchestration` where the listener lives) — CE-279's deferred A2. ⛔ A `Hrot.SimHost` reference from IG would pull the server node into every Player. Measure its dependencies first; ⚠ **move/rename only with Roslyn** | after a live exercise in which IG owned an entity, IG's `.fdp` exists; SimHost's and CGF's are unchanged |
| **P5b** | **Every ECS host holds the archive handler** — `SerializeLocalRegistrar` builds `ReferenceArchiveHandler` itself (it needs only the temp root and node id); IG (`IgNodeBootstrapper.cs:455`) and the Editor (`EditorSubsystem.cs:1559`) stop passing `null` | §4-G | the empty-manifest path end to end (`ReferenceArchiveHandler.cs:75-78` → `StorageConsensusAggregator.cs:35`) for a node with nothing recorded | `Hrot.Orchestrator.Tests/ReferenceArchiveHandlerTests`: an export over a cluster with an IG includes IG's `.fdp` |
| **P5c** | ⛔ **The rail `NodeRolePersistenceRails.IgCompositionRoot_RegistersNoCheckpointSaveHandler` is SUPERSEDED** (its premise *"IG carries no ECS frame data, so it must not record"* is pre-unification — R-4003). Replace it, in the same suite, with the positive invariant: *every ECS host composes the same recording controller and archive handler*. ⚠ `GlobalContextClusterOpHandler` (the other name in its list) is the ORCHESTRATOR's own context writer — keep whatever it asserts about it if that still holds, and say what you did | §4-G | — | `Hrot.SimHost.Tests/NodeRolePersistenceRails` + `EveryEcsHostComposesTheLoadPhaseChainRails`: the new rail goes red if any ECS host composes the listener controller or omits the archive handler |
| **P5d** | **Replay tolerates a node with no recording** — a Player that joined late or left early has a partial or missing `.fdp`. With P2, a best-effort node's failure no longer fails the round; confirm `PrepareReplay` on a node whose file is absent reports a code + message instead of throwing, and the rest replay normally | §4-G | what `EcsRecordReplayController.PrepareReplayAsync` does with no file today | `ClusterMasterReplayTests` / `ClusterMasterSeekTests`: a replay with one node's file missing completes for the others |
| **P6** | **The transfer provider.** N1–N6 of §4-E2: `IFileTransferProvider` / `IFileTransfer` / `TransferRequest` / `TransferStatus` in `Fdp.Toolkits/Orchestration`; `NodeShareProvider` (today's `File.Copy`, aimed at a share the node advertises as `hrot.transfer.share=<path>` in its greeting); `DdsFileTransferProvider` + `DdsFileReceiver` (on EVERY node). ⭐ The protocol is the MEASURED one (§4-E2): per-transfer partition `hrot.xfer.<id>`, 64 KB chunks, ACK window beyond the slowest target, done only when every target verified length + hash, a `Write` `Timeout` = back-pressure (wait, resend), chunk writer and readers DISPOSED at the end. Several targets may share one transfer. ⭐ The probe `docs/blueprints/experiments/dds-file-transfer/Xfer.cs` is its skeleton | §4-E2 (both diagrams) | ⭐ classify the 11 `File.Copy` sites (`StorageGatewayModule.cs` ×8, `AssetTreeSync.cs:126`, `StorageProcessManager.cs:261`, `ClusterDiagnosticsPanel.cs:401`) into PUSH (NAS → node) and PULL (node → NAS). ⛔ Only PUSH moves behind the provider in this batch | a new suite for the provider (it is a new feature): DDS transfer to 3 targets in one partition + a bystander that receives nothing; a cached file is skipped; a corrupted chunk fails with a code + message; the reader and writer are disposed afterwards. `StorageGatewayTests` + `ClusterMasterPrefetchTests` stay green with the share provider in the co-located case |
| **P7** | **Late join — replay the EXISTING ops to the one node.** When a node appears while the cluster is past `Idle`: `PrefetchFiles` through the provider (P6) → the state steps `ClusterMasterPlanner` already computes from `Idle` → `PrepareZone`/`CommitZone` for each loaded zone; then it is an ordinary participant. ⭐ E1: the master REMEMBERS the committed zone set (cleared at the world boundary). ⭐ E3: a cluster transition during a catch-up restarts the catch-up from the new state; the node joins cluster-wide rounds only once its heartbeat reports the cluster's state. ⭐ Nodes waiting at the same moment are caught up together (one transfer, P6) | §4-E (E1–E3), §2.2 | ⛔⛔ **each Hrot load handler accepts a state commit from whatever state the node is in** — the node layer checks nothing (`ClusterSlave.cs:411-421`), the handlers were never checked. Measure before building; a handler that refuses is a STOP-and-report for P7 only | `Hrot.Orchestrator.Tests`: a node appearing in `OperatingLive` receives prefetch → the state path → the zones, in that order. ⭐ **Live** (runbook [`RUNBOOK_Cluster_Debugging_Over_Http.md`](../../RUNBOOK_Cluster_Debugging_Over_Http.md)): start a cluster, load a scenario, START an extra IG node — it reaches `OperatingLive` and sees the entities; KILL it during a zone load — the round completes |

**Dependencies:** P7 needs P1, P2 and P6; P5d needs P2. Everything else is independent. A blocked item stops ONLY itself (R-106).

## 3. Fences

- ⛔ **R-4001: no new timer of any kind** — not on a round, not on an ACK, not on a transfer. A stall is reported (progress), never timed out.
- ⛔ **R-4000: no behaviour by a missing handler**, and no `NodeRole.Player` — authority is derived from the existing roles.
- ⛔ **R-4003: no switch to turn IG's recording off** — *"unification over optimization at this stage"*; when one comes it is a role or an optimisation setting.
- ⛔ **R-140 is NOT touched** — it is about SCENARIO persistence; the gated scenario save keeps IG's scenario slice empty.
- ⛔ `CgfRecordReplayController` has zero constructions (CGF already uses the real controller) — do not delete it here; name it in the report as a retirement candidate.
- ⛔ **No new cluster events.** The catch-up is the existing ops sent to one node; progress is the existing `InProgress` code.
- ⛔ **Not this batch:** possession release on node loss (the behaviors lane has it — `HANDOFF_Coordinator_To_Behaviors_Possession_Release.md`) ·
  interest management · real-network measurements · the PULL direction of file transfer · finishing `OrchestratorContextTopic`.
- ⚠ **The backend lane is busy adding behaviour support to the simulation models.** Stay out of the simulation modules; the files you
  touch are the orchestrator, `Fdp.Toolkits/Orchestration` + `Time`, `Hrot.Core/Services/LoadPhase`, and the composition roots'
  REGISTRATION lines (IG, Editor, SimHost, CGF). ⭐ **A cross-lane edit is allowed and SAID** — name the lane and file in the commit.

## 4. Report back — the gate report contract (CLAUDE.md rule 8)

One row per gate: verbatim command · pass / fail / skip · delta vs the dispatch sha · a `--no-build` column · every red confirmed
pre-existing against the dispatch sha · golden movement as a diff shape · the tree clean after each suite · both quarantine counts ·
`tracker-counts.py --check` · `rulings-check.py` · `design-digest.py --check` · every id allocated.

⭐⭐ **Row 8 — this batch changes the orchestrator, the clock and cross-node transport**, so name and RUN the integration suites:
`Hrot.ClusterRunner.Integration.Tests` filtered to `TimeControlIntegrationTests`, `TheBarrierHaltsEveryNodeTests` and the
orchestration/prefetch tests — in isolation if the full suite is flaky. If a suite cannot gate, that is a FINDING with the base-sha
proof. ⚠ `Fdp.Toolkits.Tests` rotates its flaky set (`DEBT-AIB-030`) — confirm by `--filter`, say so.

⭐ Plus: Q91 folded to the as-built (each deviation, prior state SUPERSEDED), the per-item sha on `coordinator`, the P4, P5a and P7
measure-first results (P5a: which assembly the controller moved to, the Roslyn diff size), and the live late-join run (P7) with what you saw.
