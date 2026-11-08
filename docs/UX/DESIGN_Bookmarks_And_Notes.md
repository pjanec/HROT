<!--STATUS
state: LIVE
build-state: BUILT — §5 is the as-built (CE-4325, `2026-11-08`); the open parts are listed there
updated: 2026-11-08 (BUILT: §5 as-built, module diagram, deviations; §2 stays the model it was built against) · 2026-11-08 (Q1–Q5 decided; §2 storage revised; the timeline corner gains ⚑ and the S/W axis toggle)
current-answer: §5 the as-built (read it first) · §0 the idea · §1 measured · §2 the model (UML) · §3 what you see (mockup) · §4 decisions Q1–Q5
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - ../DESIGN_Replay_Time_Axis_And_Review.md — OWNS "⚑ Mark this moment = take a checkpoint, no second bookmark mechanism" (the mode menu, §12) — this design builds on it
  - ../DESIGN_Checkpoint_And_Dry_Run_Restore.md — OWNS the checkpoint itself (R-3044: a named keyframe every node writes at one wall time; §1g CheckpointMark)
  - ../blueprints/Architect_Question_108_Run_Mode_Picker.md — D14: no checkpoint in an unrecorded run; R-4168: none in AAR
  - DESIGN_Scenario_Lifecycle_Review.md — LC-20 (this item), Q13
  - ../DESIGN_Recording_Storage.md — OWNS the exercise folder and its transfers: notes.json travels with the recordings (archive, import, delete)
  - ../DESIGN_Director_Timeline_Editor.md — TE-4 / TE-21: bookmarks as a timeline marker row
  - DESIGN_Seats_And_Hats.md — the Observer seat lists "bookmarks, notes" (§2.2)
  - DESIGN_Review_Perspective.md — the AAR where the marks are listed
-->

# DESIGN — bookmarks and notes: ⚑ mark a moment in Live, find it in the review *(E7, CE-4325, LC-20)*

## 0. The idea

🔒 LC-20 (R-4166): *"Bookmark (one key, a name optional) and Note on the timeline in Live, listed in DAR and AAR."* — the instructor's *"remember this
for the debrief"*.

⭐ **A bookmark IS a checkpoint** — already ruled (*"⚑ Mark this moment = take a checkpoint with an automatic label (R-3044 — a named keyframe; no second
'bookmark' mechanism)"*, [Replay time axis](../DESIGN_Replay_Time_Axis_And_Review.md) §12) and already BUILT: *Take checkpoint here* in Live, a ⚑ flag on
the timeline, *Review / Restart from a checkpoint ▸*. ⇒ E7 adds only what is missing: **one key, a name and a note, the unit it is about, and a list.**

## 1. Measured *(`2026-11-08`, graph + grep)*

