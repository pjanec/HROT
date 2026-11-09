<!--STATUS
state: LIVE — dispatched, not started (RE-STAMPED 2026-11-09, rule 1a: B-0 CE-4360 added FIRST — behaviors' CE-4361 waits on it — and B-9 CE-4355; first stamp bd4994196 never reached backend)
updated: 2026-11-09
current-answer: the whole file
-->

# HANDOFF — coordinator → backend: the next batch

**For:** backend. **From:** coordinator. **Dispatched at `bd4994196`**, scope frozen there. Merge the other lanes first; push `chore: started
HANDOFF_Coordinator_To_Backend_Next_3 at <sha>`; status file `STATUS_Backend_Next_3.md`. Same [protocol](PROTOCOL_Two_Lanes_Self_Coordinating.md): a status file after every item, fetch and merge the other lanes after every item. ⛔⛔ Never stop for a question (R-4156): an item that needs the user is SKIPPED with its question in its row; a failed premise stops that item only (R-106).

| # | id | what | done when |
|---|---|---|---|
| **B-0** | CE-4360 | ⭐ **FIRST — behaviors waits on it:** scenarios in SUB-FOLDERS, the ground work (R-4185, [Content layout §6](../../DESIGN_Content_Layout.md)): the ONE resolver finds a scenario by its LEAF name under any sub-folder (an index); a leaf-uniqueness rail; the 41 hand-built `"scenarios/" + name` paths through the resolver; the picker's sub-category = the sub-folder; *Show tests (N)* hides `tests/` — then a NOTE to behaviors that CE-4361 can start | a scenario moved into a sub-folder loads, saves, records, verifies and replays by its unchanged name |
| **B-1** | CE-3421 | the editor's memory grows ~70 MB per load cycle (your X-6 measurement) — root-cause it | the cause in the row; fixed, or filed with its owner |
| **B-2** | CE-4330 ⑤ | the one open measurement: a live run's frame time and DDS latency with a transfer running vs without (R-4173) | the numbers in the report and Recording storage §7.4 |
| **B-3** | CE-4048 | `TrafficMirrorSystem` is not replay-gated (AQ93 E5 / R-4027) | a rail |
| **B-4** | CE-4072 | a replica's `SimVelocity` has two writers | one writer, a rail |
| **B-5** | CE-4074 | a branch from a replay still differs by one tick | the cause; fixed or filed |
| **B-6** | CE-4033 | a cold navmesh bake ejects SimHost | a rail on a cold bake |
| **B-7** | CE-4110 | FDP's old generic DDS topics: delete the dead ones, retire `EntityMasterTopic` | the wire inventory check green |
| **B-8** | CE-4201 · CE-4203 | the semantic value types (dispatched in the editing-UX build) — finish, or say exactly what is left | each row closed or its remainder stated |
| **B-9** | CE-4355 | the cluster time sync's precision: measure the real error on a cluster; stamp t2 / t4 on ARRIVAL (DDS sample timestamps — `SampleInfo.SourceTimestamp`, already read by SumoSharp — or a network-thread reader), target ~1 ms; each node's sync quality (round trip, offset spread) into its recording metadata | the measured error before / after in the row and [Review §6.11a](../../UX/DESIGN_Review_Perspective.md) |

---
_coordinator lane, 2026-11-09_
