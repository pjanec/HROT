<!--STATUS
state: LIVE — a STEER on a RUNNING batch (backend's Air Defence, CE-2187..CE-2190, started at 2270e0a85); it changes CE-2188's TRANSPORT only.
updated: 2026-10-15
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: docs/designs/vehicle-tactics/DESIGN_Missiles_And_Ammo.md §3c draws a DispenseCountermeasure message (dt 134) — marked SUPERSEDED there by R-1002.
related-designs:
  - docs/designs/brain-split/BS-1-DESIGN.md — §5.4b, the design this steer points at (fire notification fields, F4 the release as a fire, F6 DecoyFlight)
  - docs/designs/vehicle-tactics/DESIGN_Missiles_And_Ammo.md — §3c, CE-2188's design: decoys, seduction, counts — unchanged except the message
  - docs/DESIGN_Visual_Effects.md — the ui side: the flare / chaff looks drawn from the fire event (CE-1111)
-->

# STEER — backend, CE-2188 (flares and chaff): the release is a FIRE, both ways — no countermeasure message

**For:** the backend lane, running the Air Defence batch ([handoff](HANDOFF_Behaviors_To_Backend_Air_Defence.md), started at `2270e0a85`).
**From:** the ui lane. The handoff is not edited (rule 1); this file changes **CE-2188's transport only**. CE-2187, CE-2189, CE-2190 and
CE-2188's decoy, seduction, counts and interval are **unchanged**. The user relays it (rule 1c ③).

🔒 **User, `2026-10-15` (R-1002):** *"Weaponfire notification can not be missing the crucial parameters, same like DIS does not. It must be a
buf. Regarding countermeasures no specific message please. We need something for generic single shot effect activation. If dis can do it as
fire, why not us?"*

## What changes for CE-2188

| | was (Missiles & Ammo §3c) | now ([BS-1 §5.4b](../../designs/brain-split/BS-1-DESIGN.md)) |
|---|---|---|
| brain → SimHost | `DispenseCountermeasureIntent` + DDS `DispenseCountermeasure` (dt 134) + two translators | ⭐ the existing `WeaponFireIntent` / `WeaponFireRequest`: `WeaponIndex = WeaponSlots.Countermeasures` (-1), `AmmoGuid` = the flare or chaff munition type |
| on the SimHost | `CountermeasureSystem` reads the intent | `FireProcessingSystem` skips the Countermeasures slot; `CountermeasureSystem` takes those intents and spawns the decoys |
| SimHost → everyone | nothing (decoys never replicated) | ⭐ a `WeaponFireNotification` for the release: `Shooter` = the aircraft, `WeaponIndex` = Countermeasures, `AmmoGuid` = the expendable type, `Origin` / `Velocity` = the aircraft's, `Quantity` = the salvo, `FiredAt` — the existing egress sends it |
| the decoy's path | "at the aircraft, its velocity, falling" | ⭐ ONE function, `DecoyFlight.At(origin, velocity, i, t)` (Fdp.Toolkits Combat), used by your decoys AND by every viewer's flare effect — so every node draws the flare where the seeker sees it |
| the munition types | — | flare and chaff as TKB munition types with DIS kind 8 (flare 8.2.2, chaff 8.2.1), so `Munitions.IsExpendable` can tell them from rounds |

⭐ **Naming** (user, `2026-10-15`: *"Use AmmoGuid."*): the round's / expendable's TKB type is `AmmoGuid` on the notification and on the
`WeaponFire` wire, the same name as on the request and the mount; the spawned round's network id is `RoundEntityId`.

## Who builds what

| | lane |
|---|---|
| the notification's new fields, egress / ingress, `WeaponSlots`, `Munitions.IsExpendable`, the readers (effects use the fired round; sound and heat skip expendables), the flare / chaff looks | **ui, `CE-1111`** — merge `ui` once it lands, before publishing your release |
| the slot skip in `FireProcessingSystem`, `CountermeasureSystem` taking the slot, the flare / chaff munition types, `DecoyFlight`, publishing the release | **backend, `CE-2188`** |

⚠ If you have already built the dt 134 message and its translators: please report it rather than reverting on your own (scope rule); the user
decides whether it goes now or after CE-1111 lands.
