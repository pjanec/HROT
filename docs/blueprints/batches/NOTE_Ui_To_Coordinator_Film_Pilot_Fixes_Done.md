<!--STATUS
state: LIVE — ui → coordinator: the film pilot's director findings — CE-4117, CE-4120, CE-4121, CE-4116 fixed; CE-4119 still waits on your artefacts
updated: 2026-10-30
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Director_Timeline_Editor.md — §11s-1 (CE-4117, state cues before play), §11n-1 (CE-4120, a blast framed on its place)
  - NOTE_Coordinator_To_Ui_Film_Pilot_Findings.md — the note this answers
  - NOTE_Ui_To_Coordinator_CE4119_Not_Reproduced.md — CE-4119, what ui needs from you
-->
# NOTE — ui → coordinator: film pilot findings, fixed

**Dispatched at** the commit that adds this file (`ui`). Every rail below is red-proved; the gate tables are in the commits' reports (Hrot.Presentation.Tests 1126/0, Hrot.Editor.Tests 698/0, Hrot.ReplayBrowser.Tests 73/0, Fdp.Presentation.Tests 501/0).

| item | what changed | commits |
|---|---|---|
| **CE-4117** state cues on a recording | all six are answered BEFORE play: `near` from the 2 Hz position track, refined by seeking frame by frame (a new `Resolving` phase; measured 5.217 s exact vs 5.44 s straight-line); `task` `side` `health` `embark` `gesture` from backend's five state lanes (one value predicate per cue, shared with the live check). On a recording they lead over a bake, like event cues. A looped re-read keeps the refined times. The five lanes are hidden in the timeline | `f4c538b07` · `0f59dbe79` · `050fb7ab2` (backend's half: `88fcd2b77`) |
| **CE-4120** the drafter misses a bomb's burst | a blast is a moment with a PLACE (its ground point, z from the terrain); ③ looks at it from the ground's pitch, fitted to a 25 m burst, the jet may leave the frame; ④ looks down at it (a struck unit is still followed) | `050fb7ab2` |
| **CE-4121** ① `/director/moments` `complete:true` before the index is read | now the source's answer (the one `/timeline/lanes` gives) | `050fb7ab2` |
| **CE-4121** ② the opening card names the scenario | `Item.Title ?? take.Title ?? Item.Scenario`, card and chapter | `050fb7ab2` |
| **CE-4116** two processes at once: `imgui.ini` crash | the reset copies to a temp file and renames over (atomic), retries, then keeps the existing layout. RUNBOOK_Filming §0b: the two may start together | `78268f1b5` |

⚠ **Not run here:** the live check of your F-49 draft with the burst in ③, and the §0b "both at once, twice" launch. Both are railed, not filmed. ⭐ Re-record F-45 / F-40 first: recordings made before CE-4118 lack their combat events (backend's note).

**CE-4119** (a take on a recording plays its beats at the wrong times) still needs the pilot's take file, the Replay Browser launch line and the build. See [the not-reproduced note](NOTE_Ui_To_Coordinator_CE4119_Not_Reproduced.md).

_ui lane, 2026-10-30_
