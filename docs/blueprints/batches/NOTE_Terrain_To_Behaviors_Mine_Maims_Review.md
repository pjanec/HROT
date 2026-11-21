<!--STATUS
state: LIVE — terrain's review of NOTE_Behaviors_To_Terrain_Mine_Maims.md (D-3, `3418dd0c8`)
updated: 2026-11-18
current-answer: the whole file
related-designs:
  - ../../designs/minefields/DESIGN_Minefields.md — MF-5, the CE-4654 / D-3 row
-->

# NOTE — terrain → behaviors: review of the AP-mine maim (D-3) — ✅ accepted, three small findings

✅ **Accepted.** One query per AP burst (never per frame); CE-2446's blast damage (`MunitionTkbCatalog`) and CE-5019's crater skip
(`AreaEffectSystem._spentOnWall`) are untouched; AT mines and the line charge unchanged.

| # | finding | `file:line` (terrain at the merge) | lean |
|---|---|---|---|
| 1 | ⚠ **the trigger man is GUESSED** — the nearest living person within 1.5 m of the burst; the trigger already KNOWS who stepped on it (`MineStruck.By`, published beside the burst) ⇒ a comrade 0.3 m off the mine can be maimed while the man on it gets the ordinary blast | `AreaEffectSystem.cs:173, :623` · `MineTriggerSystem.cs:339` | ⭐ terrain can carry the body on the burst (a `DetonationNotification` field the trigger fills for 6051, never a `Target` — the AP burst stays target-less); you then read it instead of `TriggerManOf`. ⇒ say the word and it is a small terrain row |
| 2 | ⚠ `TriggerManOf` does not skip a HIDDEN body (an embarked passenger, `PhysicalPresence.IsHidden`) — the ordinary loop does, one line below the maim | `AreaEffectSystem.cs:178-179` | add the same `IsHidden` skip in `TriggerManOf` |
| 3 | ⚠ doc comments: the new members were inserted between `IsPerson`'s `<summary>` (CE-4584) and `IsPerson` ⇒ `TriggerManRadius` carries two summaries, `IsPerson` none | `AreaEffectSystem.cs` before `TriggerManRadius` | move the CE-4584 summary back above `IsPerson` |
