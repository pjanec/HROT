<!--STATUS
state: LIVE
build-state: DESIGN — top-level, for the user's review (R-4122); the building lanes detail it (R-4123)
updated: 2026-11-06 (first version — coordinator; editing-UX design queue item 5: D1)
current-answer: §2 the model · §3 stages · §4 questions with leans
stale-below: nothing
known-rot: none
known-conflict: R-1047 deferred entity undo because editing is shared with live changes — R-4120 limits undo to Edit mode, which removes that reason; this design is the deferred CE-1462
related-designs:
  - DESIGN_Editing_UX.md — the programme: D1, R-4120 (undo in Edit mode only)
  - DESIGN_Edit_Framework.md — the journal and the step boundary (one user action = one Submit scope); an undo is itself applied through it
  - ../DESIGN_Rules_View_UI.md — §7.5 / §7.5a OWN the shared EditHistory (CE-4218) and the classified duplicates (CE-4219: the gizmo stacks, the director's)
  - ../DESIGN_Film_Director.md — the take editor's undo (today a GizmoUndoStack) moves onto the one history
  - DESIGN_Seats_And_Hats.md — undo is the Author seat's, in the Edit hat
-->

# DESIGN — undo in Edit mode: one history, world steps by snapshot, in the network-less editor

## 0. Why, and the scope the user set

🔒 User, `2026-11-06`: *"undo in edit mode is also its own design"* (R-4122) · *"if it would complicate stuff much (distributed system editing)
we do not need to force it; but in editor's all-in-one network-less composition it stays pretty useful feature"* · *"meaning the world
snapshotting is not completely out of possibilities if it helps"*.

⭐ **Scope: the editor's all-in-one, network-less composition, in Edit mode** (the authored state before Preview / Live). Elsewhere (a
distributed cluster, Live, Preview) there is no undo: the Undo item is greyed *"undo works in the editor's offline editing"*.

## 1. INVENTORY *(2026-11-06)*

Queries: `search_graph .*(Snapshot|Restore|Checkpoint|DeltaFrame|Keyframe).*` Class (>40, mostly replay / debug), grep `EditHistory`,
`GizmoUndoStack`, `SyncFrom(`.

