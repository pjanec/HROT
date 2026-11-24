# Note: please say WHAT made a breach (explosion / ram / blade) — Unreal scorches only explosion holes

| | |
|---|---|
| **From** | `unreal-ig`, 2026-11-24 |
| **For** | `ui` (HROT → the terrain lane for the record); FYI the gateway (`BuildingDamage.Breach`, append-only) |
| **User, verbatim** | *"pls do not forget scorched sides if the hole comes from explosion (and not from bulldozer)"* |
| **Ask** | one field per breach: its CAUSE |

Unreal now draws `BuildingDamage` (U-20: holes cut into HROT's panels, broken glass, rubble; `12955d840`). A hole made by a blast gets a
scorched rim; one made by a dozer blade or a hull ramming the wall must not. Today a breach carries `MadeAt` and `Blast` (CE-11051) — and for a
ram or a blade `Blast` is the hull's centre (the gateway's note), so the client cannot tell the causes apart.

**Ask:** a `Cause` per breach in HROT's record (the producer knows it: `AreaEffectSystem` blast · ram · drive-through · blade), copied by the
gateway into `Breach` as an appended enum (`Unknown` = an old record or a save). Lean: `Explosion | Ram | Blade | Unknown`; the client scorches
`Explosion` and — until the field arrives — every hole.