INVENTORY: `scripts/cbm.sh search_graph '{"name_pattern":".*(Bookmark|Annotation|Marker|Mark|Note|Comment).*","label":"Class"}'` → 80 rows (page full):
the blueprint / node-editor bookmarks (graph navigation, unrelated), Stride's `CameraBookmarkStore` (per-user camera poses, unrelated), `CheckpointMark`
and `ReviewMark` (the recording's marks — ⭐ the ones that matter) · grep `Mark this moment|TakeCheckpointHere|Checkpoints` · design corpus `bookmark`
(17 files; the ruling above, Seats §2.2, market research gap G5). ⚠ `check_index_coverage` is not available through the CLI.

| fact | where |
|---|---|
| ⭐ a checkpoint = the master names a wall time 0.5 s ahead, every node's recorder writes a keyframe there + a `CheckpointMark {Label, Frame, WallTicks}` in its recording metadata; an unnamed one is `cp<n>-<hhmmss>` | `ClusterMaster.StartCheckpoint` `:895-925` · `AsyncRecorder.cs:379-396` · `RecordingMetadata.cs:101-128` |
| *Take checkpoint here* — Live (mode menu, timeline right-click) and every **Save** in a run; toast *Checkpoint taken*; greyed with the reason in AAR / an unrecorded run | `RunModeChoice.TakeCheckpoint` `:409-416`, `:418-426` · `MainToolbarTimeControlSection.cs:443-462` · `GlobalActionIds.TakeCheckpointHere = 2405` |
| the timeline draws each checkpoint as a ⚑ on the ruler, its label as the tooltip | `TimelinePanel.cs:365-372` · `ITimelineCursor.Checkpoints` |
| a branch keeps the parent's checkpoints before the branch point | `RecordingModule.cs:85` |
| the master's ONE per-exercise record: `RecordingLedgerEntry` (persisted, survives a master restart) → written to the NAS as `Orchestrator.json` at archive | `RecordingLedgerEntry.cs:8-14` · `AssetInventoryProcessManager.WriteExerciseSidecar` `:425-450` |
| ⛔ nothing holds a NOTE, an author or the unit a mark is about; no list of marks; no key | — |

## 2. The model — the MARK stays the checkpoint; the NOTES are one `notes.json` with the recordings *(✅ R-4177)*

🔒 User, `2026-11-08`: *"note might live in a simple json file which is managed together with the exercise recording"* · *"there could be some notes
export step back to NAS when leaving Live/AAR … One managed DDS topic with a list of notes published by orchestrator, modified via single modification
message (CRUD)"*.

| ⭐ feasibility, measured | where |
|---|---|
| a managed DDS topic carrying a LIST of structs exists | `OrchestrationMessages.cs:196` (`List<FeatureOverrideWire>`), `:209`, EQS `:93` |
| the "master republishes the whole thing on every change; TransientLocal + KeepLast 1 hands the newest to a late joiner" pattern exists | `OrchestrationMessages.cs:152, :217-222` |
| the exercise folder already holds more than recordings (`Orchestrator.json`, `scenario/`) and travels as one | [mgmt-1 §13.3](../designs/mgmt-1/DESIGN.md), §13.3a |
| one writer on the master, every station a reader | the master's `ClusterMaster` / `AssetInventoryProcessManager` (as for checkpoints and the ledger) |

```mermaid
classDiagram
    direction LR
    class CheckpointMark { <<existing, every node's recording>> Label · Frame · WallTicks }
    class ClusterMaster { <<existing>> TakeCheckpoint (+ the note, NEW) · applies NoteEdit (NEW) }
    class ExerciseNotes { <<NEW, master>> the run on show: List~Note~ · Load · Apply(edit) · Save }
    class Note { <<NEW record>> Id · Mark (checkpoint label) or AtSimSeconds · Name · Text · Author (seat · station) · Subject (unit id + name) · EditedUtc }
    class NotesFile { <<NEW>> exercises/&lt;id&gt;/notes.json — master staging in Live, the NAS after archive }
    class ExerciseNotesTopic { <<NEW DDS, managed>> ExerciseId · List~NoteWire~ · TransientLocal, KeepLast 1 }
    class NoteEditRequest { <<NEW DDS>> Add / Edit / Delete · Note · AskedBy }
    class MarkPopup { <<NEW>> B in Live · name · note · unit }
    class MarksWindow { <<NEW>> the list · Add note here (AAR) }
    class TimelinePanel { <<existing>> ⚑ + note dot (NEW) · corner: ⚑ and S/W (moved from the toolbar) }
    MarkPopup --> ClusterMaster : TakeCheckpoint (existing op) + note
    MarksWindow --> NoteEditRequest : edit · delete · add (AAR)
    NoteEditRequest --> ClusterMaster
    ClusterMaster --> CheckpointMark : every node writes the keyframe
    ClusterMaster --> ExerciseNotes : ONE writer
    ExerciseNotes "1" *-- "*" Note
    ExerciseNotes --> NotesFile : saved locally on every change
    ExerciseNotes --> ExerciseNotesTopic : republished on every change
    ExerciseNotesTopic --> MarksWindow
    ExerciseNotesTopic --> TimelinePanel
```
*What the picture shows: the recorders change nothing — the moment is the existing checkpoint; the words are one JSON file with ONE writer, seen by every
station through one topic and changed through one message.*

```mermaid
sequenceDiagram
    actor I as the instructor
    participant P as MarkPopup / MarksWindow
    participant M as the master (ExerciseNotes)
    participant N as every node
    participant T as ExerciseNotes topic (every station)
    participant S as the NAS (storage gateway)
    Note over I,S: Live
    I->>P: B (the moment = the key press) · name, note, unit
    P->>M: TakeCheckpoint + the note (existing op)
    M->>N: TakeSnapshot at T (existing)
    M->>M: notes += note · save notes.json (master staging)
    M->>T: the whole list
    Note over I,S: ⏏ Close · Archive — notes.json goes WITH the recordings (it is in the manifest)
    Note over I,S: AAR (imported)
    M->>M: load notes.json (imported with the recordings)
    M->>T: the whole list
    I->>P: Add note here / edit / delete
    P->>M: NoteEditRequest (CRUD)
    M->>M: apply · save locally
    M->>T: the whole list
    Note over I,S: leaving AAR
    M->>S: write notes.json back (a small storage job, §7 of Recording storage)
```

| rule | |
|---|---|
| **ONE writer** | the master applies every edit in arrival order (two stations editing one note: the later wins); every station reads the topic |
| **saved at once, exported on leaving** | every change is saved to the local copy; the NAS copy is written when the AAR is left (a small storage job — it shows in the status bar like any other); a master that dies first pushes it at its next start |
| **with the recordings** | `notes.json` is part of the exercise folder: archived, imported, deleted with it (Recording storage §3, §5, R-4174); the standalone Replay Browser reads it beside the files (read-only) |
| **B in Live = a mark, at once** | the checkpoint is requested at the key press; the popup only asks for the optional words; ignoring it keeps an unnamed mark (*Mark 3 · 12:31*) |
| **in AAR** | *Add note here* = a note at a TIME (no ⚑, not restorable — R-4168); every note editable and deletable |
| **an unrecorded Live** | B greyed: *"no recording — nothing to mark"* (D14, R-4168) |
| **a branch** | the parent's marks before the branch point stay (as checkpoints do) and their notes are copied into the child's `notes.json` |
| **who** | Author, Instructor, Observer (Seats §2.2); not a Player |
| ⭐ **the timeline's corner** | two small icons at the top right of the Event Timeline: **⚑** (mark now in Live · *Add note here* in AAR) and **S / W** — the sim / wall axis toggle (R-3058) **moved there from the main toolbar** (`MainToolbarTimeControlSection.cs:720`); it still sets the axis for the whole session |

## 3. What you see — [`recording-marks-visual.svg`](recording-marks-visual.svg)

![bookmarks and notes](recording-marks-visual.svg)

Generated by `scripts/gen-bookmarks-mockup.py` (house style). Scope: the mark popup, the timeline's ⚑ with a note dot and its tooltip, the Marks
window; the toolbar, the map and the rest of the timeline are context, drawn unchanged (R-4151).

## 4. Decisions — ✅ the user, `2026-11-08` (R-4177)

| # | ✅ decided |
|---|---|
| **Q1** | the notes are a simple JSON file managed with the exercise recording (`notes.json` in the exercise folder) |
| **Q2** | AAR edits in full (add / edit / delete); the master holds the list, publishes it on ONE managed topic, changed by ONE CRUD message; written back to the NAS on leaving the AAR |
| **Q3** | **B**, and a small ⚑ on the timeline's top-right corner — where the **S / W** axis toggle moves too |
| **Q4** | Author, Instructor, Observer |
| **Q5** | the selected unit, changeable in the popup |

⛔ Rejected: the note inside every node's `CheckpointMark` (N copies, rewriting recordings to edit) · the note in `Orchestrator.json` (a different
document's job; a separate file keeps the notes' one writer simple).