| what exists | measured |
|---|---|
| **`EditHistory`** — a linear list of named `(Redo, Undo)` closures; THE one editor history (CE-4218) | `Hrot.Presentation/Edits/EditHistory.cs`; users: the Parameters window (`HeaderParamsSchemaSource`), the rule editor (`RuleDetails`), the AI section (`EntityAiDetailsView`), held once by `EditorSubsystem` |
| **`GizmoUndoStack`** (Fdp.Toolkits) — records replayed through an ECS command buffer, depth 50 | users: `DataDrivenGizmoSystem`, IG (`IgApplication.cs:812`, Ctrl+Z / Y on the Player's map), the director's take editor (`TakeEditor.cs:53`, `DirectorTakeSession.cs:38`, "the known wart"); ⚠ NOT wired by `MapInteractionPack` ⇒ no map edit is undoable in the editor |
| a second `GizmoUndoStack` in `GizmoMap.Presentation` | no user; a separate library's public API (CE-4219 ②: classified, not deleted) |
| ⭐ **world snapshot / restore exists and is used live** | `EntityRepository.SyncFrom(source, mask?, includeTransient?, excludeTypes?)` (`EntityRepository.Sync.cs:23`) copies a whole world; per component table **only the chunks whose version changed** (`ComponentTable.SyncFrom` → `SyncDirtyChunks`); the breakpoint manager REWINDS the live editor world with it (`DataBreakpointManager.cs:586-589`: `_liveRepo.SyncFrom(_preTickSnapshot)`) |
| ⭐ managed components are immutable records (the CLAUDE.md rule) | so a snapshot's shallow copy of a managed chunk is CORRECT — an old instance is never mutated under it |

## 2. The model

### 2.1 Classes

```mermaid
classDiagram
  direction LR
  class EditHistory { <<existing, the ONE history>> Do · Push · Undo · Redo · Label · + SavedAt }
  class HistoryStep {
    <<NEW kinds of entry>>
    DocumentStep : (Redo, Undo) closures — params, rules form, takes
    WorldStep : before-state index in the ring
  }
  class WorldSnapshotRing {
    <<NEW>>
    Slots : EntityRepository[] (depth bounded by a memory budget)
    Capture(live) : SyncFrom — dirty chunks only
    Restore(live, slot) : live.SyncFrom(slot)
  }
  class EntityRepository { <<existing>> SyncFrom · SyncDirtyChunks }
  class Edits { <<design 2>> Submit — opens / closes a step }
  class RestoreListeners { <<NEW hook>> caches derived from the world rebuild after a restore }
  EditHistory "1" o-- "*" HistoryStep
  HistoryStep ..> WorldSnapshotRing : WorldStep
  WorldSnapshotRing ..> EntityRepository
  Edits ..> EditHistory : one user action = one step
  WorldSnapshotRing ..> RestoreListeners : after Restore
```
*What the picture shows that prose hid: there is still ONE history (the existing `EditHistory`); a world edit's entry does not know how to
invert anything — it only remembers WHICH world state came before it, so EVERY write path is undoable by construction (even one that
bypasses the edit framework), and no per-kind inverse code exists to drift.*

### 2.2 A step, and its undo

```mermaid
sequenceDiagram
  participant U as the author (map drag, Details form, menu action)
  participant E as Edits.Submit (Edit mode, the editor)
  participant H as EditHistory
  participant R as WorldSnapshotRing
  participant W as the editor's world
  U->>E: a user action begins (drag start, Apply, a menu action)
  E->>H: BeginStep("Move Alpha 1-1")
  H->>R: Capture(W) — only the chunks changed since the last capture are copied
  E->>W: the edits (any number, any kind)
  E->>H: EndStep — one entry
  U->>H: Ctrl+Z (the Undo action)
  H->>R: Capture(W) as the step's AFTER state, then Restore(W, before)
  R->>W: W.SyncFrom(slot) — the world is exactly as before the action
  R-->>W: restore listeners rebuild derived caches
  U->>H: Ctrl+Y — Restore(W, after)
```
*What it shows: a step is bracketed by the user action, not by the write; Undo and Redo are a restore of a captured state — the world is
exactly what it was, entity ids included, so selection and links stay valid.*

| | rule |
|---|---|
| **a step = one user action** | the edit framework opens it when an action begins and closes it when it ends: a drag is one step, a form Apply is one, a multi-select change is one, a composition insert is one; typing in one field coalesces (the same field within ~1 s) |
| **document edits** | the Parameters window, the rule form, the director's takes stay closures (`DocumentStep`) — they are not in the world. The take editor moves OFF `GizmoUndoStack` onto `EditHistory` (CE-4219 ②'s "the one consumer that could move") |
| **memory** | a ring bounded by a budget (lean: 256 MB, depth up to 100); each capture copies only the dirty chunks into a slot; ⚠ to MEASURE in U1 — the size of one capture on the largest showcase scenario |
| **derived caches** | a restore notifies `RestoreListeners` (spatial index, navigation, terrain attachments, the rules reader's index …) — ⚠ to measure in U1 which caches already cope (the breakpoint rewind is the precedent) |
| **lifetime** | the history survives Preview (Stop returns to the authored state — checked: the world equals the last state); cleared when a scenario is opened / created or the station goes Live; **Save marks the clean point** (dirty = the history position ≠ the saved one) |
| **outside the editor's network-less Edit mode** | no world steps; Undo greyed with the reason. ⛔ No inverse-operation machinery for the distributed case (the user: *"we do not need to force it"*) |

## 3. Stages *(ui; backend for the restore listeners in the shared nodes)*

| stage | what |
|---|---|
| **U1** | measure: one capture's size and time on the largest scenario; which derived caches survive a `SyncFrom` restore (the breakpoint rewind's precedent) |
| **U2** | `WorldSnapshotRing` + `WorldStep` in `EditHistory`; step brackets from the edit framework (F1) and from the map gizmos' begin / commit; Ctrl+Z / Ctrl+Y as actions (the keymap, design 4b) |
| **U3** | restore listeners for the caches U1 names |
| **U4** | the take editor onto `EditHistory`; `GizmoUndoStack` (Fdp.Toolkits) left with no user — its removal is a separate, later decision |
| **U5** | the dirty flag from the saved point; *Edit ▸ History* (the list of steps, click to go back) |

## 4. Questions — leans for the user

| # | question | ⭐ lean | rejected |
|---|---|---|---|
| **Q1** | how world edits are undone | **world snapshots** (`SyncFrom`, dirty chunks only) in a bounded ring — every write path covered by construction | inverse operations per kind of edit — code per kind, misses bypassing writes, the distributed complexity the user does not want |
| **Q2** | where undo works | **the editor's network-less composition, in Edit mode**; greyed with the reason elsewhere | undo on CGF / a cluster — not forced (user) |
| **Q3** | one history | **`EditHistory` holds both** document steps (closures) and world steps (snapshots), in one order | two histories — two Ctrl+Z meanings |
| **Q4** | step boundaries | **one user action = one step** (opened / closed by the edit framework and the gizmo begin / commit); typing coalesces | a step per write — a drag would be hundreds |
| **Q5** | memory | **a budget (256 MB) with depth up to 100**, measured in U1 | unbounded |
| **Q6** | the gizmo stacks | the director's takes move to `EditHistory`; the IG's map undo goes with the Player seat (no authoring there); the toolkit `GizmoUndoStack` is left unused — deleting it is a later decision | delete now — "unreferenced is not unintentional" |
| **Q7** | lifetime | **survives Preview; cleared on open / new / Live; Save marks the clean point** | cleared on every run |
