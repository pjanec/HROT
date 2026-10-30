<!--STATUS
state: LIVE — backend → ui: CE-4117 backend half built — five STATE index lanes for the director's state cues
updated: 2026-10-30
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Director_Timeline_Editor.md — §9e (the lane kind and the five lanes, as built)
  - NOTE_Ui_To_Backend_State_Cue_Index_Lanes.md — the note this answers
-->
# NOTE — backend → ui: the state-cue lanes are in (CE-4117, backend half)

**Dispatched at** the commit that adds this file (`backend`).

⭐ One new lane KIND (`ValueChangePredicateDto` → `TimelineLaneCompiler`'s `ValueChangeLane`, no per-lane code — R-4079) and five lanes as
data in the existing providers, so `ShippedTimelineLanes.Catalog()` gives them to the editor's live index, the Replay Browser and every node.

## The contract you code against

An entry is `(frame, Subject, Other, X, Y, Value)`; Subject = the unit's network id (else `-(index+1)`), as every lane.

| lane id | Other | X | Value | an entry when |
|---|---|---|---|---|
| `state.task` | `ActiveBehaviorHash` — **exact; match on this** | 0 | the hash as a float (lossy) | the hash changes |
| `state.embark` | the carrier's id | 0 | 1 aboard · 0 when the tag is removed | gained / lost |
| `state.side` | 0 | 0 | `ForceId` (Neutral 0 · Friend 1 · Hostile 2) | it changes |
| `state.health` | 0 | `Max` | `Current` | either changes |
| `state.gesture` | 0 | 0 | `GestureKind` (removal ⇒ 0 = None) | it changes |

| ⭐ rules | |
|---|---|
| **frame 0 counts** | every state a unit starts with has an entry on the first frame it is seen — *"side is Red"* holds from the start |
| removed while alive ⇒ one entry (value 0, other 0); a death / departure ⇒ nothing | `life.left` says it left |
| a unit with no `Gesture` component has no `state.gesture` entry until it gains one | read "no entry yet" as None |
| no sidecar schema bump | the sidecar hashes each lane's definition, so an old sidecar indexes just these five on its next open |

## ⏳ For you

- **`Hidden`**: `TimelineLaneDescriptor` gained `bool Hidden = false` (last parameter); the five set it. ⚠ The panel does not read it yet, so
  they draw — `ReplayBrowserSubsystemTests.CE1302_TheEventTimeline_FollowsThePrimaryRecording…` now counts **14** lanes, with a comment that it
  returns to 9 when the panel honours `Hidden` (a cross-lane edit of your test — the count only).
- **`AnchorBook.TimeOf`**: wire `task` / `side` / `health` / `embark` / `gesture` to these lanes (yours, per your note).

Rails: `TimelineIndexTests.CE4117_EachStateLane_…_AndFrameZero` (red without a lane; red without the frame-0 entry),
`CE4117_TheStateLanes_LiveEqualRebuilt`. Design: [§9e](../../DESIGN_Director_Timeline_Editor.md).

_backend lane, 2026-10-30_
