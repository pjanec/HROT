<!--STATUS
state: LIVE — a STEER on a RUNNING handoff (2026-10-15); it ADDS to item P3, it withdraws nothing.
updated: 2026-10-15
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/blueprints/batches/HANDOFF_Ui_To_Backend_Presets_Elevation_Attitude.md — the running handoff; this widens its P3 (unchanged in place, rule 1)
  - docs/DESIGN_Dead_Reckoning.md — owns the replicated transform P3 completes
  - docs/designs/air-attack/DESIGN_Air_Operations.md — the attitude (bank, pitch) the aircraft models write
  - docs/DESIGN_Map_3D_Mode.md — §8f / §6s: the ui drawings that read the restored rotation
-->

# STEER — backend, handoff P3: EVERY ingress restores the full attitude, not just the heading

**For:** the backend lane (`backend`), running [HANDOFF_Ui_To_Backend_Presets_Elevation_Attitude](HANDOFF_Ui_To_Backend_Presets_Elevation_Attitude.md)
(started at `64aaca945`, marker `390d1313b`). **From:** the ui lane. ⭐ The handoff is not edited (rule 1); **this file adds to item P3**. Items
P1, P2, P4 are **unchanged**. The user relays it (rule 1c ③).

🔒 **User, `2026-10-15`:** *"Ingress shouldnt restore just heading pls add to backend handoff as well."*

## What P3 missed

P3 cited one ingress site (`GeoSpatialIngressTranslator.cs:73`) and asked egress to fill pitch and roll. 📐 Measured: **egress already does**: NED
`GeoSpatialEgressTranslator.cs:194` and BDC `BdcWorldPosTranslator.cs:134` both call `SimTransformBridgeSystem.RotationToPitchRollDeg` and send
`Ori.Pitch` / `Ori.Roll`. ⇒ the attitude **is on the wire today and every receiver throws it away**. These are all the production sites that
rebuild a rotation from the heading alone (`grep HeadingDegToRotation`, `Hrot/` + `FDP/`, tests excluded):

| # | site | path |
|---|---|---|
| 1 | `Hrot.Network.NED/…/Ingress/GeoSpatialIngressTranslator.cs:73` | NED descriptor ingress (create) |
| 2 | `…/GeoSpatialIngressTranslator.cs:148` | NED descriptor ingress (update) |
| 3 | `Hrot.Network.NED/…/Utils/DescriptorMapper.cs:130` | initial descriptors, the CGF spawn path (`NedCgfEntityLifecycleAdapters`) |
| 4 | `…/DescriptorMapper.cs:362` | the descriptor → `SimTransform` patch |
| 5 | `Hrot.Network.BDC/Replication/BdcWorldPosTranslator.cs:201` | BDC ingress |
| 6 | `…/BdcWorldPosTranslator.cs:231` | BDC ingress |
| ⚠ 7 | `Fdp.Toolkits/Replication/Attributes/SimTransformHeadingInstaller.cs:60`, `AttributeCompilerFactory.cs:109` | a HEADING-ONLY attribute patch: ⛔ it must **keep** the entity's current pitch and roll, never reset them to level |
| — | `Hrot.IG/IgApplication.cs:2217` (`TestHook_…`), `Hrot.Editor/DebugApi/DebugApiService.cs:2262` | a test hook and a debug "set heading" — heading-only by intent; leave them, or give the hook optional pitch / roll |

## The lean

⭐ **One helper beside the inverse it mirrors:** `SimTransformBridgeSystem.AttitudeDegToRotation(heading, pitch, roll)`, the exact inverse of
`RotationToHeadingDeg` + `RotationToPitchRollDeg` (Q59-C1's rule: one formula, every caller, zero copies). Sites 1–6 call it with `Ori.Heading`,
`Ori.Pitch`, `Ori.Roll`; `HeadingDegToRotation(h)` stays as `AttitudeDegToRotation(h, 0, 0)`. Site 7: rebuild from the new heading + the current
rotation's pitch and roll. ⛔ No new wire field; ⛔ no second conversion.

## Acceptance (adds to P3's)

- ★ a round-trip rail: for headings × pitches × rolls (incl. ±80° pitch, ±60° bank), `RotationTo…(AttitudeDegToRotation(h, p, r))` returns
  `(h, p, r)` within 0.1°.
- ★ each of sites 1–6: a descriptor with pitch 10° / roll −20° gives a `SimTransform.Rotation` with that pitch and roll (one rail per path:
  NED create, NED update, the CGF initial descriptor, the mapper patch, BDC).
- ★ a heading-only patch on a banked entity keeps its bank.
- ⚠ gate-report row 8: the integration suite that proves two nodes agree on a transform (P3 already asks for it).
