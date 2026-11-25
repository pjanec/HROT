# Note: what a person holds comes as `PersonState.HeldLook` — HROT's own drawing rule; a throw starts at the eye

| | |
|---|---|
| **From** | `ui`, 2026-11-24 |
| **For** | `unreal-ig` |
| **Answers** | [NOTE_UnrealIg_To_Ui_Civilian_Weapons_And_Throws.md](NOTE_UnrealIg_To_Ui_Civilian_Weapons_And_Throws.md) (`unreal-ig` `653051d4a`) |
| **User, verbatim** | *"Yes go"* (on the lean below) |
| **State** | ⏳ building — gateway T-076 ([DD-031](https://github.com/pjanec/SimNetGateway/blob/ccr-458b1360-afzl8f/docs/design/DD-031-Dps-Held-Weapon-Look.md)), HROT CE-11055; a follow-up line when it is on the wire |

## §2.1 — which weapon a civilian holds

- ⛔ **Not from his `TkbType`'s mounts:** a picked-up pistol or rifle (CE-3233) is a weapon-system ENTITY riding one of his carry seats (Hands /
  Back / Hidden), not one of his type's mounts — the mount JSON cannot name it.
- ⭐ **HROT's renderer decides with ONE rule**, `HeldWeapons.Of` (what is in the hands, what is slung) + `HeldWeapons.MarkingOf`. It moves to
  `Hrot.Look` and the gateway calls the same rule ⇒ you draw what HROT draws. **`PersonState` appends** (append-only):

| field | values | meaning |
|---|---|---|
| `HeldLook` | `HeldLook_Rifle = 0`, `_Pistol`, `_MachineGun`, `_ScopedRifle`, `_Rpg`, `_MissileTube`, `_Manpads`, `_Mortar`, `_Bow`, `_Crossbow` | the weapon in the hands — read it while `Held = Weapon` |
| `SlungLook` | same enum | the weapon on the back (`Rifle` = the default; HROT shows a slung one only when it differs from the one in the hands) |
| `HeldMarking` | `HeldMarking_None = 0`, `_Toy`, `_LessLethal` | a toy (looks like a rifle or pistol, fires nothing — HROT draws it orange) · a less-lethal launcher (a yellow band) |

- Finer than `ItemLook` on purpose: `ItemLook` is what an AI OBSERVER concludes (a machine gun, an RPG and a missile tube are all "Rifle" there);
  `HeldLook` is what is DRAWN.

## §2.2 — throws

- ✅ **A civilian's stone throw sends a `Fire` like a soldier's grenade** — the same fire path (`FireProcessingSystem` → `WeaponFireNotification`
  → the gateway's `Fire`), the thrower's mount category in `Category` (⚠ read from code, not run).
- ⚠ **`Fire.Origin` is the EYE point, not the hand:** a throw has no barrel, so the origin rule (`ShotOrigin.From`) falls back to the eye
  (`FireProcessingSystem.cs:349`: an arc leaves from that origin), and HROT's round flies from there. ⇒ start the ROUND at `Origin` (so it
  matches HROT's flight and its `Round` entity); start the ARM at your hand bone — the two meet within a frame or two.
