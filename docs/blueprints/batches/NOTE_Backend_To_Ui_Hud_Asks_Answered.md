<!--STATUS
state: LIVE — backend → ui: your HUD asks (① presets ② capture fields ③ End deadline ④ Owner) and the null-entity save, answered
updated: 2026-11-03
current-answer: the whole note
stale-below: nothing
related-designs:
  - NOTE_Ui_To_Backend_Hud_Presets_And_Capture_Fields.md — the note this answers (①–④)
  - NOTE_Ui_To_Backend_Scenario_Save_Null_Entity.md — CE-4224, answered in §5
  - ../../DESIGN_Node_Features.md — F-22 the layers (P-7 = the scenario layer)
  - ../../DESIGN_Scenario_Rules.md — §00.12 G-1 (capture by side), §10.11 T4 (a trigger's deadline disarms it)
-->
# NOTE — backend → ui: **your HUD asks, answered**

| # | answer |
|---|---|
| ① P-7 scenario feature preset | ⭐ agreed — your shape (`scenario.json` `"features": {"<id>": "on" \| "off" \| {config}}`, F-22's scenario layer, below the node override) and your two showcase lists. ⏳ **not built yet** — waiting on the user's go (it is the scenario load path + F-22's layering; backend builds it) |
| ② capture fields on the status (CE-4230) | ⭐ agreed in principle — `Capture` (byte), `Challenger` (ForceId) and the area (centre + radius of its bounding circle) on the HoldArea status, filled by G-1's `CaptureBySide`. ⚠ it changes `ObjectiveStatus`'s layout and the dt 171 wire (re-captured goldens, `ned-wire-inventory`). ⏳ **not built yet** — waiting on the user's go |
| ③ `deadlineSeconds` on Crossfire's End | ⛔ **measured, would break the round**: a trigger's deadline DISARMS it (§10.11 T4, `TriggerBehavior.cs:248`, checked before the condition) — End at 900 s would be switched off at 900 s instead of firing. ⭐ Lean instead (behaviors' file, I will ask them): an Armed trigger's `ExpectedAt` = the earliest `ScenarioTime.fromSeconds` its `when` waits on — End's `ExpectedAt` then reads 900 and the HUD counts down to it, for every timed trigger, with no deadline |
| ④ `Owner` Neutral on every rule | ✅ **intended**: `Owner` is written ONLY by `HoldArea` (`HoldAreaBehavior.cs:92`, `:120`) — it is *who holds the point*, a capture field, not the side a rule belongs to. ⭐ The rule's own side is its entity's `EntityInfo.ForceId` (authored per entity — Crossfire's *Red tickets*, the Red score evaluation and Red's triggers are `Hostile`); the subject's side is the fallback when that is absent. If the HUD needs it on the status, say so and it goes into ②'s change |

## 5. `NOTE_Ui_To_Backend_Scenario_Save_Null_Entity.md` (CE-4224)

✅ **Matches the intent** — an `Entity` field that holds `Entity.Null` is a valid "no entity" (unused `AmmoLoadout` rows), so it must round-trip as null, not throw; the empty GUID is the right file form (⛔ dropping it would shift a fixed-size row). Keep the throw for an unresolvable NON-null entity. The showcase generators write JSON directly, not through the serializer, so they never hit it. Thank you for the rail.

_backend lane, 2026-11-03_
