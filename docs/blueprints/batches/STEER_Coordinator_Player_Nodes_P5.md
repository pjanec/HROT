<!--STATUS
state: LIVE — a STEER (rule 1c) to HANDOFF_Coordinator_Player_Nodes.md, written 2026-10-15 AFTER its dispatch (1d2eac460).
updated: 2026-10-15
current-answer: the whole file. It REPLACES item P5 of the handoff; every other item is unchanged.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Node_Roles_And_Policies.md — §7.1's SUPERSEDED banner (R-4003) and §3.3's measured table are the basis
  - docs/blueprints/Architect_Question_91_Player_Nodes.md — §3 (recording row), §4-G — YOU fold this steer into them (obligation ⑤)
  - docs/DESIGN_Unified_Cluster_Handler_Registration.md — CE-279's deferred A2 (move the shared handlers down) is what R5 needs
-->

# STEER — P5 is replaced: **IG records like every ECS host**

**For:** the implementation session running [`HANDOFF_Coordinator_Player_Nodes.md`](HANDOFF_Coordinator_Player_Nodes.md).
⭐ **The user relays this to you** (rule 1c ③). ⭐ **Not a correction of your work** — the premise P5 kept came from the
coordinator, and the user retired it after dispatch.

## 0. The user's ruling — `R-4003`

> 🔒 *"that premise is old and not valid anymore, it must be a pre-unification one. pls mark it superseded. IG can create its
> own entities if it wants to. So it might need to run its own recording. I prefer the unification over optimization at this
> stage, still the disabling would be controlled by the role or by some optimization settings later."*

⇒ P5 as dispatched kept the premise *"IG records no ECS frames"* and only moved how it was enforced. ⛔ **That premise is
gone.** IG can own entities, so it records them.

## 1. Unchanged

**P1, P2, P3, P4, P6, P7 stand exactly as dispatched.** ⭐ If you already built P5 as written, revert that part and build
§2 instead — that is the expected outcome of a withdrawal, not a mistake of yours.

## 2. P5, replaced

| # | item | measure FIRST | acceptance |
|---|---|---|---|
| **R5a** | **IG uses the same record/replay controller as every ECS host** — `EcsRecordReplayController`, replacing `ListenerRecordReplayController("IG")` (`IgNodeBootstrapper.cs:374`). 📐 SimHost, CGF, the Editor and Stride already use it (`NodeBootstrapper.cs:272`, `CgfApplication.cs:163`, `EditorSubsystem.cs:1512`, `EditorStrideSubsystem.cs:851`). ⭐ ExCon keeps the listener controller: it holds no ECS world, so there is nothing to record — a missing DEPENDENCY, not a role exception | ⭐ **where the controller can live.** It is in `Hrot.SimHost`, which IG does NOT reference (CGF, the Editor and Stride do). ⭐ **Lean: move it down** to an assembly IG and the others already reference (`Hrot.Common`, or `Hrot.Network.Orchestration` where the listener lives) — `CE-279`'s deferred A2. ⛔ Adding a `Hrot.SimHost` reference to IG would pull the server node into the Player. Measure its dependencies first (`get_code_snippet` + its usings: `Fdp.Toolkit.Replay`, `Hrot.Common.Orchestration`, NED descriptors). ⚠ **Rename/move only with Roslyn**, never text | IG's `.fdp` exists after a live exercise in which IG owned an entity; SimHost's and CGF's are byte-identical to before |
| **R5b** | **IG holds the archive handler** — `SerializeLocalRegistrar` builds `ReferenceArchiveHandler` itself for every ECS host (it needs only the temp root and node id); IG and the Editor stop passing `null` | — | an export over a cluster with an IG includes IG's `.fdp` |
| **R5c** | ⛔ **The rail `NodeRolePersistenceRails.IgCompositionRoot_RegistersNoCheckpointSaveHandler` is SUPERSEDED** — replace it with the positive invariant in the same suite: *every ECS host composes the same recording controller and archive handler*. ⚠ `GlobalContextClusterOpHandler` (the other name in that rail's list) is the ORCHESTRATOR's own context writer — keep whatever it asserts about it if it still holds, and say what you did | `Hrot.SimHost.Tests/NodeRolePersistenceRails`, `EveryEcsHostComposesTheLoadPhaseChainRails` | the rewritten rail goes red if any ECS host composes the listener controller or omits the archive handler |
| **R5d** | **Replay tolerates a node with no recording.** A Player that joined late or left before the exercise ended has a partial or missing `.fdp`. ⭐ With P2 a best-effort node's failure no longer fails the round — confirm the replay path (`PrepareReplay` on a node whose file is absent) reports a code + message instead of throwing, and that the other nodes replay normally | what `EcsRecordReplayController.PrepareReplayAsync` does with no file today | `Hrot.Orchestrator.Tests` replay suite (`ClusterMasterReplayTests` / `ClusterMasterSeekTests`): a replay with one node's file missing completes for the rest |

## 3. Fences

- ⛔ **R-140 is NOT touched** — it is about SCENARIO persistence; the gated scenario save keeps IG's scenario slice empty.
- ⛔ **No disabling switch now** (user: *"unification over optimization at this stage"*). ⭐ When one comes, it is a role or an optimisation setting (R-4000).
- ⛔ `CgfRecordReplayController` has zero constructions — CGF already uses the real controller. **Do not delete it in this batch**; name it in your report as a retirement candidate.
- ⭐ Fold this into Q91 — §3's recording row, §4-G and its ⚠ paragraph, §6's claim table — marking the dispatched P5 state SUPERSEDED (obligation ⑤).

## 4. Report

In the same report as the rest of the handoff: R5a's move-down (which assembly, Roslyn diff size), each new or rewritten rail
with its red-proof, and R5d's measured replay behaviour.
