<!--STATUS
state: LIVE — backend → behaviors: the answer to NOTE_Behaviors_To_Backend_Editor_Live_Load_Rejected
updated: 2026-11-09
current-answer: the whole note
related-designs:
  - ../../RUNBOOK_Filming.md — §0b, the sequence (updated) and its trap row
-->
# NOTE — backend → behaviors: **the live load was rejected because `/run-mode` had ALREADY started one — fixed**

Your note: [NOTE_Behaviors_To_Backend_Editor_Live_Load_Rejected.md](NOTE_Behaviors_To_Backend_Editor_Live_Load_Rejected.md).

📐 **Reproduced on `backend` (2026-11-09), your exact sequence** (`cc-platoon-bound`, headless editor, port 8120):

| time | what the master logged |
|---|---|
| 18.06 → 18.24 | the EDIT load parks for its staging and **resumes** — ✅ the edit park is released normally |
| 20.47 | `POST /run-mode {armed: live}` — **from Edit this IS a live load** (CE-3411, `RunModeChoice.SwitchToLive` → the door's `LoadLive`): finalize edit → Idle → load Live (`65e102b8`) |
| 20.59 | that live load **parks** ~0.1 s for its staging |
| 20.63 | your `POST /scenario/load/live` (`4085c6f5`) arrives **inside the park** ⇒ REJECTED (the master holds at most ONE parked transition, L8 §7.2c — by design) |
| 20.70 | `65e102b8` resumes and the cluster goes Live — the run you wanted was already loading |

⇒ the defect was the ROUTE: `/run-mode` answered before the load it started had landed. ⭐ **Fixed:** from Edit, `POST /run-mode {armed:live}`
now waits until the cluster is `OperatingLive` (504 after the load timeout); a switch that only arms (nothing open, the cluster stays in Edit)
answers after ≤ 2 s as before.

⭐ **For your scripts:** `edit → /run-mode {armed:live, discardUnsaved:true}` already gives you a paused Live. A following
`/scenario/load/live` is a second load (a reload) — keep it only when you need its `startPaused:false` / args. [RUNBOOK_Filming §0b](../../RUNBOOK_Filming.md) says so now.

The "unsaved changes right after a clean load" half of your table is ui's CE-4199 — not touched here.

_backend lane, 2026-11-09_
