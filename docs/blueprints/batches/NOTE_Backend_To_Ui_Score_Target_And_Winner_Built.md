<!--STATUS
state: LIVE — backend → ui: your asks ⑤ (the Score root's target, CE-4232) and ⑥ (Crossfire's winner) are built and measured live
updated: 2026-11-03
current-answer: the whole note
stale-below: nothing
related-designs:
  - NOTE_Ui_To_Backend_Hud_Presets_And_Capture_Fields.md — your asks ⑤ ⑥ (①–④ answered in NOTE_Backend_To_Ui_Hud_Asks_Answered.md / _Presets_And_Capture_Fields_Built.md)
  - ../../DESIGN_Scenario_Rules.md — §00.12 G-9 (EndExercise winner) · §00.13 P-2 (the score target)
-->
# NOTE — backend → ui: **the score moves, and the round names its winner**

| ask | built | live (Crossfire, full 900 s round, `2026-11-03`) |
|---|---|---|
| ⑤ a target on a player's `Score` root (CE-4232) | `macros/game/score` root `target` **1000** ⇒ `Score` = `Value` / 1000 (clamped 0…1) — the `objective.score` lane now moves with every point. ⚠ above 1000 points it saturates at 1 (the best live round so far: 450) — say if you want a `Value` lane instead | 31 scores; the best `Score` 0.35 (= 350 points) |
| ⑥ the end decides win / lose | `EndExercise { reason, winner? }` — `"Friend"` / `"Hostile"` / `"Draw"` names the outcome (G-9, `8f6a3e40c`); absent ⇒ SR-122 unchanged. Crossfire's end is now three triggers: **`Blue wins`** (Red's tickets 0, or *Blue lead* > 0 at 900 s) · **`Red wins`** (the mirror) · **`Draw`** (level at 900 s); domination names its side too. On the wire: `EntityExerciseResult.FriendOutcome` / `HostileOutcome` (0 None · 1 Win · 2 Lose · 3 Draw), as before | *Red wins* fired at 900.0 s (*Blue lead* −70); the log: *"exercise ended at 900.799 s: Friend Lose 0.00, Hostile Win 0.00"* ⇒ your summary can say **RED WINS** |

_backend lane, 2026-11-03_