⚠ **Not measured:** the cost of a keyframe per mark on a long run (every mark is a full keyframe on every node). Expected small next to the periodic
keyframes; the build measures one mark's size on the largest scenario and reports it.


## 5. As built *(ui, `2026-11-08`, CE-4325 — U-10)*

⭐ INVENTORY (graph `search_graph` + `search_code`, grep): `.*Note.*|.*Mark.*` classes → the checkpoint / `ReviewMark` machinery only; **nothing held a note, a list of marks or a key** (§1 stands). Reused as drawn in §2: `CheckpointMark`, `TakeCheckpointIntent`
(the mark IS the checkpoint), `ClusterView` (the one read model), the `StorageJobsTopic` pattern (topic + wire class + ingress fold), `ReplaySession.Checkpoints` (the master carries the live run's marks on the inactive session), the title-bar pin's clip trick.

```mermaid
graph TD
    subgraph STATION["every station with an Event Timeline (the editor; IG when it composes the window)"]
        KEY["B — shell command marks.mark (Keymap, Global scope)"] --> MC[MarksController]
        CORNER["TimelineCornerBinding — ⚑ and S / W in the title bar"] --> MC
        POP["MarkPopupWindow — name · note · unit"] --> MC
        MW["MarksWindow — the list"] --> MC
        MC --> FAC["ClusterTimeTransportAdapter — EditNote · TakeMark"]
        CVIEW["ClusterView.Notes · Session.Checkpoints"] --> CUR[ClusterTimelineCursor]
        CUR --> RULER["TimelinePanel ruler — ⚑ + amber note dot + tooltip"]
        CVIEW --> MC
    end
    FAC -->|"TakeCheckpointIntent (the mark)"| MASTER
    FAC -->|"NoteEditIntent → ClusterOpType.EditExerciseNote = 28 (the ONE CRUD message)"| MASTER
    subgraph MASTER["the master — ClusterMaster.Tick, every frame"]
        PNE["ProcessNoteEditIntents (after the checkpoint intents)"] --> EN["ExerciseNotes — ONE writer, one list"]
        EN -->|"every change"| STG["master staging notes.json (nodes/node-0/exercises/id)"]
        EN -->|"every change"| PUBE["ExerciseNotesPublishedEvent → ExerciseNotesTopic (TransientLocal, KeepLast 1)"]
        EN -->|"leaving Live / AAR, when changed"| NAS["NAS exercises/id/notes.json"]
        CMT["CommitRestore (a branch)"] -->|"RetainMarks"| EN
    end
    PUBE --> CVIEW
    RB["standalone Replay Browser reads notes.json read-only"] -.->|"NOT BUILT — no master there"| NAS
    classDef dead stroke:#c33,stroke-dasharray:4 3,color:#c33;
    class RB dead;
```
*What the picture shows that the prose hid: the master's `Tick` is the ONLY caller of the list (one writer, one frame step); a station never touches a file — it asks through the one message and reads the one topic. The dashed edge is the one §2 promised and this build did not make.*

| as built | where |
|---|---|
| the model: `ExerciseNote` (immutable record), `NoteEditKind`, `NoteEditIntent`, `ExerciseNotes` (the list, `notes.json`, `RetainMarks`) | `Fdp.Toolkits/Orchestration/ExerciseNotes.cs` · rails `ExerciseNotesTests` |
| the wire: `ExerciseNotesTopic`, `ExerciseNotesWire`, ingress fold → `ClusterView.Notes`, `ClusterOpType.EditExerciseNote = 28` in BOTH enums, `NoteEditPayloadDto` | `Hrot.Network.Orchestration` · rails `ClusterMasterNotesTests` |
| the master: `ClusterMaster.Notes.cs` | `ProcessNoteEdit` (applied in arrival order, saved at once, republished whole) · `SyncNotesToExercise` (follows `_activeExerciseId`) · `WriteBackNotes` · `BranchNotes` |
| the client: `MarksModel` · `MarksController` · `TimelineCornerBinding` · `MarksWindow` · `MarkPopupWindow` · `ITimelineCorner` + `ManagedWindow.DrawTitleBarCorner` | `Hrot.Presentation/Marks`, `Windows/MarksWindows.cs`, `Fdp.Presentation` · rails `TheMarksTests` |
| **the S / W toggle moved**: the toolbar draws none; `MainToolbarTimeControlSection.ToggleAxis` is the one rule and the corner calls it | Q3 |

⭐ **Deviations from §2, each with its reason** *(this section supersedes the §2 line it names)*:

| §2 said | as built | why |
|---|---|---|
| `notes.json` is **in the archive manifest** | ⛔ **not listed** in `Orchestrator.json`'s size-checked file list; it sits beside the recordings | an AAR edit rewrites it AFTER the archive: a listed size would no longer match and a COMPLETE archive would read INCOMPLETE (`ArchiveManifest.Read`). The scan ignores it (no `.fdp`); *delete from archive* removes the folder, so it goes with it |
| write-back is **"a small storage job" shown in the status bar** | the master writes the small file itself, at once, when the exercise leaves the screen | one file of kilobytes; a queued job with a status-bar row is the machinery for recordings. ⚠ the write is not retried: a failure leaves the master's staging copy and a log line |
| "a master that dies first pushes it at its next start" | **not built** | needs a staged-but-not-exported marker; the staging copy survives, the export does not run again. Tracked on the row |
| `ClusterMaster.TakeCheckpoint (+ the note, NEW)` | the existing `TakeCheckpointIntent` carries a client-chosen label (`Mark n · m:ss`, numbered past a taken one) and the words follow as ONE `Add` on **Done** | the master needs no new checkpoint code; ignoring the popup keeps an unnamed mark (nothing sent). ⚠ two stations pressing B in the same minute with the same count collide on the label and the second checkpoint is refused by the recording |
| the popup's unit is **a combo of units** | a combo of the host's **current selection** (+ none) | the selection is what Q5 names; a full unit list is a picker (own design) |
| "a branch copies the parent's notes before the branch point" | a restore (`CommitRestore`) keeps the notes riding the marks that stay; a branch from an AAR keeps those riding the marks the replay held — **not** cut at the branch time | the replay's branch point is not known to the master's list; an AAR time note is dropped (R-4168: no state to restore) |
| the Replay Browser reads `notes.json` read-only | **not built** (the dashed edge) | the browser has no master; its own reader is a follow-up row |
| *Discard* (an unarchived recording) | leaves the master's staging `notes.json` behind | tracked on the row |

⚠ **Measured:** the cost of a mark on the largest scenario (the open item of §4) — see the tracker row CE-4325 (one keyframe per recording node; `scenarios/perf-stress`).

---
_coordinator lane, 2026-11-08 · §5 ui lane, 2026-11-08_
