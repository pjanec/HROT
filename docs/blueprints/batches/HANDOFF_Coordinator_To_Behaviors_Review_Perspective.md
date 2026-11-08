<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-08
current-answer: the whole file
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - ../../UX/DESIGN_Review_Perspective.md — THE design: §0a the idea · §3 decisions · §3a entering and leaving · §4 stages · mockup review-perspective-visual.svg
  - ../../UX/DESIGN_Replay_Browser_UX.md — the standalone Replay Browser (its Q-B3 / Q-B6 answered by the design)
  - HANDOFF_Coordinator_To_Behaviors_Taken_From_Ui.md — your running work (B-17 P2, P4–P6 resume AFTER this)
  - HANDOFF_Coordinator_To_Ui_Next.md · HANDOFF_Coordinator_To_Backend_Next.md — what the other lanes build meanwhile (the collision rules below)
-->

# HANDOFF — coordinator → behaviors: the Review perspective (CE-3419)

**For:** behaviors. **From:** coordinator. **Dispatched at `f02becbc8`**. ⭐ Scope FROZEN there. Merge `coordinator`, `ui`,
`backend` first (restore after), push `chore: started HANDOFF_Coordinator_To_Behaviors_Review_Perspective at <sha>`, then the
[protocol](PROTOCOL_Two_Lanes_Self_Coordinating.md) — three lanes: fetch and merge `ui` and `backend` after every item. ⛔⛔ Anything needing the user
is skipped (R-4156).

🔒 User, `2026-11-08`: *"could the behavior lane take the 'replay browser into editor integration task', isn't it colliding with what the ui and backend
lanes are doing?"* — the design: [DESIGN_Review_Perspective.md](../../UX/DESIGN_Review_Perspective.md) (§0a on one page; Q1–Q4′, Q7 answered by the
user; the entering / leaving rules §3a); mockup `review-perspective-visual.svg` (the acceptance spec, R-4146).

## The items — in order

| # | stage | what | done when |
|---|---|---|---|
| **R-1** | V1 | `ReviewBundle` + `ReviewHostServices` (Frame diff, Replay search, Recording info, JSON export, ◀ ▶ navigation); the standalone Replay Browser composes it — its four hand-written registrations deleted (the RulesBundle precedent, CE-4292) | the RB's window-set golden unchanged; its frame rails green |
| **R-2** | V2 | the editor composes it over its ONE world and its own run's file — search and frame diff in the editor | a search hit seeks and selects; 0 B per steady frame |
| **R-3** | V3 + §3a | the Review PERSPECTIVE (the mockup's layout, the SAME as the RB's default — Q3); entered on opening a recording for analysis (File ▸ Open Exercise Recording…, Start screen ▸ Recent ▸ AAR, the ended menu's *AAR — full replay*), left on the indicator ▸ Edit; the buttons change only the layout; a real cluster's AAR never switches by itself | side by side with the mockup's top part and the "How you get into Review" band |
| **R-4** | V4 | search hits and breakpoint stops as Event Timeline lanes; a click seeks | rails |
| **R-5** | V6 | `ReviewSource`: a participant GROUP opens through the RB's `FederatedReplayManager` + today's merge, **Merged by default** (Q7), a node's part by the Source switch; the merged world deserialized into the editor's one world (D8); the picker's rows say *"N participants — opens MERGED"* / *"this editor's run"* | a CGF-only behaviour state readable in Details on Merged; a node's part plays; Merged steps |

⛔ **Not here:** the better merge (design §6 — recorded, UNSCHEDULED: binary, persistent, sim-time aligned, incremental). R-5 uses TODAY's merge.

## ⚠ Collision rules — what ui and backend are changing now

| shared code | who is in it | your rule |
|---|---|---|
| `ScenarioDoor.OpenRecording` / `RunModeChoice` (`Hrot.Presentation/RunMode`) | ui (Z-5 input, Z-8 the mode menu's Run setup) · backend (Y-2 the HTTP asker on `Choose`) | R-5 adds ONE branch where a recording opens (a group ⇒ `ReviewSource`), nothing else in these files; write a NOTE to ui and backend BEFORE you push it; merge them right before and right after |
| the dry run / Preview (`ReferencePreviewHandler`) | backend (Y-1, CE-4326) | ⛔ do not touch — Review never runs a Preview |
| the recorder (`RecorderSystem`) | backend (Y-7, CE-4327) | ⛔ do not touch |
| `EditorSubsystem.cs` composition | everyone | add your composition in ONE block; merge often |
| `Fdp.Toolkits/ReplayBrowser/Federation` | nobody | yours — but keep today's merge as is (R-5 reuses it) |

Then resume your own handoff: [B-17 P2, P4–P6](HANDOFF_Coordinator_To_Behaviors_Taken_From_Ui.md).

## Gates

The CLAUDE.md gate contract; fidelity checked by you (R-4146); display rails under `xvfb-run -a`; 0 B per steady frame for every window the bundle adds.

---
_coordinator lane, 2026-11-08_
