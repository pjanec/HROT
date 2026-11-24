# Handoff: ui → terrain — the building / window damage records carry WHEN and WHERE FROM (CE-11051)

| | |
|---|---|
| **From** | `ui`, 2026-11-24 |
| **For** | the `terrain` lane (owner of `BuildingDamage` / `WindowDamage`, UTM design §8, R-4188) |
| **User, verbatim** | *"Can't we stamp the damage records with time in HROT as well, so that we do not invent another timestamping just in the gateway? We can change the record, it's no issue. We will tell the terrain plane to do it. Don't hesitate."* |
| **Why** | the Unreal client draws building damage from DPS `BuildingDamage` (`unreal-ig` `5c36842fa`, NOTE_UnrealIg_To_Ui_Missiles_And_Building_Damage.md §2.2): it must tell a LIVE change (dust, debris, glass burst) from the state at JOIN (a TransientLocal sample — no replay), and throw debris the right way |

## Measured (`ui`, `c51e416c6`)

- `BuildingDamage` (`FDP/Toolkits/Fdp.Toolkits/Terrain/BuildingDamage.cs:51-75`, unmanaged, RECORDED): per breach `Panel`, `From`, `To`,
  `Bottom`, `Top`, `Kind` (16 per entity) + `Collapsed` — **no time, no blast point**.
- `WindowDamage` (`WindowDamage.cs:28-46`): per window `Panel`, `Opening`, `Hits`, `U0 V0 U1 V1` (64 per entity) — **no time**.
- The moment's event `BuildingDamageEvent` (`BuildingDamageSystem.cs:35-47`) knows the shooter; the record keeps nothing of it.

## Ask

1. **Per breach: the sim time it was made** (or last widened), and **its blast point** (the detonation's world point, or the panel side +
   depth — your choice; say which) — appended fixed arrays, same capacity.
2. **Per window: the sim time of its last hit.**
3. **The collapse: the sim time it collapsed.**
4. Times as `SimInstant` (or the engine's sim-time double), the same clock the recorder and DPS use; unmanaged, fixed size (CLAUDE.md
   NEW COMPONENTS). Recordings may break (R-11013's precedent) — no compatibility shim.
5. Update the `EntityBuildingDamage` / window wire descriptors so the values cross nodes, and the save (`terrainObjects` / damage save) if it
   stores these records.
6. Rails: a breach / window / collapse carries the time it happened and the blast point; a later widening moves the time; the wire round trip
   carries both; 0 B steady.

Then the gateway copies them into DPS `BuildingDamage` (append-only) — `ui`/gateway does that half when you report.
