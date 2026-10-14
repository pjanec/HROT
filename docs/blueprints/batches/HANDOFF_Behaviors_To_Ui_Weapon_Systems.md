<!--STATUS
state: LIVE — DISPATCHED at the commit that adds this file (2026-10-14); scope frozen there.
updated: 2026-10-14
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/weapons/DESIGN_Weapon_Systems.md — THE design (§3 classes, §5 the TKB, §6 the deploy state, §8 slice WS-5)
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Backend_Weapon_Systems.md — the backend half (the data, stations, the controller)
  - docs/DESIGN_Map_3D_Mode.md — §6m riders; a weapon stowed / deployed and a gunner at his weapon are drawn the same way
-->

# HANDOFF — ui: weapon systems (drawing them, placing them into stations)

**For:** the ui lane (`ui`). **From:** the behaviours lane (`behaviors`). ⭐ **Dispatched at the commit that adds this file**: your scope
is FROZEN there; documents that change after it are FYI. If a later document invalidates an item, **STOP that item and report it**; do
every item that is not blocked (R-106).

## 0. Why

🔒 User, `2026-10-14` (R-2016): separable weapons (HMG, minigun, Javelin, NLAW, LAW, Kornet, TOW, a sentry gun) are entities that can be
placed, mounted in a host's station (a pickup's bed, a helicopter door), carried by a soldier, deployed or stowed, and manned. ⭐ **Build
against [DESIGN_Weapon_Systems.md](../../designs/weapons/DESIGN_Weapon_Systems.md)** §5 and §6.

## 1. Before anything

1. ⛔ **Start after backend's WS-1** (the descriptor, `SeatRole.Station`, `WeaponSystemState`) — merge `backend` once it lands, or STOP and
   report if it has not. Then merge `behaviors` at the dispatch commit or later and push a `chore: started …` marker (rule 1b).
2. Ids from your block (`CE-1xxx`); progress on our row **CE-2174** as appended dated lines.

## 2. The items

| # | item | design | acceptance |
|---|---|---|---|
| **U1** | **the drawings**: a weapon system drawn `Stowed` (transport) and `Deployed` (on its tripod / mount, ready), read from `WeaponSystemState`; a station weapon drawn at its seat's place and facing (the seat's `X Y Z`, `FacingDegrees`); a gunner drawn at his weapon (the weapon's `Gunner` seat pose) | §6 rows 7–8, Map 3-D §6m | frame rails under Xvfb: a tripod HMG stowed vs deployed; a pickup with its HMG and a gunner; a soldier carrying a Javelin |
| **U2** | **the editor**: Add Entity entries for the weapon types and the presets (104, 105, 2006, 2007); placing a weapon into a host's station (drop it on the host, or pick the station in the Details panel), refused when the station's `Accepts` does not fit the weapon | §5, §6 row 6 | editor rails: a weapon dropped on a pickup boards its bed station; a pintle-only weapon is refused from a door station; save / load keeps it mounted |

## 3. Fences

- ⛔ No wire or ECS change of your own: the descriptor, the seat fields and the deploy state are backend's (WS-1).
- ⛔ The behaviour lane owns the weapon behaviours and `ua-weapon-systems` — a finding there is a REPORT.
- ⚠ Keep `PossessionStation.EyeOf`'s rotorcraft-first order (air-operations handoff, §3).

## 4. Report back — the gate report contract (CLAUDE.md rule 8)

One row per gate: verbatim command · pass / fail / skip · delta vs base · `--no-build` column · every red confirmed pre-existing against the
base sha · tree clean after each suite · `tracker-counts.py --check` · every id allocated · screenshots for U1. The design folded to the
as-built. Then the sha of each item on `ui`.
