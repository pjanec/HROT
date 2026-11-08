<!--STATUS
state: LIVE — RE-DISPATCHED (rule 1a: the first dispatch f02becbc8 was never merged by behaviors), not started
updated: 2026-11-08
current-answer: the whole file
stale-below: nothing
known-rot: ⛔ the first dispatch (f02becbc8: R-1…R-5, "the perspective enters Review and the Source switch lives in it") is SUPERSEDED by this one
known-conflict: none
related-designs:
  - ../../UX/DESIGN_Review_Perspective.md — THE design, FOURTH pass: §0a the idea · §3 D1–D11 · §3a entering and leaving · §4 V1–V7 · mockup review-perspective-visual.svg
  - ../../UX/DESIGN_Replay_Browser_UX.md — the standalone Replay Browser (its Q-B3 / Q-B6 answered by the design)
  - HANDOFF_Coordinator_To_Behaviors_Taken_From_Ui.md — your running work (B-17 P2, P4–P6 resume AFTER this)
  - HANDOFF_Coordinator_To_Ui_Next.md · HANDOFF_Coordinator_To_Backend_Next.md — what the other lanes build meanwhile (the collision rules below)
-->

# HANDOFF — coordinator → behaviors: ONE AAR — the Replay Browser's tools, transport and merge in the editor (CE-3419)

**For:** behaviors. **From:** coordinator. **Dispatched at `<sha of the commit adding this file>`**. ⭐ Scope FROZEN there. Merge `coordinator`, `ui`,
`backend` first (restore after), push `chore: started HANDOFF_Coordinator_To_Behaviors_Review_Perspective at <sha>`, then the
[protocol](PROTOCOL_Two_Lanes_Self_Coordinating.md) — three lanes: fetch and merge `ui` and `backend` after every item. ⛔⛔ Anything needing the user
is skipped (R-4156).

🔒 User, `2026-11-08`: *"Yes, rewrite the design to this idea. We need some indication that we are actually running in merged federate view …
Maybe the status bar is a good place."* — the design's FOURTH pass: **one run mode (AAR); the SOURCE follows the files; ONE AAR transport; the
perspective is a LAYOUT only; the source said in the status bar.** Read [§0a](../../UX/DESIGN_Review_Perspective.md) first. Mockup
`review-perspective-visual.svg` = the acceptance spec (R-4146).

## The items — in order (the design's §4)

| # | stage | what | done when |
|---|---|---|---|
| **R-1** | V1 | `ReviewBundle` + `ReviewHostServices` (Frame diff, Replay search, Recording info, JSON export); the Replay Browser composes it — its hand-written registrations deleted | the RB's window-set golden unchanged; its frame rails green |
| **R-2** | V2 | the editor composes it (own-part AAR); the windows open in ANY layout (Window menu) | a search hit seeks and selects; 0 B per steady frame |
| **R-3** | V3 | **the ONE AAR transport** (D8): an `IReplayTransport` seam under the RB's ONE drawer (`ReplayTimelinePanel.DrawTransport`); the editor shows it as its AAR segment — history ◀ ▶, step back / forward with hold-to-scrub, the seek bar — the cluster's seek behind it; ▶ greyed while Merged | side by side with the mockup's toolbar; the RB's look unchanged; 0 B steady |
| **R-4** | V4 | the Review LAYOUT + its automatic switch (D10): on an all-in-one AAR and on a group; back to the previous layout on leaving AAR; a cluster station's AAR never switches; the buttons change only the layout | side by side with the mockup |
| **R-5** | V6 | **`ReviewSource`** (D6, D7): a participant group with no part of this node opens through the RB's `FederatedReplayManager` + today's merge, **Merged** by default, a node's part by the Source combo; the picker's rows (*"4 participants — opens MERGED"*); **the status bar's source segment** (D9) | ⭐ the EMPTY-world bug gone (a 4-file exercise opens merged in the all-in-one editor); a CGF-only behaviour state readable in Details; a part plays; Merged steps |
| **R-6** | V7 | search hits and breakpoint stops as Event Timeline lanes | a click seeks |

⛔ **Not here:** the better merge (design §6 — UNSCHEDULED; R-5 uses TODAY's merge) · the AAR / DAR rename (CE-4328 — waits for the user) · the
seat · hat text (CE-4329 — ui's).

## ⚠ Collision rules — what ui and backend are changing now

| shared code | who is in it | your rule |
|---|---|---|
| `MainToolbarTimeControlSection` (the editor's time controls) | ui (Z-5 input, Z-8 the mode menu's Run setup, CE-4329 the seat text) | ⛔ do not edit it. Register the AAR replay segment as its OWN main-toolbar entry (as the RB does, `MainToolbar.RegisterEntry`), shown only in AAR, with a drawer PROFILE that omits what ui's section already draws (▶ ⏸ ■, rate, axis, step forward) — one drawer, two profiles. If a duplicate button cannot be avoided, write ui a REQUEST note, never edit their file |
| `ScenarioDoor.OpenRecording` / `RunModeChoice` | ui · backend (Y-2) | R-5 adds ONE branch where a recording opens (a group with no part of this node ⇒ `ReviewSource`); NOTE ui and backend before pushing; merge right before and after |
| the status bar | ui (CE-4329 puts the seat · hat at its right end) | add your source segment at the LEFT as its own section; nothing else |
| the dry run / Preview · the recorder | backend (Y-1, Y-7) | ⛔ do not touch |
| `EditorSubsystem.cs` composition | everyone | one block; merge often |
| `Fdp.Toolkits/ReplayBrowser/Federation` | nobody | yours — today's merge reused unchanged |

Then resume your own handoff: [B-17 P2, P4–P6](HANDOFF_Coordinator_To_Behaviors_Taken_From_Ui.md).

## Gates

The CLAUDE.md gate contract; fidelity checked by you (R-4146); display rails under `xvfb-run -a`; 0 B per steady frame for every window and the
transport segment.

---
_coordinator lane, 2026-11-08_
