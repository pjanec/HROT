<!--STATUS
state: LIVE
updated: 2026-11-19
current-answer: the whole note
related-designs:
  - ../../designs/fun/DESIGN_Dragon.md — OWNS the dragon (R-3112, approved): DR-11 the look is this note's subject; §0 / §3 the type it is drawn from
  - ../../designs/weapons/DESIGN_Flame_Weapons.md — §8 the flame look ui already built: the breath is that jet, unchanged
  - ../../designs/vehicle-appearance/DESIGN_Vehicle_Appearance_State.md — §7 the look library (VehicleLook) a rotor / engine level is read from
-->
# NOTE backend → ui — the DRAGON's look and sound *(CE-4634, [DESIGN_Dragon.md](../../designs/fun/DESIGN_Dragon.md) DR-11; approved R-3112, `2026-11-19`)*

🔒 User: *"Dragon approved. Please record a note for the UI lane for the visuals."* · the wish: *"similar size [to the Game of Thrones dragons] …
the player could be sitting on it … a passenger seat like on a motorcycle."*

⭐ **The dragon is a ROTORCRAFT to the simulation** (a helicopter in disguise) — so today it would be drawn as a helicopter. This note asks for its own
look. ⭐ **Mockup first** (R-4143), then build, then check fidelity yourself (R-4146). Nothing for ui to wait on: backend's D-1 builds the TKB type
(a fun-pack id, `FunTkbCatalog`); until it lands, a test type with the same descriptors stands in.

## What to draw

| part | ⭐ ask | what the sim gives you |
|---|---|---|
| **family** | `VisualFamily.Dragon`, classified from the type (not from `Rotorcraft`) | the TKB type id / a classification the D-1 type carries — ⚠ agree the key with backend on D-1 (lean: the classifier keys on the type's DIS entry, as `Helicopter` does) |
| **body kit** | a `ShapeKit` of box parts: body, NECK, head, tail, four legs, two wings — ~20 m nose to tail, 26 m wingspan, ~6 m standing | the body geometry box (D-1) |
| **the neck and head** | posed by the type's TURRET 0 (the breath is on it): azimuth ±70°, elevation −60…+15° | `TurretParts.PoseOf(world, dragon, 0)` — the same pose the AH-64's chin gun is drawn from |
| **wings** | a SLOW beat — ≈ 1 Hz climbing or slow (< 10 m/s), a glide (wings out, still) at speed, folded on the ground; ⛔ not the bird's 6.5 Hz (`AnimalPoses.Flap`) | speed and climb from the body (`MotionTracker`); on the ground ⇔ the flight state's `Airborne = 0` (landed on its legs) |
| **legs** | down and planted when landed, tucked in flight | the same `Airborne` flag |
| **rider + pillion** | both men drawn SEATED astride the neck base and behind it (`SeatPose.Ride`); the pillion turns and aims his own weapon in his arc | `CarrierSeats` seat 0 (rider, Driver) and seat 1 (pillion, a firing seat) — ⚠ today the seated rider is drawn only on a HORSE (`EntityBodyLayer3D.cs:1469`): make it any `SeatPose.Ride` carrier |
| **the breath** | ⭐ nothing new — the flame jet look ui built for FL-1 (`DESIGN_Flame_Weapons` §8), leaving the MOUTH | backend D-1 moves the jet's origin to the mount's muzzle (the mouth) — the same point the flame look should start from |
| **dying / dead** | falls (the rotorcraft model drops a dead body), lies wings spread; burning wreck NOT — a dragon is fireproof | `Health`, the wreck as for any aircraft |
| **possessed** | ⭐ the dragon stays drawn when its rider is the player (like a horse: it has no cab) | — |
| **2-D map** | a dragon symbol in the fun pack's style (not the helicopter's) | — |

## ⭐ Sound — the dragon ROARS *(user, `2026-11-19`: "leave a note for the UI session that the dragons needs to make a roaring sound")*

⛔ Today a dragon would sound like a HELICOPTER: `SoundSources` falls back to `DefaultRotor` for any `LocomotionClass.Rotorcraft`
(`Hrot/Engine/Hrot.Presentation/Audio/SoundSources.cs:383`). ⭐ What the two lanes do:

| | ⭐ ask | lane |
|---|---|---|
| the type's sound | the dragon type declares its own `AcousticSignatureDto` (so the rotor fallback never applies): its MOVING sound is a wingbeat, its roar a new `SoundSourceClass` (lean: `Roar`), heard ~1.5 km | backend, D-1 — ⚠ the class id is agreed in D-1's commit; ui reads it |
| the WINGBEAT | a slow, heavy whoosh per beat, following the wing pose above (beating ⇒ whooshes; gliding ⇒ wind only; landed ⇒ silent) | ui (`SoundSynthesizer` placeholder, or a file named by the type's `SoundDto`) |
| the ROAR | a deep beast's roar: ⭐ when it starts to breathe fire (with the flame's own roar, `SoundSourceClass.Flame`), when it takes off, when hit, and now and then while flying (not a loop) | ui — the trigger from sim state the look already reads (the breath's burst record, `Airborne` 0 → 1, a `Health` drop); ⛔ never a sound driven by the wall clock alone in the sim |
| dying | a long roar falling away | ui |

## Acceptance — rails, not a person looking

- a frame rail per mockup state: flying (wings beating), gliding, landed (wings folded, legs planted), breathing (neck turned, jet from the mouth),
  rider + pillion seated — measured against the mockup (R-4138), run under `xvfb-run -a`;
- the wing pose follows speed / climb / landed (a pose rail on the kit);
- 0 B per steady frame (the hot-path rule).

⭐ Scope: the dragon's own look only. ⛔ The helicopter's look, the flame look and the horse rider are unchanged except where this note says (the
seated rider generalised to any `SeatPose.Ride` carrier).
