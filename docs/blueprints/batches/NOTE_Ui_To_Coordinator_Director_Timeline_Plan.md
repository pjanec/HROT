<!--STATUS
state: LIVE
updated: 2026-10-23
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Director_Timeline_Editor.md — the design the handoff hands over
  - docs/DESIGN_Behaviour_Debugging.md — §7 records the joint (one Timeline, one condition editor)
-->

# NOTE — ui → coordinator: where the director timeline sits in the ui plan

**Re:** [HANDOFF_Coordinator_To_Ui_Director_Timeline](HANDOFF_Coordinator_To_Ui_Director_Timeline.md), dispatched at `7cca2013e` —
merged into `ui` at `82cd65c94`. Not started (no started marker): this note answers the one thing the handoff asks back.

| ui plan order | work | tracker |
|---|---|---|
| 0 | the scenario regressions found by today's golden re-record (bisect running) | — |
| 1 | Behaviour Debugging S1 — make breakpoints actually stop (waits for the user's DBG approval) | CE-1284, CE-1291, CE-1285 + Q44 |
| **2** | ⭐ **Timeline R0 (measure) + R1 (the Timeline panel, also the Trace Timeline — R-4075)** | **CE-1301, CE-1302** |
| 3 | Behaviour Debugging S2–S4 | CE-1273…1286 |
| 4 | camera: over-the-shoulder movement fix, then the camera design | CE-1294, CE-1293 |
| **5** | **Timeline R2 → R2b → R3 → R4** — the handoff's own order | **CE-1303…1306** |

⭐ R1 before any director part, as the handoff asks. ⭐ The joint is recorded in
[Behaviour Debugging §7](../../DESIGN_Behaviour_Debugging.md): the debugged unit is the Timeline's focused unit, the Brain lane (BV-30) is
the index's Behaviour lane, and the breakpoint list (DBG-6) uses the same existing condition editor as the anchors (R-4072).

---
_ui lane, 2026-10-23_
