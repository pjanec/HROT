<!--STATUS
state: LIVE — DISPATCHED at the commit that adds this file (2026-10-20); scope frozen there.
updated: 2026-10-20
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: CE-3280 (yours, asked of the user) — superseded by Q95 A, approved 2026-10-20
related-designs:
  - docs/blueprints/Architect_Question_95_Sound_Signatures_And_Lossless_Hearing.md — OWNS the approved answers (§5) and the leans they approve (§3)
  - docs/DESIGN_Thermal_And_Acoustic_Sensing.md — OWNS hearing; fold the as-built there (§6 B the ring, §6 D the signature, §8.2 the distance rule)
  - docs/DESIGN_World_Query_Seam.md — WQ-E OWNS the obstacle loss B3's band scales (not this batch)
-->

# HANDOFF — backend: a sound signature per weapon, hearing by distance, a lossless ring (Q95)

**For:** the backend lane (`backend`). **From:** the behaviours lane (`behaviors`). ⭐ **Dispatched at the commit that adds this file**: your
scope is FROZEN there; documents that change after it are FYI. If a later document invalidates an item, **STOP and report it** (R-106).

⭐ **A FRAME.** Step 1 is yours: read [Q95](../Architect_Question_95_Sound_Signatures_And_Lossless_Hearing.md) §2–§5 and your own
[Sensing](../../DESIGN_Thermal_And_Acoustic_Sensing.md) §6 B / §6 D / §8.2, draw the change into Sensing (class + sequence), build, fold the
as-built there. The ids are yours to allocate.

🔒 **User, 2026-10-20:** *"Approved."* — A, B, C, D, E as leaned. ⚠ B3 (the band) is leaned, not yet nodded: build only its TKB field.

## 1. The items, in order

| # | item | acceptance |
|---|---|---|
| **S1** (Q95 C) | ⭐ **the lossless ring**: an UNMANAGED singleton of emissions `{serial, where, carry, kind, class, signature}`; each hearing sensor keeps the last serial it consumed and reads everything after it, whenever it runs; kept until every live sensor passed it, bounded by N entries / T s, an **overflow counter** on the diagnostics. The 0.5 s linger goes; the viewer's audio keeps its own path. Size N from shots/s on demo-city × the worst skip | a rail: one shot, the sensor skipped for 10 s, then run ⇒ heard once; two shots of one gun inside 0.5 s ⇒ heard twice; overflow ⇒ counted. ✅ red-proof each |
| **S2** (CE-3279) | the solver timeouts on demo-city — still needed: the ring keeps the sounds, but perception is dark while skipped | yours, as you filed it |
| **S3** (Q95 A) | ⭐ **the signature** = the emitter's TKB type (the weapon system for a shot, the munition for a burst) on the answer, `SoundContactEvent` and `AudioTargetDetected` — a field, not a packing trick (the 16 flag bits are full). The **ear's identification level** by proficiency: below Regular the class only, Regular the family (mortar · gun · heavy MG · AA gun · launcher), Veteran the type. The class stays | a rail per level; the 19 class readers keep compiling. ⛔ CE-3280's `Artillery` class is NOT built — close it as superseded |
| **S4** (Q95 B1) | the real carries per signature (a 155 mm 15–20 km; the mortars and the 105 mm your numbers, with the source) | the data; the estimate's radius grows with distance as today |
| **S5** (Q95 B) | state the rule in Sensing §8.2: a loud sound is heard by distance × weather wherever its source stands, on or off the terrain; and in Terrain World §4.4: off the grids a unit is heard, not seen, no collision | the two design lines |
| **S6** (Q95 B3) | a TKB `Band` (`Low` · `Mid` · `High`) on each signature, authored with S4. ⛔ nothing reads it yet — WQ-E Q3 will | the field and its data |

## 2. Fences

- ⛔ The readers are behaviors' (Q95 E1: `StaffSystems.IsLaunch`, `VehicleFightFromCoverNodes`, the squad merge) — tell me when S3 lands.
- ⛔ D (demo-city's bounds) is not this batch.
- ⛔ No obstacle loss (WQ-E) here.

## 3. Report

Per the gate contract: the suites (`Fdp.Toolkits.Tests` perception, by filter — `DEBT-AIB-030`), the new rails with red-proofs, the ids
allocated, and the design sections folded.
