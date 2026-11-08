<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-08
current-answer: the whole file
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - ../../DESIGN_Recording_Storage.md — THE design: §00 the life of a recording · §3 P1–P6 · §5/§5a import · §6 the cache · §7 the distributed model (StorageJobQueue) · §8 the mockup · §9/§9a/§9b an AAR on a different cluster, node ids by kind
  - ../Architect_Question_108_Run_Mode_Picker.md — §4c / §4c.1 autosave (D23), the amendment accepted
  - ../../designs/mgmt-1/DESIGN.md — §12.4 / §12.6 / §13.1 the gateway transport (unchanged)
-->

# HANDOFF — coordinator → backend: recording storage + autosave

**For:** backend. **From:** coordinator. **Dispatched at `84ef02e08`**. ⭐ Scope FROZEN there — documents that change after it are FYI only; one that
invalidates an item ⇒ stop THAT item and report (R-106). Merge `coordinator` and `ui` first, push `chore: started
HANDOFF_Coordinator_To_Backend_Recording_Storage at <sha>`, then the [protocol](PROTOCOL_Two_Lanes_Self_Coordinating.md): a status file
(`STATUS_Backend_Recording_Storage.md`) after every item, fetch and merge `ui` after every item. ⛔⛔ Anything needing the user is skipped (R-4156).

🔒 User, `2026-11-08`: *"Yes, approve the ui leans and dispatch to backend"* · on autosave: *"Otherwise option A accepted."*

Your previous batch ([HANDOFF_Coordinator_To_Backend_Next](HANDOFF_Coordinator_To_Backend_Next.md)) is closed: Y-5 resumes here as W-2.

## The items — in order

| # | id | what | design basis | done when |
|---|---|---|---|---|
| **W-1** | CE-4332 | the Player's node-id offset: `ResolveAppNodeId` switches on `ToUpper()` and the arm is `"Player"` (`Program.cs:799-811`) | §9b | `"PLAYER"`; a rail per arm |
| **W-2** | CE-4099 | **autosave, resumed**: A1–A8 on the AMENDED capture — all keyframe types + singletons, the header built on the frame thread, the read-only CONTENT singletons (`TerrainWorld`, …) shared by reference, a round skipped while a behaviour hot reload is pending. ⭐ The save reads `TerrainWorld` only for AUTHORED door states (R-2037 untouched) — keep it that way | AQ108 §4c, §4c.1 (✅ accepted) | §4c's rails + your A0 rail on the amended gate; a killed editor reopened offers the recovery |
| **W-3** | CE-4330 | **recording storage — the model**, in this order: ① complete = a manifest written LAST, files as `.part` → rename, the NAS scan counts only a complete manifest (§3 P4) · ② the master's `StorageJobQueue` + `StorageJob` (kinds Archive / Import / Discard / **Delete**), ONE transfer at a time, one job per exercise (a second request JOINS), cancel / resume via the existing ops, gated to Author / Instructor (§7.2, R-4173) · ③ the three replicated facts — `StorageJobs` on the cluster facts, `ExerciseInventoryItem` NAS state + cached-on, `NodeStatus.LocalRecordings` (§7.2) · ④ an import pushes only to nodes lacking the file; the local cache keeps the newest N, never an un-archived run (§5, §6, R-4172) · ⑤ a transfer never restricts a Live: it gives way while Live runs (§7.4, R-4173) — measure a live run's frame time and DDS latency with and without a transfer, numbers in the report · ⑥ ⏏ Close the session asks Archive / Keep / Discard (§3 P1, R-4171) — the QUESTION's model and HTTP; ⑦ **Delete from archive** (R-4174) · ⑧ resume an incomplete archive by itself (§3 P5) · ⑨ the quit guard on the host that runs the transfer (§8 ⑥) · ⑩ HTTP for every op and the job list. ⭐ When ③ lands: a NOTE to ui — the status bar segment, the Transfers popover, the dialogs and the picker chips are theirs (mockup §8) | `DESIGN_Recording_Storage.md` §3, §5–§8; R-4171…R-4174 | a rail per §7.2 rule (join, queue order, one at a time, discard refused while running, cancel by seat, delete confirmed, restart re-derives); the half-copied archive is INCOMPLETE; the Live measurement |
| **W-4** | CE-4331 | **an AAR on a different cluster**: ① the recording metadata gains the node's name, subsystem and roles · ② node ids granted inside a RANGE PER KIND, the previous id first (`PreferredId`, today always 0), else the lowest free (`NodeIdAllocator`); the id request gains subsystem + roles (a wire field: NED doc + `ned-wire-inventory.py --check`) · ③ the master places recordings by name, then by kind, never across kinds, one per node (§9a) and the import copies `node_<recorded>.fdp` to the chosen node · ④ the AAR start names unplaced files and listening nodes; nothing placeable ⇒ refused · ⑤ the import reads its failure count · ⑥ *Go Live from here* refused when a recorded id has no node | §9, §9a, §9b, R-4175 | a rail per row of §9a's case table; the same cluster relaunched without `-n` gets the same ids and goes Live from its AAR with a working handover; a replacement computer of a kind gets the vacant id |

⛔ **Not here:** the ui half of CE-4330 (ui, after your note) · the Review perspective (behaviors) · per-user key bindings (parked).

## Gates

The CLAUDE.md gate contract, rows 1–8. ⭐ Row 8: W-3 changes the master and every node's status ⇒ the orchestrator suites, the storage gateway
rails and `ClusterRunner.Integration.Tests` (the archive / import paths); W-4 changes id grants and the replay load ⇒ the record / replay suites and a
cluster AAR rail; W-2 the save round on every node. ⭐ Fold every as-built deviation back into `DESIGN_Recording_Storage.md` (mark the prior text
SUPERSEDED) before the batch closes.

---
_coordinator lane, 2026-11-08_
