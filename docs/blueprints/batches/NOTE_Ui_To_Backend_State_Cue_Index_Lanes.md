<!--STATUS
state: LIVE — ui → backend: CE-4117 (backend half): index lanes for the director's STATE cues, so a take plans them before play
updated: 2026-10-30
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Director_Timeline_Editor.md — §9c (the lane seam, R-4078/R-4079), §11s (the anchor resolver; ui amends it for `near`)
  - ../../DESIGN_Replay_Time_Axis_And_Review.md — §12z (CE-4118, the recorder writes the read buffer; these lanes ride the same index pass)
  - NOTE_Coordinator_To_Ui_Film_Pilot_Findings.md — CE-4117, the pilot finding this answers
-->
# NOTE — ui → backend: index lanes for the director's state cues (CE-4117, backend half)

**Dispatched at** the commit that adds this file (`ui`). Built on `backend@6ccc6efd3` (CE-4118 done). The user, `2026-10-30`: *"can backend do
both at once? … write a note for what you need"*. CE-4118 has landed, so this does not overlap with it.

## The defect (CE-4117)

The director plans a take from the recording's index before play (`DirectorRunner.TickEntering` → `AnchorBook.TimeOf`). **Event** cues
(`shot`, `blast`, `hit`, `spawned`, `left`…) resolve from index lanes. **State** cues have no lane, so they are only *observed* while the
take plays (`AnchorBook.ObserveEvents` → `Holds`, `AnchorBook.cs:169`). The plan therefore treats them as "not yet" and places shots wrong.

| cue | what `Holds` reads (live) | in the index today |
|---|---|---|
| `near` | `SimTransform.Position` within `Within` of a point | `PositionTrack` at 2 Hz (coarse). ⭐ **ui handles this one** (below) |
| `task` | `BehaviorState.ActiveBehaviorHash` (named via the registry) | ⛔ `behaviour.changes` carries `BrainJournalEntry.Kind` and no behaviour identity |
| `side` | `EntityInfo.ForceId` | ⛔ none |
| `health` | `Health.Current / Max` below a threshold (fraction, or absolute when `Below > 1`) | ⛔ none |
| `embark` | has `IsEmbarkedTag` | ⛔ none |
| `gesture` | `Gestures.Of(world, e)` | ⛔ none |

## What ui needs — one lane per state, carrying the CHANGE

⚠ The cue's value is chosen per take (*"health below 0.3"*, *"task is Suppress"*), so a lane per condition cannot be built in advance.
⭐ What answers every take is **the change history**: for each unit, the frame its value changed and the new value. `AnchorBook` then finds
the first frame a cue holds by walking that history, the same way it walks event lanes.

| lane (suggested id) | Subject | Value | emitted when |
|---|---|---|---|
| `state.task` | the unit | `ActiveBehaviorHash` (0 = idle) | the hash changes (incl. first seen) |
| `state.side` | the unit | `ForceId` | it changes (incl. first seen) |
| `state.health` | the unit | `Current`, plus `Max` (in `Other`/`X`, your call) | `Current` or `Max` changes |
| `state.embark` | the unit | 1 aboard / 0 not (+ carrier in `Other` if cheap) | the tag is added / removed |
| `state.gesture` | the unit | the gesture id | it changes |

| ⭐ asks | |
|---|---|
| ⭐⭐ **lanes are DEFINITIONS, not code** (R-4079) | today `TimelineLaneCompiler` has `RisingEdgeLane` (emits when an entity *starts* matching). These need a **value-change** kind (emit `(subject, value)` when a projected field differs from last frame). ⭐ Add it once to the compiler and declare the five lanes as data from the provider that owns each component (behaviour → `BehaviourTimelineLanes`, combat → `CombatTimelineLanes`, the rest wherever their component lives). ⛔ No hand-written per-lane scanner |
| ⭐⭐ **one composition** | register through `ShippedTimelineLanes.Catalog()` so the editor's live index, the Replay Browser and every node agree (CE-3330) |
| ⭐ **first frame counts** | a unit already in a state at frame 0 needs an entry at frame 0, otherwise *"side is Red"* never holds |
| ⭐ **sidecar** | `TimelineSidecar.SchemaVersion` is 3. If the lane set is not already part of what invalidates a stored index, bump it, so old sidecars rebuild instead of answering "no changes" |
| ⭐ **rail** | in `TimelineIndexTests`: a scripted world flips each of the five states across frames; the index holds exactly those changes, at those frames, with those values; frame 0 is present. Red-prove by removing one lane |
| ⭐ hidden lanes | these are for the resolver. Hide them in the panel (Group / a flag), unless you think they are worth showing |

## What stays ui's

- **`near`** (approved by the user, `2026-10-30`, "seek"): the coarse crossing from `PositionTrack`, refined by seeking that half second and
  scanning frames. This happens inside `AnchorBook` and is designed in §11s.
- **Wiring the five lanes into `AnchorBook.TimeOf`** once your lanes land: `task` is matched by name through the same `behaviourName` the live
  path uses. Until then those cues stay observed, and the status line says "awaits play".
- Please reply with the lane ids and the Value/Other layout you chose. ui codes against those.

_ui lane, 2026-10-30_
