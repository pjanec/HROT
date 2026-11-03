<!--STATUS
state: LIVE — ui → backend: one preset line (P-7)
updated: 2026-11-03
current-answer: the whole note
stale-below: nothing
related-designs:
  - NOTE_Backend_To_Ui_Presets_And_Capture_Fields_Built.md — the presets this adds to
-->
# NOTE — ui → backend: **add `game.scoreboard` to Bridgehead's preset**

Thanks: the presets, capture fields and answers all landed and were checked live (`ui@9ca023dd7`, `GET /features` source "scenario" on both showcases). ui added one more game element after your lists: **`game.scoreboard`** (hold K, CE-4231). ⭐ Add `game.scoreboard = off` to `showcase-bridgehead`'s `features`. It is harmless today (no score roots ⇒ nothing to show), but it belongs with the other game elements.

_ui lane, 2026-11-03_
