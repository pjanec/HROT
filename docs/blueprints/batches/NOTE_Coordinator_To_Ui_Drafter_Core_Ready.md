<!--STATUS
state: LIVE — coordinator → ui: the drafter's pure core is built (CE-4059 / CR-8); wire it in P3; one input for CR-7 (P2b)
updated: 2026-10-27
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Director_Timeline_Editor.md — §11n the drafter (rules, as built, deviations); §11e CR-7, CR-8
  - NOTE_Coordinator_To_Ui_Films_From_Recordings.md — the P3 row this fills
-->
# NOTE — coordinator → ui: the drafter is built — wire it (P3); a key belongs to a segment (CR-7)

**Dispatched at** the commit that adds this file (`coordinator`). FYI to [the films note](NOTE_Coordinator_To_Ui_Films_From_Recordings.md); its scope is unchanged.

| | |
|---|---|
| built | `Hrot.Presentation/Map3D/Director/Drafter.cs` — `Drafter.Moments(index, unitName)` (the candidates, scored, time order) and `Drafter.Draft(index, moment, unitName, options)` → an ordinary v2 `Take` (keys anchored on the moment's event, its time baked). Pure: no world, no I/O. `EventAnchors.Of(index, lane, eventIndex, unitName)` added; `NearestBefore` calls it (one naming switch). Rails `DrafterTests.CE4059_CR8_*` ×11. [§11n](../../DESIGN_Director_Timeline_Editor.md) has the rules and the as-built deviations |
| ⏭ yours (P3) | the right-click item *"Draft a shot sequence here"* on an event (→ `Draft`, opened in the take session) · `POST /director/draft {recording?, lane, eventIndex}` and `GET /director/moments` (→ `Moments`) · `DraftOptions.Kind` from the world's catalogue (person · vehicle · aircraft · group) |
| ⭐ an input for CR-7 (P2b) | a drafted replay is a time segment that goes BACK (`[t−1, t+1.5]` at 0.4) after the main pass. Keys are placed by SOURCE time, so its reverse-angle key cannot sit at its own time (it would cut the main pass) — it is parked at the main pass's end. ⇒ **when segments play, a key should belong to a SEGMENT** (looked up by film time within its segment), or the replay never shows its reverse angle. Your design to make |

_coordinator lane, 2026-10-27_
