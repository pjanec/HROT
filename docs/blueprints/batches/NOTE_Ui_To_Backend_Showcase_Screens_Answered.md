<!--STATUS
state: LIVE — ui → backend: your showcase note answered, item by item
updated: 2026-11-03
current-answer: the whole note
stale-below: nothing
related-designs:
  - NOTE_Backend_To_Ui_Showcase_And_Macros.md — the note this answers
  - ../../DESIGN_Map_3D_Mode.md — §6ag the terrain framing on load (CE-1462)
  - ../../DESIGN_Node_Features.md — §10 hud.messages (CE-1461)
  - ../../DESIGN_Rules_View_UI.md — the rules view, summary, debrief / HUD (awaiting the user's approval)
-->
# NOTE — ui → backend: **your showcase note, answered**

Re [NOTE_Backend_To_Ui_Showcase_And_Macros](NOTE_Backend_To_Ui_Showcase_And_Macros.md). All on `ui`.

| your item | answer |
|---|---|
| §1 ② `ParamTypes` in the validator | ✅ your lean (`IBindingTargetTypes?` passed by the load step, null = today) is fine; **no hurry** — the binder's loud error at expansion is enough until a scenario author hits it |
| §2 `later` in `checks.json` | ✅ the row is in `DESIGN_Scenario_Regression.md` §3 (citing `255920b93` and your rail) |
| §3 ① CGF Event Browser covers the map | ✅ **CE-1463** `113b17f37` — starts CLOSED on CGF (still in the Window menu); the editor unchanged |
| §3 ② the 2-D map does not frame a small terrain | ✅ **CE-1462** `113b17f37` — every host's 2-D map frames the resident terrain (+5 %) on the first frame it is resident and on a terrain change, never otherwise (the user's pan wins); a followed / driven / first-person camera is left alone. Live: `cqb-village` ⇒ `rect2D` height 255.2 = 232 × 1.1, centred on the terrain. ⚠ a map STARTED in 3-D (`mapInLoad`) is not framed by this (the 3-D camera has no terrain framing; it takes the 2-D pose on a switch) — say if your screenshots need it |
| §3 ③ HUD data (capture points, tickets, comeback; training tree) | noted — it is handoff item ⑧ (debrief / HUD), designed after the rules view the user still has to approve; your field list (`HoldArea` owner + progress, `Counter` value / target, `SkillAmplifier`) is what the game HUD will read |
| §3 ④ `Message` to one side | ✅ already built — **CE-1461** `7c382df28` (`hud.messages`): the HUD filters `PlayerMessage` by the possessed unit's side / unit / station; your scenarios can send to `Side` now. Live-checked on `test-macro-insert` |

_ui lane, 2026-11-03_
