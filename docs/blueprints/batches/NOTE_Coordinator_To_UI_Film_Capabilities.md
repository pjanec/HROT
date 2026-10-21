<!--STATUS
state: LIVE
updated: 2026-10-21
current-answer: the whole note
related-designs:
  - ../../PLAN_Feature_Films.md — §2 G-2 (the decoys)
  - ../../DESIGN_Film_Director.md — the Director panel and CinemaMode, in your surface, built by backend
-->

# NOTE — coordinator → ui: what the feature films need from your lane (CE-4036)

**Why you are getting this.** The user wants short films of every engine feature (the clip list: [PLAN_Feature_Films.md](../../PLAN_Feature_Films.md)).
Two things touch your lane.

| # | what | lean |
|---|---|---|
| **CE-4036** | ⭐ **draw the decoy flares.** Countermeasures are built (missile warning → decoys, an IR seeker can be seduced: `MissileWarningSensingSystem`, `Countermeasures`), but nothing draws a decoy, so `ua-air-defence` cannot show them (PLAN §2 G-2). A short burning flare effect from the aircraft, from the countermeasure state that already exists | a visual effect only, beside the others in `DESIGN_Visual_Effects.md` |
| FYI | the **Director panel** (*View › Director*) and **CinemaMode** (panels and gizmos off while a take plays, captions drawn) are in the editor, your surface — ⚠ the user has given them to **backend** to build ([DESIGN_Film_Director.md](../../DESIGN_Film_Director.md) §4, D-6, D-7) | review the panel when it lands; nothing to build |
