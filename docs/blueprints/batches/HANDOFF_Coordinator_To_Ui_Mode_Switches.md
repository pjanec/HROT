<!--STATUS
state: LIVE — coordinator → ui: after the armed-mode MVP — prove every mode switch end to end (CE-4104), incl. the new DAR → Live from the DAR moment (CE-4105)
updated: 2026-10-29
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../Architect_Question_108_Run_Mode_Picker.md — §3 (the moves and their confirmations), D9 (a DAR needs its start), D18 (the branch), D21 (the timeline menu), §2.8 / §2.8a (CE-4097 as built)
  - ../../DESIGN_Replay_Time_Axis_And_Review.md — DAR (the review) and ■ in a review = back to live (R-3060)
  - ../PLAN_Run_Modes_And_Content_Build.md — §4 the order (user, 2026-10-29)
  - NOTE_Coordinator_To_Backend_After_MVP.md — backend's part (CE-4103 first, then on call)
-->
# HANDOFF — coordinator → ui: the mode switches, end to end

**Dispatched at** the commit that adds this line (`coordinator`); scope frozen there. Merge `coordinator` and `backend`; push an empty
`chore: started mode-switches at <sha>`. One Sonnet agent at a time.

🔒 **User, `2026-10-29`:** *"fixing the issues found and then continuing in the rest of the armed mode switching stuff, prioritizing the mode
switches like live->AAR, AAR->live, live->DAR, DAR->live from where DAR is (not sure if this one was designd, but is basically same as AAR->Live),
DAR -> live from live pause point etc. Only once this is working we can focus on change indication, auto-save and such."*

⭐ **The method is CE-4102's** — it worked: a rail through the real hosts (headless `EditorSubsystem`, the UI's own ▶ / indicator / timeline
calls, not HTTP) + one live run of the processes, the report naming the exercise ids. ⭐ **Each recording a switch produces must open in the Replay
Browser** (CE-4102's chain), since filming is the point. A break in backend's code ⇒ a NOTE to backend with the measurement.

## CE-4104 — the switches, in this order

| # | switch | entry points to prove | design | what must hold |
|---|---|---|---|---|
| 1 | **Live → AAR** | the indicator's AAR item (confirms *"Ends the live session; its recording opens for AAR"*) · the timeline's *AAR from here* (no confirmation) | §3, D21 | the live run ends, its recording opens in AAR (at the clicked moment from the timeline); ■ in AAR = the recording's start, still AAR (R2) |
| 2 | **AAR → Live** (the branch) | the indicator (confirms *"A new exercise starts from 07:04; this recording stays unchanged"*) · the timeline's *Go live from here* | §3, D18, D21 | a NEW exercise from that moment, its own recording, the parent untouched; ■ returns to the parent's AAR at the branch point (D5) |
| 3 | **Live → DAR** | the indicator's windows (*the last 30 s / 2 min / from a checkpoint*) · the timeline's *DAR from here* | §3, D9 | ⭐ **CE-4100 ③ belongs here:** `EnterReview()`'s default — a DAR always has an explicit start, never "the live end"; live is PAUSED during the DAR and the recording shows no gap |
| 4 | **DAR → Live at the live pause point** | ■ / ⏏ in a DAR | R-3060 (*"■ in a review = back to live"*) | live resumes exactly where it paused; ONE recording, continuous |
| 5 | **DAR → Live from the DAR moment** — ⭐ **CE-4105, not designed yet** | the indicator inside a DAR · the timeline's *Go live from here* inside a DAR | below | below |

### CE-4105 — DAR → Live from the DAR moment: the lean to design against

AQ108 offers *Go live from here* only in an AAR (§3, D21). The user: *"basically same as AAR->Live"*. ⇒ **Lean:** the SAME branch (D18,
`BranchLive`) from the DAR's position — a new exercise starts at that moment with the history up to it, inheriting the scenario copy — and the
running live session **ends** there (its recording kept, exactly as *Live → AAR* ends it). ⛔ Never rewrite or truncate the running recording.
Indicator: confirm *"Ends the live session; a new exercise starts from 07:04 — the recording so far stays unchanged"*; the timeline in a DAR:
*Go live from here*, no confirmation (D21).

⚠ **Design it first, as D24 in AQ108 (§3 row + UML for the sequence: DAR → finalize the live run → branch), and STOP for the user's nod before
building** — the one consequence they have not said explicitly is that the live session ends. Rejected: resume the SAME exercise from the DAR
point (a rewrite of a running recording — the recorder appends; the moments after the DAR point would be lost or contradicted).

## ⛔ Still out of scope (user)

Change indication beyond what is built · autosave (CE-4099) · CE-4100 ① (keep/discard) · CE-3339 (the two hashes) · CE-4095 · CE-4092 · the
ExCon retirement · CE-3342.

Report per switch with the gate contract (CLAUDE.md, Rule 8); fold deviations into AQ108.

_coordinator lane, 2026-10-29_
