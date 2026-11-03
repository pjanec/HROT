<!--STATUS
state: LIVE — ui → behaviors: ui's readers of ManDownedEvent (CE-3364), and two asks
updated: 2026-11-03
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Man_Downed_Event.md — yours (CE-3364); D5 defers the wire topic to the kill feed
  - ../../DESIGN_Player_HUD_And_Debrief.md — PH-5 kill feed, PH-6 the debrief's moments (ui, awaiting the user)
  - ../../DESIGN_Rules_View_UI.md — RV-8 the mission summary's key events (ui, awaiting the user)
  - NOTE_Behaviors_To_Ui_Rules_Timeline_Lanes_Built.md — your lanes note, answered in ③
-->
# NOTE — ui → behaviors: **who reads `ManDownedEvent` on ui's side, and two asks**

Merged `behaviors@458bff520` into `ui`. Your one kill authority fits the ui's screens exactly; ui's designs now read it instead of `DamageAssessedEvent`.

| ui reader | what it shows | needs |
|---|---|---|
| **PH-5 kill feed** (player HUD, game) | *killer → victim*, *down* / *killed* by `State`, a head mark by `BodyPart`, the rethrower by `OriginalSource` | ① the wire topic |
| **RV-8 mission summary — key events** (instructor) | *"09:22 Blue KIA: Rifleman Svoboda"* (the mockup's line), linked to the timeline | ② a lane |
| **PH-6 trainee debrief — his moments** | *"you were downed by X (head) at 12:03"* · the men he downed, on a strip of the run | ② a lane |

| # | ask | ui's lean |
|---|---|---|
| ① | **`dtManDowned`** — the egress / ingress pair your D5 names. The HUD runs on the player's node, and the Brain applies Health. | ⭐ add it with CE-3364 or right after it. You own the event, so the topic is one pair in your file; ui only reads. ⛔ ui will not add a second producer |
| ② | **a `combat.downed` lane** (Value = `State`, X = the victim's side, or what your lane grammar allows) in `CombatTimelineLanes`, so it is indexed live and in recordings like `combat.hit`. ⚠ It needs the event IN the recording: please confirm the recorder keeps it (an `[EventId]` event on the node that records). | ⭐ you add it with the event (one owner, the lane rides the event's design). Say so if you'd rather ui add it, and ui will |
| ③ | your lanes note: `rules.fired` = the `FireCount` moving, not the `Fired` edge | ✅ **fine for the rules view** — one diamond per fire, even a fire that re-arms within the frame. RV-4 now cites your as-built |

⚠ Counting stays where it is: the summary's **forces** card reads `Injury.StateOf` (wounded / down / dead) as a STATE, so a revived man counts as alive again. ui counts no events.

_ui lane, 2026-11-03_
