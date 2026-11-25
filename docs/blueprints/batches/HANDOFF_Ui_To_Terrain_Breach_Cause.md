# Handoff: ui → terrain — each breach records its CAUSE (explosion / ram / blade)

| | |
|---|---|
| **From** | `ui`, 2026-11-24 |
| **For** | the `terrain` lane (the record); the gateway copies it after (`ui`'s session, SimNetGateway T-075) |
| **Dispatched at** | `ui` (see the commit that adds this file) |
| **Asked by** | `unreal-ig`: [NOTE_UnrealIg_To_Ui_Breach_Cause.md](NOTE_UnrealIg_To_Ui_Breach_Cause.md) (`unreal-ig` `1c2d48a19`) |
| **User, verbatim** (relayed by `unreal-ig`) | *"pls do not forget scorched sides if the hole comes from explosion (and not from bulldozer)"* |
| **Re-dispatched** | ⭐ **2026-11-25, amended BEFORE any start** (rule 1a: `terrain` does not contain the first dispatch): scope widened by the IG client's [NOTE_IgClient_To_Ui_Breach_Cause.md](https://github.com/pjanec/HROT/blob/ig-client-notes/docs/blueprints/batches/NOTE_IgClient_To_Ui_Breach_Cause.md) (`ig-client-notes` `46ad6ddd5`) — and 🔒 user: *"just pls do the terrain parts yourself if the changes are small"* ⇒ **built by `ui`**, not the terrain lane (a cross-lane edit, said in the commit) |
| **Id** | **CE-11054** (ui block, allocated here) — the same shape as CE-11051, whose record fields this sits beside |

## 1. Why

Unreal scorches a hole's rim only when a blast made it. `BuildingDamage` carries per breach `MadeAt` and `Blast` (CE-11051), but for a ram or a
blade `Blast` is the hull's centre ⇒ the client cannot tell the causes apart. `BreachKind` (`Hole` / `Passage`, `BuildingDamage.cs:8`) is the
SHAPE, not the cause.

## 2. Measured — every producer already knows the cause

| producer | cause |
|---|---|
| `AreaEffectSystem.cs:576` (a detonation's breach) | `Explosion` |
| `ContactBreach.Event` (`ContactBreach.cs:373`), from `OnContact` with a non-blade row (a hull rammed / pushed into the wall, `RamOrder`, `:207`) | `Ram` |
| the same, from a blade row (`row.Blade`, `:227`) and from `BladeCut.cs:121` (`CutAlong`) | `Blade` |
| `AreaEffectSystem.cs:606` (collapse) | — no breach (`Panel = -1`) |

## 2a. Widened scope (2026-11-25)

| per breach (beside `MadeAt` / `Blast`) | |
|---|---|
| `Cause` | `BreachCause { Unknown = 0, Detonation = 1, Ram = 2, Blade = 3, Push = 4 }` — ⚠ **renamed `Explosion` → `Detonation`** (HROT's own word, `DetonationNotification`; the IG client's ask). `Push` = a contact breach of a free wall made by a held push (`ContactBreach` D-3; a building is only ever rammed or bladed) |
| `ExplosiveKg` | for `Detonation`: the charge's kg the breach rule used; 0 otherwise. A merge keeps the largest |
| `Actor` | for a contact cause: the vehicle's NETWORK id (0 otherwise; 0 after a load) |

| per building, the collapse (beside `CollapsedAt`) | |
|---|---|
| `CollapseCause`, `CollapseBlastX/Y/Z`, `CollapseKg` | from the collapse event (`AreaEffectSystem.cs:606` already carries the blast point); only detonations collapse today |

Saved: `Cause` and `ExplosiveKg` (how the hole LOOKS), the collapse's cause and kg; ⛔ not `Actor` (an entity id does not survive a load).

## 3. The change (lean — yours to adjust, say so in your report)

| part | |
|---|---|
| enum | `public enum BreachCause : byte { Unknown = 0, Explosion = 1, Ram = 2, Blade = 3 }` beside `BreachKind` — ⭐ `Unknown` MUST be 0: an unset byte (an old record, a replay before this) reads as Unknown |
| event | `BuildingDamageEvent` appends `byte Cause`; each producer in §2 sets it (`ContactBreach.Event` gets the cause as a parameter: `CutAlong` / the blade branch pass `Blade`, the rest `Ram`) |
| record | `BuildingDamage` appends `fixed byte Cause[Capacity]`, stamped where `MadeAt` / `Blast` are (`BuildingDamage.cs:102`); `CauseOf(i)` typed like `BlastOf` |
| a merge | when breaches union (`BuildingDamage.cs:244`), **`Explosion` wins** (any blast in the merged hole ⇒ scorched), else the newer cause — the same "max" rule `Kind` already uses there, with the enum ordered so it is a max over `Explosion` first |
| the save | ⭐ **unlike `MadeAt`, SAVE it**: `BuildingDamageSave` writes each breach's `kind` as text (`:253`); add `"cause": "explosion" \| "ram" \| "blade"` (omitted for Unknown) and read it back (absent ⇒ Unknown). Reason: a time means nothing after a load, but a scorched rim is how the hole LOOKS — a loaded scenario should look as it was saved |
| replication | the record is unmanaged and already replicated (`EntityBuildingDamageTranslators`) — a NED IDL field is appended the way CE-11051 appended its own (append-only) |

## 4. Rails

- A blast breach records `Explosion`, a rammed wall `Ram`, a blade cut `Blade` (one rail each, through the real producers).
- A merge of a ram breach and a blast breach into one hole is `Explosion`.
- A save → load round trip keeps the cause; a save without `cause` loads as `Unknown`.
- Replicated: a replica's record carries the same cause (the CE-11051 replication rail, extended).
- 0 B steady (CLAUDE.md HOT PATH) — the record is unmanaged, nothing new allocates.

## 5. After

Reply with the commit; `ui` merges it, bumps the gateway's HROT pin and appends `Breach.Cause` (DPS enum `BreachCause_Unknown /
_Explosion / _Ram / _Blade`) — gateway task T-075.
