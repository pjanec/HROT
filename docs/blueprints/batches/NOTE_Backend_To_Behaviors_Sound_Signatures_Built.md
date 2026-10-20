<!--STATUS
state: LIVE — backend → behaviors: Q95's handoff (S1–S6), built except S2; what your readers can now use (E1).
updated: 2026-10-20
current-answer: the whole note
stale-below: nothing
related-designs:
  - docs/DESIGN_Thermal_And_Acoustic_Sensing.md — §8.3 the ring, §8.4 the signature, §8.5 the real carries and the band
  - docs/blueprints/Architect_Question_95_Sound_Signatures_And_Lossless_Hearing.md — the approved leans (R-3024)
  - docs/DESIGN_Terrain_World.md — off the grids a unit is heard, not seen (the line before §5)
-->
# Note — backend → behaviors: sound signatures and lossless hearing, built

Answers [the handoff](HANDOFF_Behaviors_To_Backend_Sound_Signatures_And_Lossless_Hearing.md). Ids: `CE-3282` (S1), `CE-3283` (S3),
`CE-3284` (S4), `CE-3285` (S6); S2 stays `CE-3279`; `CE-3280` closed as superseded; ruling `R-3024` (Q95 approved).

| item | built | what you read |
|---|---|---|
| **S1** the ring (`CE-3282`) | every shot / burst goes into `SoundEmissionRing` (4096 entries, 30 s); each hearing sensor reads everything after its own cursor, whenever it runs | nothing new: a heard sound is still a `SoundContactEvent` — but now once per sound, never lost to a skipped solve |
| **S3** signature (`CE-3283`) | `SoundContactEvent` and `AudioTargetDetected` carry `Family` (byte, `SoundFamily`) and `Signature` (long, the TKB type: the weapon system for a shot, the munition for a burst) | ⭐ **E1 is yours now**: `StaffSystems.IsLaunch` can read `Family == Mortar \|\| Gun` instead of CE-2309's ≥ 1 km guess; `SourceClass` is unchanged |
| the ear's level | by `ProficiencyOf.Tier`: below Regular `Family = 0` and `Signature = 0` (the class only) · Regular: + family · Veteran / Elite: + the type | ⚠ a `0` means *"this man could not tell"*, never *"no weapon"* |
| **S4** real carries (`CE-3284`) | 60 mm 4 km · 81 mm 5 km · 120 mm 7 km · 105 mm 12 km · M109 18 km (burst = shot); the urban `MortarTeam` 5 km (it had the RPG's 500 m) | ⚠ on an 800 m terrain every soldier now hears every tube and gun — E3's *"guns 2–3 km off the terrain"* is inside every carry |
| **S5** the rule | Sensing §8.2 (distance × weather, on or off the terrain) and Terrain World (off the grids: heard, not seen) | — |
| **S6** band (`CE-3285`) | `AcousticSignatureDto.MovingBand` / `FiringBand` / `DetonationBand` authored on every firing weapon | ⛔ nothing reads it (WQ-E Q3) |

⚠ **Deviations, each argued in the design:** the ring is a MANAGED singleton the snapshot borrows by reference, guarded by a per-slot
sequence number (§8.3 — "unmanaged" would still be borrowed, and copied 256 KB at a time); the signature rides the answer as a 16-bit
ring token in `EqsResult`'s padding, turned back into the 64-bit type in the same solve (§8.4 — the 32-byte pins and recordings); the
family is in the answer's flag bits 13–15, which were free. The enum's members are `Untold` and `Rifle`, not `Unknown` / `SmallArms`
(the DDS IDL puts every enumerator in one scope, and `SoundSourceClass` owns those names).

⏳ **S2 (`CE-3279`, the solver timeouts on demo-city)** is next on backend.

---
_backend lane, 2026-10-20_
