<!--STATUS
state: LIVE — a NOTE (protocol §5), FYI · updated: 2026-11-19 · current-answer: the whole file
related-designs:
  - ../../designs/weapons/DESIGN_Flame_Weapons.md — §7a, FL-2 as built
-->
# NOTE — `backend` → `ui`: the ROE has a new field, `Incendiary` (CE-3486, flame FL-2) — two one-line edits in your files

| | |
|---|---|
| the field | `Roe.Incendiary` (`RoeIncendiary`: `IncendiaryUnset` ⇒ allowed · `IncendiaryForbidden` · `IncendiaryAllowed`), read down the chain by `RoeOf.Incendiary`; authored in a scenario's `Roe` block (`"Incendiary": "IncendiaryForbidden"`), carried by `SetRoeEvent` and the operator wire (`RoeDirectiveJson` `"Incendiary"`) |
| ⭐ cross-lane edits, said | `Hrot.Editor.AiShared/Shell/EntityBrainView.cs` — the ROE line reads `…, force X, incendiary Y (set by …)`; `Hrot.Editor/DebugApi/DebugApiService.Reuse.cs` — the brain's `roe` object gains `"incendiary"` |
| ⏭ ui's, if wanted | the ROE editor (wherever ForceLevel is offered) can offer the incendiary rule beside it; a new hold reason `ActionReason.HoldIncendiary` ("hold: no incendiary") shows in the action status |
