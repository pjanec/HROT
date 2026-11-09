<!--STATUS
state: LIVE — dispatched, not started (RE-STAMPED, rule 1a: G-7 the better merge added — the user, 2026-11-09: "Can we give the better merge to behaviors? It hadnt started yet"; first stamp bd4994196 never reached behaviors)
updated: 2026-11-09
current-answer: the whole file
related-designs:
  - ../../DESIGN_Scenario_Regression.md — goldens are re-recorded by the change that moved them
  - ../../UX/DESIGN_Review_Perspective.md — §6 the better merge (G-7): §6.3 the UML, §6.4 I1–I9, §6.6 the proofs, §6.8 / §6.9 the M1 / M2 lists, §6.10 what JSON was chosen for
  - ../../designs/replay-browser-frankenstein/DESIGN.md — §7.4–§7.5 / §9 superseded where G-7 lands; §7.3 kept
-->

# HANDOFF — coordinator → behaviors: the next batch

**For:** behaviors. **From:** coordinator. **Dispatched at `d751a3c02`** (re-stamped; first `bd4994196`), scope frozen there. Merge the other lanes first; push `chore: started
HANDOFF_Coordinator_To_Behaviors_Next_2 at <sha>`; status file `STATUS_Behaviors_Next_2.md`. Same [protocol](PROTOCOL_Two_Lanes_Self_Coordinating.md): a status file after every item, fetch and merge the other lanes after every item. ⛔⛔ Never stop for a question (R-4156): an item that needs the user is SKIPPED with its question in its row; a failed premise stops that item only (R-106).

| # | id | what | done when |
|---|---|---|---|
| **G-1** | — | ⭐ **your Ask 1, authorised by the goldens' owner (coordinator):** re-capture the goldens of the ten scenarios that pass every check but differ only in representation (`ua-shootdown-eject`, `ua-car-bomb`, `ua-night-sensing`, `ua-weather-regions`, `ua-weather-sensing`, `ua-m2-engage`, `ua-weapon-systems`, `ua-casualty-care`, `ua-javelin-lock`, `ld-sentry-surprise-360`) — `--verify <list> --verify-capture`; ⛔ commit one only when its diff is representation-only (ids, param objects, passenger-list shape; positions equal) | per scenario: the diff shape in the report (CLAUDE.md gate row 3) |
| **G-2** | CE-4124 | ⏳ ONLY when the user's decision is in RULINGS (your Ask 2; lean: the ladder warns an armed man it SEES) — otherwise skip | its rail |
| **G-3** | CE-4041 (rest) · CE-4136 · CE-4137 · CE-4139 | the measured findings: fix the behaviour or correct the scenario's description — each row closed with its cause | one line per row; a rail where a defect was fixed |
| **G-4** | CE-4132 · CE-4133 | your verify PASSED — close the rows with the evidence | rows closed |
| **G-5** | CE-4035 | the tenth film scenario (the one not yet passing its checks) | it passes |
| **G-6** | CE-4151 | a TKB type's default task carries its params, like its default SOP (user request) | a rail: a type's default task arrives with its params |
| **G-7** | CE-4354 | ⭐ **the better merge** — [Review perspective §6](../../UX/DESIGN_Review_Perspective.md). ✅ **Q9–Q14 decided (R-4181, Q11 revised) — build it.** Design step first (FRAME delegation): extend §6 with the module diagram (who calls the merger each step, on both hosts) and your stages, then build in this order — **S1** I1 + I2 + I9 (binary copy of each owned component into ONE persistent target, the `Entity` fields remapped through the EXISTING `IGuidResolver` machinery, a `NodeLocal` data policy for components that never merge — Q14) with `TransientMasterBuilder` KEPT as the oracle · **S2** I8 ownership from the recorded components (D004 is fixed at the recorder, CE-4327) · **S3** I4 sim-time alignment PLUS a per-node offset for the inter-node network latency, applied the SAME to sim time and wall time (R-4181 revised Q11) · **S4** I3 incremental merge ⇒ merged PLAY, measured against R-2084 (0 B steady) · **S5** I5 the views lens, I6 federated search, I7 local entities from every node. ⛔ §6.10's two constraints hold: never a raw chunk / `EntityIndex` merge (indices are node-local) and correctness over speed | §6.6: the oracle, owner, remap (incl. inline arrays — the old debt D03), handover and play rails; §6 folded to the as-built |

---
_coordinator lane, 2026-11-09_
