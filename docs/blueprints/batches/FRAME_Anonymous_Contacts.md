<!--STATUS
state: LIVE
updated: 2026-10-05
current-answer: the whole file — a FRAME from the backend lane to the behaviors lane for CE-3063 (anonymous contacts, G6).
stale-below: nothing.
related-designs:
  - docs/DESIGN_Thermal_And_Acoustic_Sensing.md — OWNS S7; §5.1 / §6 D′–D″ are the anonymous-contact decisions this frame splits between lanes.
  - docs/DESIGN_Sensors_And_Doctrine.md — OWNS TargetMemory, the memory stage and §7.8 (CE-3054: memory = identity + freshness, danger at read time).
-->

# FRAME — anonymous contacts in `TargetMemory` (`CE-3063`, G6)

**From:** backend · **To:** behaviors · **Dispatched at:** see the commit that adds this file.
🔒 **User, `2026-10-05`:** *"I want Shot from north realism."* (R-205) · *"Send the 3063 frame."*

## Goal

A unit that HEARS something keeps "something about here" in its memory — a position and an uncertainty radius, no identity — and
its AI can react to it: face it, move away from it, report it, treat it as a threat to avoid. When a sense later confirms what is
there, the anonymous contact becomes that entity.

## What already exists (built by the backend, `CE-3062`)

- An acoustic sensor answers anonymous estimates; on the Brain they arrive as `SoundContactEvent { Observer, X, Y, Z, Radius, Kind }`
  (`Perception/Events/SoundContactEvent.cs`; DDS `AudioTargetDetected` → `SimHost/AudioTargetDetectedIngressTranslator`).
- ⛔ Nothing on the Brain consumes it yet. That is this item.

## Split of the work

| part | lane | what |
|---|---|---|
| ① storage + merge | **backend** | `TargetMemory`: a `Radius[]` per slot and reserved ids for anonymous slots (lean: **negative ids**, `TargetMemory.IsAnonymous(id) => id < 0`; real entity ids are always > 0). `ThreatEvaluationSystem` consumes `SoundContactEvent`: refresh a KNOWN contact within the radius (add the Acoustic kind) · else FUSE with an anonymous slot within the radius (radius shrinks) · else a new anonymous slot. A sighting (a real track) inside an anonymous slot's radius ABSORBS it (the slot becomes the entity, freshness kept). The forget rule stops treating an anonymous slot as dead (`!IsAlive`) — it fades by freshness only |
| ② the readers | **behaviors** | every reader that turns the id into an ENTITY skips anonymous slots; every reader that needs only a POSITION may use them. Measured `2026-10-05` (grep of `EntityIds[` over production): **entity readers** — `ThreatMatrixAssignmentSystem.cs:84,108`, `UtilityScorer.cs:228` (and any accessor `CE-3054` B–D is writing); **id-match readers, safe by construction** (a negative id never matches a real target) — `StandardInputs.cs:161,183`, `CgfNodes.cs:437`, `HillAttackTankNodes.cs:145,234`; **position sharer** — `SquadPerceptionMergeSystem.cs:76` (copy anonymous slots; two members' synthetic ids may differ for the same sound, so merge them by position, as ① does) |
| ③ the AI using it | **behaviors** | e.g. `HaveLiveTarget` stays entity-only (you cannot aim at a guess); a new input / condition "heard something within N s / within R m" and its bearing, so an SOP can face it, take cover from it or move off |

## Key decisions — backend leans, yours to confirm or change

| # | question | lean |
|---|---|---|
| **K1** | how is a slot marked anonymous? | negative id (no new field, no layout change for the id; `IsAnonymous` is the ONE rule every reader calls) |
| **K2** | do anonymous contacts count toward `FirstThreat` / `AllClear` edges? | **yes** — "something out there" is the start of a threat; ⚠ say if a doctrine should only wake on identified ones |
| **K3** | danger of an anonymous contact (CE-3054 "danger at read time")? | a fixed, low default per `SoundKind` (shot > detonation > movement) — there is no TKB to read |
| **K4** | does `CE-3054` B–D land first, or this? | **B–D first**, then ② on top — so you rewrite each reader once |

## Fences

- ⛔ No identity of a heard source anywhere on the Brain — not in memory, not in a debug field. The backend never sends it.
- ⛔ No second memory component for anonymous contacts — one memory, one freshness rule (R-194).
- ⭐ Cross-lane edits are fine either way; name them in the commit.

## Acceptance

1. A unit that hears a shot from 200 m north has ONE anonymous contact there, radius ≈ 0.15 × 200 m; repeated shots keep ONE contact and shrink it.
2. Seeing the shooter inside the radius turns that contact into the shooter (no duplicate).
3. No entity reader ever dereferences an anonymous id (a rail per entity reader); the squad shares the contact.
4. An SOP can react to it (one rail through ③).

📄 Design basis: [`DESIGN_Thermal_And_Acoustic_Sensing.md`](../../DESIGN_Thermal_And_Acoustic_Sensing.md) §5.1, §6 D–D″ · tracker `CE-3063`.
