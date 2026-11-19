<!--STATUS
state: LIVE — a NOTE (protocol §5), FYI + a question for backend · updated: 2026-11-19 · current-answer: the whole file
related-designs:
  - ../../designs/weapons/DESIGN_Flame_Weapons.md — §6 the look contract; ui's look section (the jet replaces the muzzle flash)
-->
# NOTE — `ui` → `backend`: the flamethrower soldier is HEARD as a rifle, 10 times a second (CE-4635 FL-1)

Read on `origin/backend` (`ca674b085`), not yet merged into `ui`: `UrbanCombatTkbCatalog`'s `FlamethrowerSoldier` (2022) takes the soldier's sound set —
`Sounds(FootstepsRange, SoundSourceClass.Footsteps, RifleShotRange, SoundSourceClass.SmallArms, RpgBurstRange)` — and his fuel fires at 600 rounds a minute,
each a fired `WeaponFireNotification`. ⇒ every fuel shot is a **SmallArms shot sound** at rifle range: the AI hears gunfire (10 shots a second), and the
audio plays a rifle burst.

| | |
|---|---|
| ui does | the LOOK: a Flamethrower mount's shot gets **no muzzle flash** (the jet is its look) — in ui's flame-visuals build (`EffectRules.ForShot`) |
| ⏭ backend's call | the HEARING / sound class of a flame burst: `SoundSourceClass` has no flame class today (Unknown, Footsteps, WheeledEngine, TrackedEngine, SmallArms, HeavyWeapon, Explosion, Rotor, Animal). ⭐ ui's lean: a burst is one continuous roar, not N shots — heard once per burst (or per second) at a shorter range than a rifle; the sound ui plays then follows the class |
