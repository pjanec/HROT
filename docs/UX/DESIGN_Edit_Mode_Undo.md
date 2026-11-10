<!--STATUS
state: LIVE
build-state: DESIGN — top-level, ✅ APPROVED Q1–Q7 by the user 2026-11-06 (R-4133); the building lanes detail it (R-4123)
updated: 2026-11-08 (§1.5 U4 + U5 History BUILT — Z-2, ui) · 2026-11-06 (✅ Q1–Q7 approved — R-4133) · 2026-11-06 (§1.1 what a world snapshot misses — authored NoPreview components, singletons, files, version-compare caches, pure UI state; §2.3 the restore = the dry run's sequence, not a raw SyncFrom) · 2026-11-06 (first version — coordinator; editing-UX design queue item 5: D1)
current-answer: §2 the model · §3 stages · §4 questions with leans
stale-below: nothing
known-rot: the node called IG here is the Player node since CE-3371 (backend, 2026-11-06): Hrot.IG → Hrot.Player, IgApplication → PlayerApplication (line numbers cited before the rename may have moved)
known-conflict: R-1047 deferred entity undo because editing is shared with live changes — R-4120 limits undo to Edit mode, which removes that reason; this design is the deferred CE-1462
related-designs:
  - DESIGN_Rules_Bundle.md — every host composes the rules windows as ONE bundle; editing decided by the seat (CE-4292)
  - DESIGN_Map_Interaction.md — E8: one finished map edit = one step
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
| ⚠ **world snapshot / restore exists** (see §1.1: the right one is `WorldKeyframes`) | `EntityRepository.SyncFrom(source, mask?, includeTransient?, excludeTypes?)` (`EntityRepository.Sync.cs:23`) copies a whole world; per component table **only the chunks whose version changed** (`ComponentTable.SyncFrom` → `SyncDirtyChunks`); the breakpoint manager REWINDS the live editor world with it (`DataBreakpointManager.cs:586-589`: `_liveRepo.SyncFrom(_preTickSnapshot)`) |
| ⭐ managed components are immutable records (the CLAUDE.md rule) | so a snapshot's shallow copy of a managed chunk is CORRECT — an old instance is never mutated under it |

### 1.1 What a world snapshot would MISS — measured *(user, `2026-11-06`: "what about the pure ui state (not in ECS repo), i believe there some cases like that, can you analyze")*

Queries: `search_graph` `Scenario.*(Save|Serializer|Resident)` (17), `.*(Selection|Pin|Camera|Layout|Recent).*State` (10),
`.*(EntityMap|GuidIndex|…|SpatialGrid).*` (18), `.*Undo.*`; grep `ConditionalWeakTable<EntityRepository` (**33 declarations in 30 files**),
`DataPolicy(… NoPreview …)` (24, of which **9 are saved** with the scenario).

| class | what | measured | ⇒ in the undo |
|---|---|---|---|
| **⚠ raw `SyncFrom` is the wrong tool** | its default mask copies only "snapshotable" types and STRIPS the rest from the live entities; it shares managed instances (`ManagedComponentTable.cs:324-327`); it moves `GlobalVersion` and the tick BACK; it never touches singletons, the bus, the lifecycle stream | `EntityRepository.Sync.cs:42, :63, :117-128, :155-156`; the dry run LEFT `SyncFrom` for this reason (`ReferencePreviewHandler.cs:53-55`: `AirPath` mutated) | ⭐ use the EXISTING world-restore sequence instead — §2.3 |
| **(A) authored, saved, but NOT in the copied tables** | **9 saved components marked `NoPreview`** — `RoutePlan`, `ActiveMissionPlan`, `PositionGeodetic`, `ChildMap`, `SimCombatDef`, `TkbCompositionDef` … — and the behaviour slots' sources (`BehaviorStartRecord`, `SopState`) | `RoutePlan.cs:69`, `DomainMissionPlan.cs:46`, `BehaviorStartRecord.cs:28`, `SopState.cs:85` | ⭐ the capture uses an **AUTHORED mask** (every component saved with the scenario, i.e. not `NoScenario`), not the preview mask |
| (A) singletons | the **scenario header** (`ScenarioDescriptionResident`: description, name, args, the macro header — replaced whole by `Stamp`), the terrain name, the terrain objects (door states, `TerrainWorld`) | `ScenarioDescription.cs:16-41, :87, :109`; `TerrainObjectsSection.cs:31-35` | the header's edits are already **document steps** (Parameters window); door edits become a document step on `TerrainWorld`; the terrain name never changes (R-4124) |
| (A) files | `checks.json`, `preview.jpg`, director takes, macro files | `ScenarioChecks.cs:65`; `TakeStore.cs:25`; `MacroLibrary.cs:12` | takes → document steps (U4); writing a file (a preview capture, a macro save) is NOT undone — it says so |
| **(B) beside the world** | the network-id ↔ entity map, the id allocator, the lifecycle queues, `WorldEpoch`, egress dirty marks | `WorldKeyframes.cs:166-176`, `PreviewParticipants.cs:31-82` | restored by the existing sequence (§2.3) |
| (B) caches that compare a VERSION | `PartIndex`, `RuleTree`, `LiveRefScope` (250 ms throttle), the rules reader's scopes (`AuthoredRules.Scopes`) | `PartIndex.cs:58`, `RuleTree.cs:45,63`, `LiveRefScope.cs:56` | ⭐ **a restore never moves the version backwards** — it advances it (and the restored chunks carry the new version), so every version-compare cache sees a change by construction; the ones with a throttle get `Invalidate` |
| (B) per-world logs | `EditOutcomeLog`, `RuleRequestIds`, `ShotLog`, `KillFeed` … (run-time logs) | 33 CWTs | untouched — run-time only, empty in Edit mode; `RuleRequestIds` holds entity handles ⇒ cleared on a restore |
| **(C) pure UI state** | selection (`SelectionStore`, a transient singleton), gizmo pins (`UnitDebugSettings`), the rubber band, RTS mode, the map canvas view, the camera, the window layout, the Details tab, panel filters, picker recents, the active tool / hat | `SelectionStore.cs:16-18`, `UnitDebugSettings.cs:21-22`, `MapUiStates.cs:16-18`, `AssetRecents.cs` | ⭐ **never undone** — the user's ruling: *"selection is … a UI state having nothing to do with simulation and recording"* (`UX_Feature_Selection.md:1902-1905`); after an undo, a selected entity that no longer exists drops out of the selection |

### 1.2 ⭐ MEASURED — U1, one undo capture *(backend, `2026-11-07`, B-13; Debug build, the real headless editor)*

`WorldKeyframes.Capture(world, CaptureOptions.Undo)` — the NEW `KeyframeTypes.Authored` mask (`ComponentTypeRegistry.IsSaveable`: every type not
`NoScenario`, the saved `NoPreview` ones included; no singletons). Rail `TheWorldChangeCheckOnTheEditorTests.U1_OneUndoCapture_SizeAndTime`.
⭐ **AS BUILT, CE-4411 (2026-11-10) — the mask is "authored" PLUS the replication-identity components** (`NetworkIdentity`, `NetworkAuthority`,
`ReplicationRoute` — `CaptureOptions.IsReplicationIdentity`). All three are `NoScenario`, so the pure "authored" mask left them out and an UNDONE DELETE
brought the entity back with no network id: `Edits.Delete` refused it (*"the entity is not replicated — delete it locally"*) and no edit knew it as a replicated
entity. Rail `WorldUndoTests.CE4411_AnUndoneDelete_RestoresTheNetworkIdentity_…`. ⛔ HISTORY: "every type not `NoScenario`" alone. (`DescriptorOwnership`, a managed record, is not carried — split authority has no Edit-mode meaning.)

| scenario | entities in the editor | one undo capture | capture | apply | the recorder's mask | a 256 MB ring holds |
|---|---|---|---|---|---|---|
| `showcase-crossfire` (the largest showcase) | 157 | **2.3 MiB** | **6 ms** | 15 ms | 6.0 MiB | 100 steps (the cap) |
| `perf-stress` (1128 authored, the stress scene) | 5 604 | **9.6 MiB** | **22 ms** | 54 ms | 56 MiB | 26 steps |

⇒ ⭐ the budget (Q5: 256 MB, depth ≤ 100) HOLDS for every showcase; only the stress scene trims the depth. ⚠ **Correction to §2.2's "memory" row:**
a `WorldKeyframes` capture serialises the WHOLE authored world, not "only the dirty chunks" (that was `SyncFrom`'s behaviour, which §2.3 rejected).
At these sizes that is affordable; a dirty-chunk delta is an optimisation to take only if a scene ever needs more depth. The 6–22 ms capture is
paid once per user action (the step's start), never per frame.

### 1.3 ⭐ AS-BUILT — the ring and U3 *(backend, `2026-11-07`, B-13)*

| piece | as built |
|---|---|
| `WorldSnapshotRing` (`Fdp.Toolkits/Replay/WorldSnapshotRing.cs`) | `Capture(world)` → an opaque slot id: one `CaptureOptions.Undo` keyframe + a `PreviewStateBracket` over the SAME `IPreviewRewindable` participants the dry run uses (passed in). `Restore(world, slot)` = the dry run's exit verbatim (`WorldKeyframes.Apply` · the bracket's restore · `AfterRestore.Run`); `false` for an evicted slot (the history greys that step). `DropFrom(slot)` drops the redo branch; `Clear()` on open / create / Live. Budget 256 MB, depth 100: the oldest goes first, the newest is always kept |
| U3 — which caches already cope | ✅ version-compared (`PartIndex`, `RuleTree`) and epoch-keyed caches: covered by construction (a restore advances the version and moves `WorldEpoch`). ⚠ two were not: **`LiveRefScope`** (the editor's 250 ms throttle — a restore could leave links stale for up to 250 ms with an unchanged entity count) now rebuilds at once on `WorldEpoch.Moved`; **`RuleRequestIds`** (entity HANDLES, never version-checked) forgets its ids when the epoch moves. ⛔ No new listener hook was needed: `WorldEpoch` already is it |
| ⏭ B-14 | `EditHistory`'s world step holds a slot; Ctrl+Z / Y, the dirty flag and *Edit ▸ History* are the ui side |

Rails: `WorldSnapshotRingTests` (restore with participants, eviction, redo branch, the two U3 caches).

### 1.4 ⭐ AS-BUILT — U2 + U5, the ui side *(ui, `2026-11-07`, B-14)*

| piece | as built |
|---|---|
| the step bracket | `IEditStepRecorder` / `EditSteps` (`Fdp.Toolkits/Replication/Edits/EditOp.cs`, beside `EditGate`): a per-world recorder, `Begin(label)` / `End` / `Cancel`, NESTED (only the outer pair is a step). `Edits.Submit` brackets every accepted authoring edit (not orders / interactions); the map drag opens it at the FIRST preview write (`EntityDragGizmo.OnDragUpdate` — the preview pokes the world directly, so a "before" taken at the commit would be the dragged state) and closes it at the commit, cancels it on Esc or a dead entity; the rotator brackets its one commit |
| the world step | `WorldUndoRecorder` (`Hrot.Presentation/Edits`): `Begin` captures the ring; `End` pushes ONE entry on the editor's `EditHistory` — undo captures the AFTER state then restores the before, redo restores the after; available only while the ring holds the before (an evicted step is greyed and still named). Records only while the station is in Edit mode. The same label again within 1 s with nothing undone between joins the last step |
| the ring | `EcsHostClusterHandlers.UndoRing`: built in `Compose` from the SAME participant list as the dry run (R-174: one list, so they cannot differ) — every ECS host has one, only the editor fills it |
| `EditHistory` | + `Push(…, available)`, `Version`, `IsDirty` (position ≠ `MarkSaved`'s; the saved point in a discarded redo tail ⇒ dirty until saved again), `Clear()` |
| lifetime | `EditorScenarioSession.Opened` (any load — to edit or Live — and a clear) ⇒ `EditHistory.Clear()` + the ring cleared; `Saved` (Save, Save As; ⛔ not a checkpoint, not Save selection as macro) ⇒ `MarkSaved` |
| keys and menu | `EditUndoCommands`: shell commands `edit.undo` (Ctrl+Z), `edit.redo` (Ctrl+Y), `edit.redoAlt` (Ctrl+Shift+Z) — the key router gives a focused graph canvas its own Ctrl+Z first; *Edit ▸ Undo Move* / *Redo …* name the step, *(only in Edit mode)* elsewhere. ⛔ The Parameters drawer's private Ctrl+Z chord (CE-4254) is removed — it drove the SAME history and would have undone twice |
| the dirty dot (U5, LC-12 — supersedes AQ108 D20) | `ScenarioWindowTitle.Detail(…, dirty)`: `● my-scenario · on highway-rest` while `EditHistory.IsDirty` (the editor; CGF passes nothing) |
| ⏭ not built | brackets for the vertex / route-waypoint / area gizmos (their writes inside a Submit are covered, their preview pokes are not); autosave (D23) |
| ⛔ SUPERSEDED `2026-11-08` | *"not built: Edit ▸ History (the step list); U4 (the take editor onto `EditHistory`)"* — both are built, §1.5 |

⚠ An undo's own AFTER capture can evict the before it is about to restore when the ring holds a single slot (depth 1 or one keyframe over the budget): the
ring keeps the newest, so a depth of 2+ is required (the default is 100).

Rails: `WorldUndoTests` (Presentation.Tests).

### 1.5 ⭐ AS-BUILT — U4 + U5 *Edit ▸ History…* *(ui, `2026-11-08`, Z-2)*

Queries: `search_graph .*(Undo|History).*` Class (70) + Interface (7) — the only step lists are `EditHistory` and the Message Log's edit-journal source (`EditHistoryMessageLogSource`, a different thing: outcomes of edits, not undoable steps); grep `GizmoUndoStack`, `EditHistory`, `TakeEditor(`. Coverage could not be checked (`check_index_coverage` is not in the CLI).

| piece | as built |
|---|---|
| U4 — the take editor onto the ONE history | `TakeEditor` takes the host's `EditHistory`; one `Apply` = ONE named step (*Move camera key*, *Add time segment* …) pushed as closures over the take before / after. `DirectorTakeSession(…, history)` passes it (the editor: `DirectorPackHost.History = EditorSubsystem.EditHistory`; a host without one — the Replay Browser — gets a private history). `TakeEditRecord` and the session's `GizmoUndoStack` are gone from the director; the toolkit `GizmoUndoStack` is untouched (Q6: unused by the director now, still used by `DataDrivenGizmoSystem` and the Player) |
| steps that are not the scenario's | `EditHistory.Push(…, owner, dirties)`: a take step is tagged with its session (`UndoOwner` / `RedoOwner`) and `dirties: false` — a take is a file of its own, so it never puts the dot on the scenario title; the Director window's Undo / Redo buttons act on a take step only (a world step on top is Edit ▸ Undo's) |
| closing a take | the editor is `Retire`d: its steps STAY on the history, greyed (*no longer available*) — a shared history cannot be cleared by a take opening |
| the panel's own chord | on a host with the shared history the Event Timeline panel's Ctrl+Z / Ctrl+Y hooks are NOT wired (Edit ▸ Undo's chord already reaches the take steps; both would undo twice); a host with a private history keeps them |
| U5 — Edit ▸ History… | `EditHistoryWindow` (`Hrot.Presentation/Windows`): the steps oldest first, the state before the first step on top (*Scenario as opened*), the current position a blue dot + the selection bar, undone steps dimmed; a click = `EditHistory.GoTo` (repeated Undo / Redo); a row whose way is blocked by an unavailable step is greyed WITH the reason (*'<step>' can no longer be undone*); outside Edit mode every row is greyed and the header says so. Row texts are rebuilt only when the history, the mode or the count of unavailable steps changed (0 B steady) |
| the menu | `EditUndoCommands.HistoryId` (`edit.history`, no chord): *Edit ▸ History…* right under Redo, greyed outside Edit mode (*History… (only in Edit mode)*); the editor registers the window as `edit_history` |

⚠ Deviation / limit: the real menu draws no glyph beside Undo / Redo / History… (the menu tree carries atlas icon keys, not FontAwesome glyphs — pre-existing for Undo / Redo); the mockup keeps the coloured glyphs as the intent (`DESIGN_Colour_Icons`).

Rails: `EditHistoryStepsTests`, `TheEditHistoryWindowTests` (Presentation.Tests), `TakeEditsOnTheOneHistoryTests`, `DirectorTracksOnTheEditorTests` (Editor.Tests), `TheEditMenuHasHistoryTests` (AiShared.Tests).

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
    Capture(live) : WorldKeyframes, the AUTHORED mask, dirty chunks only
    Restore(live, slot) : the dry run's sequence (§2.3)
  }
  class WorldKeyframes { <<existing, the dry run's>> Capture · Apply · AfterRestore }
  class Edits { <<design 2>> Submit — opens / closes a step }
  class RestoreListeners { <<NEW hook>> caches derived from the world rebuild after a restore }
  EditHistory "1" o-- "*" HistoryStep
  HistoryStep ..> WorldSnapshotRing : WorldStep
  WorldSnapshotRing ..> WorldKeyframes
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
| **memory** | a ring bounded by a budget (256 MB, depth up to 100); each capture is one authored-mask keyframe — ⭐ MEASURED in §1.2: 2.3 MiB / 6 ms on the largest showcase (⛔ SUPERSEDED: "copies only the dirty chunks") |
| **derived caches** | a restore notifies `RestoreListeners` (spatial index, navigation, terrain attachments, the rules reader's index …) — ⚠ to measure in U1 which caches already cope (the breakpoint rewind is the precedent) |
| **lifetime** | the history survives Preview (Stop returns to the authored state — checked: the world equals the last state); cleared when a scenario is opened / created or the station goes Live; **Save marks the clean point** (dirty = the history position ≠ the saved one) |
| **outside the editor's network-less Edit mode** | no world steps; Undo greyed with the reason. ⛔ No inverse-operation machinery for the distributed case (the user: *"we do not need to force it"*) |

### 2.3 The restore — the dry run's sequence, not a raw `SyncFrom` *(revised `2026-11-06`)*

⭐ The project already has ONE way to put a world back: the dry run's exit (`ReferencePreviewHandler.cs:225-233`): **`WorldKeyframes.Apply`**
(no shared managed instances) → **`PreviewStateBracket.Restore()`** (the id allocator, the network-id map, the lifecycle participants —
registered at `EcsHostClusterHandlers.cs:131-139`) → **`AfterRestore.Run`** (map rebuild, `WorldEpoch.Restored`, republish). ⭐ Undo reuses
it, with two differences: the capture mask is the AUTHORED set (§1.1 A), and the version moves FORWARD (§1.1 B). `RestoreListeners` (§2.1)
is `AfterRestore`'s list, extended with the throttled caches' `Invalidate` and the `RuleRequestIds` clear. ⛔ No second restore path (R-174).


### 2.9 The graphical design *(R-4143)* — the Edit menu names the step to undo / redo; greyed outside Edit mode

![A unit's context menu in Live and in Review, the Edit menu's undo, the one live-destructive confirmation](menus-visual.svg)

*What the picture shows that the prose hid:* one catalogue makes every menu look and behave the same. Groups sit in a fixed order, every item shows its shortcut, a forbidden item is greyed with its reason, and confirmation is so rare that the one dialog is easy to read. Generated by `scripts/gen-menus-mockup.py`.


## 3. Stages *(ui; backend for the restore listeners in the shared nodes)*

| stage | what |
|---|---|
| **U1** | measure: one AUTHORED-mask capture's size and time on the largest scenario through `WorldKeyframes`; confirm §1.1's list of what the dry run's restore sequence already covers |
| **U2** | `WorldSnapshotRing` + `WorldStep` in `EditHistory`; step brackets from the edit framework (F1) and from the map gizmos' begin / commit; Ctrl+Z / Ctrl+Y as actions (the keymap, design 4b) |
| **U3** | restore listeners for the caches U1 names |
| **U4** ✅ BUILT `2026-11-08` (§1.5) | the take editor onto `EditHistory`; `GizmoUndoStack` (Fdp.Toolkits) left with no user — its removal is a separate, later decision |
| **U5** ✅ BUILT (dirty dot `2026-11-07`, B-14 · *Edit ▸ History…* `2026-11-08`, §1.5) | the dirty flag from the saved point; *Edit ▸ History* (the list of steps, click to go back) |

## 4. Questions — ✅ all seven APPROVED as leaned (R-4133, user `2026-11-06`: *"Q1-Q7 approved, go on with the map interaction design"*)

| # | question | ⭐ lean | rejected |
|---|---|---|---|
| **Q1** | how world edits are undone | **world snapshots** through the dry run's existing capture / restore sequence (`WorldKeyframes` + bracket + `AfterRestore`), with the authored mask and a forward-moving version, in a bounded ring — every write path covered by construction | inverse operations per kind of edit — code per kind, misses bypassing writes, the distributed complexity the user does not want |
| **Q2** | where undo works | **the editor's network-less composition, in Edit mode**; greyed with the reason elsewhere | undo on CGF / a cluster — not forced (user) |
| **Q3** | one history | **`EditHistory` holds both** document steps (closures) and world steps (snapshots), in one order | two histories — two Ctrl+Z meanings |
| **Q4** | step boundaries | **one user action = one step** (opened / closed by the edit framework and the gizmo begin / commit); typing coalesces | a step per write — a drag would be hundreds |
| **Q5** | memory | **a budget (256 MB) with depth up to 100**, measured in U1 | unbounded |
| **Q6** | the gizmo stacks | the director's takes move to `EditHistory`; the IG's map undo goes with the Player seat (no authoring there); the toolkit `GizmoUndoStack` is left unused — deleting it is a later decision | delete now — "unreferenced is not unintentional" |
| **Q7** | lifetime | **survives Preview; cleared on open / new / Live; Save marks the clean point** | cleared on every run |
