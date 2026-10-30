<!--STATUS
state: LIVE — coordinator → ui: what the film pilot found in the director (CE-4119, CE-4117, CE-4120, CE-4121) and the shared layout file (CE-4116)
updated: 2026-10-30
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../Blueprint_Issues_Tracker.md — CE-4116, CE-4117, CE-4119, CE-4120, CE-4121 (the full measurements)
  - ../../PLAN_Feature_Films.md — §1c the grammar on a recording, §1d the defects filming found
  - ../../DESIGN_Director_Timeline_Editor.md — §11s the one anchor resolver (CE-4117, CE-4119 fold there), §11n the drafter (CE-4120)
  - HANDOFF_Coordinator_To_Ui_Editor_Polish.md — the batch already dispatched to you (CE-4111, CE-4112, CE-4114, CE-4115)
-->
# NOTE — coordinator → ui: the film pilot's findings in the director

🔒 **User, `2026-10-30`:** *"These scenario behavior differences should be all noted as filming reveals lots of bugs in them. They will be
solved later and refilmed then."* · *"Of course write bugs notes for ui and backend"*.

⭐ **Context:** films are now cut from Live recordings ([PLAN_Feature_Films §1c](../../PLAN_Feature_Films.md), approved). The pilot filmed
F-49 (the A-10's bomb) end to end in the Replay Browser — it works, but only with FIXED times, and four things in the director got in the way.
Backend has CE-4118 (Live recordings lose their combat events) — not yours.

## The items, in this order — Sonnet for each, your own model reviews

| # | id | what | why this order |
|---|---|---|---|
| 1 | **CE-4119** | on a recording, `/director/stills` finds a take's event anchor but `/director/play` with `capture` does not — every key unresolved, 45 frames, `beat-missing`; the play plans once at load (`DirectorRunner.cs:~616`), lead: before the index is readable | ⭐ blocks every anchored render — the pilot had to strip the anchors, so its take cannot be promoted |
| 2 | **CE-4117** | on a recording, the STATE cues (`near`, `task`, `side`, `health`, `embark`, `gesture`) answered from the index ahead of play, in the ONE resolver (`AnchorBook`), as the event cues are | 22 of 54 takes cut on one; approved by the user |
| 3 | **CE-4120** | the drafter frames a bomb's burst at the aircraft's altitude and follows the departing jet — frame a blast on its ground point | the drafter is the agent's first draft for every clip |
| 4 | **CE-4121** | `/director/moments` says `complete: true, count: 0` before the index is read · a capture's opening card shows the scenario's name, not the take's title | small, misleads an agent |
| 5 | **CE-4116** | the editor and the Replay Browser started in the same second: one dies on the shared `imgui.ini` (`LayoutPaths.TryResetUserLayout`, `File.Copy` overwrite) | RUNBOOK §0b now says to start them one after the other |

⭐ **With the editor-polish batch:** ⭐ lean — CE-4119 first (small, it unblocks every film render), then the polish batch, then the rest here.
⛔ Do not re-open the polish batch's scope; this is a separate list.

⭐ **The reproduction for 1–4** is the pilot's own: RUNBOOK_Filming §0b (record `ua-air-strike` Live in the editor, open it in the Replay
Browser), the take is in the rows. Rails in each feature's own suite (`DirectorTimelineTests`, `DrafterTests`, the AnchorBook rails), each
red-proved; fold CE-4117 / CE-4119 into Director Timeline §11s and CE-4120 into §11n. Report with the gate contract (CLAUDE.md, Rule 8).

_coordinator lane, 2026-10-30_
