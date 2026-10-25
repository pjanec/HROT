<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-10-25
current-answer: the whole file (short)
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - docs/DESIGN_Director_Timeline_Editor.md — OWNS the timeline and its event index (§2b); this asks its Behaviour lane group to grow
  - docs/DESIGN_Entity_Brain_View.md — §12d BV-59 (long history = the recording) · §12e BV-60/BV-61 · §12k (B4 as-built: what is now recorded)
  - docs/DESIGN_Behaviour_Debugging.md — §7 "the joint with the Timeline" (R-4075): a journal entry is a timeline event, one condition editor
  - docs/blueprints/batches/HANDOFF_Coordinator_To_Ui_Director_Timeline.md — the BUILD of the timeline (CE-1301…1306), already ui's
-->

# HANDOFF — ui → coordinator: the Timeline's Behaviour lane, over the brain's recorded histories

**For:** the coordinator lane (`coordinator`). **From:** the ui lane (`ui`). **Dispatched at** the sha of the commit that adds this file. ⭐ Scope
frozen at that sha; later documents are FYI. Questions ⇒ a NOTE to ui.

## Why this exists

🔒 User, `2026-10-25`, approving BV-59: *"BV-59 long recording scan - the time line scan in replay browser can do the job, approved"*. The
Brain tab's histories are short on purpose: the brain journal keeps 24 changes per unit, and a commander's report and order histories keep
32 each (CE-1319, recorded, `NoScenario`). They answer *"what just happened"*. ⭐ *"Why did this unit do that, over the whole exercise"* is
the **Timeline over a recording**: its Behaviour lane reads the same recorded entries, and a Brain-tab row's *Seek here* jumps the timeline
there. Your design (DESIGN_Director_Timeline_Editor §2b) already has a **Behaviour** lane group reading `BehaviorState` changes and the
journal's `Seq`. This asks the DESIGN to grow it to what B4 now records. ⛔ The BUILD of the timeline is already ui's (your
HANDOFF_Coordinator_To_Ui_Director_Timeline, CE-1301…1306); ui builds this extension with R1 once the design names it.

## What is recorded now (measured, `ui` @ `be80d3348` and later)

| component | per | holds | written by |
|---|---|---|---|
| `BrainJournal` (460) | brain unit | the last 24 changes with their cause (task start/pause/end, refusals, SOP, decision switch, move start, play phase, order issued, HSM state) | the brain (CE-1249/1250) |
| `CommandReportHistory` (462) | commander | the last 32 reports to and from him, newest first: time, direction, fate (folded / stale / not owned here), from / to network ids, the report | `StaffInboxSystem` via `CommandHistoryOf.Report` |
| `CommandOrderHistory` (463) | commander | the last 32 orders sent and received: time, direction, the other side, the behaviour hash, the WHY copied from the journal (kind, cause, A), repeats | `CommandOrders.Journal` via `CommandHistoryOf.Order` |
| `UtilityDecisionLog` (333) | EVERY brain unit now (BV-61) | the last ranking of up to 4 utility decisions (winner, previous winner, margin, switch count) | `UtilityScorer.LogDecision` |

📐 Proven replayable: `CommandComponentsTests.CE1319_ARecording_RebuildsTheHistoriesAndTheWhy_AtTheScrubbedFrame`.

## The ask — design decisions, with ui's leans

| # | decision | ⭐ ui's lean | rejected (one line each) |
|---|---|---|---|
| **1** | the Behaviour lane group's rows | ⭐ four lanes, one per source: **Changes** (a journal entry, the kind as its glyph), **Orders** (`CommandOrderHistory`, sent and received), **Reports** (`CommandReportHistory`, in and out), **Decisions** (a `UtilityDecisionLog` winner switch). Each event keyed by the entry's own counter (`BrainJournal.Seq`, a history entry's time + counterpart), so a ring's eviction never loses an event the index already took | one "Behaviour" lane mixing all four (a commander's traffic drowns his own changes) |
| **2** | how the index reads a ring | ⭐ diff the ring frame to frame and emit only NEW entries (a journal's `Seq` grew; a history's newest entry changed), the way TE-4's index already walks the recording once | re-read every ring every frame (the same entry would emit 32 times) |
| **3** | *Seek here* from the Brain tab | ⭐ a row's menu item calls the timeline's existing seek with the entry's time; live (no recording) the item is absent. The seam: the timeline publishes `Seek(time, unit)`; the tab never touches playback itself | the tab driving `PlaybackController` directly (a second seek path) |
| **4** | which units | ⭐ the pinned unit's swimlane shows its four lanes (TE-21); a commander's Orders / Reports rows show only on a commander | every unit's lanes at once (the exercise view shows the group lanes, not per-unit ones) |
| **5** | the journal break as a search | ⭐ the existing `BrainJournalBreak.Condition` (a `Changed` on `BrainJournal.Seq` + an optional kind) is the "find where he changes his mind" search over the recording, in the ONE condition editor (R-4072); the hits land on the Behaviour lane | a second, timeline-only filter |

## Acceptance (for the design, then ui's build)

- DESIGN_Director_Timeline_Editor §2b names the four Behaviour lanes, their sources and their event key, and decides 1–5 (approve, or say
  which lean changes).
- ⭐ For ui's build (R1, after the design): over a recording of `hill-attack`, a commander's Orders lane shows each order he sent with its why
  in the hover; a click seeks the replay there and the Brain tab at that frame lists the same order.

---
_ui lane, 2026-10-25_
